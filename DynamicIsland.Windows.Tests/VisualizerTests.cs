using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class VisualizerTests
{
    private const uint SampleRate = 48000;

    private static float[] Sine(int bin, double amplitude, double phase = 0)
    {
        var frequency = (bin * SampleRate) / (double)SpectrumAnalyzer.WindowSize;
        var samples = new float[SpectrumAnalyzer.WindowSize];
        for (var n = 0; n < samples.Length; n++)
            samples[n] = (float)(amplitude * Math.Sin(((2 * Math.PI * frequency * n) / SampleRate) + phase));
        return samples;
    }

    private static float[] Noise(double amplitude, int seed = 7)
    {
        var random = new Random(seed);
        var samples = new float[SpectrumAnalyzer.WindowSize];
        for (var n = 0; n < samples.Length; n++)
            samples[n] = (float)((random.NextDouble() * 2 * amplitude) - amplitude);
        return samples;
    }

    private static double[] Analyze(float[] samples)
    {
        var levels = new double[SpectrumAnalyzer.BandCount];
        SpectrumAnalyzer.Analyze(samples, SampleRate, levels);
        return levels;
    }

    [Fact]
    public void Silence_ProducesNoLevels()
    {
        var levels = Analyze(new float[SpectrumAnalyzer.WindowSize]);
        Assert.All(levels, level => Assert.Equal(0, level));
    }

    [Fact]
    public void BassTone_DrivesBassBandOnly()
    {
        var levels = Analyze(Sine(bin: 8, amplitude: 0.9));

        Assert.True(levels[0] > 0.7, $"Bass level was {levels[0]}");
        for (var band = 1; band < levels.Length; band++)
            Assert.True(levels[band] < 0.25, $"Band {band} leaked: {levels[band]}");
    }

    [Fact]
    public void TrebleTone_DrivesTrebleBandOnly()
    {
        var levels = Analyze(Sine(bin: 936, amplitude: 0.9));

        Assert.True(levels[6] > 0.75, $"Treble level was {levels[6]}");
        Assert.True(levels[0] < 0.2, $"Bass leaked: {levels[0]}");
    }

    [Fact]
    public void QuietTrebleTone_RemainsVisible()
    {
        // This is the reported failure: a quiet high-frequency tone used to fall below the
        // analyzer's minimum and freeze the right-side bars at one identical height.
        var levels = Analyze(Sine(bin: 936, amplitude: 0.01));

        Assert.True(levels[6] > 0.25, $"Quiet treble level was {levels[6]}");
    }

    [Fact]
    public void BroadbandNoise_MovesEveryBand()
    {
        var levels = Analyze(Noise(amplitude: 0.4));

        Assert.All(levels, level => Assert.InRange(level, 0.2, 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 0.3939)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    public void AmplitudeMapping_UsesDecibelScale(double amplitude, double expected)
    {
        Assert.Equal(expected, SpectrumAnalyzer.MapAmplitudeToLevel(amplitude), precision: 4);
    }

    [Fact]
    public void Analyze_ReturnsBroadbandRootMeanSquare()
    {
        var levels = new double[SpectrumAnalyzer.BandCount];
        var rms = SpectrumAnalyzer.Analyze(Sine(bin: 8, amplitude: 0.9), SampleRate, levels);

        Assert.Equal(0.6364, rms, precision: 3);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(0.03, 0.701)]
    [InlineData(0.01, 0.429)]
    [InlineData(0, 0.12)]
    public void LoudnessMapping_RestrainsQuietAudio(double rms, double expected)
    {
        Assert.Equal(expected, VisualizerDynamics.MapLoudnessToGain(rms), precision: 3);
    }

    [Fact]
    public void ConstantTarget_KeepsBreathing()
    {
        var motion = new VisualizerMotion();
        var targets = new double[VisualizerMotion.BandCount];
        Array.Fill(targets, 0.3);
        motion.Advance(targets, 1.0 / 30);

        var initial = motion.Level(6);
        var changed = false;
        for (var frame = 0; frame < 90 && !changed; frame++)
        {
            motion.Advance(targets, 1.0 / 30);
            changed = Math.Abs(motion.Level(6) - initial) > 0.0005;
        }

        Assert.True(changed, "The treble bar did not continue moving for a constant target.");
    }

    [Fact]
    public void TrebleTarget_MovesTrebleBar()
    {
        var motion = new VisualizerMotion();
        motion.Advance(new double[VisualizerMotion.BandCount], 0.1);

        var targets = new double[VisualizerMotion.BandCount];
        targets[6] = 0.8;
        motion.Advance(targets, 0.05);

        Assert.True(motion.Level(6) > motion.Level(0) + 0.1,
            $"Treble {motion.Level(6)} did not separate from bass {motion.Level(0)}");
    }

    [Fact]
    public void ReducedMotion_RemainsStill()
    {
        var motion = new VisualizerMotion();
        var targets = new double[VisualizerMotion.BandCount];
        Array.Fill(targets, 0.3);
        motion.Advance(targets, 1.0 / 30, lively: false);

        var initial = motion.Level(6);
        motion.Advance(targets, 1.0 / 30, lively: false);

        Assert.Equal(initial, motion.Level(6));
    }
}
