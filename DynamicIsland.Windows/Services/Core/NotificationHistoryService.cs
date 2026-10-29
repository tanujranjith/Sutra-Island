using System.Text.Json;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>Small local-only notification history used by the island's optional history popover.</summary>
public sealed class NotificationHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private const int MaxItems = 100;
    private readonly LoggingService _log;
    private readonly string _path;
    private readonly object _itemsGate = new();
    private readonly List<NotificationHistoryItem> _items = [];

    public NotificationHistoryService(LoggingService log)
        : this(log, Path.Combine(DynamicIsland.Windows.Infrastructure.AppDataPaths.Root, "notification-history.json"))
    {
    }

    internal NotificationHistoryService(LoggingService log, string path)
    {
        _log = log;
        _path = path;
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        Load();
    }

    public IReadOnlyList<NotificationHistoryItem> Items
    {
        get { lock (_itemsGate) return _items.ToArray(); }
    }

    public NotificationHistoryItem Add(string app, string title, string body, DateTimeOffset? createdAt = null, string appId = "") =>
        AddRange([new NotificationInfo(app, title, body, CreatedAt: createdAt, AppId: appId)])[0];

    /// <summary>Adds a batch in acceptance order and persists one snapshot for the batch.</summary>
    public IReadOnlyList<NotificationHistoryItem> AddRange(IEnumerable<NotificationInfo> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        var incoming = notifications.ToArray();
        if (incoming.Length == 0) return [];

        lock (_itemsGate)
        {
            var added = new List<NotificationHistoryItem>(incoming.Length);
            foreach (var notification in incoming)
            {
                // Keep the same prune/insert/trim sequence as repeated Add calls, including
                // the treatment of stale timestamps inside a batch.
                PruneLocked();
                var item = new NotificationHistoryItem(Guid.NewGuid(), notification.App,
                    notification.Title, notification.Body, notification.CreatedAt ?? DateTimeOffset.Now,
                    AppId: notification.AppId);
                _items.Insert(0, item);
                TrimLocked();
                added.Add(item);
            }
            SaveLocked();
            return added;
        }
    }

    public void Dismiss(Guid id)
    {
        lock (_itemsGate)
        {
            var index = _items.FindIndex(x => x.Id == id);
            if (index < 0) return;
            _items.RemoveAt(index);
            SaveLocked();
        }
    }

    public void Clear()
    {
        lock (_itemsGate)
        {
            if (_items.Count == 0) return;
            _items.Clear();
            SaveLocked();
        }
    }

    public void PruneAndSave()
    {
        lock (_itemsGate)
        {
            if (PruneLocked()) SaveLocked();
        }
    }

    private bool PruneLocked()
    {
        var cutoff = DateTimeOffset.Now - Retention;
        var before = _items.Count;
        _items.RemoveAll(x => x.CreatedAt < cutoff);
        TrimLocked();
        return before != _items.Count;
    }

    private void TrimLocked()
    {
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<NotificationHistoryItem>>(File.ReadAllText(_path), JsonOptions);
            if (loaded is not null) _items.AddRange(loaded.OrderByDescending(x => x.CreatedAt));
            PruneLocked();
            TrimLocked();
        }
        catch (Exception ex)
        {
            _log.Debug($"Notification history could not be loaded: {ex.Message}");
            _items.Clear();
        }
    }

    private readonly object _saveGate = new();
    private Task _saveTask = Task.CompletedTask;
    public Task FlushAsync()
    {
        lock (_itemsGate)
        lock (_saveGate)
            return _saveTask;
    }
    private void SaveLocked()
    {
        var snapshot = _items.ToArray();
        lock (_saveGate) _saveTask = _saveTask.ContinueWith(_ => WriteSnapshot(snapshot), TaskScheduler.Default);
    }
    private void WriteSnapshot(NotificationHistoryItem[] snapshot)
    {
        try
        {
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) { _log.Debug($"Notification history could not be saved: {ex.Message}"); }
    }
}
