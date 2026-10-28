using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;
namespace DynamicIsland.Windows.Tests;
public sealed class PaletteCommandHistoryTests
{
    [Fact] public void PreservesArgumentsAndRestoresDraft()
    {
        var history=new PaletteCommandHistory();history.Remember("volume 30");history.Remember("seek back 45");
        Assert.True(history.Previous("timer draft",out var latest));Assert.Equal("seek back 45",latest);
        history.Previous("",out var older);Assert.Equal("volume 30",older);
        history.Next(out var newer);Assert.Equal("seek back 45",newer);
        history.Next(out var draft);Assert.Equal("timer draft",draft);Assert.False(history.IsBrowsing);
    }
    [Fact] public void SkipsEmptyAndAdjacentDuplicatesAndClampsOldest()
    {
        var history=new PaletteCommandHistory();history.Remember(" ");Assert.False(history.Previous("",out _));
        history.Remember("volume 30");history.Remember("volume 30");history.Previous("",out _);history.Previous("",out var oldest);
        Assert.Equal("volume 30",oldest);history.Next(out var draft);Assert.Equal("",draft);
    }
    [Fact] public void RetainsOnlyFiftyEntriesAndEditingEndsRecall()
    {
        var history=new PaletteCommandHistory();for(var index=0;index<60;index++)history.Remember("timer "+index);
        for(var index=0;index<60;index++)history.Previous("",out _);
        history.Previous("",out var oldest);Assert.Equal("timer 10",oldest);
        history.StopBrowsing();Assert.False(history.Next(out _));
    }
}
