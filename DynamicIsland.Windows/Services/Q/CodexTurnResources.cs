namespace DynamicIsland.Windows.Services.Q;

// Starts immediately after thread/start so failures while preparing an image are
// covered by the same cleanup as a completed or interrupted turn.
internal sealed class CodexTurnResources(Func<Task> releaseThread, string? temporaryDirectory = null) : IAsyncDisposable
{
    private string? _imagePath;
    private bool _disposed;

    public string CreateImagePath()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _imagePath ??= Path.Combine(temporaryDirectory ?? Path.GetTempPath(), $"dynamic-island-codex-{Guid.NewGuid():N}.png");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_imagePath is not null) try { File.Delete(_imagePath); } catch { }
        await releaseThread().ConfigureAwait(false);
    }
}
