using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class AssistantWakeTests
{
    [Theory]
    [InlineData("Hey Sutra.", "")]
    [InlineData("hey sutro", "")]
    [InlineData("Hey, Sutra! Unpause the music.", "Unpause the music.")]
    [InlineData("Hey sue tra what's the weather", "What's the weather")]
    [InlineData("A Sutra, skip this song.", "Skip this song.")]
    [InlineData("Hi Shutra", "")]
    [InlineData("Sutra, pause.", "Pause.")]
    [InlineData("Okay Sutter set a timer", "Set a timer")]
    public void HearsWakeWordAndKeepsTheCommand(string transcript, string command)
    {
        var match = WakePhrase.Check(transcript);
        Assert.True(match.Woke);
        Assert.Equal(command, match.Command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I was reading a sutra yesterday")]
    [InlineData("Hey Sarah")]
    [InlineData("Hey, how are you?")]
    [InlineData("Thank you.")]
    [InlineData("hey super")]
    public void IgnoresOtherSpeech(string transcript) => Assert.False(WakePhrase.Check(transcript).Woke);

    [Fact]
    public void StrictNeedsAGreeting()
    {
        Assert.False(WakePhrase.Check("Sutra, pause.", strict: true).Woke);
        Assert.True(WakePhrase.Check("Hey Sutra, pause.", strict: true).Woke);
    }

    private static float[] Frame(float amplitude, int samples = 480)
    {
        var f = new float[samples];
        for (var i = 0; i < samples; i++) f[i] = (float)(amplitude * Math.Sin(i * 0.3));
        return f;
    }

    [Fact]
    public void SegmenterReturnsUtteranceAfterSilence()
    {
        var seg = new VoiceSegmenter(16000, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(10));
        for (var i = 0; i < 30; i++) Assert.Null(seg.Push(Frame(0.001f)));   // quiet room
        for (var i = 0; i < 30; i++) Assert.Null(seg.Push(Frame(0.2f)));     // ~0.9 s of speech
        Assert.True(seg.InSpeech);
        float[]? result = null;
        for (var i = 0; i < 40 && result is null; i++) result = seg.Push(Frame(0.001f));
        Assert.NotNull(result);
        Assert.True(result!.Length >= 16000 * 0.9 + 16000 * 0.8 - 480 * 3); // speech + tail (+ lead-in)
        Assert.False(seg.InSpeech);
    }

    [Fact]
    public void SegmenterKeepsGoingThroughShortPauses()
    {
        var seg = new VoiceSegmenter(16000, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(10));
        for (var i = 0; i < 20; i++) seg.Push(Frame(0.001f));
        for (var i = 0; i < 15; i++) seg.Push(Frame(0.2f));
        for (var i = 0; i < 10; i++) Assert.Null(seg.Push(Frame(0.001f))); // 0.3 s pause between words
        for (var i = 0; i < 15; i++) Assert.Null(seg.Push(Frame(0.2f)));
        Assert.True(seg.InSpeech);
    }

    [Fact]
    public void SegmenterIgnoresClicks()
    {
        var seg = new VoiceSegmenter(16000, TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(7));
        for (var i = 0; i < 20; i++) seg.Push(Frame(0.001f));
        seg.Push(Frame(0.5f)); seg.Push(Frame(0.5f)); // two loud frames: a click, not speech
        for (var i = 0; i < 40; i++) Assert.Null(seg.Push(Frame(0.001f)));
        Assert.False(seg.InSpeech);
    }

    [Fact]
    public void SegmenterCutsOffAtMaxLength()
    {
        var seg = new VoiceSegmenter(16000, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(2));
        for (var i = 0; i < 20; i++) seg.Push(Frame(0.001f));
        float[]? result = null;
        for (var i = 0; i < 100 && result is null; i++) result = seg.Push(Frame(0.2f));
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("high", 7)]
    [InlineData("normal", 10)]
    [InlineData("low", 14)]
    [InlineData(null, 10)]
    public void SensitivityMapsToThreshold(string? s, double db) =>
        Assert.Equal(db, WakePhrase.SensitivityThreshold(s));
}
