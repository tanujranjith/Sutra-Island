using System.Text.Json;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class NotificationHistoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"notification-history-tests-{Guid.NewGuid():N}");
    private readonly LoggingService _log = new();

    public NotificationHistoryServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task AddRange_PersistsTheSameOrderAsRepeatedAddCalls()
    {
        var notifications = Enumerable.Range(0, 4)
            .Select(index => new NotificationInfo("Mail", $"Message {index}", $"Body {index}",
                CreatedAt: DateTimeOffset.UtcNow.AddSeconds(index), AppId: "mail.app"))
            .ToArray();
        var repeated = new NotificationHistoryService(_log, Path.Combine(_directory, "repeated.json"));
        foreach (var notification in notifications)
            repeated.Add(notification.App, notification.Title, notification.Body, notification.CreatedAt, notification.AppId);
        await repeated.FlushAsync();

        var batched = new NotificationHistoryService(_log, Path.Combine(_directory, "batched.json"));
        var returned = batched.AddRange(notifications);
        await batched.FlushAsync();

        Assert.Equal(Signatures(repeated.Items), Signatures(batched.Items));
        Assert.Equal(notifications.Select(item => item.Title), returned.Select(item => item.Title));
        var persisted = ReadItems(Path.Combine(_directory, "batched.json"));
        Assert.Equal(Signatures(batched.Items), Signatures(persisted));
    }

    [Fact]
    public async Task AddRange_UsesTheSameNewestHundredItemLimit()
    {
        var service = new NotificationHistoryService(_log, Path.Combine(_directory, "bounded.json"));
        var notifications = Enumerable.Range(0, 150)
            .Select(index => new NotificationInfo("App", $"Message {index}", "Body",
                CreatedAt: DateTimeOffset.UtcNow, AppId: "app"))
            .ToArray();

        service.AddRange(notifications);
        await service.FlushAsync();

        Assert.Equal(100, service.Items.Count);
        Assert.Equal("Message 149", service.Items[0].Title);
        Assert.Equal("Message 50", service.Items[^1].Title);
        Assert.Equal(Signatures(service.Items), Signatures(ReadItems(Path.Combine(_directory, "bounded.json"))));
    }

    [Fact]
    public async Task ConcurrentAddsAndFlushes_PersistEveryAcceptedItem()
    {
        const int workerCount = 4;
        const int itemsPerWorker = 12;
        var service = new NotificationHistoryService(_log, Path.Combine(_directory, "concurrent.json"));

        var workers = Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
        {
            for (var index = 0; index < itemsPerWorker; index++)
            {
                service.Add($"App {worker}", $"Message {worker}-{index}", "Body", DateTimeOffset.UtcNow, $"app.{worker}");
                if (index % 3 == 0) await service.FlushAsync();
            }
        }));
        await Task.WhenAll(workers);
        await service.FlushAsync();

        var persisted = ReadItems(Path.Combine(_directory, "concurrent.json"));
        Assert.Equal(workerCount * itemsPerWorker, persisted.Count);
        Assert.Equal(workerCount * itemsPerWorker, persisted.Select(item => item.Id).Distinct().Count());
        Assert.Equal(Signatures(service.Items), Signatures(persisted));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static List<NotificationHistoryItem> ReadItems(string path) =>
        JsonSerializer.Deserialize<List<NotificationHistoryItem>>(File.ReadAllText(path)) ?? [];

    private static (string App, string Title, string Body, DateTimeOffset CreatedAt, string AppId)[] Signatures(
        IEnumerable<NotificationHistoryItem> items) => items
        .Select(item => (item.App, item.Title, item.Body, item.CreatedAt, item.AppId))
        .ToArray();
}
