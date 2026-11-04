using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowTextCleanerTests
{
    [Theory]
    [InlineData("um so i think we should uh meet at three", "So I think we should meet at three.")]
    [InlineData("the the demo went well", "The demo went well.")]
    [InlineData("hello comma how are you question mark", "Hello, how are you?")]
    [InlineData("text me when you land new line i'll pick you up", "Text me when you land\nI'll pick you up.")]
    [InlineData("first paragraph here new paragraph second one starts", "First paragraph here\n\nSecond one starts.")]
    [InlineData("bye bye", "Bye bye")]
    [InlineData("the total is fifty dollars scratch that it is sixty dollars", "It is sixty dollars.")]
    [InlineData("ok", "Ok")]
    public void BasicCleanup(string raw, string expected) => Assert.Equal(expected, FlowTextCleaner.Basic(raw));

    [Fact]
    public void BasicKeepsMeaningfulPeriodWord() =>
        Assert.Contains("period of time", FlowTextCleaner.Basic("it was a long period of time ago"));

    [Theory]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" Thank you.", "Thank you.")]
    [InlineData("Hello there (music) friend", "Hello there friend")]
    public void StripsWhisperArtifacts(string raw, string expected) => Assert.Equal(expected, FlowTextCleaner.StripWhisperArtifacts(raw));

    [Theory]
    [InlineData("Thank you.", 0.0, true)]
    [InlineData("you", 0.1, true)]
    [InlineData("Thank you.", 0.9, false)]
    [InlineData("Thank you for the help.", 0.0, false)]
    public void HallucinationNeedsSilentAudio(string text, double voiced, bool expected) =>
        Assert.Equal(expected, FlowTextCleaner.IsLikelyHallucination(text, voiced));

    [Fact]
    public void RejectsAnsweringRewrite() =>
        Assert.False(FlowTextCleaner.LooksSafe("What is the capital of France?", "Sure! The capital of France is Paris, a city known for the Eiffel Tower and great food."));

    [Fact]
    public void AcceptsFaithfulRewrite() =>
        Assert.True(FlowTextCleaner.LooksSafe("Book the room for Monday, no wait, Tuesday.", "Book the room for Tuesday."));

    private const string Dinner = "For dinner I was thinking we could do tomato soup and grilled cheese. Actually no, I was thinking we could do chicken farm zone. Parmesan.";

    [Fact]
    public void AcceptsWholeSentenceSelfCorrection() =>
        Assert.True(FlowTextCleaner.LooksSafe(Dinner, "For dinner I was thinking we could do chicken parmesan."));

    [Fact]
    public void RejectsBigCutWithoutACorrection() =>
        Assert.False(FlowTextCleaner.LooksSafe("We need to finish the report, send the slides to the team, and book the room for the review on Friday.", "Finish the report."));

    [Fact]
    public void RejectsBigCutThatInventsWords() =>
        Assert.False(FlowTextCleaner.LooksSafe(Dinner, "Let's order pizza from the new place downtown."));

    [Theory]
    [InlineData("chicken farm zone.Parmesan.", "chicken farm zone. Parmesan.")]
    [InlineData("Made in the U.S.A today.", "Made in the U.S.A today.")]
    [InlineData("It costs 3.50 dollars.", "It costs 3.50 dollars.")]
    public void SeparatesGluedWhisperSegments(string raw, string expected) =>
        Assert.Equal(expected, FlowTextCleaner.StripWhisperArtifacts(raw));

    [Theory]
    [InlineData("uhhhhhh i dont know maybe tomorrow", "Uhhhhhh I dont know maybe tomorrow.")]
    [InlineData("OOOOOHHhhhhhh that is so cool", "OOOOOHHhhhhhh that is so cool.")]
    [InlineData("um so we should uh meet at three", "Um so we should uh meet at three.")]
    public void ExpressiveKeepsTheSounds(string raw, string expected) =>
        Assert.Equal(expected, FlowTextCleaner.Basic(raw, expressive: true));

    [Fact]
    public void NormalModeStillDropsDrawnOutFillers() =>
        Assert.Equal("I dont know maybe tomorrow.", FlowTextCleaner.Basic("uhhhhhh i dont know maybe tomorrow"));
}
