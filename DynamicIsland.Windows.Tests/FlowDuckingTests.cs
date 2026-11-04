using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowDuckingTests
{
    private const uint Me = 100;

    [Fact]
    public void LowersOnlyPlayingOtherApps()
    {
        var plan = FlowDucking.Plan(
        [
            new("spotify", 200, false, true, 0.8f),
            new("island", Me, false, true, 1f),
            new("system", 300, true, true, 1f),
            new("paused", 400, false, false, 1f),
            new("silent", 500, false, true, 0f),
        ], Me, 0.2f);
        var only = Assert.Single(plan);
        Assert.Equal("spotify", only.Id);
        Assert.Equal(0.8f, only.Original);
        Assert.Equal(0.16f, only.Lowered, 3);
    }

    [Fact]
    public void MuteLevelGoesToZero() =>
        Assert.Equal(0f, Assert.Single(FlowDucking.Plan([new("a", 2, false, true, 0.5f)], Me, 0f)).Lowered);

    [Fact]
    public void RestoresOnlyWhatTheUserDidNotChange()
    {
        FlowDuckedSession[] ducked = [new("a", 0.8f, 0.16f), new("b", 1f, 0.2f), new("gone", 1f, 0.2f)];
        var current = new Dictionary<string, float> { ["a"] = 0.16f, ["b"] = 0.6f };
        var restore = FlowDucking.Restore(ducked, current);
        Assert.Equal([("a", 0.8f)], restore);
    }
}
