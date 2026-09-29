using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class NotificationSnapshotTrackerTests
{
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
