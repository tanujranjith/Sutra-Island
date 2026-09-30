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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly NotificationSnapshotTracker _tracker = new();
    private UserNotificationListener? _listener;
    private TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs>? _notificationChangedHandler;
    private int _generation;
    private bool _enabled;
    private bool _polling;
    public event EventHandler<IReadOnlyList<NotificationInfo>>? BatchReceived;
    public event EventHandler? StatusChanged;
    public IntegrationStatus Status { get; private set; } = IntegrationStatus.Disabled;
    public bool IsActive => Status.State == IntegrationState.Ready;
    public NotificationListenerService(LoggingService log)
    {
        _log = log;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer.Tick += async (_, _) => await PollAsync();
    }
    public TimeSpan PollInterval => _timer.Interval;
    public void Configure(TimeSpan pollInterval)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(pollInterval.TotalSeconds, 1, 30));
        if (interval == _timer.Interval) return;
        var wasRunning = _timer.IsEnabled;
        if (wasRunning) _timer.Stop();
        _timer.Interval = interval;
        if (wasRunning) _timer.Start();
    }
    private void SetStatus(IntegrationStatus value) { if (Status == value) return; Status = value; StatusChanged?.Invoke(this, EventArgs.Empty); }
    public async Task StartAsync(bool requestPermission = false)
    {
        if (_enabled) return;
        _enabled = true; var generation = ++_generation;
        SetStatus(new(IntegrationState.Connecting, "Connecting…"));
        try
        {
            _listener = UserNotificationListener.Current;
            var access = requestPermission ? await _listener.RequestAccessAsync() : _listener.GetAccessStatus();
            if (!_enabled || generation != _generation) return;
            if (access != UserNotificationListenerAccessStatus.Allowed)
            { SetStatus(new(IntegrationState.PermissionRequired, "Allow notification access in Windows Settings.", "ms-settings:privacy-notifications")); return; }
            await PollAsync();
            if (!_enabled || generation != _generation || Status.State == IntegrationState.PermissionRequired) return;
            if (_enabled && generation == _generation)
            {
                SubscribeToNotificationChanges(generation);
                // Reconcile the brief gap between the initial baseline and event subscription.
                await PollAsync();
                if (_enabled && generation == _generation && Status.State != IntegrationState.PermissionRequired)
                    _timer.Start();
            }
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                UnsubscribeFromNotificationChanges();
                SetStatus(new(IntegrationState.Unavailable, "Notification access is unavailable in this Windows/app configuration."));
                _log.Debug(ex.Message);
            }
        }
    }
    public async Task RetryAsync() { Stop(); await StartAsync(true); }
    public void Stop()
    {
        _enabled = false;
        _generation++;
        _timer.Stop();
        UnsubscribeFromNotificationChanges();
        _listener = null;
        _tracker.Reset();
        SetStatus(IntegrationStatus.Disabled);
    }

    private void SubscribeToNotificationChanges(int generation)
    {
        if (_listener is null || _notificationChangedHandler is not null) return;
        _notificationChangedHandler = (sender, args) =>
        {
            if (args.ChangeKind != UserNotificationChangedKind.Added) return;
            try
            {
                var notification = sender.GetNotification(args.UserNotificationId);
                var extracted = notification is null ? null : Extract(notification);
                if (extracted is null) return;

                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (!_enabled || generation != _generation) return;
                    // Periodic snapshot polling remains as a recovery path. The view model
                    // deduplicates this event against the poll if both observe the same toast.
                    BatchReceived?.Invoke(this, new[] { extracted });
                }));
            }
            catch (Exception ex)
            {
                if (generation == _generation)
                    _log.Debug($"Notification change could not be read: {ex.Message}");
            }
        };
        _listener.NotificationChanged += _notificationChangedHandler;
    }

    private void UnsubscribeFromNotificationChanges()
    {
        if (_listener is not null && _notificationChangedHandler is not null)
        {
            try { _listener.NotificationChanged -= _notificationChangedHandler; }
            catch (Exception ex) { _log.Debug($"Notification change listener could not be detached: {ex.Message}"); }
        }
        _notificationChangedHandler = null;
    }

    private async Task PollAsync()
    {
        if (!_enabled || _listener is null || _polling) return;
        _polling = true; var generation = _generation;
        try
        {
            if (_listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            {
                _timer.Stop();
                UnsubscribeFromNotificationChanges();
                SetStatus(new(IntegrationState.PermissionRequired, "Notification access was revoked.", "ms-settings:privacy-notifications"));
                return;
            }
            var notes = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (!_enabled || generation != _generation) return;
            var fresh = _tracker.Observe(notes.Select(Extract).OfType<NotificationInfo>());
            SetStatus(new(IntegrationState.Ready, "Connected"));
            if (fresh.Count > 0) BatchReceived?.Invoke(this, fresh);
        }
        catch (Exception ex) { if (generation == _generation) { SetStatus(new(IntegrationState.Error, "Notifications could not refresh. Retry.")); _log.Debug(ex.Message); } }
        finally { _polling = false; }
    }
    private static NotificationInfo? Extract(UserNotification n)
    {
        try
        {
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return null;
            var app = n.AppInfo?.DisplayInfo?.DisplayName?.Trim() ?? string.Empty;
            var text = binding.GetTextElements()
                .Select(element => element.Text?.Trim() ?? string.Empty)
                .Where(NotificationContent.HasVisibleText)
                .ToArray();
            var appId = n.AppInfo?.AppUserModelId ?? app;
            var notification = new NotificationInfo(app,
                text.FirstOrDefault() ?? string.Empty,
                string.Join("  ", text.Skip(1)), n.Id, n.CreationTime, appId);
            return NotificationContent.HasVisibleContent(notification) ? notification : null;
        }
        catch { return null; }
    }
    public void Dispose() => Stop();
}
