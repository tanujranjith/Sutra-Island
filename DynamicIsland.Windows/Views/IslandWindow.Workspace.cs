using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace DynamicIsland.Windows.Views;

internal enum WorkspaceTab { Tray, Transfers, Pinned, Capture }

public partial class IslandWindow
{
    private static readonly HttpClient TransferClient = new();
    private readonly DispatcherTimer _workspaceMessageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private WorkspaceWindow? _workspaceWindow;
    private string? _workspaceMessage;
    internal event Action? WorkspacePinChanged;
    internal ObservableCollection<TrayItem> WorkspaceTray { get; } = [];
    internal ObservableCollection<CapturedNote> WorkspaceNotes { get; } = [];
    internal ObservableCollection<WorkspaceTransfer> WorkspaceTransfers { get; } = [];
    internal IslandGlance WorkspacePin => _viewModel.Settings.PinnedGlance;

    private void InitializeWorkspace()
    {
        foreach (var item in _viewModel.Settings.DropTrayItems ?? []) WorkspaceTray.Add(item);
        foreach (var item in _viewModel.Settings.CapturedNotes ?? []) WorkspaceNotes.Add(item);
        _workspaceMessageTimer.Tick += (_, _) =>
        {
            _workspaceMessageTimer.Stop();
            _workspaceMessage = null;
            RefreshWorkspaceGlance();
        };
        _viewModel.PropertyChanged += WorkspaceIslandChanged;
        _timerViewModel.PropertyChanged += WorkspaceTimerChanged;
        Closed += (_, _) =>
        {
            _workspaceMessageTimer.Stop();
            _viewModel.PropertyChanged -= WorkspaceIslandChanged;
            _timerViewModel.PropertyChanged -= WorkspaceTimerChanged;
            foreach (var transfer in WorkspaceTransfers.Where(t => t.IsBusy)) transfer.Cancellation.Cancel();
        };
        RefreshWorkspaceGlance();
    }

    internal void ShowWorkspace(WorkspaceTab tab = WorkspaceTab.Tray)
    {
        if (_workspaceWindow is null)
        {
            _workspaceWindow = new WorkspaceWindow(this) { Owner = this };
            _workspaceWindow.Closed += (_, _) => _workspaceWindow = null;
        }
        _workspaceWindow.ShowTab(tab);
    }

    private void WorkspaceTray_Click(object sender, RoutedEventArgs e) => ShowWorkspace();
    private void WorkspaceTransfers_Click(object sender, RoutedEventArgs e) => ShowWorkspace(WorkspaceTab.Transfers);
    private void WorkspacePinned_Click(object sender, RoutedEventArgs e) => ShowWorkspace(WorkspaceTab.Pinned);
    private void WorkspaceCapture_Click(object sender, RoutedEventArgs e) => ShowWorkspace(WorkspaceTab.Capture);

    internal async Task SetWorkspacePinAsync(IslandGlance pin)
    {
        if (WorkspacePin == pin) return;
        _viewModel.Settings.PinnedGlance = pin;
        WorkspacePinChanged?.Invoke();
        RefreshWorkspaceGlance();
        await _settingsService.SaveAsync(_viewModel.Settings);
    }

    internal async Task SaveWorkspaceNoteAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text.Length > 4000) throw new ArgumentException("A quick note can contain up to 4,000 characters.");
        WorkspaceNotes.Insert(0, new CapturedNote(text, DateTimeOffset.Now));
        await SaveWorkspaceNotesAsync();
        ShowWorkspaceMessage("Note saved");
    }

    internal Task SaveWorkspaceNotesAsync()
    {
        _viewModel.Settings.CapturedNotes = [.. WorkspaceNotes];
        return _settingsService.SaveAsync(_viewModel.Settings);
    }

    internal Task SaveWorkspaceTrayAsync()
    {
        _viewModel.Settings.DropTrayItems = [.. WorkspaceTray];
        return _settingsService.SaveAsync(_viewModel.Settings);
    }

    internal static bool SupportsWorkspaceDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) ||
        data.GetDataPresent("UniformResourceLocatorW") ||
        (data.GetDataPresent(DataFormats.UnicodeText) &&
         WorkspaceTransferService.TryDownloadUri(data.GetData(DataFormats.UnicodeText) as string, out _));

    internal async Task<int> AddWorkspaceDropAsync(IDataObject data, BitmapSource? clipboardImage = null)
    {
        var added = 0;
        if (data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                if (!Path.IsPathFullyQualified(path)) continue;
                var kind = File.Exists(path) ? TrayItemKind.File : Directory.Exists(path) ? TrayItemKind.Folder : (TrayItemKind?)null;
                if (kind is null) continue;
                var label = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                if (AddTrayItem(new(kind.Value, path, string.IsNullOrWhiteSpace(label) ? path : label))) added++;
            }
        }
        else if ((clipboardImage ?? data.GetData(DataFormats.Bitmap) as BitmapSource) is { } bitmap)
        {
            var folder = Path.Combine(AppDataPaths.Root, "drop-tray");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
            var image = bitmap.Clone();
            image.Freeze();
            await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(path);
                encoder.Save(output);
            });
            if (AddTrayItem(new(TrayItemKind.Screenshot, path, $"Screenshot · {DateTime.Now:t}"))) added++;
        }
        else
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (!WorkspaceTransferService.TryDownloadUri(text, out var uri))
            {
                var raw = data.GetData("UniformResourceLocatorW");
                text = raw is MemoryStream bytes ? System.Text.Encoding.Unicode.GetString(bytes.ToArray()).TrimEnd('\0') : raw as string;
            }
            if (WorkspaceTransferService.TryDownloadUri(text, out uri) && AddTrayItem(new(TrayItemKind.Link, uri.AbsoluteUri, uri.AbsoluteUri))) added++;
        }
        if (added > 0)
        {
            await SaveWorkspaceTrayAsync();
            ShowWorkspaceMessage(added == 1 ? "Added to Drop Tray" : $"{added} items added to Drop Tray");
        }
        return added;
    }

    private bool AddTrayItem(TrayItem item)
    {
        var comparison = item.Kind == TrayItemKind.Link ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (WorkspaceTray.Any(existing => existing.Kind == item.Kind && existing.Value.Equals(item.Value, comparison))) return false;
        WorkspaceTray.Insert(0, item);
        return true;
    }

    private void WorkspaceShell_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = SupportsWorkspaceDrop(e.Data) && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void WorkspaceShell_Drop(object sender, DragEventArgs e)
    {
        WorkspaceShell_DragOver(sender, e);
        if (e.Effects == DragDropEffects.None) return;
        try { await AddWorkspaceDropAsync(e.Data); }
        catch (Exception ex)
        {
            _log.Error("Drop Tray could not add an item", ex);
            ShowWorkspaceMessage("Couldn't add the dropped item");
        }
    }

    internal Task StartWorkspaceDownloadAsync(Uri uri, string destination) =>
        StartWorkspaceTransferAsync(destination, (progress, token) =>
            WorkspaceTransferService.DownloadAsync(TransferClient, uri, destination, progress, token));

    internal Task StartWorkspaceCopyAsync(string path, string destination) =>
        StartWorkspaceTransferAsync(destination, (progress, token) =>
            WorkspaceTransferService.CopyAsync(path, destination, progress, token));

    private async Task StartWorkspaceTransferAsync(string destination, Func<IProgress<int?>, CancellationToken, Task> work)
    {
        var transfer = new WorkspaceTransfer(Path.GetFileName(destination), Path.GetDirectoryName(destination)!);
        WorkspaceTransfers.Insert(0, transfer);
        foreach (var old in WorkspaceTransfers.Where(t => !t.IsBusy).Skip(30).ToArray()) WorkspaceTransfers.Remove(old);
        RefreshWorkspaceGlance();
        var progress = new Progress<int?>(value =>
        {
            transfer.Report(value);
            RefreshWorkspaceGlance();
        });
        try
        {
            await Task.Run(() => work(progress, transfer.Cancellation.Token));
            transfer.Finish("Complete", succeeded: true);
            ShowWorkspaceMessage("Transfer complete");
        }
        catch (OperationCanceledException) { transfer.Finish("Canceled"); }
        catch (Exception ex) { transfer.Finish(ex.Message); ShowWorkspaceMessage("Transfer failed"); }
        finally { RefreshWorkspaceGlance(); }
    }

    private void WorkspaceIslandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IslandViewModel.IsCompact) or nameof(IslandViewModel.IsAirPodsBannerActive)
            or nameof(IslandViewModel.CompactVoiceActive) or nameof(IslandViewModel.QShowCompactComparison)
            || (WorkspacePin, e.PropertyName) is
                (IslandGlance.Timer, nameof(IslandViewModel.TimerText) or nameof(IslandViewModel.DisplayTimer))
                or (IslandGlance.Cpu, nameof(IslandViewModel.CpuText))
                or (IslandGlance.Memory, nameof(IslandViewModel.RamText))
                or (IslandGlance.Battery, nameof(IslandViewModel.BatteryText))
                or (IslandGlance.Clock, nameof(IslandViewModel.ClockTimeText)))
            RefreshWorkspaceGlance();
    }

    private void WorkspaceTimerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (WorkspacePin == IslandGlance.Stopwatch && e.PropertyName == nameof(TimerAlarmViewModel.StopwatchText))
            RefreshWorkspaceGlance();
    }

    private void ShowWorkspaceMessage(string text)
    {
        _workspaceMessage = text;
        _workspaceMessageTimer.Stop();
        _workspaceMessageTimer.Start();
        RefreshWorkspaceGlance();
    }

    private void RefreshWorkspaceGlance()
    {
        var active = WorkspaceTransfers.FirstOrDefault(t => t.IsBusy);
        var text = active is not null ? (active.IsIndeterminate ? "Transferring…" : $"Transfer {active.Percent}%") :
            _workspaceMessage ?? (WorkspacePin switch
            {
                IslandGlance.Timer => _viewModel.PinnedGlanceTimerText,
                IslandGlance.Stopwatch => $"Stopwatch  {_timerViewModel.StopwatchText}",
                IslandGlance.Cpu => $"CPU  {_viewModel.CpuText}",
                IslandGlance.Memory => $"Memory  {_viewModel.RamText}",
                IslandGlance.Battery => string.IsNullOrEmpty(_viewModel.BatteryText) ? "Battery unavailable" : $"Battery  {_viewModel.BatteryText}",
                IslandGlance.Clock => _viewModel.ClockTimeText,
                _ => ""
            });
        WorkspaceGlanceLabel.Text = text;
        WorkspaceGlanceOverlay.Visibility = text.Length > 0 && _viewModel.IsCompact && !_viewModel.IsAirPodsBannerActive &&
            !_viewModel.CompactVoiceActive && !_viewModel.QShowCompactComparison ? Visibility.Visible : Visibility.Collapsed;
    }
}
