using System.Text.Json;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WakeDetectionPolicyTests
{
    [Fact]
    public void OnlyTheWholeConfidentPhraseCanWake()
    {
        Assert.True(WakeDetectionPolicy.IsPhrase("Hey Sutra", .92, [.85, .90], 1.1));
        Assert.False(WakeDetectionPolicy.IsPhrase("Hey Sutra", .70, [.9, .9], 1.1));
        Assert.False(WakeDetectionPolicy.IsPhrase("Hey Sutra", .96, [.3, .98], 1.1));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey", .99, [.99], .7));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra please", .99, [.99, .99, .99], 1.6));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra", .99, [.99, .99], .1));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra", .99, [.99, .99], 6));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra", double.NaN, [.99, .99], 1));
    }

    [Fact]
    public void ConfidenceCustomizationStillRequiresBothWords()
    {
        Assert.True(WakeDetectionPolicy.IsPhrase("hey sutra", .84, [.75, .80], 1, .80));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra", .94, [.84, .90], 1, .97));
        Assert.False(WakeDetectionPolicy.IsPhrase("hey sutra", .99, [.70, .98], 1, .97));
    }

    [Fact]
    public void TrainingMatchesSimilarExamplesAndRejectsDifferentAcoustics()
    {
        var examples = Enumerable.Range(0, 5).Select(i => Template(.01 * i)).ToArray();
        var profile = WakeDetectionPolicy.CreateProfile(examples, [.8, .9, .85, 1, .95]);
        Assert.True(profile.IsValid);
        Assert.True(WakeDetectionPolicy.Matches(profile, Template(.025), .9));
        Assert.False(WakeDetectionPolicy.Matches(profile, Template(0).Select(x => -x).ToArray(), .9));
        Assert.False(WakeDetectionPolicy.Matches(profile, Template(0), .36));
        Assert.False(WakeDetectionPolicy.Matches(profile, Template(0), 2.8));
        var reloaded = JsonSerializer.Deserialize<WakeVoiceProfile>(JsonSerializer.Serialize(profile))!;
        Assert.True(reloaded.IsValid);
        Assert.True(WakeDetectionPolicy.Matches(reloaded, Template(.02), .9));
    }

    [Fact]
    public void InvalidOrInconsistentEnrollmentCannotBecomeAProfile()
    {
        Assert.Throws<InvalidOperationException>(() => WakeDetectionPolicy.CreateProfile([Template(0)], [1]));
        var examples = Enumerable.Range(0, 5).Select(_ => Template(0)).ToArray();
        examples[4] = examples[4].Select(x => -x).ToArray();
        Assert.Throws<InvalidOperationException>(() => WakeDetectionPolicy.CreateProfile(examples, [1, 1, 1, 1, 1]));
        var profile = WakeDetectionPolicy.CreateProfile(Enumerable.Range(0, 5).Select(_ => Template(0)).ToArray(), [1, 1, 1, 1, 1]);
        profile.Templates[0][0] = double.NaN;
        Assert.False(profile.IsValid);
        Assert.False(WakeDetectionPolicy.Matches(profile, Template(0), 1));
    }

    [Fact]
    public void AudioFeaturesRejectSilenceAndSurviveMicrophoneVolumeChanges()
    {
        Assert.Null(WakeDetectionPolicy.ExtractFeatures(new byte[32000], 16000, 16, 1));
        Assert.Null(WakeDetectionPolicy.ExtractFeatures(new byte[32000], 16000, 8, 1));
        var normal = WakeDetectionPolicy.ExtractFeatures(SyntheticPhrase(.35), 16000, 16, 1);
        var quiet = WakeDetectionPolicy.ExtractFeatures(SyntheticPhrase(.12), 16000, 16, 1);
        Assert.NotNull(normal);
        Assert.NotNull(quiet);
        Assert.Equal(192, normal.Length);
        Assert.True(WakeDetectionPolicy.Distance(normal, quiet) < .02);
    }

    [Fact]
    public void EnrolledAcousticExamplesDoNotMatchBroadbandNoise()
    {
        var examples = Enumerable.Range(0, 5).Select(i => WakeDetectionPolicy.ExtractFeatures(SyntheticPhrase(.20 + i * .02), 16000, 16, 1)!).ToArray();
        var profile = WakeDetectionPolicy.CreateProfile(examples, [1, 1, 1, 1, 1]);
        var random = new Random(119);
        var noise = new byte[32000];
        for (var i = 0; i < 16000; i++)
        {
            var value = (short)((random.NextDouble() * 2 - 1) * 8000);
            noise[i * 2] = (byte)value;
            noise[i * 2 + 1] = (byte)(value >> 8);
        }
        var noiseFeatures = WakeDetectionPolicy.ExtractFeatures(noise, 16000, 16, 1);
        Assert.NotNull(noiseFeatures);
        Assert.True(WakeDetectionPolicy.Matches(profile, examples[0], 1));
        Assert.False(WakeDetectionPolicy.Matches(profile, noiseFeatures, 1));
    }

    private static double[] Template(double variation)
    {
        var result = new double[192];
        for (var segment = 0; segment < 16; segment++)
            for (var c = 0; c < 12; c++) result[segment * 12 + c] = Math.Sin(c * .7 + segment * .12 + variation);
        return result;
    }

    private static byte[] SyntheticPhrase(double volume)
    {
        var bytes = new byte[32000];
        for (var i = 0; i < 16000; i++)
        {
            var time = i / 16000d;
            var envelope = Math.Sin(Math.PI * time) * (.7 + .3 * Math.Sin(6 * Math.PI * time));
            var sound = volume * envelope * (Math.Sin(2 * Math.PI * (180 + 20 * time) * time)
                + .4 * Math.Sin(2 * Math.PI * 720 * time) + .2 * Math.Sin(2 * Math.PI * 1900 * time));
            var value = (short)(sound * 32767);
            bytes[i * 2] = (byte)value;
            bytes[i * 2 + 1] = (byte)(value >> 8);
        }
        return bytes;
    }
}
