using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class DeferredEngineDisposalTests
{
    [Fact]
    public async Task CleanupWaitsForActiveEngineOperation()
    {
        using var gate = new SemaphoreSlim(1, 1);
        await gate.WaitAsync();
        var disposed = false;
        var cleanup = DeferredEngineDisposal.RunAsync(gate, () => disposed = true);
        Assert.False(cleanup.IsCompleted);
        Assert.False(disposed);
        gate.Release();
        await cleanup.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(disposed);
    }
    [Fact]
    public async Task CleanupReleasesGateEvenWhenDisposalFails()
    {
        using var gate = new SemaphoreSlim(1, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DeferredEngineDisposal.RunAsync(gate, () => throw new InvalidOperationException("fixture")));
        Assert.Equal(1, gate.CurrentCount);
    }
}
