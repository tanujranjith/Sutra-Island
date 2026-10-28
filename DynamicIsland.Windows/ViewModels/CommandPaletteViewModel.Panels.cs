using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class CommandPaletteViewModel
{
    private PalettePanel _panel;
    private double _volumePercent;
    private AudioState _panelAudio = AudioState.Unknown;
    private string _timerMinutes = "10", _timerLabel = "", _alarmTime = "", _alarmRepeat = "Once";
    public PalettePanel Panel => _panel;
    public bool HasPanel => _panel != PalettePanel.None;
    public bool ShowSuggestions => !HasPanel;
    public bool IsVolumePanel => _panel == PalettePanel.Volume;
    public bool IsSeekPanel => _panel == PalettePanel.Seek;
    public bool IsCompactPanel => IsVolumePanel || IsSeekPanel || IsToolPanel;
    public double PreferredPanelWidth => IsClipboardPanel || IsNotificationsPanel || IsConvertPanel || IsAudioOutputPanel ? 520 : IsCompactPanel ? 440 : 640;
    public bool IsTimerPanel => _panel == PalettePanel.Timer;
    public bool IsAlarmPanel => _panel == PalettePanel.Alarm;
    public bool ShowPanelHeader => HasPanel && !IsCompactPanel;
    public bool ShowPaletteFooter => !IsCompactPanel;
    public string PanelTitle => _panel switch { PalettePanel.Volume => "Volume", PalettePanel.Seek => "Seek", PalettePanel.Timer => "New timer", PalettePanel.Alarm => "New alarm", _ => "" };
    public string PanelSubtitle => _panel switch { PalettePanel.Volume => "Audio · System output", PalettePanel.Timer => "A little time for what matters", PalettePanel.Alarm => "Your next reminder, right on time", _ => "" };
    public string FooterHint => HasPanel ? "Esc back to commands · Tab move between controls" : "↵ run    ↑↓ navigate    Ctrl+D favorite    Esc dismiss";
    public string TimerMinutes { get => _timerMinutes; set { if (SetProperty(ref _timerMinutes, value)) ClearStatus(); } }
    public string TimerLabel { get => _timerLabel; set { if (SetProperty(ref _timerLabel, value)) ClearStatus(); } }
    public string AlarmTime { get => _alarmTime; set { if (SetProperty(ref _alarmTime, value)) ClearStatus(); } }
    public string AlarmRepeat { get => _alarmRepeat; set { if (SetProperty(ref _alarmRepeat, value)) ClearStatus(); } }
    public IReadOnlyList<string> AlarmRepeatOptions { get; } = ["Once", "Every day", "Weekdays", "Weekends"];
    public bool AudioAvailable => _panelAudio.Availability == AudioAvailability.Available;
    public string VolumeValueText => $"{_volumePercent:0}%";
    public bool ShowVolumeStatus => !AudioAvailable || _panelAudio.SystemMuted;
    public string VolumeStateText => !AudioAvailable ? "Audio unavailable. Connect an output device." : _panelAudio.SystemMuted ? "Muted · your volume level is saved" : "";
    public string VolumeDeviceText => string.IsNullOrWhiteSpace(_panelAudio.OutputDeviceName) ? "System output" : _panelAudio.OutputDeviceName;
    public string VolumeMuteText => _panelAudio.SystemMuted ? "Unmute" : "Mute";
    public string PanelVolumeGlyph => _panelAudio.SystemMuted ? "\uE74F" : "\uE767";
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (!double.IsFinite(value) || !AudioAvailable) return;
            var percent = Math.Clamp((int)Math.Round(value), 0, 100);
            if (_volumePercent == percent) return;
            _volumePercent = percent;
            RaisePropertyChanged(); RaisePropertyChanged(nameof(VolumeValueText));
            _runner.Audio.SetMasterVolume(percent);
            SyncPanelAudio();
        }
    }
    private void OpenPanel(PalettePanel panel)
    {
        ClosePanel(); ClearStatus();
        _panel = panel;
        if (IsVolumePanel) { _runner.Audio.Changed += OnPanelAudioChanged; SyncPanelAudio(); }
        if (IsSeekPanel) { _runner.Seek.Changed += OnPanelMediaChanged; SyncSeekMedia(); }
        if (IsTimerPanel) { TimerMinutes = "10"; TimerLabel = ""; }
        if (IsAlarmPanel) { AlarmTime = DateTime.Now.AddHours(1).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture); AlarmRepeat = "Once"; }
        if (IsToolPanel) OpenToolPanel();
        RaisePanelProperties();
    }
    public void ClosePanel()
    {
        if (!HasPanel) return;
        if (IsVolumePanel) _runner.Audio.Changed -= OnPanelAudioChanged;
        if (IsSeekPanel) { _runner.Seek.Changed -= OnPanelMediaChanged; _pendingSeek = null; _seekDragging = false; _seekEpoch++; }
        if (IsToolPanel) CloseToolPanel();
        _panel = PalettePanel.None;
        ClearStatus(); RaisePanelProperties();
    }
    private void RaisePanelProperties()
    {
        foreach (var name in new[] { nameof(Panel), nameof(HasPanel), nameof(ShowSuggestions), nameof(IsVolumePanel), nameof(IsSeekPanel), nameof(IsCompactPanel), nameof(IsTimerPanel), nameof(IsAlarmPanel), nameof(ShowPanelHeader), nameof(ShowPaletteFooter), nameof(PanelTitle), nameof(PanelSubtitle), nameof(FooterHint) }) RaisePropertyChanged(name);
        RaiseToolPanelProperties();
    }
    private void OnPanelAudioChanged(object? sender, AudioState state)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(() => { if (IsVolumePanel) SyncPanelAudio(); });
        else if (IsVolumePanel) SyncPanelAudio();
    }
    private void SyncPanelAudio()
    {
        _panelAudio = _runner.Audio.Current;
        _volumePercent = _panelAudio.MasterVolumePercent;
        foreach (var name in new[] { nameof(VolumePercent), nameof(VolumeValueText), nameof(VolumeDeviceText), nameof(VolumeStateText), nameof(ShowVolumeStatus), nameof(VolumeMuteText), nameof(PanelVolumeGlyph), nameof(AudioAvailable) }) RaisePropertyChanged(name);
    }
    public void TogglePanelMute() { if (AudioAvailable) { _runner.Audio.ToggleMuted(); SyncPanelAudio(); } }
    public async Task SubmitPanelAsync()
    {
        if (IsToolPanel) { await SubmitToolPanelAsync(); return; }
        if (IsCompactPanel) { CloseRequested?.Invoke(this, EventArgs.Empty); return; }
        if (IsTimerPanel)
        {
            if (!PalettePanelPolicy.TryTimerArgument(TimerMinutes, TimerLabel, out var argument))
            { ShowStatus("Choose a duration from 1 second to 24 hours, in minutes.", true); return; }
            await ExecuteMatchAsync(new(CommandCatalog.ById(PaletteCommand.Timer), argument));
        }
        else if (IsAlarmPanel)
        {
            if (string.IsNullOrWhiteSpace(AlarmTime)) { ShowStatus("Enter a time such as 7:30 AM or 19:30.", true); return; }
            var suffix = AlarmRepeat switch { "Every day" => " daily", "Weekdays" => " weekdays", "Weekends" => " weekends", _ => "" };
            await ExecuteMatchAsync(new(CommandCatalog.ById(PaletteCommand.Alarm), AlarmTime.Trim() + suffix));
        }
    }
    public Task OpenPanelListAsync() => ExecuteMatchAsync(new(CommandCatalog.ById(IsAlarmPanel ? PaletteCommand.ShowAlarms : PaletteCommand.ShowTimers), ""));
}
