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
    [InlineData(" Thank you.", "")]
    [InlineData("Hello there (music) friend", "Hello there friend")]
    public void StripsWhisperArtifacts(string raw, string expected) => Assert.Equal(expected, FlowTextCleaner.StripWhisperArtifacts(raw));

    [Fact]
    public void RejectsAnsweringRewrite() =>
        Assert.False(FlowTextCleaner.LooksSafe("What is the capital of France?", "Sure! The capital of France is Paris, a city known for the Eiffel Tower and great food."));

    [Fact]
    public void AcceptsFaithfulRewrite() =>
        Assert.True(FlowTextCleaner.LooksSafe("Book the room for Monday, no wait, Tuesday.", "Book the room for Tuesday."));
}
