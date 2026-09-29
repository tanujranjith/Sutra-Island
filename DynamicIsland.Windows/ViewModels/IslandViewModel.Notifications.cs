using System.Windows.Input;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class IslandViewModel
{
    private readonly NotificationQueue _pendingNotifications = new();
    private readonly List<(NotificationInfo Message, DateTimeOffset AcceptedAt)> _recentAcceptedNotifications = [];
    private static readonly TimeSpan NotificationRepeatWindow = TimeSpan.FromMinutes(2);
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

    private void ClearCurrentNotificationBanner()
    {
        if (_currentGroup is { } group) _pendingNotifications.RemoveMatching(group.Items);
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
        var accepted = new List<NotificationHistoryItem>();
        var now = DateTimeOffset.UtcNow;
        _recentAcceptedNotifications.RemoveAll(item => now - item.AcceptedAt > NotificationRepeatWindow);
        foreach (var n in batch.Where(n => PassesNotificationFilter(n.App) && NotificationContent.HasVisibleContent(n)))
        {
            if (_recentAcceptedNotifications.Any(item => NotificationContent.SameMessage(
                    item.Message.AppId, item.Message.App, item.Message.Title, item.Message.Body,
                    n.AppId, n.App, n.Title, n.Body)))
                continue;
            // The listener can be restarted while Windows still holds a toast, and some
            // publishers replace a toast with a new system ID. History is the durable
            // record of what we have already presented, including across app restarts.
            if (_notificationHistoryService.Items.Any(item => NotificationContent.WasRecentlyPresented(item, n)))
                continue;
            if (accepted.Any(item => NotificationContent.SameMessage(item.AppId, item.App, item.Title,
                    item.Body, n.AppId, n.App, n.Title, n.Body)))
                continue;
            accepted.Add(Settings.NotificationHistoryEnabled
                ? _notificationHistoryService.Add(n.App, n.Title, n.Body, n.CreatedAt, n.AppId)
                : new(Guid.NewGuid(), n.App, n.Title, n.Body, n.CreatedAt ?? DateTimeOffset.Now, AppId: n.AppId));
            _recentAcceptedNotifications.Add((n, now));
        }
        if (accepted.Count == 0) return;
        _pendingNotifications.Enqueue(accepted);
        RefreshNotificationHistory(); PumpNotifications();
    });
    private void PumpNotifications()
    {
        var contentChanged = false;
        var now = DateTimeOffset.Now;
        var elapsed = Math.Clamp((now - _lastBannerTick).TotalSeconds, 0, 2);
        _lastBannerTick = now;
        if (!Settings.ShowNotifications) { _pendingNotifications.Clear(); _currentGroup = null; _notification = null; }
        else
        {
            if (_currentGroup is not null) _bannerSeconds -= elapsed;
            if (_currentGroup is not null && _bannerSeconds <= 0) { _currentGroup = null; _notification = null; _currentNotificationHistoryItem = null; }
            if (!DeferOrdinaryBanners && !FocusModeEnabled &&
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
