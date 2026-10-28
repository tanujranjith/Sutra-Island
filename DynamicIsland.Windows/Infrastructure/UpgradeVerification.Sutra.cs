using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    internal static async Task RunSutraAsync(bool commandPanelsOnly = false, bool composerOnly = false, bool mascotOnly = false, bool graphiteOnly = false, bool notificationMotionOnly = false)
    {
        if (!AppDataPaths.IsPreview) throw new InvalidOperationException("Verification requires preview isolation.");
        var output = Path.Combine(AppDataPaths.Root, "captures"); Directory.CreateDirectory(output);
        var report = new List<string>();
        void Check(bool condition, string label)
        { report.Add((condition ? "PASS " : "FAIL ") + label); File.WriteAllLines(Path.Combine(output, "sutra-checks.txt"), report); if (!condition) throw new InvalidOperationException(label); }
        async Task Idle() => await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var log = new LoggingService(); log.SetDebugEnabled(true);
        var settings = new AppSettings { HasOnboarded = true, ShowNotifications = true, NotificationHistoryEnabled = true, ShowWeather = false, ShowSystemMonitor = false, QDisclosureAccepted = true, QSelectedProvider = "fixture", QSelectedModel = "fixture-default", ShowIslandInScreenshots = true, AlwaysOnTop = false, ExpandOnHover = false, AutoHideFullscreen = false };
        var persistence = new SettingsService(log);
        File.WriteAllText(persistence.SettingsPath, "{\"SchemaVersion\":12,\"NotificationPollSeconds\":4,\"ShowDate\":false,\"QSelectedProvider\":\"ollama\",\"QSelectedModel\":\"local\"}");
        var migrated = await persistence.LoadAsync();
        Check(migrated.SchemaVersion == new AppSettings().SchemaVersion && migrated.NotificationPollSeconds == 1 && !migrated.ShowCompactDate && !migrated.WakeListeningEnabled && !migrated.ShowDate && migrated.QSelectedModel == "local", "Settings migration preserves preferences and adds opt-in voice/compact date defaults");
        File.WriteAllText(persistence.SettingsPath, "{\"SchemaVersion\":12,\"NotificationPollSeconds\":8}");
        Check((await persistence.LoadAsync()).NotificationPollSeconds == 8, "Custom notification polling interval survives migration");
        using var media = new MediaSessionService(log); using var audio = new AudioSessionService(log); using var battery = new BatteryService(); using var clock = new ClockService();
        using var timers = new TimerAlarmService(log); using var theme = new ThemeService(); using var weather = new WeatherService(log); using var monitor = new SystemMonitorService();
        using var spectrum = new AudioSpectrumService(log); using var stocks = new StocksService(log); using var calendar = new CalendarService(log); using var privacy = new PrivacySensorService(log);
        var source = new NotificationSourceFixture { Snapshot = [new("Fixture", "Before first expansion", "Startup delivery", 901, DateTimeOffset.Now, "fixture.startup")] };
        using var notifications = new NotificationListenerService(log, source);
        var history = new NotificationHistoryService(log);
        var registry = new QProviderRegistry([new ProviderFixture("fixture"), new ProviderFixture("gemini")]);
        using var q = new QSessionController(registry);
        var screen = new CountingScreen();
        using var vm = new IslandViewModel(settings, media, audio, battery, clock, timers, theme, weather, monitor, spectrum, stocks, calendar, notifications, privacy, history, q, screen, new FakeSpeech(), new FakeSecrets(), qProviders: registry);
        using var timerVm = new TimerAlarmViewModel(timers, false);
        var position = new WindowPositionService();
        var window = new IslandWindow(vm, timerVm, position, persistence, log, new ScreenContextService(log)) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Title = "Sutra verification " + Guid.NewGuid().ToString("N") };
        window.Closing += (_, _) => File.AppendAllText(Path.Combine(output, "island-close-trace.txt"), Environment.StackTrace + Environment.NewLine);
        var bindingLog = new TextWriterTraceListener(Path.Combine(output, "sutra-bindings.log"));
        var bindingClosed = false;
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingLog); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        try
        {
            if (Environment.GetEnvironmentVariable("ISLAND_VERIFY_BACKDROP")=="1") { await VerifyDesktopFrostAsync(vm,window,output); return; }
            if (Environment.GetEnvironmentVariable("ISLAND_VERIFY_TIMER_ONLY")=="1")
            {
                timers.StartTimer(TimeSpan.FromMinutes(5),"Fixture");
                window.Show(); await Idle();
                await VerifyTimerMotionAsync(settings,vm,window,Check,output);
                return;
            }
            if (notificationMotionOnly) { window.Show(); await Idle(); await VerifyNotificationMotionAsync(settings, vm, window, Check, output); await VerifyNotificationClicksAsync(settings, vm, window, Check); bindingLog.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog); bindingLog.Close(); bindingClosed=true; Check(!File.ReadAllText(Path.Combine(output,"sutra-bindings.log")).Contains("Error:"), "Standalone notification presentation has no WPF binding errors"); return; }
            if (graphiteOnly) { await VerifyGraphiteAsync(vm, timerVm, window, persistence, log, registry, Check, output); await VerifyGraphiteMaterialAsync(vm,window,timerVm,media,timers,persistence,log,history,Check,output); bindingLog.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog); bindingLog.Close(); bindingClosed = true; Check(!File.ReadAllText(Path.Combine(output, "sutra-bindings.log")).Contains("Error:"), "Graphite screens render without WPF binding errors"); return; }
            if (mascotOnly)
            {
                await VerifyMascotCornerBulbAsync(Check, output);
                await VerifyAssistantReplyMotionAsync(Check, output);
                bindingLog.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog);
                bindingLog.Close(); bindingClosed = true;
                Check(!File.ReadAllText(Path.Combine(output, "sutra-bindings.log")).Contains("Error:"), "Mascot corner bulb verification completed without WPF binding errors");
                return;
            }
            if (composerOnly)
            {
                window.Show(); await Idle();
                await VerifyComposerKeysAsync(vm, window, Check);
                bindingLog.Flush();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog);
                bindingLog.Close(); bindingClosed = true;
                Check(!File.ReadAllText(Path.Combine(output, "sutra-bindings.log")).Contains("Error:"), "Composer keyboard verification completed without WPF binding errors");
                return;
            }
            if (commandPanelsOnly)
            {
                await VerifyBrandingAndMiniPlaybackAsync(vm, window, Check, output);
                await VerifyComposerKeysAsync(vm, window, Check);
                var panelAudio = new PaletteAudioFixture();
                var panelMedia = new PaletteSeekFixture();
                var panelClipboard = new PaletteClipboardFixture(); var panelOutputs = new PaletteOutputFixture();
                var panelHost = new SutraPaletteHost(vm, window);
                var panelRunner = new CommandRunner(vm, media, panelAudio, timers, timerVm, panelHost, panelMedia, panelClipboard, panelOutputs);
                var panelVm = new CommandPaletteViewModel(panelRunner, settings, () => persistence.SaveAsync(settings));
                var panelWindow = new CommandPaletteWindow(panelVm, () => true) { Opacity = 0, ShowActivated = false };
                try
                {
                    await VerifyCommandPanelsAsync(panelVm, panelWindow, panelAudio, timers, Check, output);
                    await VerifySeekPanelAsync(panelVm, panelWindow, panelMedia, Check, output);
                    await VerifyToolPanelsAsync(panelVm, panelWindow, panelClipboard, panelOutputs, history, window, Check, output);
                    await VerifyClipboardImagesAndCaretsAsync(panelVm, panelWindow, panelClipboard, window, Check, output);
                    await VerifyPalettePresentationAsync(panelVm, panelWindow, log, Check, output);
                    await VerifyDelayedOutputLayoutAsync(panelVm, panelWindow, panelOutputs, Check, output);
                    await VerifyInstalledCodexVersionAsync(window, log, Check);
                    await VerifyPercentageCalculatorAsync(panelVm, panelWindow, Check, output);
                    await VerifyAssistantIslandControlsAsync(vm, panelRunner, panelAudio, panelOutputs, panelHost, persistence, Check);
                    await VerifyAssistantActionMotionAsync(Check, output);
                    await VerifyAssistantReplyMotionAsync(Check, output);
                    await VerifyMascotMotionAsync(Check, output);
                    await VerifyMascotCornerBulbAsync(Check, output);
                    bindingLog.Flush();
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog);
                    bindingLog.Close(); bindingClosed = true;
                    Check(!File.ReadAllText(Path.Combine(output, "sutra-bindings.log")).Contains("Error:"), "Command panels have no WPF binding errors");
                }
                finally { panelWindow.Close(); }
                return;
            }
            await notifications.StartAsync();
            Check(history.Items.Count == 1 && !vm.ShowNotification, "Notification received before visual readiness remains queued");
            window.Show(); await Idle(); window.UpdateLayout();
            var banner = (NotificationMorphHost)window.FindName("NotifBanner");
            Check(vm.ShowNotification && !vm.IsExpanded && banner.Visibility == Visibility.Visible, "First notification appears while collapsed without expansion or input");
            await WaitForSutraAsync(() => banner.RevealProgress > .99);
            window.UpdateLayout();
            Check(banner.RevealProgress > .99 && ((TextBlock)window.FindName("NotifTitleText")).Text == "Before first expansion", "First banner completes the existing morph with correct text");
            var bottom = banner.TranslatePoint(new System.Windows.Point(0, banner.ActualHeight), window).Y;
            Check(bottom <= window.ActualHeight + 1, "Banner fits inside the compact window bounds");
            vm.DismissVisibleBanner(); await Idle();
            Check(!vm.ShowNotification && source.Dismissed.Count == 1, "Banner dismissal reaches its original Windows source");
            var latency = Stopwatch.StartNew();
            source.Snapshot = [new("Fixture", "Second arrival", "Event delivery", 902, DateTimeOffset.Now, "fixture.second")]; source.Added!();
            await Idle(); window.UpdateLayout();
            Check(vm.ShowNotification && banner.Visibility == Visibility.Visible, "Event-driven delivery works without hover");
            report.Add($"METRIC accepted-event-to-layout-ms={latency.Elapsed.TotalMilliseconds:0.0}");
            vm.ShowKeyboardLock("Caps Lock", true); await Idle();
            Check(vm.IsLockBannerActive && vm.BannerTitle == "Caps Lock On" && !vm.ShowNotification, "Caps Lock temporarily replaces ordinary notification");
            Check(((Border)window.FindName("NotifCard")).MinWidth == 180, "Lock feedback uses the smaller banner layout");
            await Task.Delay(420); await CaptureSutraAsync(window, output, "caps-lock");
            vm.ShowKeyboardLock("Num Lock", false); Check(vm.BannerTitle == "Num Lock Off", "Rapid lock toggles replace feedback with latest state");
            Check(history.Items.Count == 1, "Lock feedback never enters Windows notification history");
            await Task.Delay(2100); await Idle();
            Check(!vm.IsLockBannerActive && vm.ShowNotification && vm.NotificationTitle == "Second arrival", "Interrupted notification resumes after lock feedback expires");
            vm.DismissVisibleBanner(); vm.SetFocusMode(true); vm.ShowKeyboardLock("Caps Lock", false);
            Check(vm.ShowBanner && vm.BannerTitle == "Caps Lock Off", "Lock feedback remains visible in Focus mode");
            vm.DismissVisibleBanner(); vm.SetFocusMode(false);
            timers.StartTimer(TimeSpan.FromSeconds(1), "Urgent fixture");
            var urgentTimer = timers.State.Timers.Last(); urgentTimer.Phase = TimerPhase.Completed; timers.SelectTimer(urgentTimer.Id);
            vm.ShowKeyboardLock("Caps Lock", true);
            Check(vm.HasUrgentAlert && !vm.IsLockBannerActive, "Urgent timer alerts take priority over lock feedback");
            vm.DismissUrgentCommand.Execute(null); vm.DismissVisibleBanner();
            settings.CapsLockNotifications = false; vm.ShowKeyboardLock("Caps Lock", true); Check(!vm.IsLockBannerActive, "Caps Lock feedback toggle disables only that key"); settings.CapsLockNotifications = true;
            vm.InteractionProtected = true;
            source.Snapshot = [new("Fixture", "Deferred arrival", "Queued", 903, DateTimeOffset.Now, "fixture.deferred")]; source.Added!(); await Idle();
            Check(!vm.ShowNotification, "Protected interaction defers ordinary banners");
            vm.InteractionProtected = false;
            Check(vm.ShowNotification && vm.NotificationTitle == "Deferred arrival", "Ending interaction presents pending notification immediately"); vm.DismissVisibleBanner();
            // Functional visibility checks should not race an in-flight expansion.
            // Motion timing is exercised separately below with normal animation enabled.
            settings.AnimationIntensity = AnimationIntensity.Reduced;
            settings.ShowBattery = false; settings.ShowClock = false; settings.ShowDate = true; settings.ShowCompactDate = true;
            vm.ApplySettings(); vm.IsExpanded = true; window.ApplySettings(); await Idle(); window.UpdateLayout();
            var dates = VisualDescendants(window).OfType<TextBlock>().Where(x => System.Windows.Data.BindingOperations.GetBindingExpression(x, TextBlock.TextProperty)?.ParentBinding.Path?.Path == "DateText").ToArray();
            Check(dates.Any(x => x.IsVisible && x.ActualHeight > 0), "Expanded date renders with battery and clock disabled");
            await CaptureSutraAsync(window, output, "date-desktop");
            settings.IslandVisualMode = IslandVisualMode.Stats; vm.ApplySettings(); window.ApplySettings(); await Idle();
            Check(dates.Any(x => x.IsVisible), "Stats expanded date renders independently of battery and clock");
            vm.IsExpanded = false; window.ApplySettings(); await Idle();
            Check(VisualDescendants(window).OfType<TextBlock>().Any(x => x.IsVisible && System.Windows.Data.BindingOperations.GetBindingExpression(x, TextBlock.TextProperty)?.ParentBinding.Path?.Path == "CompactDateText"), "Optional compact date renders with clock disabled");
            var priorWidth = settings.IslandWidth; var priorHeight = settings.IslandHeight;
            settings.IslandWidth = 90; settings.IslandHeight = 48; vm.ApplySettings(); window.ApplySettings(); await Idle();
            Check(!vm.ShowCompactDate && (string?)((FrameworkElement)window.FindName("CompactContent")).ToolTip == vm.DateLongText, "Hole-punch layout preserves the date through its tooltip");
            settings.IslandWidth = priorWidth; settings.IslandHeight = priorHeight; vm.ApplySettings(); window.ApplySettings();
            settings.ShowCompactDate = false; vm.ApplySettings(); Check(!vm.ShowCompactDate, "Compact date switch applies immediately");
            settings.IslandVisualMode = IslandVisualMode.Apple; vm.ApplySettings(); window.ApplySettings();
            await vm.StartChatAsync("hello");
            Check(screen.Captures == 0 && q.Snapshot.Context is null && vm.QResponse.Length > 0, "Ordinary assistant chat answers without screen capture");
            await vm.StartChatAsync("follow up"); Check(screen.Captures == 0, "Follow-up chat does not trigger screen capture");
            ((System.Windows.Controls.CheckBox)window.FindName("UseScreenCheckBox")).IsChecked = true;
            Check(vm.QUseScreenNext, "Visible Use screen control enables one-shot context capture");
            await vm.SubmitQAsync("screen question");
            Check(screen.Captures == 1 && !vm.QUseScreenNext, "Use screen captures exactly once and resets its toggle");
            await vm.SubmitQAsync("ordinary follow up"); Check(screen.Captures == 1 && q.Snapshot.Context is null, "Next ordinary question clears previous screen context");
            vm.QCompareEnabled = true; await vm.SubmitQAsync("compare ordinary options");
            Check(screen.Captures == 1 && !string.IsNullOrWhiteSpace(vm.QLeftAnswer) && !string.IsNullOrWhiteSpace(vm.QRightAnswer), "Ordinary comparison answers from both providers without screen capture");
            vm.QCompareEnabled = false;
            typeof(IslandViewModel).GetMethod("OnVoiceChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [new AssistantVoiceSnapshot(AssistantVoiceState.Listening, "Listening…")]);
            Check(vm.PinExpanded && vm.IsExpanded, "Voice listening keeps the auto-expanded assistant visible without stealing focus");
            Check(((TextBlock)window.FindName("VoiceStatusText")).Text.StartsWith("Listening"), "Voice listening status is visible in the assistant composer");
            typeof(IslandViewModel).GetMethod("OnVoiceChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [new AssistantVoiceSnapshot(AssistantVoiceState.Off, "Voice ready")]);
            await VerifyWakeLifetimeAsync(settings, vm, Check);
            vm.KeepExpanded = true; vm.IsExpanded = true; window.ApplySettings(); await Task.Delay(450); await Idle();
            await CaptureSutraAsync(window, output, "sutra-assistant"); vm.KeepExpanded = false;
            vm.ClearQ(); await Idle();
            var host = new SutraPaletteHost(vm, window);
            var paletteAudio = new PaletteAudioFixture();
            var runner = new CommandRunner(vm, media, paletteAudio, timers, timerVm, host);
            var paletteVm = new CommandPaletteViewModel(runner, settings, () => persistence.SaveAsync(settings));
            var prepare = Stopwatch.StartNew();
            var palette = new CommandPaletteWindow(paletteVm, () => true) { Opacity = 0, ShowActivated = false };
            new System.Windows.Interop.WindowInteropHelper(palette).EnsureHandle(); palette.Measure(new System.Windows.Size(palette.Width, double.PositiveInfinity));
            palette.WarmLayout();
            await palette.PrimePresentationAsync();
            report.Add($"METRIC palette-prepare-ms={prepare.Elapsed.TotalMilliseconds:0.0}");
            try
            {
                var samples = new List<double>();
                for (var i = 0; i < 8; i++)
                {
                    palette.PrepareForShow(); var watch = Stopwatch.StartNew(); palette.Show(); await Idle(); palette.UpdateLayout(); samples.Add(watch.Elapsed.TotalMilliseconds); palette.Hide();
                }
                report.Add($"METRIC palette-first-ms={samples[0]:0.0}, palette-reopen-p95-ms={samples.Skip(1).Max():0.0}");
                Check(samples[0] < 300 && samples.Skip(1).Max() < 150, "Reusable palette meets first-open and subsequent-open latency targets");
                paletteVm.Reset(); var chatIndex = paletteVm.Suggestions.ToList().FindIndex(x => x.Command?.Id == PaletteCommand.Chat); paletteVm.SelectedIndex = chatIndex;
                await paletteVm.ExecuteAsync(); Check(host.OpenedChat, "Empty-query Enter executes highlighted suggestion");
                paletteVm.Query = "pause"; paletteVm.SelectedIndex = paletteVm.Suggestions.ToList().FindIndex(x => x.Command?.Id == PaletteCommand.PauseTimer);
                Check(PaletteSelection.Resolve(paletteVm.Query, paletteVm.SelectedSuggestion?.Command)?.Command.Id == PaletteCommand.PauseTimer, "Palette selection overrides a different raw-query command");
                paletteVm.Query = "volume"; await paletteVm.ToggleFavoriteAsync();
                var loaded = await persistence.LoadAsync();
                Check(loaded.PaletteFavorites.Contains("Volume") && loaded.PaletteRecents.Contains("Chat"), "Favorite and recent identifiers survive settings reload");
                Check(!File.ReadAllText(persistence.SettingsPath).Contains("ordinary follow up"), "Palette persistence does not retain questions or command arguments");
                paletteVm.Query = "compact date"; Check(paletteVm.Suggestions.Any(x => x.Argument == "ShowCompactDate"), "Settings names and descriptions appear in palette search");
                palette.Show(); await Idle(); await CaptureSutraAsync(palette, output, "command-hub"); palette.Hide();
                paletteVm.Query = "12 in to ft"; Check(paletteVm.SelectedSuggestion?.CalculationValue == "1 ft", "Existing unit conversion remains available");
                await VerifyCommandPanelsAsync(paletteVm, palette, paletteAudio, timers, Check, output);
            }
            finally { palette.Close(); }
            using var codex = new AsyncDisposeAdapter(new CodexAppServerClient(log: log));
            var editorVm = new SettingsViewModel(settings, persistence, new StartupService(log), vm.ApplySettings, () => { }, () => { }, new FakeSecrets(), registry, new CodexAccountCoordinator(codex.Client, log));
            var editor = new SettingsWindow(editorVm, vm, window) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Title = "Settings verification " + Guid.NewGuid().ToString("N") };
            try
            {
                editor.Show(); editor.OpenSearch("ShowCompactDate"); await Idle(); Check(editorVm.SelectedSectionKey == "content", "Settings search opens the correct section");
                editor.OpenSearch("WakeListeningEnabled"); await Idle(); Check(editorVm.SelectedSectionKey == "q", "Wake settings can be located directly from the command hub");
                var wakeSwitch = VisualDescendants(editor).OfType<System.Windows.Controls.CheckBox>().Single(x =>
                    System.Windows.Data.BindingOperations.GetBindingExpression(x, System.Windows.Controls.CheckBox.IsCheckedProperty)?.ParentBinding.Path?.Path == "WakeListeningEnabled");
                wakeSwitch.IsChecked = true;
                await WaitForSutraAsync(() => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(persistence.SettingsPath))?.WakeListeningEnabled == true);
                vm.ClearQ(); editorVm.RefreshRuntimePreferences(); await Idle();
                Check(editor.IsVisible && settings.WakeListeningEnabled && wakeSwitch.IsChecked == true,
                    "Wake switch persists immediately while Settings stays open and remains enabled after chat closes");
                wakeSwitch.IsChecked = false;
                await WaitForSutraAsync(() => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(persistence.SettingsPath))?.WakeListeningEnabled == false);
                Check(!settings.WakeListeningEnabled, "Explicit wake opt-out persists immediately without closing Settings");
            }
            finally { editor.Close(); }
            Check(window.IsLoaded, "Closing the Settings fixture leaves the island window available");
            await VerifyNotificationListenerAsync(log, Check);
            settings.AnimationIntensity = AnimationIntensity.Normal; vm.ApplySettings();
            await VerifyNotificationMotionAsync(settings, vm, window, Check, output);
            await VerifyNotificationClicksAsync(settings, vm, window, Check);
            bindingLog.Flush();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog); bindingLog.Close(); bindingClosed = true;
            Check(!File.ReadAllText(Path.Combine(output, "sutra-bindings.log")).Contains("Error:"), "Native surfaces have no WPF binding errors");
            await history.FlushAsync();
            File.WriteAllLines(Path.Combine(output, "sutra-checks.txt"), report);
        }
        finally { window.Close(); if (!bindingClosed) { bindingLog.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog); bindingLog.Dispose(); } }
    }
    private static async Task WaitForSutraAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(4)) throw new TimeoutException("Voice lifetime fixture did not reach its expected state.");
            await Task.Delay(20);
        }
    }
    private static async Task VerifyWakeLifetimeAsync(AppSettings settings, IslandViewModel vm, Action<bool, string> check)
    {
        var previousAutoClose = settings.QAutoCloseAfterResponse;
        var previousDelay = settings.QAutoCloseDelaySeconds;
        var platform = new SutraVoiceLifetimeFixture();
        using var voice = new AssistantVoiceController(platform, platform, platform, vm.SubmitVoiceTurnAsync, vm.CancelQ);
        vm.AttachVoice(voice);
        try
        {
            settings.WakeListeningEnabled = true;
            settings.QAutoCloseAfterResponse = true;
            settings.QAutoCloseDelaySeconds = 1;
            await vm.ConfigureVoiceAsync();
            platform.TriggerWake();
            await WaitForSutraAsync(() => platform.DictationCount == 1 && vm.QState == QRunState.Complete && voice.Snapshot.State == AssistantVoiceState.WakeListening);
            await WaitForSutraAsync(() => !vm.IsQActive);
            check(settings.WakeListeningEnabled && platform.WakeActive && voice.Snapshot.State == AssistantVoiceState.WakeListening,
                "Auto-closing a completed voice chat keeps wake listening enabled");
            platform.TriggerWake();
            await WaitForSutraAsync(() => platform.DictationCount == 2 && vm.QState == QRunState.Complete && voice.Snapshot.State == AssistantVoiceState.WakeListening);
            check(vm.IsQActive, "Hey Sutra opens another voice turn after the prior chat auto-closes");
            vm.ClearQ();
            check(platform.WakeActive && voice.Snapshot.State == AssistantVoiceState.WakeListening,
                "Manually closing chat also preserves idle wake listening");
            settings.WakeListeningEnabled = false;
            await vm.ConfigureVoiceAsync();
            platform.TriggerWake(); await Task.Delay(50);
            check(!platform.WakeActive && voice.Snapshot.State == AssistantVoiceState.Off && platform.DictationCount == 2,
                "Explicitly disabling wake releases the microphone and prevents another turn");
        }
        finally
        {
            settings.WakeListeningEnabled = false;
            await vm.ConfigureVoiceAsync();
            settings.QAutoCloseAfterResponse = previousAutoClose;
            settings.QAutoCloseDelaySeconds = previousDelay;
            vm.ClearQ();
        }
    }
    private sealed class SutraVoiceLifetimeFixture : IWakeWordInput, IQSpeechInputService, ISpeechOutput
    {
        public event Action? WakeDetected;
        public bool IsAvailable => true;
        public bool WakeActive { get; private set; }
        private int _dictationCount;
        public int DictationCount => Volatile.Read(ref _dictationCount);
        public void TriggerWake() => WakeDetected?.Invoke();
        public Task StartWakeAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); WakeActive = true; return Task.CompletedTask; }
        public Task StopWakeAsync() { WakeActive = false; return Task.CompletedTask; }
        public Task<string?> DictateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref _dictationCount); return Task.FromResult<string?>("Voice fixture question"); }
        public Task SpeakAsync(string text, string voice, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public void StopSpeaking() { }
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in VisualDescendants(child)) yield return item; } }
    private static Task CaptureSutraAsync(Window window, string output, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file); return Task.CompletedTask;
    }
    private sealed class CountingScreen : IQScreenContextService
    {
        public int Captures { get; private set; }
        public Task<QScreenContext?> CaptureAsync(nint window, DynamicIsland.Q.Core.QCaptureMode mode, CancellationToken token)
        { Captures++; return Task.FromResult<QScreenContext?>(new("Fixture", "test", 20, 20, "fixture OCR", null, DateTimeOffset.Now)); }
    }
    private sealed class SutraPaletteHost(IslandViewModel vm, IslandWindow window) : ICommandPaletteHost
    {
        public string LastPanel = "", LastSetting = "";
        public bool OpenedChat { get; private set; }
        public void ShowSettings() { }
        public void ShowTimerPanel() { }
        public void ShowAlarmPanel() { }
        public void ShowStopwatchPanel() { }
        public void OpenQ(string? question, QMode mode, bool compare) { }
        public void OpenChat(string? question, bool useScreen = false) { OpenedChat = true; }
        public void ShowSettingsSearch(string query) { LastSetting = query; }
        public Task OpenCommandPanelAsync(string command) { LastPanel = command; return Task.CompletedTask; }
        public void RecenterIsland() { }
        public Task OpenClipboardAsync() => Task.CompletedTask;
        public void ShowNotificationHistory() => window.ShowHistory();
        public Task SetNotificationsAsync(bool enabled) { vm.Settings.ShowNotifications = enabled; return Task.CompletedTask; }
    }
}
