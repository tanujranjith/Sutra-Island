using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class OrderedBackgroundWorkTests
{
    [Fact]
    public async Task SlowWorkDoesNotBlockEnqueueAndFlushIncludesEveryOperation()
    {
        var queue = new OrderedBackgroundWork();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new List<int>();
        var first = queue.Enqueue(async () => { await release.Task; values.Add(0); });
        var tasks = Enumerable.Range(1, 100).Select(i => queue.Enqueue(() =>
        {
            values.Add(i);
            return Task.CompletedTask;
        })).ToArray();
        var flush = queue.FlushAsync();
        Assert.False(first.IsCompleted);
        Assert.False(flush.IsCompleted);
        release.SetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(0, 101), values);
    }

    [Fact]
    public async Task AnOperationFailureDoesNotPreventLaterWork()
    {
        var queue = new OrderedBackgroundWork();
        var failure = queue.Enqueue(() => throw new IOException("fixture"));
        var ran = false;
        var next = queue.Enqueue(() => { ran = true; return Task.CompletedTask; });
        await Assert.ThrowsAsync<IOException>(() => failure);
        await next;
        Assert.True(ran);
    }
}
