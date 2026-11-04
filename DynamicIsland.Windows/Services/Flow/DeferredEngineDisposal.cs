namespace DynamicIsland.Windows.Services.Flow;

/// <summary>Runs native-engine cleanup only after the active operation has left its gate.</summary>
public static class DeferredEngineDisposal
{
    public static async Task RunAsync(SemaphoreSlim gate, Action dispose)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { dispose(); }
        finally { gate.Release(); }
    }
}
