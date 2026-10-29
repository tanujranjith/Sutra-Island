using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CommandHubTests
{
    [Theory]
    [InlineData("screenshot")]
    [InlineData("capture")]
    [InlineData("snip")]
    [InlineData("screen shot")]
    public void ScreenshotCommandsAreDiscoverableAndResolveToCaptureTool(string query)
    {
        var match=CommandCatalog.Match(query);
        Assert.NotNull(match);
        Assert.Equal(PaletteCommand.Screenshot,match.Command.Id);
        Assert.Contains(CommandCatalog.Filter(query),x=>x.Id==PaletteCommand.Screenshot);
        Assert.Equal("Tools",match.Command.Category);
        Assert.Contains(CommandCatalog.DefaultSuggestions,x=>x.Id==PaletteCommand.Screenshot);
    }
    [Theory]
    [InlineData("volume", PalettePanel.Volume)]
    [InlineData("vol", PalettePanel.Volume)]
    [InlineData("seek", PalettePanel.Seek)]
    [InlineData("seek forward 10 seconds", PalettePanel.None)]
    [InlineData("timer", PalettePanel.Timer)]
    [InlineData("alarm", PalettePanel.Alarm)]
    [InlineData("stopwatch", PalettePanel.Stopwatch)]
    [InlineData("focus", PalettePanel.Focus)]
    [InlineData("clipboard", PalettePanel.Clipboard)]
    [InlineData("notifications", PalettePanel.Notifications)]
    [InlineData("notification history", PalettePanel.Notifications)]
    [InlineData("convert", PalettePanel.Convert)]
    [InlineData("audio output", PalettePanel.AudioOutput)]
    [InlineData("output", PalettePanel.AudioOutput)]
    [InlineData("volume 50", PalettePanel.None)]
    [InlineData("volume +10", PalettePanel.None)]
    [InlineData("timer 10 minutes", PalettePanel.None)]
    [InlineData("alarm 7:30 AM", PalettePanel.None)]
    [InlineData("show timers", PalettePanel.None)]
    public void BareCommandsOpenPanelsAndArgumentsStillExecuteDirectly(string query, PalettePanel expected)
    { Assert.Equal(expected, PalettePanelPolicy.For(CommandCatalog.Match(query)!)); }

    [Theory]
    [InlineData("vo", false)]
    [InlineData("volume", false)]
    [InlineData("settings volume", true)]
    [InlineData("find setting volume", true)]
    [InlineData("settings search volume", true)]
    public void VolumeVisibilitySettingDoesNotDuplicateTheAudioAction(string query, bool visible)
    { Assert.Equal(visible, PalettePanelPolicy.ShowSetting(new("ShowVolume", "Volume", "Visibility"), query, [PaletteCommand.Volume])); }

    [Fact]
    public void DistinctVolumeSettingsRemainSearchable()
    {
        Assert.True(PalettePanelPolicy.ShowSetting(new("VolumeWarningEnabled", "Volume warning", "Warning"), "volume", [PaletteCommand.Volume]));
        Assert.True(PalettePanelPolicy.ShowSetting(new("ShowVolume", "Volume", "Visibility"), "show volume", []));
    }

    [Theory]
    [InlineData("0.5", " Tea ", "30 seconds for Tea")]
    [InlineData("25", "", "1500 seconds")]
    [InlineData("1440", "Long timer", "86400 seconds for Long timer")]
    public void TimerFormProducesBoundedDurationsAndPreservesLabels(string minutes, string label, string expected)
    { Assert.True(PalettePanelPolicy.TryTimerArgument(minutes, label, out var argument)); Assert.Equal(expected, argument); }

    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1441")]
    [InlineData("0.001")]
    public void TimerFormRejectsInvalidAndOutOfRangeDurations(string minutes)
    { Assert.False(PalettePanelPolicy.TryTimerArgument(minutes, "", out _)); }

    [Theory]
    [InlineData("timer cancel", PaletteCommand.CancelTimer, "")]
    [InlineData("timer cancel laundry", PaletteCommand.CancelTimer, "laundry")]
    [InlineData("cancel timer laundry", PaletteCommand.CancelTimer, "laundry")]
    [InlineData("timer pause tea", PaletteCommand.PauseTimer, "tea")]
    [InlineData("timer resume tea", PaletteCommand.ResumeTimer, "tea")]
    [InlineData("timer 10 minutes", PaletteCommand.Timer, "10 minutes")]
    public void TimerActionsRankFirstAndPreserveTheirArguments(string query, PaletteCommand expected, string argument)
    {
        var suggestion = CommandCatalog.Filter(query)[0];
        var match = PaletteSelection.Resolve(query, suggestion);
        Assert.Equal(expected, suggestion.Id);
        Assert.Equal(expected, match!.Command.Id);
        Assert.Equal(argument, match.Argument);
    }
    [Theory]
    [InlineData("forward 30 seconds", 30)]
    [InlineData("back 30 seconds", -30)]
    [InlineData("backward 1 minute", -60)]
    [InlineData("fwd 10s", 10)]
    [InlineData("rewind 15 seconds", -15)]
    [InlineData("seek forward 10 seconds", 10)]
    public void NaturalSeekPhrasesRankAndPreserveDirection(string query, int seconds)
    {
        var suggestion = CommandCatalog.Filter(query)[0];
        var match = PaletteSelection.Resolve(query, suggestion);
        Assert.Equal(PaletteCommand.Seek, suggestion.Id);
        Assert.True(CommandParsers.TryParseSeek(match!.Argument, out var offset, out _));
        Assert.Equal(TimeSpan.FromSeconds(seconds), offset);
        Assert.Equal(PalettePanel.None, PalettePanelPolicy.For(match));
    }
    [Fact]
    public void HighlightedCommandWinsOverRawQuery()
    {
        var match = PaletteSelection.Resolve("pause", CommandCatalog.ById(PaletteCommand.PauseTimer));
        Assert.Equal(PaletteCommand.PauseTimer, match!.Command.Id);
        Assert.Equal("", match.Argument);
    }
    [Theory]
    [InlineData("volume +10", PaletteCommand.Volume, "+10")]
    [InlineData("vol -10", PaletteCommand.Volume, "-10")]
    [InlineData("tim 10 minutes", PaletteCommand.Timer, "10 minutes")]
    [InlineData("", PaletteCommand.Chat, "")]
    public void ArgumentsAndEmptyQuerySelectionArePreserved(string query, PaletteCommand selected, string argument)
    {
        var match = PaletteSelection.Resolve(query, CommandCatalog.ById(selected));
        Assert.Equal(selected, match!.Command.Id); Assert.Equal(argument, match.Argument);
    }
    [Fact]
    public void LongestAliasStillMatches()
    { Assert.Equal(PaletteCommand.AskScreen, CommandCatalog.Match("ask screen what is this")!.Command.Id); }
    [Fact]
    public void FavoritesAndRecentsPrecedeDefaultsWithoutDuplicates()
    {
        var items = CommandCatalog.Filter("", ["Volume"], ["Chat", "Volume"]);
        Assert.Equal(PaletteCommand.Volume, items[0].Id);
        Assert.Equal(PaletteCommand.Chat, items[1].Id);
        Assert.Equal(items.Count, items.DistinctBy(x => x.Id).Count());
    }
    [Fact]
    public void ExactQueryBeatsFavoriteBroaderMatches()
    { Assert.Equal(PaletteCommand.Pause, CommandCatalog.Filter("pause", ["PauseTimer"])[0].Id); }
    [Fact]
    public void SettingsAreSearchableByDescriptionAndName()
    {
        Assert.Contains(SettingsSearchIndex.Search("compact date"), x => x.Property == "ShowCompactDate");
        Assert.Contains(SettingsSearchIndex.Search("local microphone"), x => x.Property == "WakeListeningEnabled");
        var match = PaletteSelection.Resolve("date", CommandCatalog.ById(PaletteCommand.SearchSettings), "ShowCompactDate");
        Assert.Equal("ShowCompactDate", match!.Argument);
    }
    [Fact]
    public void SayKeepsWordingSuggestionSemantics()
    { Assert.Contains("wording", CommandCatalog.ById(PaletteCommand.Say).Description); }
}
