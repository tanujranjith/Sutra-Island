using DynamicIsland.Windows.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Windows.Services;

public sealed class MediaSessionService : IDisposable, Infrastructure.CommandPalette.IPaletteSeekControl
{
    private readonly LoggingService log;
    private readonly Infrastructure.CoalescingSeekQueue _seekQueue;
    public MediaSessionService(LoggingService log)
    {
        this.log = log;
        _seekQueue = new(ReadSeekContext, WriteQueuedSeekAsync, _shutdown.Token);
    }
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, DateTimeOffset> _recentActivity = new(StringComparer.OrdinalIgnoreCase);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _selectedSession;
    private Task? _pollTask;
    private int _pollIntervalMilliseconds = 500;
    private string _preferredApp = "Automatic";
    private string _lastIdentity = string.Empty;
    private string[] _availableApps = [];
    private byte[]? _artwork;
    private DateTimeOffset _artworkRetryUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextArtworkRetry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private (string Identity, TimeSpan Position, DateTimeOffset At)? _pendingSeek;

    // Browser media sessions can publish new metadata before their thumbnail changes.
    // Retry briefly after a track transition so the previous/video thumbnail is not cached.
    private static readonly TimeSpan ArtworkRetryWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ArtworkRetryInterval = TimeSpan.FromMilliseconds(500);

    public event EventHandler<MediaInfo>? Changed;
    public event EventHandler<IReadOnlyList<string>>? AvailableAppsChanged;
    public MediaInfo Current { get; private set; } = MediaInfo.Empty;

    public void ConfigurePolling(int intervalMilliseconds) =>
        Interlocked.Exchange(ref _pollIntervalMilliseconds, Math.Clamp(intervalMilliseconds, 100, 3000));

    public async Task StartAsync()
    {
        if (_pollTask is not null) return;
        if (Environment.GetEnvironmentVariable("DI_DEMO") == "1")
        {
            _pollTask = DemoLoopAsync(_shutdown.Token);
            return;
        }
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _pollTask = PollLoopAsync(_shutdown.Token);
        }
        catch (Exception ex)
        {
            log.Error("Windows media sessions are unavailable", ex);
            Publish(MediaInfo.Empty);
        }
    }

    // Off by default. With DI_DEMO=1 the island shows a fixed, royalty-free demo track (for screenshots
    // and promo clips) instead of whatever is really playing.
    private async Task DemoLoopAsync(CancellationToken token)
    {
        byte[]? art = null;
        try
        {
            var path = Path.Combine(DynamicIsland.Windows.Infrastructure.AppDataPaths.Root, "demo-cover.png");
            if (File.Exists(path)) art = await File.ReadAllBytesAsync(path, token);
        }
        catch { }

        var start = DateTimeOffset.Now;
        while (!token.IsCancellationRequested)
        {
            var elapsed = 38 + (DateTimeOffset.Now - start).TotalSeconds;
            Publish(new MediaInfo
            {
                Title = "Neon Drift",
                Artist = "Aurora",
                SourceAppName = "Aurora",
                PlaybackState = MediaPlaybackState.Playing,
                CanPlayPause = true,
                CanPrevious = true,
                CanNext = true,
                CanSeek = true,
                Position = TimeSpan.FromSeconds(Math.Min(elapsed, 89)),
                Duration = TimeSpan.FromSeconds(90),
                Artwork = art,
                UpdatedAt = DateTimeOffset.Now
            });
            try { await Task.Delay(250, token); } catch { break; }
        }
    }

    public void SetPreferredApp(string? appId) =>
        _preferredApp = string.IsNullOrWhiteSpace(appId) ? "Automatic" : appId;

    public Task TogglePlayPauseAsync() => RunCommandAsync("Play/pause", async session =>
        { await session.TryTogglePlayPauseAsync(); });

    // Explicit play/pause for the command palette — toggles are ambiguous when a command
    // names the exact action it wants.
    public Task PauseAsync() => RunCommandAsync("Pause", async session =>
        { await session.TryPauseAsync(); });

    public Task PlayAsync() => RunCommandAsync("Play", async session =>
        { await session.TryPlayAsync(); });

    public Task PreviousAsync() => RunCommandAsync("Previous", async session =>
        { await session.TrySkipPreviousAsync(); });

    public Task NextAsync() => RunCommandAsync("Next", async session =>
        { await session.TrySkipNextAsync(); });

    public async Task<string> ApplyAssistantMediaAsync(string command, TimeSpan? offset, CancellationToken token)
    {
        if (command == "Seek")
        {
            if (offset is null || !await _seekQueue.RelativeAsync(offset.Value, token))
                throw new InvalidOperationException("The media app did not accept that seek request. Check that the current track supports seeking.");
            return "The media app accepted the seek request.";
        }
        var session = _selectedSession ?? throw new InvalidOperationException("No media app is active.");
        await _commandGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, _selectedSession)) throw new InvalidOperationException("The media source changed. Try again.");
            if (command == "Play" && Current.PlaybackState == MediaPlaybackState.Playing) return "Media is already playing.";
            if (command == "Pause" && Current.PlaybackState == MediaPlaybackState.Paused) return "Media is already paused.";
            var accepted = command switch
            {
                "Play" => await session.TryPlayAsync().AsTask(token),
                "Pause" => await session.TryPauseAsync().AsTask(token),
                "Next" => await session.TrySkipNextAsync().AsTask(token),
                "Previous" => await session.TrySkipPreviousAsync().AsTask(token),
                _ => false
            };
            if (!accepted) throw new InvalidOperationException("The media app did not accept that control request.");
            return command switch { "Play" => "The media app accepted play.", "Pause" => "The media app accepted pause.", "Next" => "Requested the next track.", "Previous" => "Requested the previous track.", _ => "The media app accepted the seek request." };
        }
        finally { _commandGate.Release(); }
    }

    private async Task RunCommandAsync(string name, Func<GlobalSystemMediaTransportControlsSession, Task> action)
    {
        var session = _selectedSession;
        if (session is null || _shutdown.IsCancellationRequested) return;
        var entered = false;
        try
        {
            await _commandGate.WaitAsync(_shutdown.Token);
            entered = true;
            // A queued click belongs to the source that was selected when it was made.
            if (!ReferenceEquals(session, _selectedSession) || _shutdown.IsCancellationRequested) return;
            await action(session);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { log.Error($"{name} command failed", ex); }
        finally { if (entered) _commandGate.Release(); }
    }

    public Task SeekFractionAsync(double fraction) => ObserveSeekAsync(() => _seekQueue.FractionAsync(fraction));

    // Seek a fixed amount relative to the current position (used by the 10s rewind/forward controls).
    // The target is clamped to [0, duration] so a near-start rewind lands exactly on 0 and a near-end
    // forward lands exactly on the track length — never overshooting either end. Seeking is relative to
    // the latest accepted seek target while the source's timeline is catching up, so rapid taps stack.
    public Task SeekByAsync(TimeSpan offset) => ObserveSeekAsync(() => _seekQueue.RelativeAsync(offset));

    private async Task ObserveSeekAsync(Func<Task<bool>> request)
    {
        try { await request(); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { log.Error("Seek command failed", ex); }
    }

    private Infrastructure.SeekContext ReadSeekContext() => new(_selectedSession, _lastIdentity,
        ResolvePendingSeek(Current.Position, _lastIdentity, Current.PlaybackState), Current.Duration, Current.CanSeek);

    private async Task<bool> WriteQueuedSeekAsync(Infrastructure.SeekContext context, TimeSpan target, CancellationToken token)
    {
        await _commandGate.WaitAsync(token);
        try
        {
            if (!context.SameTrack(ReadSeekContext()) || !Current.CanSeek || context.Source is not GlobalSystemMediaTransportControlsSession session) return false;
            return await ChangePositionAsync(session, target, token);
        }
        finally { _commandGate.Release(); }
    }

    private async Task<bool> ChangePositionAsync(GlobalSystemMediaTransportControlsSession session, TimeSpan target, CancellationToken token)
    {
        var identity = _lastIdentity;
        if (!await session.TryChangePlaybackPositionAsync(target.Ticks).AsTask(token)) return false;
        if (!ReferenceEquals(session, _selectedSession) || identity != _lastIdentity) return false;
        var now = DateTimeOffset.Now;
        _pendingSeek = (identity, target, now);
        Publish(Current with { Position = target, UpdatedAt = now });
        return true;
    }

    private TimeSpan ResolvePendingSeek(TimeSpan reported, string identity, MediaPlaybackState state, bool fromSource = false)
    {
        if (_pendingSeek is not { } pending) return reported;
        var elapsed = DateTimeOffset.Now - pending.At;
        if (pending.Identity != identity || elapsed > TimeSpan.FromSeconds(3))
        {
            _pendingSeek = null;
            return reported;
        }
        var expected = pending.Position + (state == MediaPlaybackState.Playing ? elapsed : TimeSpan.Zero);
        if (fromSource && Math.Abs((reported - expected).TotalSeconds) < 1)
        {
            _pendingSeek = null;
            return reported;
        }
        return Infrastructure.SeekMath.ClampSeek(expected, TimeSpan.Zero, Current.Duration);
    }

    public void LaunchSource()
    {
        try
        {
            var id = Current.SourceAppId;
            if (string.IsNullOrWhiteSpace(id)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"shell:AppsFolder\\{id}",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { log.Error("Launching media source failed", ex); }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var isPlaying = false;
            try { isPlaying = await RefreshAsync(); }
            catch (Exception ex) { log.Error("Media session refresh failed", ex); }
            // Position needs frequent updates while playing. When nothing is playing,
            // polling less often avoids repeatedly querying media properties in the background.
            var activeInterval = Volatile.Read(ref _pollIntervalMilliseconds);
            var idleInterval = Math.Max(1000, activeInterval * 3);
            try { await Task.Delay(isPlaying ? activeInterval : idleInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<bool> RefreshAsync()
    {
        if (_manager is null) return false;
        var sessions = _manager.GetSessions().ToArray();
        var apps = sessions.Select(s => s.SourceAppUserModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!_availableApps.SequenceEqual(apps, StringComparer.OrdinalIgnoreCase))
        {
            _availableApps = apps;
            AvailableAppsChanged?.Invoke(this, apps);
        }

        var current = _manager.GetCurrentSession();
        var candidates = new List<SessionCandidate>();
        foreach (var session in sessions)
        {
            try
            {
                var playback = session.GetPlaybackInfo();
                var properties = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var id = session.SourceAppUserModelId ?? string.Empty;
                var state = MapPlayback(playback.PlaybackStatus);
                var identity = $"{id}|{properties.Title}|{properties.Artist}|{properties.AlbumTitle}|{state}";
                if (state == MediaPlaybackState.Playing || identity != _lastIdentity)
                    _recentActivity[id] = DateTimeOffset.Now;
                var score = Score(session, current, id, state, properties.Title, properties.Artist);
                candidates.Add(new SessionCandidate(session, playback, properties, timeline, id, state, score));
            }
            catch { }
        }

        var best = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => _recentActivity.GetValueOrDefault(c.AppId))
            .ThenBy(c => c.AppId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (best is null)
        {
            _selectedSession = null;
            _lastIdentity = string.Empty;
            _artwork = null;
            _artworkRetryUntil = DateTimeOffset.MinValue;
            _nextArtworkRetry = DateTimeOffset.MinValue;
            Publish(MediaInfo.Empty);
            return false;
        }

        _selectedSession = best.Session;
        var newIdentity = $"{best.AppId}|{best.Properties.Title}|{best.Properties.Artist}|{best.Properties.AlbumTitle}";
        var now = DateTimeOffset.Now;
        var identityChanged = !string.Equals(newIdentity, _lastIdentity, StringComparison.Ordinal);
        if (identityChanged)
        {
            _lastIdentity = newIdentity;
            _artworkRetryUntil = now + ArtworkRetryWindow;
            _nextArtworkRetry = DateTimeOffset.MinValue;
        }

        // Do not trust the first thumbnail during a track transition. Byte comparison keeps
        // unchanged thumbnails from causing a presentation update every polling interval.
        if (identityChanged || (now < _artworkRetryUntil && now >= _nextArtworkRetry))
        {
            var refreshedArtwork = await ReadArtworkAsync(best.Properties.Thumbnail);
            if (!ArtworkBytesEqual(_artwork, refreshedArtwork))
                _artwork = refreshedArtwork;
            _nextArtworkRetry = now + ArtworkRetryInterval;
        }

        var controls = best.Playback.Controls;
        var (position, duration) = ResolvePosition(best.Timeline, best.State);
        position = ResolvePendingSeek(position, newIdentity, best.State, fromSource: true);
        Publish(new MediaInfo
        {
            Title = best.Properties.Title ?? string.Empty,
            Artist = string.IsNullOrWhiteSpace(best.Properties.Artist)
                ? best.Properties.AlbumArtist ?? string.Empty : best.Properties.Artist,
            Album = best.Properties.AlbumTitle ?? string.Empty,
            SourceAppId = best.AppId,
            SourceAppName = FriendlyAppName(best.AppId),
            PlaybackState = best.State,
            CanPlayPause = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
            CanPrevious = controls.IsPreviousEnabled,
            CanNext = controls.IsNextEnabled,
            CanSeek = controls.IsPlaybackPositionEnabled,
            Position = position,
            Duration = duration,
            Artwork = _artwork,
            UpdatedAt = DateTimeOffset.Now
        });
        return best.State == MediaPlaybackState.Playing;
    }

    // GSMTC reports the position as a snapshot taken at LastUpdatedTime — it does not advance on its
    // own, so polling it returns a stale value (it freezes, and can show the previous track's position
    // until the app pushes a new timeline). When playing, extrapolate from that timestamp so progress
    // tracks the song. Clamped to the track length so it never overshoots.
    private static (TimeSpan Position, TimeSpan Duration) ResolvePosition(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline, MediaPlaybackState state)
    {
        var start = timeline.StartTime;
        var duration = timeline.EndTime > start
            ? timeline.EndTime - start
            : timeline.MaxSeekTime > TimeSpan.Zero ? timeline.MaxSeekTime : TimeSpan.Zero;

        var position = timeline.Position - start;
        if (state == MediaPlaybackState.Playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
        {
            var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(4)) position += elapsed;
        }
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && position > duration) position = duration;
        return (position, duration);
    }

    private int Score(GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSession? current, string appId, MediaPlaybackState state,
        string? title, string? artist)
    {
        var score = state switch
        {
            MediaPlaybackState.Playing => 10_000,
            MediaPlaybackState.Paused => 4_000,
            MediaPlaybackState.Stopped => 1_000,
            _ => 0
        };
        if (!string.Equals(_preferredApp, "Automatic", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_preferredApp, appId, StringComparison.OrdinalIgnoreCase)) score += 20_000;
        if (ReferenceEquals(session, current)) score += 800;
        if (!string.IsNullOrWhiteSpace(title)) score += 300;
        if (!string.IsNullOrWhiteSpace(artist)) score += 100;
        if (_recentActivity.TryGetValue(appId, out var recent))
            score += Math.Max(0, 200 - (int)(DateTimeOffset.Now - recent).TotalSeconds);
        return score;
    }

    private static MediaPlaybackState MapPlayback(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) =>
        status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.NoSession,
            _ => MediaPlaybackState.Unknown
        };

    private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size is < 32 or > 5_000_000) return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = await reader.LoadAsync((uint)stream.Size);
            var data = new byte[size];
            reader.ReadBytes(data);
            return data;
        }
        catch { return null; }
    }

    private static bool ArtworkBytesEqual(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Length != right.Length) return false;
        return left.AsSpan().SequenceEqual(right);
    }

    private static string FriendlyAppName(string appId)
    {
        var value = appId.ToLowerInvariant();
        if (value.Contains("spotify")) return "Spotify";
        if (value.Contains("zune") || value.Contains("music")) return "Media Player";
        if (value.Contains("vlc")) return "VLC";
        if (value.Contains("chrome")) return "Chrome";
        if (value.Contains("edge")) return "Microsoft Edge";
        var name = appId.Split('!')[0].Split('_')[0].Split('.').LastOrDefault();
        return string.IsNullOrWhiteSpace(name) ? "Media" : name;
    }

    private void Publish(MediaInfo info)
    {
        if (info == Current) return;
        Current = info;
        Changed?.Invoke(this, info);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _pollTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException)) { }
        _shutdown.Dispose();
    }

    private sealed record SessionCandidate(
        GlobalSystemMediaTransportControlsSession Session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo Playback,
        GlobalSystemMediaTransportControlsSessionMediaProperties Properties,
        GlobalSystemMediaTransportControlsSessionTimelineProperties Timeline,
        string AppId,
        MediaPlaybackState State,
        int Score);
}
