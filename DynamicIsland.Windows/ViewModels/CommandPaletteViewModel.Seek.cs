using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class CommandPaletteViewModel
{
    private MediaInfo _seekMedia = MediaInfo.Empty;
    private double _seekPosition;
    private bool _seekDragging, _seekWriting, _syncingSeek;
    private long _seekEpoch;
    private (double Seconds, long Epoch)? _pendingSeek;
    public bool CanSeekTrack => _seekMedia.HasSession && _seekMedia.CanSeek && _seekMedia.Duration > TimeSpan.Zero;
    public string SeekTrackTitle => _seekMedia.HasSession ? _seekMedia.DisplayTitle : "Nothing playing";
    public string SeekTrackSubtitle => !string.IsNullOrWhiteSpace(_seekMedia.Artist) ? _seekMedia.Artist : _seekMedia.SourceAppName;
    public bool ShowSeekStatus => !CanSeekTrack;
    public string SeekStatus => !_seekMedia.HasSession ? "Play something in a media app to use seek." : "This source doesn’t support seeking.";
    public double SeekDurationSeconds => Math.Max(1, _seekMedia.Duration.TotalSeconds);
    public string SeekElapsedText => FormatSeekTime(_seekPosition);
    public string SeekDurationText => FormatSeekTime(_seekMedia.Duration.TotalSeconds);
    public double SeekPositionSeconds
    {
        get => _seekPosition;
        set
        {
            if (_syncingSeek || !CanSeekTrack || !double.IsFinite(value)) return;
            var seconds = Math.Clamp(value, 0, _seekMedia.Duration.TotalSeconds);
            if (Math.Abs(seconds - _seekPosition) < .01) return;
            _seekPosition = seconds; RaiseSeekPosition();
            if (!_seekDragging) QueueSeek(seconds);
        }
    }
    private static string FormatSeekTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
    private void OnPanelMediaChanged(object? sender, MediaInfo media)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(() => { if (IsSeekPanel) SyncSeekMedia(); });
        else if (IsSeekPanel) SyncSeekMedia();
    }
    private void SyncSeekMedia()
    {
        var next = _runner.Seek.Current;
        var changedTrack = next.SourceAppId != _seekMedia.SourceAppId || next.Title != _seekMedia.Title
            || next.Artist != _seekMedia.Artist || next.Album != _seekMedia.Album || next.Duration != _seekMedia.Duration;
        if (changedTrack || !next.CanSeek || !next.HasSession) { _seekEpoch++; _pendingSeek = null; _seekDragging = false; }
        _seekMedia = next;
        _syncingSeek = true;
        try
        {
            if (changedTrack || !_seekDragging && !_seekWriting && _pendingSeek is null)
                _seekPosition = Math.Clamp(next.Position.TotalSeconds, 0, Math.Max(0, next.Duration.TotalSeconds));
            foreach (var name in new[] { nameof(CanSeekTrack), nameof(SeekTrackTitle), nameof(SeekTrackSubtitle), nameof(SeekDurationSeconds), nameof(SeekDurationText), nameof(SeekStatus), nameof(ShowSeekStatus) }) RaisePropertyChanged(name);
            RaiseSeekPosition();
        }
        finally { _syncingSeek = false; }
    }
    private void RaiseSeekPosition() { RaisePropertyChanged(nameof(SeekPositionSeconds)); RaisePropertyChanged(nameof(SeekElapsedText)); }
    public void BeginSeekDrag() { if (CanSeekTrack) _seekDragging = true; }
    public void EndSeekDrag()
    {
        if (!_seekDragging) return;
        _seekDragging = false;
        if (CanSeekTrack) QueueSeek(_seekPosition);
    }
    public void SkipSeek(double seconds) { if (CanSeekTrack) SeekPositionSeconds = _seekPosition + seconds; }
    private void QueueSeek(double seconds)
    {
        ClearStatus();
        _pendingSeek = (seconds, _seekEpoch);
        if (!_seekWriting) _ = FlushSeekAsync();
    }
    private async Task FlushSeekAsync()
    {
        _seekWriting = true;
        try
        {
            while (IsSeekPanel && CanSeekTrack && _pendingSeek is { } request)
            {
                _pendingSeek = null;
                if (request.Epoch != _seekEpoch) continue;
                await _runner.Seek.SeekFractionAsync(Math.Clamp(request.Seconds / _seekMedia.Duration.TotalSeconds, 0, 1));
            }
        }
        catch (Exception) { if (IsSeekPanel) ShowStatus("Couldn’t seek. Try again or check the media app.", true); }
        finally { _seekWriting = false; if (IsSeekPanel) SyncSeekMedia(); }
    }
}
