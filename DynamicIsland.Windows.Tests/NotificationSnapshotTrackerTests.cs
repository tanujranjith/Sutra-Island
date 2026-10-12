using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class NotificationSnapshotTrackerTests
{
    [Fact]
    public void DismissedToastAndTwoDelayedCopiesStayGoneAcrossListenerReset()
    {
        var clock = new MutableTimeProvider();
        var tracker = new NotificationSnapshotTracker(clock);
        var original = new NotificationInfo("Mail", "New message", "Hello", 42, clock.GetUtcNow(), "mail.app");
        Assert.Single(tracker.Observe([original], replayExisting: true));
        tracker.Dismiss([original]);
        clock.Advance(TimeSpan.FromMinutes(3));
        tracker.Reset();
        Assert.Empty(tracker.Observe([original with { Body = "Hello (updated)" }], replayExisting: true));
        Assert.Empty(tracker.Observe([]));
        Assert.Empty(tracker.Observe([original with { Id = 43, CreatedAt = original.CreatedAt!.Value.AddSeconds(5) }]));
        Assert.Empty(tracker.Observe([original with { Id = 44, CreatedAt = original.CreatedAt!.Value.AddSeconds(10) }]));
        Assert.Single(tracker.Observe([original with { Id = 45, CreatedAt = clock.GetUtcNow() }]));
    }

    [Fact]
    public void DismissalAllowsDifferentMessagesAndOtherApps()
    {
        var tracker = new NotificationDismissalTracker();
        var original = new NotificationInfo("Mail", "Update", "Ready", 42, DateTimeOffset.UtcNow, "mail.app");
        tracker.Remember([original]);
        Assert.True(tracker.Contains(original with { AppId = "", Body = "Updated content" }));
        Assert.False(tracker.Contains(original with { Id = 43, Body = "Different message" }));
        Assert.False(tracker.Contains(original with { Id = 44, AppId = "chat.app", App = "Chat" }));
        Assert.False(tracker.Contains(original with { CreatedAt = original.CreatedAt!.Value.AddMinutes(3) }));
    }

    [Fact]
    public void ReappearingSnapshotItemIsNotDeliveredTwice()
    {
        var tracker = new NotificationSnapshotTracker();
        var notification = new NotificationInfo("Mail", "New message", "Hello", 42,
            DateTimeOffset.UtcNow, "mail.app");

        Assert.Empty(tracker.Observe([notification])); // Existing toasts establish the baseline.
        Assert.Empty(tracker.Observe([]));             // Windows briefly omits the toast.
        Assert.Empty(tracker.Observe([notification])); // Its return is not a second arrival.
    }

    [Fact]
    public void RecreatedToastWithSameContentIsSuppressedUntilWindowExpires()
    {
        var clock = new MutableTimeProvider();
        var tracker = new NotificationSnapshotTracker(clock);
        var created = clock.GetUtcNow();

        Assert.Empty(tracker.Observe([]));
        Assert.Single(tracker.Observe([new NotificationInfo("Mail", "New message", "Hello", 42, created, "mail.app")]));

        clock.Advance(TimeSpan.FromSeconds(35));
        Assert.Empty(tracker.Observe([new NotificationInfo("Mail", "New message", "Hello", 43,
            created.AddSeconds(35), "mail.app")]));

        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));
        Assert.Single(tracker.Observe([new NotificationInfo("Mail", "New message", "Hello", 44,
            clock.GetUtcNow(), "mail.app")]));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
