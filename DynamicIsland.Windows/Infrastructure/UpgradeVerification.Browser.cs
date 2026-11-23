using System.Windows;
using DynamicIsland.Q.Core;
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

            var searchVm = new CommandPaletteViewModel(runner, settings);
            var searchWindow = new CommandPaletteWindow(searchVm, () => true, island) { ShowActivated = false, Opacity = 0 };
            try
            {
                var anchor = new Rect(600, 0, 240, 44);
                var workArea = new Rect(0, 0, 1440, 900);
                void Click(string name) => ((System.Windows.Controls.Button)searchWindow.FindName(name))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                bool Visible(string name) => ((FrameworkElement)searchWindow.FindName(name)).IsVisible;
                void ShowSearchPalette()
                {
                    searchWindow.PrepareForShow();
                    searchWindow.PresentFromIsland(anchor, workArea, reducedMotion: true);
                }

                ShowSearchPalette();
                searchVm.Query = "C# & C++";
                Click("OpenGoogleSearch");
                var searchBox = (System.Windows.Controls.TextBox)searchWindow.FindName("SearchBox");
                check(searchBox.Text == "C# & C++" && Visible("SubmitSearch"), "Google search shows its results pill under the existing draft");
                Click("SearchAiMode");
                Click("SearchGoogleMode");
                check(searchBox.Text == "C# & C++" && Visible("SubmitSearch"), "Switching between Google and Ask AI preserves the draft");
                Click("SubmitSearch");
                check(!searchWindow.IsVisible && host.LastBrowserDestination?.AbsoluteUri == "https://www.google.com/search?q=C%23%20%26%20C%2B%2B",
                    "The search pill hands the encoded query to the browser and dismisses the palette");

                ShowSearchPalette();
                Click("OpenAiSearch");
                searchBox.Text = "How does binary search work?";
                Click("SubmitSearch");
                check(Visible("SearchWorking") && !Visible("SearchComposer") && !island.ShowQSurface,
                    "Ask AI retracts to a working pill and keeps the full chat surface closed");
                await WaitForSutraAsync(() => island.QState == QRunState.Complete && Visible("SearchComposer"));
                var reply = (AssistantReplyView)searchWindow.FindName("SearchReply");
                check(!Visible("SearchWorking") && Visible("SearchAnswer") && reply.DisplayedText.Contains("Local fixture response"),
                    "The working pill expands into the answer card and offers a follow-up");
                searchBox.Text = "Could you give an example?";
                Click("SubmitSearch");
                await WaitForSutraAsync(() => island.QState == QRunState.Complete && Visible("SearchComposer"));
                check(island.QPromptText == "Could you give an example?", "A follow-up submits from the same answer card");
                searchWindow.Dismiss();
                check(!searchWindow.IsVisible && !island.IsQActive, "Closing the search card clears its assistant session");
            }
            finally { searchWindow.Close(); }
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
