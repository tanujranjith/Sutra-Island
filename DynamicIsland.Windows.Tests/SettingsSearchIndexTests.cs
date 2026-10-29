using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class SettingsSearchIndexTests
{
    [Theory]
    [InlineData("COMPACT date", "ShowCompactDate")]
    [InlineData("LOCAL MICROPHONE", "WakeListeningEnabled")]
    [InlineData("qincludescreen", "QIncludeScreenImage")]
    public void SearchMatchesAllWordsAcrossIndexedFieldsCaseInsensitively(string query, string property)
    {
        Assert.Contains(SettingsSearchIndex.Search(query), entry => entry.Property == property);
    }

    [Fact]
    public void SearchRequiresEveryWordAndReturnsAtMostEightResults()
    {
        Assert.Empty(SettingsSearchIndex.Search("local no-match"));
        Assert.Equal(8, SettingsSearchIndex.Search("open").Count());
    }
}
