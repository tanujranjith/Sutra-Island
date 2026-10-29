using System.Windows.Input;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class IslandViewModel
{
    private readonly NotificationQueue _pendingNotifications = new();
    private readonly NotificationDismissalTracker _dismissedNotifications = new();
    private readonly List<(NotificationInfo Message, DateTimeOffset AcceptedAt)> _recentAcceptedNotifications = [];
    private readonly List<DeliveredNotificationSource> _deliveredNotificationSources = [];
    private readonly Dictionary<string, DateTimeOffset> _handledNotificationIdentities = new(StringComparer.Ordinal);
    private static readonly TimeSpan NotificationRepeatWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NotificationIdentityRetention = TimeSpan.FromHours(24);
    private const int MaxRememberedNotificationIdentities = 512;
    private NotificationGroup? _currentGroup;
    private double _bannerSeconds;
    private bool _lastNotificationVisible;
    private DateTimeOffset _lastBannerTick = DateTimeOffset.Now;
    public event EventHandler? NotificationGroupOpenRequested;
    public ICommand OpenCurrentNotificationCommand => new RelayCommand(() =>
    {
        if (_currentGroup is { } group && (group.Summary || group.Items.Count > 1))
        {
            NotificationHistory.Clear(); foreach (var item in group.Items.Reverse()) NotificationHistory.Add(item);
            RaiseMany(nameof(HasNotificationHistory), nameof(ShowEmptyNotificationHistory));
            NotificationGroupOpenRequested?.Invoke(this, EventArgs.Empty);
        }
        else OpenNotification(_currentNotificationHistoryItem);
        ClearCurrentNotificationBanner();
    });

    private void ClearCurrentNotificationBanner(bool dismissFromSource = false)
    {
        if (_currentGroup is { } group)
        {
            if (dismissFromSource)
            {
                var historyIds = group.Items.Select(item => item.Id).ToHashSet();
                var sources = _deliveredNotificationSources.Where(item => historyIds.Contains(item.HistoryId))
                    .Select(item => item.Notification).ToArray();
                _dismissedNotifications.Remember(sources);
                _notificationService.Dismiss(sources);
            }
            RememberHandledNotifications(group);
            _pendingNotifications.RemoveMatching(group.Items);
        }
        _currentGroup = null;
        _currentNotificationHistoryItem = null;
        _notification = null;
        _bannerSeconds = 0;
        _lastNotificationVisible = false;
        RaiseMany(nameof(ShowNotification), nameof(ShowBanner), nameof(IsAirPodsBannerActive),
            nameof(BannerApp), nameof(BannerTitle), nameof(BannerBody));
    }
    private void OnNotificationBatch(object? sender, IReadOnlyList<NotificationInfo> batch) => OnUi(() =>
    {
        if (!Settings.ShowNotifications) return;
        var acceptedNotifications = new List<NotificationInfo>();
        var savedHistory = _notificationHistoryService.Items;
        var now = DateTimeOffset.UtcNow;
        _recentAcceptedNotifications.RemoveAll(item => now - item.AcceptedAt > NotificationRepeatWindow);
        _deliveredNotificationSources.RemoveAll(item => now - item.AcceptedAt > NotificationIdentityRetention);
        foreach (var expired in _handledNotificationIdentities
                     .Where(item => now - item.Value > NotificationIdentityRetention)
                     .Select(item => item.Key).ToArray())
            _handledNotificationIdentities.Remove(expired);
        foreach (var n in batch.Where(n => PassesNotificationFilter(n.App) && NotificationContent.HasVisibleContent(n)))
        {
            if (_dismissedNotifications.Contains(n)) continue;
            var sourceIdentity = GetNotificationSourceIdentity(n);
            if (sourceIdentity is not null && _handledNotificationIdentities.ContainsKey(sourceIdentity))
                continue;
            if (_recentAcceptedNotifications.Any(item => NotificationContent.SameMessage(
                    item.Message.AppId, item.Message.App, item.Message.Title, item.Message.Body,
                    n.AppId, n.App, n.Title, n.Body)))
                continue;
            // The listener can be restarted while Windows still holds a toast, and some
            // publishers replace a toast with a new system ID. History is the durable
            // record of what we have already presented, including across app restarts.
            if (savedHistory.Any(item => NotificationContent.WasRecentlyPresented(item, n)))
                continue;
            if (acceptedNotifications.Any(item => NotificationContent.SameMessage(item.AppId, item.App, item.Title,
                    item.Body, n.AppId, n.App, n.Title, n.Body)))
                continue;
            acceptedNotifications.Add(n);
            _recentAcceptedNotifications.Add((n, now));
        }
        if (acceptedNotifications.Count == 0) return;
        IReadOnlyList<NotificationHistoryItem> accepted = Settings.NotificationHistoryEnabled
            ? _notificationHistoryService.AddRange(acceptedNotifications)
            : acceptedNotifications.Select(n => new NotificationHistoryItem(Guid.NewGuid(), n.App, n.Title,
                n.Body, n.CreatedAt ?? DateTimeOffset.Now, AppId: n.AppId)).ToArray();
        for (var index = 0; index < accepted.Count; index++)
        {
            var sourceIdentity = GetNotificationSourceIdentity(acceptedNotifications[index]);
            if (sourceIdentity is not null)
                _deliveredNotificationSources.Add(new(accepted[index].Id, sourceIdentity, acceptedNotifications[index], now));
        }
        if (_deliveredNotificationSources.Count > MaxRememberedNotificationIdentities)
            _deliveredNotificationSources.RemoveRange(0,
                _deliveredNotificationSources.Count - MaxRememberedNotificationIdentities);
        _notificationService.TraceAdmission(accepted.Count);
        _bannerAcceptedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _pendingNotifications.Enqueue(accepted);
        RefreshNotificationHistory(); PumpNotifications();
    });

    private void RememberHandledNotifications(NotificationGroup group)
    {
        var historyIds = group.Items.Select(item => item.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var source in _deliveredNotificationSources.Where(item => historyIds.Contains(item.HistoryId)))
            _handledNotificationIdentities[source.Identity] = now;

        if (_handledNotificationIdentities.Count > MaxRememberedNotificationIdentities)
        {
            foreach (var oldest in _handledNotificationIdentities.OrderBy(item => item.Value)
                         .Take(_handledNotificationIdentities.Count - MaxRememberedNotificationIdentities)
                         .Select(item => item.Key).ToArray())
                _handledNotificationIdentities.Remove(oldest);
        }
    }

    private static string? GetNotificationSourceIdentity(NotificationInfo notification) =>
        notification.Id is uint id && notification.CreatedAt is DateTimeOffset createdAt
            ? $"{notification.AppId}|{id}|{createdAt:O}"
            : null;

    private sealed record DeliveredNotificationSource(Guid HistoryId, string Identity, NotificationInfo Notification, DateTimeOffset AcceptedAt);

    private bool _bannerPresentationReady;
    private bool _ordinaryBannerPaused;
    private long _bannerAcceptedAt;
    public double BannerLatencyMilliseconds => _bannerAcceptedAt == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(_bannerAcceptedAt).TotalMilliseconds;
    public void SetBannerPresentationReady() { _bannerPresentationReady = true; PumpNotifications(); RaisePropertyChanged(nameof(ShowBanner)); }
    private void PumpNotifications()
    {
        var contentChanged = false;
        var now = DateTimeOffset.Now;
        var elapsed = Math.Clamp((now - _lastBannerTick).TotalSeconds, 0, 2);
        _lastBannerTick = now;
        if (!Settings.ShowNotifications) { _pendingNotifications.Clear(); _currentGroup = null; _notification = null; }
        else
        {
            var paused = IsLockBannerActive || SuppressOrdinaryBanners || FocusModeEnabled;
            if (_currentGroup is not null && !paused && !_ordinaryBannerPaused) _bannerSeconds -= elapsed;
            _ordinaryBannerPaused = paused;
            if (_currentGroup is not null && _bannerSeconds <= 0) { _currentGroup = null; _notification = null; _currentNotificationHistoryItem = null; }
            if (_bannerPresentationReady && !IsLockBannerActive && !DeferOrdinaryBanners && !FocusModeEnabled &&
                _currentGroup is null && _pendingNotifications.Take(false) is { } group)
            {
                _currentGroup = group; _currentNotificationHistoryItem = group.Latest;
                _notification = new(group.Summary ? "Notifications" : group.Latest.App, group.Title,
                    group.Summary ? "Open to review received notifications." : group.Latest.Body, CreatedAt: group.Latest.CreatedAt, AppId: group.Latest.AppId);
                _bannerSeconds = 6; _notificationSeq++; _bannerSeq++; contentChanged = true;
            }
        }
        if (contentChanged || _lastNotificationVisible != ShowNotification)
            RaiseMany(nameof(ShowNotification), nameof(NotificationApp), nameof(NotificationTitle), nameof(NotificationBody),
                nameof(ShowBanner), nameof(IsAirPodsBannerActive), nameof(BannerApp), nameof(BannerTitle), nameof(BannerBody));
        if (contentChanged) RaiseMany(nameof(NotificationSeq), nameof(BannerSeq));
        _lastNotificationVisible = ShowNotification;
    }
    public string CalendarStatusText => _calendarService.Status.Message;
    public string NotificationStatusText => _notificationService.Status.Message;
    public string ClipboardStatusText => _clipboardIntegration?.Status.Message ?? "Disabled";
    private ClipboardService? _clipboardIntegration;
    public void AttachClipboard(ClipboardService clipboard) { _clipboardIntegration = clipboard; clipboard.StatusChanged += OnIntegrationStatusChanged; }
    public ICommand RetryCalendarCommand => new RelayCommand(() => { if (Settings.ShowNextMeeting) _ = _calendarService.RetryAsync(); });
    public ICommand RetryNotificationsCommand => new RelayCommand(() => { if (Settings.ShowNotifications) _ = _notificationService.RetryAsync(); });
    public ICommand RetryClipboardCommand => new RelayCommand(() => { if (Settings.ShowClipboard && _clipboardIntegration is not null) _ = _clipboardIntegration.GetRecentTextAsync(); });
    public ICommand CalendarSettingsCommand => new RelayCommand(() => LaunchApp("ms-settings:privacy-calendar"));
    public ICommand NotificationSettingsCommand => new RelayCommand(() => LaunchApp("ms-settings:privacy-notifications"));
    public ICommand ClipboardSettingsCommand => new RelayCommand(() => LaunchApp("ms-settings:clipboard"));
    private void OnIntegrationStatusChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        RaiseMany(nameof(CalendarStatusText), nameof(NotificationStatusText), nameof(ClipboardStatusText), nameof(ShowNextMeeting), nameof(MeetingTitle), nameof(MeetingWhen), nameof(HasMeetingJoin), nameof(ShowWidgetsPanel));
        RaiseLiveWidgetLayoutProperties();
    });
}
