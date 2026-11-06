using System.Text;
using System.Text.Json;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WakeTrainingRobustnessTests
{
    private sealed record SpeechFixture(float[] Audio, int Rate);
    private static readonly SpeechFixture SpokenPhrase = ReadSyntheticSpeech();

    [Fact]
    public void SpokenPhraseWithTempoTimingAndMicrophoneVariationsCanEnrollAndMatch()
    {
        var examples = GoodExamples();
        var durations = new[] { .94, 1.02, 1.09, .97, 1.04 }.Select(speed => SpokenPhrase.Audio.Length / (double)SpokenPhrase.Rate / speed).ToArray();
        var profile = WakeDetectionPolicy.CreateProfile(examples, durations);
        var heldOut = Features(VariedSpeech(1.01, .035, .72, 28));
        Assert.True(profile.IsValid);
        Assert.Equal(2, profile.Version);
        Assert.True(WakeDetectionPolicy.Matches(profile, heldOut, SpokenPhrase.Audio.Length / (double)SpokenPhrase.Rate / 1.01));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoisyOutlierAtStartOrMiddleRetainsGoodSamplesAndOnlyNeedsOneRetake(int outlierAt)
    {
        var examples = GoodExamples();
        var noise = Features(BroadbandNoise(SpokenPhrase.Audio.Length));
        var training = new WakeTrainingSession();
        var offered = examples.Take(4).ToList();
        offered.Insert(outlierAt, noise);
        foreach (var example in offered) training.Add(example, 1.5);
        Assert.Equal(4, training.AcceptedCount);
        Assert.False(training.IsComplete);
        Assert.False(training.TryCreateProfile(out _));

        var replacement = training.Add(examples[4], 1.5);
        Assert.True(replacement.SampleAccepted);
        Assert.True(replacement.IsComplete);
        Assert.Equal(5, replacement.AcceptedCount);
        Assert.True(training.TryCreateProfile(out var profile));
        Assert.NotNull(profile);
        Assert.True(WakeDetectionPolicy.Matches(profile, Features(VariedSpeech(1.01, .035, .72, 28)), 1.5));
        Assert.False(WakeDetectionPolicy.Matches(profile, noise, 1.5));
        Assert.InRange(profile.MaximumDistance, .10, .30);
    }

    [Fact]
    public void RoomNoiseReproducesLegacyBatchFailureWithoutWideningEnrollmentLimit()
    {
        var clean = Features(SpokenPhrase.Audio);
        var roomNoise = Features(AddNoise(SpokenPhrase.Audio, 15));
        // The old fixed-sixteen DTW metric rejected this same spoken phrase at
        // a modest 15 dB signal-to-noise ratio, discarding all five examples.
        Assert.True(LegacyDistance(clean, roomNoise) > .24);
        Assert.Equal(.24, WakeDetectionPolicy.MaximumEnrollmentDistance);

        var training = new WakeTrainingSession();
        training.Add(roomNoise, 1.5);
        foreach (var example in GoodExamples()) training.Add(example, 1.5);
        Assert.True(training.TryCreateProfile(out var profile));
        Assert.NotNull(profile);
        Assert.False(WakeDetectionPolicy.Matches(profile, Features(BroadbandNoise(SpokenPhrase.Audio.Length)), 1.5));
        Assert.InRange(profile.MaximumDistance, .10, .30);
    }

    [Fact]
    public void LegacyProfileJsonKeepsItsOriginalDistanceGate()
    {
        var clean = Features(SpokenPhrase.Audio);
        var noisy = Features(AddNoise(SpokenPhrase.Audio, 15));
        var oldDistance = LegacyDistance(clean, noisy);
        var newDistance = WakeDetectionPolicy.Distance(clean, noisy);
        Assert.True(oldDistance > newDistance + .005);
        var threshold = (oldDistance + newDistance) / 2;
        Assert.InRange(threshold, .08, .32);
        var original = new WakeVoiceProfile
        {
            Version = 1, SampleCount = 5, Templates = Enumerable.Range(0, 5).Select(_ => clean.ToArray()).ToArray(),
            MinimumDurationSeconds = .35, MaximumDurationSeconds = 3, MaximumDistance = threshold
        };
        var reloaded = JsonSerializer.Deserialize<WakeVoiceProfile>(JsonSerializer.Serialize(original))!;
        Assert.True(reloaded.IsValid);
        Assert.Equal(1, reloaded.Version);
        Assert.False(WakeDetectionPolicy.Matches(reloaded, noisy, 1.5));
        reloaded.Version = 2;
        Assert.True(WakeDetectionPolicy.Matches(reloaded, noisy, 1.5));
    }

    [Fact]
    public void SilenceAndInvalidAudioCannotReplaceAnAcceptedExample()
    {
        var training = new WakeTrainingSession();
        training.Add(GoodExamples()[0], 1.5);
        Assert.False(training.Add(new double[192], 1.5).SampleAccepted);
        Assert.False(training.Add(Enumerable.Repeat(double.NaN, 192).ToArray(), 1.5).SampleAccepted);
        Assert.False(training.Add(GoodExamples()[1], .1).SampleAccepted);
        Assert.Equal(1, training.AcceptedCount);
        Assert.Null(WakeDetectionPolicy.ExtractFeatures(new byte[SpokenPhrase.Rate * 2], SpokenPhrase.Rate, 16, 1));
    }

    private static double[][] GoodExamples() =>
    [
        Features(VariedSpeech(.94, -.055, .8, 35)),
        Features(VariedSpeech(1.02, .025, .65, 32)),
        Features(VariedSpeech(1.09, -.02, .9, 30)),
        Features(VariedSpeech(.97, .055, .7, 33)),
        Features(VariedSpeech(1.04, -.035, .75, 31))
    ];

    private static float[] VariedSpeech(double speed, double timingShift, double gain, double snr)
    {
        var source = SpokenPhrase.Audio;
        var output = new float[(int)Math.Round(source.Length / speed)];
        // Change relative phoneme timing as well as whole-phrase speed. The two
        // halves use different rates, unlike the old tests' scaled identical tone.
        var split = .35 + timingShift;
        for (var i = 0; i < output.Length; i++)
        {
            var t = i / (double)output.Length;
            var normalized = t < split ? t / split * .35 : .35 + (t - split) / (1 - split) * .65;
            var position = normalized * (source.Length - 1);
            var left = Math.Clamp((int)position, 0, source.Length - 1);
            var right = Math.Min(left + 1, source.Length - 1);
            var value = source[left] + (source[right] - source[left]) * (position - left);
            // A small one-pole room/microphone coloration, with no clipping.
            output[i] = (float)(gain * value * .9 + (i > 0 ? output[i - 1] : 0) * .1);
        }
        return AddNoise(output, snr);
    }

    private static float[] AddNoise(float[] source, double snr)
    {
        var rms = Math.Sqrt(source.Average(x => x * (double)x));
        var amplitude = rms / Math.Pow(10, snr / 20) * Math.Sqrt(3);
        var random = new Random(113);
        return source.Select(sample => (float)Math.Clamp(sample + (random.NextDouble() * 2 - 1) * amplitude, -.99, .99)).ToArray();
    }

    private static float[] BroadbandNoise(int samples)
    {
        var random = new Random(831);
        return Enumerable.Range(0, samples).Select(_ => (float)((random.NextDouble() * 2 - 1) * .25)).ToArray();
    }

    private static double[] Features(float[] audio)
    {
        var pcm = new byte[audio.Length * 2];
        for (var i = 0; i < audio.Length; i++)
        {
            var sample = (short)Math.Clamp((int)Math.Round(audio[i] * 32767), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)sample;
            pcm[i * 2 + 1] = (byte)(sample >> 8);
        }
        return WakeDetectionPolicy.ExtractFeatures(pcm, SpokenPhrase.Rate, 16, 1)
            ?? throw new InvalidOperationException("Synthetic speech fixture did not produce valid acoustic features.");
    }

    private static SpeechFixture ReadSyntheticSpeech()
    {
        using var stream = typeof(WakeTrainingRobustnessTests).Assembly.GetManifestResourceStream("WakeTraining.SyntheticHeySutra.wav")!;
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        reader.ReadInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        var rate = 0;
        byte[]? pcm = null;
        while (stream.Position < stream.Length - 8)
        {
            var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            var next = stream.Position + size + size % 2;
            if (chunk == "fmt ")
            {
                Assert.Equal(1, reader.ReadInt16()); // PCM
                Assert.Equal(1, reader.ReadInt16()); // mono
                rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadInt16();
                Assert.Equal(16, reader.ReadInt16());
            }
            else if (chunk == "data") pcm = reader.ReadBytes(size);
            stream.Position = next;
        }
        Assert.Equal(22050, rate);
        Assert.NotNull(pcm);
        var audio = new float[pcm.Length / 2];
        for (var i = 0; i < audio.Length; i++) audio[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        return new(audio, rate);
    }

    private static double LegacyDistance(double[] first, double[] second)
    {
        var previous = Enumerable.Repeat(double.PositiveInfinity, 17).ToArray();
        previous[0] = 0;
        for (var i = 1; i <= 16; i++)
        {
            var current = Enumerable.Repeat(double.PositiveInfinity, 17).ToArray();
            for (var j = Math.Max(1, i - 3); j <= Math.Min(16, i + 3); j++)
            {
                double dot = 0, aNorm = 0, bNorm = 0;
                for (var k = 0; k < 12; k++)
                {
                    var a = first[(i - 1) * 12 + k]; var b = second[(j - 1) * 12 + k];
                    dot += a * b; aNorm += a * a; bNorm += b * b;
                }
                var cost = 1 - Math.Clamp(dot / Math.Sqrt(Math.Max(1e-12, aNorm * bNorm)), -1, 1);
                current[j] = cost + Math.Min(previous[j - 1], Math.Min(previous[j], current[j - 1]));
            }
            previous = current;
        }
        return previous[16] / 16;
    }
}
