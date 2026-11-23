using System.Net.Http;
using DynamicIsland.Windows.Services;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class IslandUpdateMonitorTests : IDisposable
{
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"island-update-notification-{Guid.NewGuid():N}.txt");
    private readonly LoggingService _log = new();

    [Fact]
    public async Task DuplicateNotificationsAreSuppressedAcrossChecksAndRestarts()
    {
        IslandUpdate? latest = Release(9);
        var reports = new List<(Version? Version, bool Notify)>();
        void Report(IslandUpdate? update, bool notify) => reports.Add((update?.Version, notify));
        var monitor = new IslandUpdateMonitor(_statePath, _ => Task.FromResult<IslandUpdate?>(latest), _log);

        await monitor.CheckOnceAsync(Report, default);
        await monitor.CheckOnceAsync(Report, default);

        var restarted = new IslandUpdateMonitor(_statePath, _ => Task.FromResult<IslandUpdate?>(latest), _log);
        await restarted.CheckOnceAsync(Report, default);
        latest = Release(10);
        await restarted.CheckOnceAsync(Report, default);
        latest = null;
        await restarted.CheckOnceAsync(Report, default);

        Assert.Equal(
            [(new Version(1, 4, 9, 0), true), (new Version(1, 4, 9, 0), false),
                (new Version(1, 4, 9, 0), false), (new Version(1, 4, 10, 0), true), (null, false)],
            reports);
        Assert.Null(restarted.Available);
    }

    [Fact]
    public async Task FailedCheckCanRetryAndShutdownSuppressesPendingResults()
    {
        using var shutdown = new CancellationTokenSource();
        var release = Release(9);
        var attempt = 0;
        var monitor = new IslandUpdateMonitor(_statePath, _ =>
        {
            if (++attempt == 1) throw new HttpRequestException("Offline");
            if (attempt == 3) shutdown.Cancel();
            return Task.FromResult<IslandUpdate?>(release);
        }, _log);
        var notifications = new List<IslandUpdate?>();

        Assert.False(await monitor.CheckOnceAsync((update, _) => notifications.Add(update), shutdown.Token));
        Assert.True(await monitor.CheckOnceAsync((update, _) => notifications.Add(update), shutdown.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            monitor.CheckOnceAsync((update, _) => notifications.Add(update), shutdown.Token));

        Assert.Equal(release, Assert.Single(notifications));
        Assert.Equal(release, monitor.Available);
    }

    public void Dispose() => File.Delete(_statePath);

    private static IslandUpdate Release(int patch) =>
        new(new Version(1, 4, patch, 0), new Uri("https://example.invalid/DynamicIsland.exe"), 42, new byte[32]);
}
