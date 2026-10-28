using Windows.Foundation;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Infrastructure;

namespace DynamicIsland.Windows.Services;

public sealed class NotificationListenerService : IDisposable
{
    private readonly LoggingService _log;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly INotificationSource _source;
    private readonly TimeSpan _readTimeout;
    private readonly NotificationSnapshotTracker _tracker = new();
    private CancellationTokenSource? _activeRead;
    private int _generation;
    private bool _enabled;
    private bool _pollAgain;
    private bool _subscriptionAttempted;
    private bool _eventsAvailable;
    private bool _replayExisting;
    private IReadOnlyList<NotificationInfo> _lastSnapshot = [];
    internal long SuccessfulPollCount { get; private set; }
    internal DateTimeOffset? LastSuccessfulPollAt { get; private set; }
    public event EventHandler<IReadOnlyList<NotificationInfo>>? BatchReceived;
    public event EventHandler? StatusChanged;
    public IntegrationStatus Status { get; private set; } = IntegrationStatus.Disabled;
    public bool IsActive => Status.State == IntegrationState.Ready;

    public NotificationListenerService(LoggingService log) : this(log, new WindowsNotificationSource(log)) { }

    internal NotificationListenerService(LoggingService log, INotificationSource source, TimeSpan? readTimeout = null)
    {
        _log = log;
        _source = source;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(8);
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public TimeSpan PollInterval => _timer.Interval;
    internal void TraceAdmission(int count) => _log.Debug($"Notification queue admission: accepted={count}.");
    public void Configure(TimeSpan pollInterval)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(pollInterval.TotalSeconds, 0.1, 30));
        if (interval == _timer.Interval) return;
        var wasRunning = _timer.IsEnabled;
        if (wasRunning) _timer.Stop();
        _timer.Interval = interval;
        if (wasRunning) _timer.Start();
    }

    private void SetStatus(IntegrationStatus value)
    {
        if (Status == value) return;
        Status = value;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartAsync(bool requestPermission = false)
    {
        if (_enabled) return;
        _enabled = true;
        var generation = ++_generation;
        _replayExisting = true;
        SetStatus(new(IntegrationState.Connecting, "Connecting…"));
        // Polling is the independent fallback, not a consequence of successfully attaching
        // the foreground event. An unpackaged desktop app can read toasts while that event fails.
        _timer.Start();
        try
        {
            if (requestPermission) await _source.RequestAccessAsync();
            if (_enabled && generation == _generation) await PollAsync();
        }
        catch (Exception ex)
        {
            if (_enabled && generation == _generation) ReportPollError(ex);
        }
    }

    public async Task RetryAsync() { Stop(); await StartAsync(true); }

    internal void Dismiss(IEnumerable<NotificationInfo> notifications)
    {
        var shown = notifications.ToArray();
        // Include duplicate system IDs that the arrival debounce hid from the banner.
        var dismissed = shown.Concat(_lastSnapshot.Where(candidate => shown.Any(item =>
            NotificationContent.SameMessage(candidate.AppId, candidate.App, candidate.Title, candidate.Body,
                item.AppId, item.App, item.Title, item.Body)))).DistinctBy(item => item.Identity).ToArray();
        _tracker.Dismiss(dismissed);
        foreach (var notification in dismissed)
        {
            try { _source.Dismiss(notification); }
            catch (Exception ex) { _log.Debug($"Windows notification dismissal failed; local suppression remains active: {ex.Message}"); }
        }
    }

    public void Stop()
    {
        _enabled = false;
        _generation++;
        _timer.Stop();
        _pollAgain = false;
        var pending = _activeRead;
        _activeRead = null;
        pending?.Cancel();
        UnsubscribeFromNotificationChanges();
        _tracker.Reset();
        SetStatus(IntegrationStatus.Disabled);
    }

    private void SubscribeToNotificationChanges(int generation)
    {
        if (_subscriptionAttempted) return;
        _subscriptionAttempted = true;
        try
        {
            _source.Subscribe(() =>
            {
                // WinRT callbacks can arrive off the UI thread. Serialize snapshot reads on
                // the dispatcher, and ignore callbacks belonging to a stopped subscription.
                if (_dispatcher.HasShutdownStarted) return;
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_enabled && generation == _generation) _ = PollAsync();
                }));
            });
            _eventsAvailable = true;
        }
        catch (Exception ex)
        {
            _eventsAvailable = false;
            _log.Info($"Notification change events unavailable (0x{ex.HResult:X8}); continuing background polling every {_timer.Interval.TotalSeconds:0}s.");
        }
    }

    private void UnsubscribeFromNotificationChanges()
    {
        try { _source.Unsubscribe(); }
        catch (Exception ex) { _log.Debug($"Notification change listener could not be detached: {ex.Message}"); }
        _subscriptionAttempted = false;
        _eventsAvailable = false;
    }

    private async Task PollAsync()
    {
        if (!_enabled) return;
        if (_activeRead is not null) { _pollAgain = true; return; }
        using var cancellation = new CancellationTokenSource();
        _activeRead = cancellation;
        var generation = _generation;
        try
        {
            if (!_source.HasAccess)
            {
                UnsubscribeFromNotificationChanges();
                SetStatus(new(IntegrationState.PermissionRequired, "Allow notification access in Windows Settings.", "ms-settings:privacy-notifications"));
                // Keep the fallback alive so restoring Windows access resumes delivery without
                // opening the island or pressing Retry. Never request permission from a timer.
                return;
            }
            SubscribeToNotificationChanges(generation);
            var notes = await _source.ReadAsync(cancellation.Token).WaitAsync(_readTimeout, cancellation.Token);
            if (!_enabled || generation != _generation) return;
            _lastSnapshot = notes;
            var fresh = _tracker.Observe(notes, _replayExisting);
            _replayExisting = false;
            SuccessfulPollCount++;
            LastSuccessfulPollAt = DateTimeOffset.UtcNow;
            if (Status.State != IntegrationState.Ready)
                _log.Info($"Notification polling connected: interval={_timer.Interval.TotalSeconds:0}s, foreground events={_eventsAvailable}.");
            SetStatus(new(IntegrationState.Ready, _eventsAvailable ? "Connected" : "Connected (polling)"));
            if (fresh.Count > 0) BatchReceived?.Invoke(this, fresh);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ex is TimeoutException) cancellation.Cancel();
            if (_enabled && generation == _generation) ReportPollError(ex);
        }
        finally
        {
            // Stop/restart can start another read before the old WinRT operation finishes.
            // Only the read that owns this gate may release it or consume the queued refresh.
            if (ReferenceEquals(_activeRead, cancellation))
            {
                _activeRead = null;
                if (_pollAgain)
                {
                    _pollAgain = false;
                    if (_enabled && generation == _generation) _ = PollAsync();
                }
            }
        }
    }

    private void ReportPollError(Exception ex)
    {
        if (Status.State != IntegrationState.Error)
            _log.Error("Windows notification refresh failed; background polling will retry automatically", ex);
        SetStatus(new(IntegrationState.Error, "Notification refresh delayed. Retrying automatically…"));
    }

    public void Dispose() => Stop();
}

internal interface INotificationSource
{
    bool HasAccess { get; }
    Task RequestAccessAsync();
    Task<IReadOnlyList<NotificationInfo>> ReadAsync(CancellationToken cancellationToken);
    void Subscribe(Action added);
    void Unsubscribe();
    void Dismiss(NotificationInfo notification);
}

internal sealed class WindowsNotificationSource(LoggingService log) : INotificationSource
{
    private UserNotificationListener? _listener;
    private NotificationSnapshotReader? _snapshotReader;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _appNames = new(StringComparer.Ordinal);
    private readonly Dictionary<(uint Id, DateTimeOffset CreatedAt), (string App, string AppId)> _appMetadata = new();
    private TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs>? _handler;
    private UserNotificationListener Listener => _listener ??= UserNotificationListener.Current;
    public bool HasAccess => Listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;
    public async Task RequestAccessAsync() => await Listener.RequestAccessAsync();
    public void Dismiss(NotificationInfo notification)
    {
        if (notification.Id is not uint id || notification.CreatedAt is not { } createdAt || !HasAccess) return;
        // A publisher may reuse an ID. Never remove a newer toast using a stale banner.
        if (Listener.GetNotification(id) is { } current && current.CreationTime == createdAt)
            Listener.RemoveNotification(id);
    }
    public Task<IReadOnlyList<NotificationInfo>> ReadAsync(CancellationToken cancellationToken) =>
        (_snapshotReader ??= new(ReadSnapshotAsync)).ReadAsync(cancellationToken);

    private async Task<IReadOnlyList<NotificationInfo>> ReadSnapshotAsync()
    {
        // Keep the WinRT read in the listener's original dispatcher context. Some desktop
        // Windows configurations do not complete this API when called from a worker thread.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var notes = await Listener.GetNotificationsAsync(NotificationKinds.Toast);
        var readMilliseconds = timer.Elapsed.TotalMilliseconds;
        var result = new List<NotificationInfo>();
        var active = new HashSet<(uint Id, DateTimeOffset CreatedAt)>();
        foreach (var note in notes)
        {
            if (Extract(note) is { } notification)
            {
                result.Add(notification);
                if (notification.Id is uint id && notification.CreatedAt is DateTimeOffset createdAt)
                    active.Add((id, createdAt));
            }
        }
        foreach (var expired in _appMetadata.Keys.Where(key => !active.Contains(key)).ToArray()) _appMetadata.Remove(expired);
        log.Debug($"Windows notification snapshot: count={notes.Count}, readMs={readMilliseconds:0}, decodeMs={timer.Elapsed.TotalMilliseconds - readMilliseconds:0}.");
        if (timer.Elapsed >= TimeSpan.FromSeconds(2))
            log.Info($"Slow Windows notification snapshot: count={notes.Count}, readMs={readMilliseconds:0}, decodeMs={timer.Elapsed.TotalMilliseconds - readMilliseconds:0}.");
        return result;
    }
    public void Subscribe(Action added)
    {
        TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs> handler = (_, args) =>
        {
            if (args.ChangeKind == UserNotificationChangedKind.Added) added();
        };
        Listener.NotificationChanged += handler;
        _handler = handler;
    }
    public void Unsubscribe()
    {
        if (_listener is not null && _handler is not null)
        {
            var handler = _handler;
            _handler = null;
            _listener.NotificationChanged -= handler;
        }
    }
    private NotificationInfo? Extract(UserNotification n)
    {
        try
        {
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return null;
            var identity = (n.Id, n.CreationTime);
            if (!_appMetadata.TryGetValue(identity, out var metadata))
            {
                // AppInfo/DisplayInfo can require expensive package metadata resolution. A
                // retained toast's app identity cannot change, while its text still can.
                var info = n.AppInfo;
                var appId = info?.AppUserModelId ?? string.Empty;
                var app = info is null ? string.Empty : string.IsNullOrEmpty(appId)
                    ? info.DisplayInfo?.DisplayName?.Trim() ?? string.Empty
                    : _appNames.GetOrAdd(appId, _ => info.DisplayInfo?.DisplayName?.Trim() ?? string.Empty);
                metadata = (app, string.IsNullOrEmpty(appId) ? app : appId);
                _appMetadata[identity] = metadata;
            }
            var text = binding.GetTextElements().Select(element => element.Text?.Trim() ?? string.Empty)
                .Where(NotificationContent.HasVisibleText).ToArray();
            var notification = new NotificationInfo(metadata.App, text.FirstOrDefault() ?? string.Empty,
                string.Join("  ", text.Skip(1)), identity.Id, identity.CreationTime, metadata.AppId);
            return NotificationContent.HasVisibleContent(notification) ? notification : null;
        }
        catch { return null; }
    }
}

internal sealed class NotificationSnapshotReader(Func<Task<IReadOnlyList<NotificationInfo>>> read)
{
    private Task<IReadOnlyList<NotificationInfo>>? _pending;

    public async Task<IReadOnlyList<NotificationInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A service timeout cancels only its wait. Keep the Windows operation alive so
        // the next poll can receive it, rather than repeatedly restarting a slow read
        // or piling overlapping requests onto the Windows notification service.
        var pending = _pending ??= read();
        try { return await pending.WaitAsync(cancellationToken); }
        finally
        {
            // Preserve a result that arrives between a timeout and the next poll.
            if (pending.IsCompleted && !cancellationToken.IsCancellationRequested && ReferenceEquals(_pending, pending))
                _pending = null;
        }
    }
}
