using DynamicIsland.Windows.Models;
namespace DynamicIsland.Windows.Infrastructure;

public sealed class NotificationSnapshotTracker(TimeProvider? clock = null)
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan DuplicateContentWindow = TimeSpan.FromMinutes(2);
    private const int MaxRemembered = 512;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, SeenNotification> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<(string App, string Title, string Body), DateTimeOffset> _recentContent = new();
    private bool _initialized;

    public IReadOnlyList<NotificationInfo> Observe(IEnumerable<NotificationInfo> snapshot, bool replayExisting = false)
    {
        var items = snapshot.DistinctBy(n => n.Identity).OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToArray();
        var now = _clock.GetUtcNow();
        var fresh = new List<NotificationInfo>();

        // Windows can briefly omit a toast from one snapshot and return it in the next one.
        // Keep delivered identities across those gaps so the same toast cannot fire twice.
        foreach (var item in items)
        {
            var content = (string.IsNullOrWhiteSpace(item.AppId) ? item.App.Trim() : item.AppId.Trim(),
                item.Title.Trim(), item.Body.Trim());
            var knownIdentity = _recent.TryGetValue(item.Identity, out var previous);
            var identityContentChanged = knownIdentity && !NotificationContent.SameMessage(
                previous!.AppId, previous.App, previous.Title, previous.Body,
                item.AppId, item.App, item.Title, item.Body);
            var recentlyVisible = _recentContent.TryGetValue(content, out var seenAt) &&
                                  now - seenAt <= DuplicateContentWindow;
            // Windows may reuse a toast ID when its message changes. Treat changed content
            // under a known ID as new, while suppressing identical messages across IDs.
            if ((_initialized || replayExisting) && (!knownIdentity || identityContentChanged) && !recentlyVisible)
                fresh.Add(item);
            _recent[item.Identity] = new(item.App, item.AppId, item.Title, item.Body, now);
            _recentContent[content] = now;
        }
        foreach (var expired in _recent.Where(pair => now - pair.Value.SeenAt > Retention).Select(pair => pair.Key).ToArray())
            _recent.Remove(expired);
        if (_recent.Count > MaxRemembered)
        {
            var active = items.Select(item => item.Identity).ToHashSet(StringComparer.Ordinal);
            var overflow = _recent.Count - MaxRemembered;
            foreach (var oldest in _recent.Where(pair => !active.Contains(pair.Key))
                         .OrderBy(pair => pair.Value.SeenAt).Take(overflow).Select(pair => pair.Key).ToArray())
                _recent.Remove(oldest);
        }
        foreach (var expired in _recentContent.Where(pair => now - pair.Value > DuplicateContentWindow)
                     .Select(pair => pair.Key).ToArray())
            _recentContent.Remove(expired);
        if (_recentContent.Count > MaxRemembered)
        {
            foreach (var oldest in _recentContent.OrderBy(pair => pair.Value)
                         .Take(_recentContent.Count - MaxRemembered).Select(pair => pair.Key).ToArray())
                _recentContent.Remove(oldest);
        }

        _initialized = true;
        return fresh;
    }

    public void Reset()
    {
        _recent.Clear();
        _recentContent.Clear();
        _initialized = false;
    }

    private sealed record SeenNotification(string App, string AppId, string Title, string Body, DateTimeOffset SeenAt);
}

public sealed class NotificationQueue(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly List<NotificationGroup> _pending = [];
    public int Count => _pending.Count;
    public void Enqueue(IEnumerable<NotificationHistoryItem> batch)
    {
        foreach (var group in batch.GroupBy(n => string.IsNullOrEmpty(n.AppId) ? n.App : n.AppId))
            _pending.Add(new(group.OrderBy(n => n.CreatedAt).ToArray(), _clock.GetUtcNow()));
        if (_pending.Count > 50) _pending.RemoveRange(0, _pending.Count - 50);
    }
    public NotificationGroup? Take(bool blocked)
    {
        if (blocked || _pending.Count == 0) return null;
        var cutoff = _clock.GetUtcNow().AddSeconds(-30);
        var stale = _pending.Where(g => g.EnqueuedAt < cutoff).ToArray();
        if (stale.Length > 0)
        {
            _pending.RemoveAll(g => g.EnqueuedAt < cutoff);
            var items = stale.SelectMany(g => g.Items).OrderBy(i => i.CreatedAt).ToArray();
            return new(items, _clock.GetUtcNow(), Summary: items.Length > 1);
        }
        var next = _pending[0]; _pending.RemoveAt(0); return next;
    }
    public void Remove(Guid id)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var remaining = _pending[i].Items.Where(n => n.Id != id).ToArray();
            if (remaining.Length == 0) _pending.RemoveAt(i);
            else _pending[i] = _pending[i] with { Items = remaining };
        }
    }
    public void RemoveMatching(IEnumerable<NotificationHistoryItem> shownItems)
    {
        var shown = shownItems.ToArray();
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var remaining = _pending[i].Items.Where(candidate => !shown.Any(item =>
                NotificationContent.SameMessage(candidate.AppId, candidate.App, candidate.Title, candidate.Body,
                    item.AppId, item.App, item.Title, item.Body))).ToArray();
            if (remaining.Length == 0) _pending.RemoveAt(i);
            else _pending[i] = _pending[i] with { Items = remaining };
        }
    }
    public void Clear() => _pending.Clear();
}
