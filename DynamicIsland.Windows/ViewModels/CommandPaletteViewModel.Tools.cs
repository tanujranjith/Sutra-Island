using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.ViewModels;

public sealed class PaletteClip
{
    public PaletteClip(ClipboardEntry entry)
    {
        Entry = entry;
        if (entry.PreviewBgra is { } bytes && entry.PreviewWidth > 0 && entry.PreviewHeight > 0)
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(entry.PreviewWidth, entry.PreviewHeight, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, bytes, entry.PreviewWidth * 4);
            bitmap.Freeze(); Thumbnail = bitmap;
        }
    }
    public ClipboardEntry Entry { get; }
    public string Text => Entry.Text;
    public bool IsImage => Entry.IsImage;
    public System.Windows.Media.ImageSource? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public string Preview => IsImage ? "Image" : string.Join(' ', Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public string SearchText => IsImage ? $"image screenshot picture {Text} {Entry.ImageWidth} {Entry.ImageHeight}" : Text;
    public string Detail => IsImage ? Entry.ImageWidth > 0 ? $"{Entry.ImageWidth:N0} × {Entry.ImageHeight:N0} · image" : "Image · preview unavailable"
        : $"{Text.Length:N0} characters · {Text.Count(c => c == '\n') + 1} lines";
}

public sealed partial class CommandPaletteViewModel
{
    private long _toolEpoch;
    private int _outputLoad;
    private int _clipLoad;
    private string _clipFilter = "", _notificationFilter = "", _outputFilter = "", _toolMessage = "", _focusMinutes = "25";
    private bool _toolsLoading, _outputSwitching, _mirroringChanging;
    private string _lastOutputId = "";
    private readonly List<PaletteClip> _allClips = [];
    private IReadOnlyList<AudioOutputDevice> _allOutputs = [];
    public bool IsStopwatchPanel => _panel == PalettePanel.Stopwatch;
    public bool IsFocusPanel => _panel == PalettePanel.Focus;
    public bool IsClipboardPanel => _panel == PalettePanel.Clipboard;
    public bool IsNotificationsPanel => _panel == PalettePanel.Notifications;
    public bool IsConvertPanel => _panel == PalettePanel.Convert;
    public bool IsAudioOutputPanel => _panel == PalettePanel.AudioOutput;
    public bool IsToolPanel => IsStopwatchPanel || IsFocusPanel || IsClipboardPanel || IsNotificationsPanel || IsConvertPanel || IsAudioOutputPanel;
    public string ToolTitle => _panel switch { PalettePanel.Stopwatch => "Stopwatch", PalettePanel.Focus => "Focus", PalettePanel.Clipboard => "Clipboard", PalettePanel.Notifications => "Notifications", PalettePanel.Convert => "Convert", PalettePanel.AudioOutput => "Audio output", _ => "" };
    public TimerAlarmViewModel Stopwatch => _runner.Stopwatch;
    public IslandViewModel Island => _runner.Island;
    public string FocusMinutes { get => _focusMinutes; set { if (SetProperty(ref _focusMinutes, value)) ClearStatus(); } }
    public ObservableCollection<PaletteClip> Clips { get; } = [];
    public ObservableCollection<NotificationHistoryItem> PanelNotifications { get; } = [];
    public ObservableCollection<AudioOutputDevice> Outputs { get; } = [];
    public PaletteClip? SelectedClip { get; set; }
    public AudioOutputDevice? SelectedOutput { get; set; }
    public bool ToolsLoading { get => _toolsLoading; private set => SetProperty(ref _toolsLoading, value); }
    public bool OutputSwitching { get => _outputSwitching; private set { if (SetProperty(ref _outputSwitching, value)) RaisePropertyChanged(nameof(CanSwitchOutput)); } }
    public bool CanSwitchOutput => !OutputSwitching && !ToolsLoading;
    public string ToolMessage { get => _toolMessage; private set => SetProperty(ref _toolMessage, value); }
    public string MirroringLabel => Island.Settings.ShowNotifications ? "Mirroring on" : "Mirroring off";
    public string ClipFilter { get => _clipFilter; set { if (SetProperty(ref _clipFilter, value)) FilterClips(); } }
    public string NotificationFilter { get => _notificationFilter; set { if (SetProperty(ref _notificationFilter, value)) FilterNotifications(); } }
    public string OutputFilter { get => _outputFilter; set { if (SetProperty(ref _outputFilter, value)) FilterOutputs(); } }
    private static bool Matches(string text, string query) => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    private void OpenToolPanel()
    {
        _toolEpoch++; ToolsLoading = false; OutputSwitching = false; ToolMessage = "";
        ClipFilter = ""; NotificationFilter = ""; OutputFilter = "";
        if (IsClipboardPanel) _ = RefreshClipsAsync();
        if (IsNotificationsPanel)
        {
            Island.OpenNotificationHistoryCommand.Execute(null);
            Island.NotificationHistory.CollectionChanged += OnPanelHistoryChanged;
            FilterNotifications(); RaisePropertyChanged(nameof(MirroringLabel));
        }
        if (IsAudioOutputPanel)
        {
            _lastOutputId = _runner.Audio.Current.OutputDeviceId;
            _runner.Audio.Changed += OnOutputAudioChanged;
            _ = RefreshOutputsAsync();
        }
        if (IsConvertPanel) RebuildConversionUnits();
    }
    private void CloseToolPanel()
    {
        _toolEpoch++; _outputLoad++; _clipLoad++;
        if (IsNotificationsPanel) Island.NotificationHistory.CollectionChanged -= OnPanelHistoryChanged;
        if (IsAudioOutputPanel) _runner.Audio.Changed -= OnOutputAudioChanged;
        _allClips.Clear(); Clips.Clear(); SelectedClip = null;
        _runner.Clipboard?.ReleasePaletteEntries();
        _allOutputs = []; Outputs.Clear(); SelectedOutput = null;
        PanelNotifications.Clear(); ToolsLoading = false;
    }
    private void RaiseToolPanelProperties()
    {
        foreach (var name in new[] { nameof(IsToolPanel), nameof(IsStopwatchPanel), nameof(IsFocusPanel), nameof(IsClipboardPanel), nameof(IsNotificationsPanel), nameof(IsConvertPanel), nameof(IsAudioOutputPanel), nameof(ToolTitle), nameof(PreferredPanelWidth) }) RaisePropertyChanged(name);
    }
    public void RequestPaletteClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    public void StartPanelFocus()
    {
        if (!PalettePanelPolicy.TryTimerArgument(FocusMinutes, "", out var argument)
            || !CommandParsers.TryParseDuration(argument, out var duration)) { ShowStatus("Choose a focus duration from 1 second to 24 hours, in minutes.", true); return; }
        Island.StartFocusSession(duration); ClearStatus();
    }
    public async Task RefreshClipsAsync()
    {
        var epoch = _toolEpoch; var load = ++_clipLoad;
        ToolsLoading = true; ToolMessage = "Loading clipboard history…";
        try
        {
            var clipboard = _runner.Clipboard;
            var items = clipboard is null ? [] : await clipboard.ReadPaletteAsync(30);
            if (!IsClipboardPanel || epoch != _toolEpoch || load != _clipLoad) return;
            _allClips.Clear(); _allClips.AddRange(items.Select(entry => new PaletteClip(entry)));
            FilterClips();
            ToolMessage = items.Count > 0 ? $"{items.Count} recent clips · select to copy" : clipboard?.Status.Message ?? "Clipboard history is unavailable.";
        }
        catch { if (epoch == _toolEpoch && IsClipboardPanel) ToolMessage = "Couldn’t load clipboard history. Enable it in Windows Settings, then retry."; }
        finally { if (epoch == _toolEpoch && load == _clipLoad) ToolsLoading = false; }
    }
    private void FilterClips()
    {
        Clips.Clear(); foreach (var clip in _allClips.Where(clip => Matches(clip.SearchText, ClipFilter))) Clips.Add(clip);
        SelectedClip = Clips.FirstOrDefault(); RaisePropertyChanged(nameof(SelectedClip));
    }
    public void CopyClip(PaletteClip? clip)
    {
        if (clip is null) return;
        var copied = _runner.Clipboard?.CopyEntry(clip.Entry) == true;
        ShowStatus(copied ? "Copied to clipboard." : "Couldn’t copy this item. Refresh history and try again.", !copied);
    }
    private void OnPanelHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => FilterNotifications();
    private void FilterNotifications()
    {
        PanelNotifications.Clear();
        foreach (var item in Island.NotificationHistory.Where(item => Matches($"{item.App} {item.Title} {item.Body}", NotificationFilter))) PanelNotifications.Add(item);
        ToolMessage = Island.Settings.NotificationHistoryEnabled ? PanelNotifications.Count == 0 ? "No matching notifications." : $"{PanelNotifications.Count} notifications" : "Notification history is disabled in Island settings.";
    }
    public void RemoveHistoryItem(NotificationHistoryItem? item) { if (item is not null) Island.DismissHistoryItemCommand.Execute(item); }
    public async Task ToggleMirroringAsync()
    {
        if (_mirroringChanging) return;
        _mirroringChanging = true;
        try { await _runner.SetMirroringAsync(!Island.Settings.ShowNotifications); RaisePropertyChanged(nameof(MirroringLabel)); }
        catch { ShowStatus("Couldn’t change notification mirroring. Try again.", true); }
        finally { _mirroringChanging = false; }
    }
    public void ClearPanelHistory() => Island.ClearNotificationHistoryCommand.Execute(null);
    public void DismissPanelBanner() => Island.DismissVisibleBanner();
    private void OnOutputAudioChanged(object? sender, AudioState state)
    {
        if (state.OutputDeviceId == _lastOutputId) return;
        _lastOutputId = state.OutputDeviceId;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(() => { if (IsAudioOutputPanel) _ = RefreshOutputsAsync(); });
        else if (IsAudioOutputPanel) _ = RefreshOutputsAsync();
    }
    public async Task RefreshOutputsAsync()
    {
        var epoch = _toolEpoch; var load = ++_outputLoad;
        ToolsLoading = true; RaisePropertyChanged(nameof(CanSwitchOutput)); ToolMessage = "Finding output devices…";
        try
        {
            var devices = _runner.Outputs is { } outputs ? await outputs.GetPaletteOutputsAsync() : [];
            if (!IsAudioOutputPanel || epoch != _toolEpoch || load != _outputLoad) return;
            _allOutputs = devices; FilterOutputs();
            ToolMessage = devices.Count == 0 ? "No output devices found. Connect a device and refresh." : "Select a device to make it the system output.";
        }
        catch { if (IsAudioOutputPanel && epoch == _toolEpoch) ToolMessage = "Couldn’t load output devices. Refresh to try again."; }
        finally { if (epoch == _toolEpoch && load == _outputLoad) { ToolsLoading = false; RaisePropertyChanged(nameof(CanSwitchOutput)); } }
    }
    private void FilterOutputs()
    {
        Outputs.Clear(); foreach (var device in _allOutputs.Where(device => Matches(device.Name, OutputFilter))) Outputs.Add(device);
        SelectedOutput = Outputs.FirstOrDefault(device => device.IsCurrent) ?? Outputs.FirstOrDefault(); RaisePropertyChanged(nameof(SelectedOutput));
    }
    public async Task SelectOutputAsync(AudioOutputDevice? device)
    {
        if (device is null || !CanSwitchOutput || device.IsCurrent || _runner.Outputs is null) return;
        var epoch = _toolEpoch; OutputSwitching = true;
        try
        {
            await _runner.Outputs.SwitchPaletteOutputAsync(device.Id);
            if (epoch == _toolEpoch && IsAudioOutputPanel) { await RefreshOutputsAsync(); ShowStatus($"Using {device.Name}.", false); }
        }
        catch (Exception ex) { if (epoch == _toolEpoch && IsAudioOutputPanel) ShowStatus(ex is InvalidOperationException ? ex.Message : "Couldn’t switch output. Refresh and try again.", true); }
        finally { if (epoch == _toolEpoch) OutputSwitching = false; }
    }
    private Task SubmitToolPanelAsync()
    {
        if (IsStopwatchPanel) Stopwatch.StopwatchPrimaryCommand.Execute(null);
        else if (IsFocusPanel && !Island.FocusSessionActive) StartPanelFocus();
        else if (IsClipboardPanel) CopyClip(SelectedClip);
        else if (IsConvertPanel) CopyConversion();
        else if (IsAudioOutputPanel) return SelectOutputAsync(SelectedOutput);
        return Task.CompletedTask;
    }
}
