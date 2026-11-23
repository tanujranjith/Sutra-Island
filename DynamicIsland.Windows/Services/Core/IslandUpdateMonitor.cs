namespace DynamicIsland.Windows.Services;

internal sealed record IslandUpdate(Version Version, Uri DownloadUrl, long Size, byte[] Sha256);

internal sealed class IslandUpdateMonitor(
    string notificationPath,
    Func<CancellationToken, Task<IslandUpdate?>> check,
    LoggingService log)
{
    private Version? _lastNotified;

    internal IslandUpdate? Available { get; private set; }

    internal async Task RunAsync(Action<IslandUpdate?, bool> report, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            while (true)
            {
                var succeeded = await CheckOnceAsync(report, cancellationToken);
                await Task.Delay(succeeded ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(30), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task<bool> CheckOnceAsync(Action<IslandUpdate?, bool> report, CancellationToken cancellationToken)
    {
        try
        {
            var update = await check(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Available = update;
            if (update is null)
            {
                report(null, false);
                return true;
            }

            if (_lastNotified is null)
                _lastNotified = await ReadLastNotifiedAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var shouldNotify = update.Version != _lastNotified;
            report(update, shouldNotify);
            if (shouldNotify)
            {
                _lastNotified = update.Version;
                await RememberNotificationAsync(update.Version, cancellationToken);
            }
            return true;
        }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log.Debug($"Background update check failed: {error.Message}");
            return false;
        }
    }

    private async Task<Version?> ReadLastNotifiedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var saved = await File.ReadAllTextAsync(notificationPath, cancellationToken);
            return Version.TryParse(saved, out var version) ? version : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Debug($"Could not read the last update notification: {error.Message}");
            return null;
        }
    }

    private async Task RememberNotificationAsync(Version version, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(notificationPath, version.ToString(), cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Debug($"Could not save the last update notification: {error.Message}");
        }
    }
}
