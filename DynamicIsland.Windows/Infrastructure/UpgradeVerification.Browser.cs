using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyGoogleBrowserAsync(IslandViewModel island, IslandWindow window,
        MediaSessionService media, TimerAlarmService timers, TimerAlarmViewModel timerVm,
        Action<bool, string> check)
    {
        var settings = island.Settings;
        var originalEnabled = settings.GoogleSearchEnabled;
        var originalHomepage = settings.GoogleSearchOpenHomepageWhenEmpty;
        var originalSafeSearch = settings.GoogleSearchSafeSearch;
        var originalLanguage = settings.GoogleSearchLanguage;
        var originalRecents = settings.PaletteRecents.ToArray();
        var host = new SutraPaletteHost(island, window);
        var runner = new CommandRunner(island, media, new PaletteAudioFixture(), timers, timerVm, host);
        try
        {
            settings.GoogleSearchEnabled = true;
            settings.GoogleSearchOpenHomepageWhenEmpty = true;
            settings.GoogleSearchSafeSearch = true;
            settings.GoogleSearchLanguage = "hi";
            var result = await runner.ExecuteAsync(CommandCatalog.Match("google C# & C++")!);
            check(result.Close && !result.IsError && host.LastBrowserDestination?.AbsoluteUri ==
                "https://www.google.com/search?q=C%23%20%26%20C%2B%2B&hl=hi&lr=lang_hi&safe=active",
                "Google command queues an encoded default-browser search with selected language and SafeSearch");

            settings.GoogleSearchSafeSearch = false;
            settings.GoogleSearchLanguage = "auto";
            result = await runner.ExecuteAsync(CommandCatalog.Match("google")!);
            check(result.Close && !result.IsError && host.LastBrowserDestination?.AbsoluteUri == "https://www.google.com/",
                "Bare Google command queues the homepage when the empty-query preference allows it");

            var previousDestination = host.LastBrowserDestination;
            settings.GoogleSearchOpenHomepageWhenEmpty = false;
            result = await runner.ExecuteAsync(CommandCatalog.Match("google")!);
            check(!result.Close && result.IsError && ReferenceEquals(previousDestination, host.LastBrowserDestination),
                "Google requires a query without launching or closing when empty-query homepage is disabled");

            settings.GoogleSearchEnabled = false;
            var disabledPalette = new CommandPaletteViewModel(runner, settings) { Query = "google" };
            check(disabledPalette.Suggestions.All(item => item.Command?.Id != PaletteCommand.GoogleSearch),
                "Disabling Google search removes its command from palette suggestions");
            result = await runner.ExecuteAsync(CommandCatalog.Match("google disabled request")!);
            check(!result.Close && result.IsError && ReferenceEquals(previousDestination, host.LastBrowserDestination),
                "Disabled Google search cannot queue a browser even when its command is invoked directly");

            settings.GoogleSearchEnabled = true;
            var pendingSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saveStarted = false;
            var closeRequested = false;
            var queueObservedBeforeClose = false;
            var closeObservedBeforeSave = false;
            var palette = new CommandPaletteViewModel(runner, settings, () =>
            {
                saveStarted = true;
                return pendingSave.Task;
            }) { Query = "google immediate handoff verification" };
            palette.CloseRequested += (_, _) =>
            {
                closeRequested = true;
                queueObservedBeforeClose = host.LastBrowserDestination?.AbsoluteUri ==
                    "https://www.google.com/search?q=immediate%20handoff%20verification";
                closeObservedBeforeSave = !saveStarted;
            };
            var execution = palette.ExecuteAsync();
            try
            {
                check(closeRequested && queueObservedBeforeClose && closeObservedBeforeSave && saveStarted && !execution.IsCompleted,
                    "Google queues its browser handoff and requests palette dismissal before waiting for settings persistence");
            }
            finally
            {
                pendingSave.TrySetResult();
                await execution;
            }
            check(!palette.IsStatusError && settings.PaletteRecents.Contains(PaletteCommand.GoogleSearch.ToString()),
                "Google execution completes and records its recent command after persistence resumes");
        }
        finally
        {
            settings.GoogleSearchEnabled = originalEnabled;
            settings.GoogleSearchOpenHomepageWhenEmpty = originalHomepage;
            settings.GoogleSearchSafeSearch = originalSafeSearch;
            settings.GoogleSearchLanguage = originalLanguage;
            settings.PaletteRecents.Clear();
            settings.PaletteRecents.AddRange(originalRecents);
        }
    }
}
