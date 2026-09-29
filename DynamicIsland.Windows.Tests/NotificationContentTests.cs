using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class NotificationContentTests
{
    [Fact]
    public void BlankNotification_HasNoVisibleContent()
    {
        var notification = new NotificationInfo("", "", "", 1, DateTimeOffset.Now, "");

        Assert.False(NotificationContent.HasVisibleContent(notification));
    }

    [Fact]
    public void InvisibleUnicodeText_IsNotVisibleContent()
    {
        var notification = new NotificationInfo("\u200B", "\uFE0F", "\u2060", 1, DateTimeOffset.Now, "");

        Assert.False(NotificationContent.HasVisibleContent(notification));
    }

    [Theory]
    [InlineData("", "Title", "")]
    [InlineData("", "", "Body")]
    public void AnyVisibleText_HasVisibleContent(string app, string title, string body)
    {
        var notification = new NotificationInfo(app, title, body, 1, DateTimeOffset.Now, "");

        Assert.True(NotificationContent.HasVisibleContent(notification));
    }

    [Fact]
    public void AppNameAlone_DoesNotCreateAnEmptyBanner()
    {
        Assert.False(NotificationContent.HasVisibleContent(new NotificationInfo("Mail", "", "")));
    }

    [Fact]
    public void SameMessage_IgnoresWhitespaceAndCaseButKeepsDifferentContent()
    {
        Assert.True(NotificationContent.SameMessage("mail.app", "Mail", "New  message", "Hello\nthere",
            "mail.app", "Mail", "new message", "Hello there"));
        Assert.False(NotificationContent.SameMessage("mail.app", "Mail", "New message", "Hello there",
            "mail.app", "Mail", "New message", "Another message"));
    }

    [Fact]
    public void RecentPresentationSuppressesRecreatedToastButAllowsLaterMessage()
    {
        var created = DateTimeOffset.UtcNow;
        var presented = new NotificationHistoryItem(Guid.NewGuid(), "Mail", "Update", "Ready", created, AppId: "mail.app");
        Assert.True(NotificationContent.WasRecentlyPresented(presented,
            new NotificationInfo("Mail", "Update", "Ready", 2, created.AddSeconds(40), "mail.app")));
        Assert.False(NotificationContent.WasRecentlyPresented(presented,
            new NotificationInfo("Mail", "Update", "Ready", 3, created.AddMinutes(3), "mail.app")));
    }

    [Fact]
    public void LongText_IsTruncatedWithEllipsis()
    {
        var truncated = NotificationContent.Truncate(new string('a', 50) + " " + new string('b', 50), 40);

        Assert.EndsWith("…", truncated);
        Assert.True(truncated.Length <= 40);
    }
}
