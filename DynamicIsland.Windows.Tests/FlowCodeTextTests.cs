using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowCodeTextTests
{
    [Theory]
    [InlineData("Rename it to camel case get user by id.", "Rename it to getUserById.")]
    [InlineData("Use snake case user name end for the column.", "Use user_name for the column.")]
    [InlineData("Call it pascal case auth service", "Call it AuthService")]
    [InlineData("Constant case max retries, please.", "MAX_RETRIES, please.")]
    [InlineData("Kebab case main nav.", "main-nav.")]
    [InlineData("I like this case a lot", "I like this case a lot")]
    public void SpokenCasing(string input, string expected) => Assert.Equal(expected, FlowCodeText.ApplySpokenCasing(input));

    [Theory]
    [InlineData("Look at index dot tsx and fix it.", "Look @index.tsx and fix it.")]
    [InlineData("Tag app.py please", "@app.py please")]
    [InlineData("Check at FlowPipeline dot cs", "Check @FlowPipeline.cs")]
    [InlineData("Look at the report dot md", "Look at the report dot md")]
    [InlineData("Meet at noon.", "Meet at noon.")]
    [InlineData("Look at the dot com bubble.", "Look at the dot com bubble.")]
    public void FileTags(string input, string expected) => Assert.Equal(expected, FlowCodeText.TagFiles(input));

    [Fact]
    public void EditorsAreRecognised()
    {
        Assert.True(FlowCodeText.IsCodeEditor("Code"));
        Assert.True(FlowCodeText.IsCodeEditor("Cursor"));
        Assert.False(FlowCodeText.IsCodeEditor("chrome"));
        Assert.True(FlowCodeText.TakesFileTags("Windsurf"));
        Assert.False(FlowCodeText.TakesFileTags("Code"));
    }

    [Fact]
    public void ParsesVocabularyFiles() =>
        Assert.Equal(["Sutra", "Tanuj", "DCCC", "Whisper.net"],
            FlowCodeText.ParseVocabulary("# my words\nSutra, Tanuj\r\n\"DCCC\";Whisper.net\n\nsutra"));
}
