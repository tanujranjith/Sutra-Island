using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class GoogleSearchTests
{
    [Fact]
    public async Task SlowBrowserHandlerDoesNotBlockTheCallingThread()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var callerThread = Environment.CurrentManagedThreadId;
        var workerThread = callerThread;
        var apartment = ApartmentState.Unknown;
        var pending = GoogleSearch.LaunchAsync(GoogleSearch.Destination("coffee"), _ =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(pending.IsCompleted);
            Assert.NotEqual(callerThread, workerThread);
            if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, apartment);
        }
        finally { release.Set(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BrowserHandlerFailureIsReportedToTheCaller()
    {
        var failure = new InvalidOperationException("No browser is registered.");
        var reported = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GoogleSearch.LaunchAsync(GoogleSearch.Destination("coffee"), _ => throw failure));
        Assert.Same(failure, reported);
    }

    [Theory]
    [InlineData("google coffee near me")]
    [InlineData("google search coffee near me")]
    [InlineData("search google coffee near me")]
    [InlineData("web search coffee near me")]
    [InlineData("search coffee near me")]
    [InlineData("g coffee near me")]
    public void SearchCommandsAreDiscoverableAndPreserveTheQuery(string input)
    {
        var command = CommandCatalog.Filter(input)[0];
        var match = PaletteSelection.Resolve(input, command);
        Assert.Equal(PaletteCommand.GoogleSearch, command.Id);
        Assert.Equal("coffee near me", match!.Argument);
        Assert.Equal(PalettePanel.None, PalettePanelPolicy.For(match));
        Assert.Contains(CommandCatalog.DefaultSuggestions, x => x.Id == PaletteCommand.GoogleSearch);
    }

    [Theory]
    [InlineData("C# & C++", "https://www.google.com/search?q=C%23%20%26%20C%2B%2B")]
    [InlineData("https://example.com/?a=1&b=2", "https://www.google.com/search?q=https%3A%2F%2Fexample.com%2F%3Fa%3D1%26b%3D2")]
    [InlineData(" café ", "https://www.google.com/search?q=caf%C3%A9")]
    [InlineData("", "https://www.google.com/")]
    [InlineData("   ", "https://www.google.com/")]
    public void QueryIsEncodedAsSearchText(string query, string expected)
    {
        Assert.Equal(expected, GoogleSearch.Destination(query).AbsoluteUri);
    }

    [Fact]
    public void SelectedLanguageAndSafeSearchArePassedToGoogle()
    {
        Assert.Equal("https://www.google.com/search?q=coffee&hl=hi&lr=lang_hi&safe=active",
            GoogleSearch.Destination("coffee", "hi", safeSearch: true).AbsoluteUri);
        Assert.Equal("https://www.google.com/?hl=en&lr=lang_en&safe=active",
            GoogleSearch.Destination("", "en", safeSearch: true).AbsoluteUri);
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("invalid&safe=off", "auto")]
    [InlineData(" EN ", "en")]
    [InlineData("zh-cn", "zh-CN")]
    public void LanguageIsNormalizedToAnAvailableOption(string? value, string expected)
    {
        Assert.Equal(expected, GoogleSearch.NormalizeLanguage(value));
    }

    [Fact]
    public void InvalidLanguageCannotAddQueryParameters()
    {
        Assert.Equal("https://www.google.com/search?q=coffee",
            GoogleSearch.Destination("coffee", "en&safe=off").AbsoluteUri);
    }

    [Fact]
    public void SettingsSearchKeepsItsExistingMeaning()
    {
        Assert.Equal(PaletteCommand.SearchSettings, CommandCatalog.Match("settings search volume")!.Command.Id);
    }
}
