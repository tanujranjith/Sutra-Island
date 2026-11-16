using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WindowsLaunchTests
{
    [Theory]
    [InlineData("open Notepad", "OpenApp", "Notepad")]
    [InlineData("please launch Calculator", "OpenApp", "Calculator")]
    [InlineData("Hey Sutra, can you open the Spotify app please?", "OpenApp", "Spotify")]
    [InlineData("start Visual Studio Code", "OpenApp", "Visual Studio Code")]
    [InlineData("open app Microsoft Store", "OpenApp", "Microsoft Store")]
    [InlineData("open settings", "OpenWindowsSettings", "")]
    [InlineData("open Windows settings", "OpenWindowsSettings", "")]
    [InlineData("open Bluetooth settings", "OpenWindowsSettings", "Bluetooth")]
    [InlineData("show Wi-Fi settings", "OpenWindowsSettings", "Wi-Fi")]
    [InlineData("open Windows Settings display", "OpenWindowsSettings", "display")]
    [InlineData("go to sound settings", "OpenWindowsSettings", "sound")]
    [InlineData("open settings for privacy", "OpenWindowsSettings", "privacy")]
    [InlineData("open Windows Update", "OpenWindowsSettings", "Windows Update")]
    [InlineData("open island settings", "OpenSettings", "")]
    [InlineData("open clipboard", "Clipboard", "")]
    [InlineData("open notifications", "NotificationHistory", "")]
    [InlineData("list installed apps", "ListApps", "")]
    [InlineData("apps Microsoft", "ListApps", "Microsoft")]
    public void TypedAndVoiceRequestsResolveLocally(string prompt, string command, string argument)
    {
        Assert.True(AssistantControlIntent.TryResolve(prompt, out var request));
        Assert.Equal(command, request.Command);
        Assert.Equal(argument, request.Argument);
    }

    [Theory]
    [InlineData("open Notepad", PaletteCommand.OpenApp, "Notepad")]
    [InlineData("open the Calculator app", PaletteCommand.OpenApp, "Calculator")]
    [InlineData("open settings", PaletteCommand.OpenWindowsSettings, "")]
    [InlineData("windows settings sound", PaletteCommand.OpenWindowsSettings, "sound")]
    [InlineData("open display settings", PaletteCommand.OpenWindowsSettings, "display")]
    [InlineData("island settings", PaletteCommand.OpenSettings, "")]
    [InlineData("start stopwatch", PaletteCommand.StartStopwatch, "")]
    [InlineData("open media", PaletteCommand.OpenMedia, "")]
    public void PaletteRoutesToTheSameCommands(string text, PaletteCommand expected, string argument)
    {
        var match = CommandCatalog.Match(text);
        Assert.NotNull(match);
        Assert.Equal(expected, match.Command.Id);
        Assert.Equal(argument, match.Argument);
        Assert.Equal(match, PaletteSelection.Resolve(text, match.Command));
    }

    [Theory]
    [InlineData("Do not open Chrome")]
    [InlineData("How do I open Notepad?")]
    [InlineData("Explain how to open Bluetooth settings")]
    [InlineData("open C:\\Windows\\System32\\cmd.exe")]
    [InlineData("open notepad; calc")]
    [InlineData("open Chrome and mute sound")]
    [InlineData("open $(calc)")]
    public void ExplanationsPathsAndCompoundActionsAreNotDirectAppRequests(string prompt)
        => Assert.False(AssistantControlIntent.TryResolve(prompt, out _));

    [Theory]
    [InlineData("", "ms-settings:")]
    [InlineData("Wi-Fi", "ms-settings:network-wifi")]
    [InlineData("Windows settings Bluetooth", "ms-settings:bluetooth")]
    [InlineData("microphone permissions", "ms-settings:privacy-microphone")]
    [InlineData("Windows Update settings", "ms-settings:windowsupdate")]
    [InlineData("ms-settings:display", "ms-settings:display")]
    public void WindowsSettingsResolveToFixedDestinations(string query, string uri)
        => Assert.Equal(uri, WindowsLaunchCatalog.FindSettings(query)?.Uri);

    [Theory]
    [InlineData("unknown page")]
    [InlineData("ms-settings:windowsupdate-action")]
    [InlineData("ms-settings:display?arbitrary=1")]
    [InlineData("https://example.com")]
    public void UnknownOrParameterizedSettingsAreRejected(string query)
        => Assert.Null(WindowsLaunchCatalog.FindSettings(query));

    [Fact]
    public void InstalledAppsUseExactNamesBeforeAliasesAndPreserveAmbiguity()
    {
        WindowsAppEntry[] apps = [new("Google Chrome", "chrome"), new("Chrome", "exact"), new("Visual Studio Code", "code"), new("Visual Studio", "vs"), new("Café", "cafe")];
        Assert.Equal("exact", Assert.Single(WindowsLaunchCatalog.FindApps(apps, "Chrome")).Id);
        Assert.Equal("code", Assert.Single(WindowsLaunchCatalog.FindApps(apps, "VS Code")).Id);
        Assert.Equal(2, WindowsLaunchCatalog.FindApps(apps, "Visual").Length);
        Assert.Equal("cafe", Assert.Single(WindowsLaunchCatalog.FindApps(apps, "café")).Id);
        Assert.Empty(WindowsLaunchCatalog.FindApps(apps, "missing"));
        Assert.Empty(WindowsLaunchCatalog.FindApps(apps, "C:\\chrome.exe"));
    }
}
