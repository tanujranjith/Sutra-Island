using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CoalescingSeekQueueTests
{
    private sealed class Player
    {
        public SeekContext State = new(new object(), "track", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240), true);
        public readonly List<double> Writes = [];
        public bool Accept = true;
        public TaskCompletionSource? Hold;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InFlight, MaximumInFlight;
        public CoalescingSeekQueue Queue(CancellationToken token = default) => new(() => State, async (context,target,cancellation) =>
        {
            InFlight++; MaximumInFlight = Math.Max(MaximumInFlight,InFlight); Writes.Add(target.TotalSeconds); Started.TrySetResult();
            try
            {
                if (Hold is { } held) { Hold = null; await held.Task.WaitAsync(cancellation); }
                if (!Accept || !context.SameTrack(State)) return false;
                State = State with { Position = target }; return true;
            }
            finally { InFlight--; }
        }, token, TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task TenRapidThreeSecondBackClicksProduceOneThirtySecondRewind()
    {
        var player = new Player(); var queue = player.Queue();
        var clicks = Enumerable.Range(0,10).Select(_ => queue.RelativeAsync(TimeSpan.FromSeconds(-3))).ToArray();
        Assert.All(await Task.WhenAll(clicks).WaitAsync(TimeSpan.FromSeconds(3)), accepted => Assert.True(accepted));
        Assert.Equal(new double[] {90},player.Writes); Assert.Equal(90,player.State.Position.TotalSeconds);
    }

    [Fact]
    public async Task MoreClicksDuringAnInflightWriteUseItsAcceptedTargetAndNeverOverlap()
    {
        var player = new Player(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); player.Hold = release;
        var queue = player.Queue(); var first = queue.RelativeAsync(TimeSpan.FromSeconds(-3));
        await player.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var more = Enumerable.Range(0,9).Select(_ => queue.RelativeAsync(TimeSpan.FromSeconds(-3))).ToArray();
        Assert.Single(player.Writes); release.SetResult();
        await Task.WhenAll(more.Append(first)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new double[] {117,90},player.Writes); Assert.Equal(1,player.MaximumInFlight);
    }

    [Fact]
    public async Task MixedDirectionsAndSliderChangesRetainInputOrderAndClampEachStep()
    {
        var player = new Player(); var queue = player.Queue();
        var requests = new[] { queue.RelativeAsync(TimeSpan.FromSeconds(-500)), queue.RelativeAsync(TimeSpan.FromSeconds(3)),
            queue.FractionAsync(.5), queue.RelativeAsync(TimeSpan.FromSeconds(10)) };
        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(new double[] {130},player.Writes);
        var edge = new[] {queue.RelativeAsync(TimeSpan.FromSeconds(-500)),queue.RelativeAsync(TimeSpan.FromSeconds(3))};
        await Task.WhenAll(edge).WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(3,player.State.Position.TotalSeconds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OldClicksNeverSeekANewTrackOrSource(bool changeSource)
    {
        var player = new Player(); var queue = player.Queue(); var old = queue.RelativeAsync(TimeSpan.FromSeconds(-30));
        player.State = player.State with { Source = changeSource ? new object() : player.State.Source, Track = changeSource ? player.State.Track : "next track", Position = TimeSpan.FromSeconds(60) };
        var current = queue.RelativeAsync(TimeSpan.FromSeconds(3));
        Assert.False(await old.WaitAsync(TimeSpan.FromSeconds(3))); Assert.True(await current.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(new double[] {63},player.Writes);
    }

    [Fact]
    public async Task CancelledQueuedRequestDoesNotContributeToTheTarget()
    {
        var player = new Player(); var queue = player.Queue(); using var cancellation = new CancellationTokenSource();
        var cancelled = queue.RelativeAsync(TimeSpan.FromSeconds(-30),cancellation.Token); cancellation.Cancel();
        var current = queue.RelativeAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled); Assert.True(await current.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(new double[] {125},player.Writes);
    }

    [Fact]
    public async Task RejectionDoesNotInventANewPositionAndTheQueueRecovers()
    {
        var player = new Player { Accept = false }; var queue = player.Queue();
        Assert.False(await queue.RelativeAsync(TimeSpan.FromSeconds(-30)).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(120,player.State.Position.TotalSeconds); player.Accept = true;
        Assert.True(await queue.RelativeAsync(TimeSpan.FromSeconds(-3)).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(117,player.State.Position.TotalSeconds);
    }

    [Fact]
    public async Task ShutdownCancelsPendingWritesAndInvalidSourcesNeverWrite()
    {
        var player = new Player(); using var shutdown = new CancellationTokenSource(); var queue = player.Queue(shutdown.Token);
        var pending = queue.RelativeAsync(TimeSpan.FromSeconds(-30)); shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Empty(player.Writes);
        var other = player.Queue(); player.State = player.State with { CanSeek = false };
        Assert.False(await other.RelativeAsync(TimeSpan.FromSeconds(-30))); Assert.Empty(player.Writes);
    }
}
