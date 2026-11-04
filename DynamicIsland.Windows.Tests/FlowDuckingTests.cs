using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowDuckingTests
{
    private const uint Me = 100;

    [Fact]
    public void RecoveryPlanIsPersistedBeforeDucking()
    {
        var directory = Path.Combine(Path.GetTempPath(), "flow-recovery-" + Guid.NewGuid());
        var path = Path.Combine(directory, "ducked.json");
        try
        {
            var plan = new[] { new FlowDuckedSession("music", 0.8f, 0.1f) };
            Assert.True(FlowDucking.TrySaveRecovery(path, plan));
            var restored = System.Text.Json.JsonSerializer.Deserialize<FlowDuckedSession[]>(File.ReadAllText(path));
            Assert.Equal(plan, restored);
            Assert.Single(Directory.GetFiles(directory));
            // An existing file cannot serve as the parent directory; the operation fails closed.
            Assert.False(FlowDucking.TrySaveRecovery(Path.Combine(path, "invalid.json"), plan));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LowersOtherAppsPlayingOrPaused()
    {
        var plan = FlowDucking.Plan(
        [
            new("spotify", 200, false, true, 0.8f),
            new("island", Me, false, true, 1f),
            new("system", 300, true, true, 1f),
            new("paused", 400, false, false, 1f),
            new("silent", 500, false, true, 0f),
        ], Me, 0.2f);
        Assert.Equal(["spotify", "paused"], plan.Select(p => p.Id));
        var only = plan[0];
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

    [Theory]
    [InlineData(100, 1f)]
    [InlineData(50, 0.25f)]
    [InlineData(10, 0.01f)]
    [InlineData(2, 0.0004f)]
    [InlineData(0, 0f)]
    public void PercentFollowsAnAudioTaper(int percent, float gain) => Assert.Equal(gain, FlowDucking.GainFor(percent), 5);

    [Fact]
    public void TwoPercentStillLowersQuietMusic()
    {
        var only = Assert.Single(FlowDucking.Plan([new("a", 2, false, true, 0.3f)], Me, FlowDucking.GainFor(2)));
        Assert.True(only.Lowered < 0.001f);
    }
}
