using System.Globalization;
using System.Text;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Infrastructure;

/// <summary>Guards against blank notification banners.</summary>
public static class NotificationContent
{
    public static bool WasRecentlyPresented(NotificationHistoryItem item, NotificationInfo incoming) =>
        Math.Abs((item.CreatedAt - (incoming.CreatedAt ?? DateTimeOffset.Now)).TotalMinutes) < 2 &&
        SameMessage(item.AppId, item.App, item.Title, item.Body,
            incoming.AppId, incoming.App, incoming.Title, incoming.Body);

    public static bool SameMessage(string leftAppId, string leftApp, string leftTitle, string leftBody,
        string rightAppId, string rightApp, string rightTitle, string rightBody) =>
        string.Equals(Normalize(string.IsNullOrWhiteSpace(leftAppId) ? leftApp : leftAppId),
            Normalize(string.IsNullOrWhiteSpace(rightAppId) ? rightApp : rightAppId), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Normalize(leftTitle), Normalize(rightTitle), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Normalize(leftBody), Normalize(rightBody), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) => string.Join(' ',
        (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // The app name is metadata. A toast with no title or body renders as an empty card.
    public static bool HasVisibleContent(NotificationInfo notification) =>
        HasVisibleText(notification.Title) || HasVisibleText(notification.Body);

    public static bool HasVisibleText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned or
                UnicodeCategory.PrivateUse)
                continue;
            return true;
        }
        return false;
    }

    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            return value ?? string.Empty;

        // Reserve room for the ellipsis, then prefer a word boundary when one is nearby.
        var cutoff = Math.Max(0, maxLength - 1);
        var text = value[..cutoff].TrimEnd();
        var boundary = text.LastIndexOf(' ');
        if (boundary >= cutoff / 2)
            text = text[..boundary];
        return text + "…";
    }
}
