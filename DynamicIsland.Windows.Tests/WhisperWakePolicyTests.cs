using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WhisperWakePolicyTests
{
    [Theory]
    [InlineData("Hey Sutra.", "")]
    [InlineData("Hey, Sutra! Pause the music.", "Pause the music.")]
    [InlineData("Hey sutro what's the weather?", "What's the weather?")]
    [InlineData("Hey sue tra, set a timer", "Set a timer")]
    public void RequiresLeadingWakePhraseAndPreservesCommands(string text, string command)
    {
        var result = WhisperWakePolicy.CheckTranscript(text);
        Assert.True(result.Woke);
        Assert.Equal(command, result.Command);
    }

    [Theory]
    [InlineData("Sutra, pause")]
    [InlineData("A sutra, pause")]
    [InlineData("Okay Sutter pause")]
    [InlineData("I was reading a sutra")]
    [InlineData("Hello hey sutra")]
    [InlineData("Hey Sarah")]
    [InlineData("Hey super")]
    [InlineData("hey sutras")]
    public void RejectsLooseNamesAndEmbeddedPhrases(string text) => Assert.False(WhisperWakePolicy.CheckTranscript(text).Woke);

    [Fact]
    public void HallucinatedPhraseOnSilenceOrClickCannotWake()
    {
        Assert.False(WhisperWakePolicy.Check("Hey Sutra", new float[32000], 16000, null, false).Woke);
        var click = new float[32000];
        for (var i = 0; i < 200; i++) click[12000 + i] = .4f;
        Assert.False(WhisperWakePolicy.Check("Hey Sutra", click, 16000, null, false).Woke);
        var invalid = Signal(); invalid[0] = float.NaN;
        Assert.False(WhisperWakePolicy.Check("Hey Sutra", invalid, 16000, null, false).Woke);
    }

    [Fact]
    public void TrainingRequiresPhraseAloneAndMatchesTheRecordedAcoustics()
    {
        var samples = new List<double[]>(); var durations = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(WhisperWakePolicy.TryTrainingSample("Hey Sutra", Signal(.20f + i * .02f), 16000, out var features, out var duration));
            samples.Add(features); durations.Add(duration);
        }
        var profile = WakeDetectionPolicy.CreateProfile(samples, durations);
        Assert.True(WhisperWakePolicy.Check("Hey Sutra", Signal(), 16000, profile, true).Woke);
        Assert.False(WhisperWakePolicy.Check("Hey Sutra pause", Signal(), 16000, profile, true).Woke);
        Assert.False(WhisperWakePolicy.TryTrainingSample("Hey Sutra pause", Signal(), 16000, out _, out _));
        Assert.False(WhisperWakePolicy.Check("Hey Sutra", Signal(), 16000, null, true).Woke);
        Assert.True(WhisperWakePolicy.Check("Hey Sutra pause", Signal(), 16000, null, false).Woke);

        var noise = new float[32000]; var random = new Random(217);
        for (var i = 5000; i < 20000; i++) noise[i] = (float)((random.NextDouble() * 2 - 1) * .3);
        Assert.False(WhisperWakePolicy.Check("Hey Sutra", noise, 16000, profile, true).Woke);
    }

    private static float[] Signal(float amplitude = .3f)
    {
        // One second of voiced audio with the same pre-roll and silence used by the VAD.
        var result = new float[28000];
        for (var i = 0; i < 16000; i++)
        {
            var t = i / 16000d;
            result[4800 + i] = (float)(amplitude * Math.Sin(Math.PI * t) * (Math.Sin(2 * Math.PI * (180 + 20 * t) * t)
                + .4 * Math.Sin(2 * Math.PI * 720 * t) + .2 * Math.Sin(2 * Math.PI * 1900 * t)));
        }
        return result;
    }
}
