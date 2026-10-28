namespace DynamicIsland.Windows.Models;

public sealed record NotificationHistoryItem(
    Guid Id,
    string App,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    bool IsDismissed = false,
    string AppId = "");

public sealed record FocusModeState(bool Enabled);

public sealed record AudioOutputDevice(string Id, string Name, bool IsCurrent);

// Only bounded thumbnail pixels live in the palette; original images stay in Windows history.
public sealed record ClipboardEntry(string Id, string Text, bool IsImage = false,
    byte[]? PreviewBgra = null, int PreviewWidth = 0, int PreviewHeight = 0,
    int ImageWidth = 0, int ImageHeight = 0);
