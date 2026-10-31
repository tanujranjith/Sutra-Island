using System.Threading;
using System.Windows;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Q.Core;
using System.Windows.Media;

namespace DynamicIsland.Windows;

public partial class App : System.Windows.Application, ICommandPaletteHost
{
    private static string ShowSettingsSignalName => "Local\\DynamicIsland.Windows.ShowSettings" + Infrastructure.AppDataPaths.InstanceSuffix;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _showSettingsRegistration;
    private LoggingService? _log;
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private MediaSessionService? _media;
    private AudioSessionService? _audio;
    private BatteryService? _battery;
    private ClockService? _clock;
    private TimerAlarmService? _timerAlarm;
    private ThemeService? _theme;
    private AirPodsService? _airPods;
    private WeatherService? _weather;
    private SystemMonitorService? _sysMon;
    private AudioSpectrumService? _spectrum;
    private StocksService? _stocks;
    private CalendarService? _calendar;
    private NotificationListenerService? _notifications;
    private NotificationHistoryService? _notificationHistory;
    private GlobalHotkeyService? _hotkeys;
    private QShortcutService? _qShortcuts;
    private PrivacySensorService? _privacy;
    private ClipboardService? _clipboard;
    private WindowPositionService? _position;
    private IslandViewModel? _islandViewModel;
    private SettingsViewModel? _settingsViewModel;
    private IslandWindow? _islandWindow;
    private SettingsWindow? _settingsWindow;
    private TimerAlarmViewModel? _timerViewModel;
    private TrayService? _tray;
    private AppSettings? _settings;
    private volatile bool _isShuttingDown;
    private ScreenContextService? _qScreen;
    private SpeechInputService? _qSpeech;
    private AssistantVoiceController? _voice;
    private KeyboardLockService? _keyboardLocks;
    private readonly System.Diagnostics.Stopwatch _startupWatch = System.Diagnostics.Stopwatch.StartNew();
    private DpapiSecretStore? _qSecrets;
    private CodexAppServerClient? _codexClient;
    private CodexAccountCoordinator? _codexAccount;
    private IQSessionController? _qSession;
    private CommandPaletteWindow? _paletteWindow;
    private CommandRunner? _paletteRunner;
    private CommandPaletteViewModel? _paletteViewModel;
    private int _paletteHotkeyId;
    private int _screenshotHotkeyId;
    private string _registeredScreenshotHotkey="";
    private ScreenshotTool? _screenshots;
    private string _registeredPaletteHotkey = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Contains("--verify-screenshots"))
        {
            if(!Infrastructure.AppDataPaths.IsPreview){Shutdown(2);return;}
            try{await Infrastructure.ScreenshotVerification.RunAsync();Shutdown(0);}
            catch(Exception ex){Directory.CreateDirectory(Infrastructure.AppDataPaths.Root);File.WriteAllText(Path.Combine(Infrastructure.AppDataPaths.Root,"verification-error.txt"),ex.ToString());Shutdown(1);}
            return;
        }
        Infrastructure.GraphiteMenuSupport.Initialize();
        if (e.Args.Contains("--verify-notification-motion") || e.Args.Contains("--verify-graphite") || e.Args.Contains("--verify-mascot-bulb") || e.Args.Contains("--verify-composer-keys") || e.Args.Contains("--verify-command-panels") || e.Args.Contains("--verify-sutra-update") || e.Args.Contains("--verify-upgrades") || e.Args.Contains("--verify-q-providers") || e.Args.Contains("--verify-q-compare") || e.Args.Contains("--verify-notification-click"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!Infrastructure.AppDataPaths.IsPreview) { Shutdown(2); return; }
            try { if (e.Args.Contains("--verify-notification-motion")) await Infrastructure.UpgradeVerification.RunSutraAsync(notificationMotionOnly: true); else if (e.Args.Contains("--verify-graphite")) await Infrastructure.UpgradeVerification.RunSutraAsync(graphiteOnly: true); else if (e.Args.Contains("--verify-mascot-bulb")) await Infrastructure.UpgradeVerification.RunSutraAsync(mascotOnly: true); else if (e.Args.Contains("--verify-composer-keys")) await Infrastructure.UpgradeVerification.RunSutraAsync(composerOnly: true); else if (e.Args.Contains("--verify-command-panels")) await Infrastructure.UpgradeVerification.RunSutraAsync(commandPanelsOnly: true); else if (e.Args.Contains("--verify-sutra-update")) await Infrastructure.UpgradeVerification.RunSutraAsync(); else await Infrastructure.UpgradeVerification.RunAsync(e.Args.Contains("--verify-q-providers"), e.Args.Contains("--verify-q-compare"), e.Args.Contains("--verify-notification-click")); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(Infrastructure.AppDataPaths.Root); File.WriteAllText(Path.Combine(Infrastructure.AppDataPaths.Root, "verification-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        _log = new LoggingService();
        _log.Info($"Application startup entered: pid={Environment.ProcessId}");
        var openSettingsFromCommandLine = e.Args
            .Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase));
        var showIslandFromCommandLine = e.Args
            .Any(arg => string.Equals(arg, "--show-island", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new Mutex(true, "Local\\DynamicIsland.Windows.SingleInstance" + Infrastructure.AppDataPaths.InstanceSuffix, out var firstInstance);
        if (!firstInstance)
        {
            _log.Info("Another instance owns the single-instance lock; asking it to open Settings.");
            // Double-clicking the executable should reveal the running app too.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSettingsSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(_showSettingsSignal, (_, _) =>
        {
            if (!_isShuttingDown && !Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(() => { if (!_isShuttingDown) ShowSettings(); });
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        _settingsService = new SettingsService(_log);
        _log.Info("Loading application settings.");
        try
        {
            _settings = await _settingsService.LoadAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException ex)
        {
            _log.Error("Application settings did not load asynchronously within 10 seconds; retrying synchronously.", ex);
            _settings = _settingsService.LoadSynchronously();
        }
        _log.Info("Application settings loaded.");
        _log.SetDebugEnabled(_settings.DebugLogging);
        _log.Info("Application starting");
        _tray = new TrayService(_settings, ShowSettings,
            () =>
            {
                if (_islandViewModel is not null && _qScreen is not null)
                    _ = _islandViewModel.StartChatAsync();
            },
            Recenter, SaveAndApplyAsync, ShutdownApplication, ToggleFocus,
            () => _settings?.FocusModeEnabled ?? false);
        _log.Info("System tray icon initialized");

        DispatcherUnhandledException += (_, args) =>
        {
            _log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _log.Error("Unhandled application exception", args.ExceptionObject as Exception);
        };

        _startupService = new StartupService(_log);
        _startupService.SetEnabled(_settings.LaunchOnStartup);

        _media = new MediaSessionService(_log);
        _audio = new AudioSessionService(_log);
        _battery = new BatteryService();
        _clock = new ClockService();
        _timerAlarm = new TimerAlarmService(_log);
        _theme = new ThemeService();
        ApplyGlobalTheme();
        _theme.SystemThemeChanged += (_, _) => Dispatcher.BeginInvoke(ApplyGlobalTheme);
        _airPods = new AirPodsService(_log);
        _weather = new WeatherService(_log);
        _sysMon = new SystemMonitorService();
        _spectrum = new AudioSpectrumService(_log);
        _stocks = new StocksService(_log);
        _calendar = new CalendarService(_log);
        _notifications = new NotificationListenerService(_log);
        _notificationHistory = new NotificationHistoryService(_log);
        _privacy = new PrivacySensorService(_log);
        _clipboard = new ClipboardService(_log);
        _qScreen = new ScreenContextService(_log);
        _qSpeech = new SpeechInputService(_log);
        _qSecrets = new DpapiSecretStore(_log);
        _codexClient = new CodexAppServerClient("1.0.6", log: _log);
        _codexAccount = new CodexAccountCoordinator(_codexClient, _log);
        var providers = new QProviderRegistry([
            new OpenAiQProvider(), new AnthropicQProvider(), new GeminiQProvider(), new GroqQProvider(),
            new XaiQProvider(), new OpenRouterQProvider(), new DeepSeekQProvider(), new OllamaQProvider(),
            new CodexQProvider(_codexClient)
        ]);
        _qSession = new QSessionController(providers);
        _battery.LowBattery += (_, pct) => Dispatcher.BeginInvoke(() =>
            _tray?.ShowNotification("Battery low", $"{pct}% remaining — plug in soon."));
        _position = new WindowPositionService();
        _islandViewModel = new IslandViewModel(_settings, _media, _audio, _battery, _clock, _timerAlarm, _theme,
            _weather, _sysMon, _spectrum, _stocks, _calendar, _notifications, _privacy, _notificationHistory,
            _qSession, _qScreen, _qSpeech, _qSecrets, _codexAccount, _airPods, _settingsService, providers);
        _islandViewModel.AttachClipboard(_clipboard);
        _voice = new AssistantVoiceController(_qSpeech, _qSpeech, _qSpeech, _islandViewModel.SubmitVoiceTurnAsync, CancelAssistantResponse);
        _islandViewModel.AttachVoice(_voice);
        _keyboardLocks = new KeyboardLockService(Dispatcher, _log);
        _keyboardLocks.Changed += _islandViewModel.ShowKeyboardLock;
        _timerViewModel = new TimerAlarmViewModel(_timerAlarm, _settings.Use24HourClock);
        _islandWindow = new IslandWindow(_islandViewModel, _timerViewModel, _position, _settingsService, _log, _qScreen);
        _islandWindow.OpenSettingsRequested += (_, _) => ShowSettings();
        _screenshots=new ScreenshotTool(_islandWindow,_qScreen,()=>_islandViewModel.IsReducedMotion);
        _islandWindow.ScreenshotRequested+=(_,_)=>_screenshots.Open();
        _islandWindow.OpenQSettingsRequested += (_, _) => { ShowSettings(); _settingsWindow?.OpenQSettings(); };
        _islandWindow.OpenClipboardRequested += (_, _) => _ = ShowClipboardAsync();
        _islandWindow.RecenterRequested += (_, _) => Recenter();
        _islandWindow.Closed += (_, _) => { if (!_isShuttingDown) ShutdownApplication(); };

        _settingsViewModel = new SettingsViewModel(_settings, _settingsService, _startupService,
            ApplySettings, Recenter, () => _settingsWindow?.Hide(), _qSecrets, providers, _codexAccount);
        _islandViewModel.QProviderSelectionChanged += (_, _) => _settingsViewModel.RefreshQProviderControls();
        _media.AvailableAppsChanged += (_, apps) => Dispatcher.BeginInvoke(() =>
            _settingsViewModel.SetAvailableApps(apps));

        _timerAlarm.EventRaised += (_, args) => Dispatcher.BeginInvoke(() =>
            _tray.ShowNotification(args.Title, args.Message));

        _islandWindow.ContentRendered += (_, _) => _log.Debug($"Island first render: startupMs={_startupWatch.Elapsed.TotalMilliseconds:0}.");
        _islandWindow.Show();
        if (!Infrastructure.AppDataPaths.IsPreview) _keyboardLocks.Start();
        _notifications.Configure(TimeSpan.FromSeconds(_settings.NotificationPollSeconds));
        if (_settings.ShowNotifications) _ = _notifications.StartAsync();
        _ = _islandViewModel.ConfigureVoiceAsync();
        if (showIslandFromCommandLine) Recenter();
        // Provider discovery starts on demand when the assistant is opened.
        _hotkeys = new GlobalHotkeyService(_log);
        _hotkeys.Attach(_islandWindow);
        _hotkeys.Register("Expand/collapse", Interop.NativeMethods.HotkeyModifierControl | Interop.NativeMethods.HotkeyModifierAlt, 0x20,
            () => _islandViewModel.ToggleExpandedCommand.Execute(null));
        _hotkeys.Register("Mute", Interop.NativeMethods.HotkeyModifierControl | Interop.NativeMethods.HotkeyModifierAlt, (uint)'M',
            () => _islandViewModel.ToggleMuteCommand.Execute(null));
        _hotkeys.Register("Timer", Interop.NativeMethods.HotkeyModifierControl | Interop.NativeMethods.HotkeyModifierAlt, (uint)'T',
            ToggleDefaultTimer);
        _hotkeys.Register("Focus mode", Interop.NativeMethods.HotkeyModifierControl | Interop.NativeMethods.HotkeyModifierAlt, (uint)'F',
            ToggleFocus);
        _hotkeys.Register("Open settings", GlobalHotkeyService.SettingsModifiers, GlobalHotkeyService.SettingsKey,
            ShowSettings);
        RegisterQHotkey();
        _ = Dispatcher.BeginInvoke(async () =>
        {
            PrepareCommandPalette();
            await _paletteWindow!.PrimePresentationAsync();
            RegisterCommandPaletteHotkey();
            RegisterScreenshotHotkey();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        _clock.Start();
        _battery.Start();
        _audio.Start();
        _timerAlarm.Start();
        _privacy.Start();
        _airPods?.Start();
        _weather.Start();
        _stocks.Start();
        ApplyLiveActivitySettings();
        // First-run onboarding: open the (new) settings so people can explore what's customisable.
        if (!_settings.HasOnboarded || openSettingsFromCommandLine)
        {
            if (!_settings.HasOnboarded)
            {
                _settings.HasOnboarded = true;
                await _settingsService.SaveAsync(_settings);
            }
            ShowSettings();
        }
        // WinRT media discovery is independent of the requested Settings window.
        if (!_isShuttingDown) await _media.StartAsync();
    }

    private void ShowSettings()
    {
        if (_settingsViewModel is null) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel, _islandViewModel!, _islandWindow!);
            _settingsWindow.OpenTimerRequested += (_, _) => ShowTimerAlarm();
            _settingsWindow.IsVisibleChanged += (_, _) => UpdateKeepExpanded();
            _settingsWindow.Closing += (_, args) =>
            {
                if (_isShuttingDown) return;
                args.Cancel = true;
                _ = _settingsViewModel.SaveAsync();
                _settingsWindow.Hide();
            };
        }
        _settingsWindow.Show();
        _settingsViewModel.RefreshQProviderControls();
        _settingsViewModel.RefreshRuntimePreferences();
        _settingsViewModel.RefreshPositionControls();
        _settingsWindow.Activate();
        FadeIn(_settingsWindow);
    }

    private static void FadeIn(Window window)
    {
        window.Opacity = 0;
        window.BeginAnimation(Window.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void ShowTimerAlarm()
    {
        _islandWindow?.ShowTimerPanel();
    }

    private async Task SaveAndApplyAsync()
    {
        if (_settingsService is null || _settings is null) return;
        await _settingsService.SaveAsync(_settings);
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_settings is null) return;
        _log?.SetDebugEnabled(_settings.DebugLogging);
        RegisterQHotkey();
        RegisterCommandPaletteHotkeyIfChanged();
        RegisterScreenshotHotkey();
        ApplyGlobalTheme();
        _islandViewModel?.ApplySettings();
        if (_islandViewModel is not null) _ = _islandViewModel.ConfigureVoiceAsync();
        _islandWindow?.ApplySettings();
        ApplyLiveActivitySettings();
        _tray?.SyncChecks();
    }

    // Re-registers only when the stored combo actually changed (or a previous attempt failed),
    // so ordinary settings tweaks don't churn the global hotkey table.
    private void RegisterCommandPaletteHotkeyIfChanged()
    {
        if (_settings is null) return;
        if (_paletteHotkeyId != 0 &&
            string.Equals(_registeredPaletteHotkey, _settings.CommandPaletteHotkey, StringComparison.OrdinalIgnoreCase))
            return;
        RegisterCommandPaletteHotkey();
    }

    private void RegisterScreenshotHotkey()
    {
        if(_settings is null || _settingsViewModel is null || _hotkeys is null)return;
        var text=_settings.ScreenshotHotkey;
        if(_screenshotHotkeyId!=0 && _registeredScreenshotHotkey==text)return;
        if(!Infrastructure.HotkeyParser.TryParse(text,out var modifiers,out var key,out var canonical,out var error))
        { _settingsViewModel.SetScreenshotStatus(error??"Invalid shortcut.");return; }
        if(_screenshotHotkeyId!=0)_hotkeys.Unregister(_screenshotHotkeyId);
        _screenshotHotkeyId=_hotkeys.Register("Screenshot",modifiers | 0x4000,key,()=>_screenshots?.Open());
        _registeredScreenshotHotkey=canonical;
        _settingsViewModel.SetScreenshotStatus(_screenshotHotkeyId==0?"Shortcut unavailable; choose another combination. Screenshot is also in the island menu.":"Opens the capture overlay · Region by default · Esc cancels");
    }

    private void RegisterCommandPaletteHotkey()
    {
        if (_paletteWindow?.IsPresentationReady != true) return;
        if (_hotkeys is null || _settings is null || _settingsViewModel is null) return;
        if (_paletteHotkeyId != 0)
        {
            _hotkeys.Unregister(_paletteHotkeyId);
            _paletteHotkeyId = 0;
        }
        string? parseError = null;
        if (!Infrastructure.HotkeyParser.TryParse(_settings.CommandPaletteHotkey,
                out var modifiers, out var key, out var canonical, out parseError))
        {
            // Fall back to the default so the palette always has a way to open.
            if (!Infrastructure.HotkeyParser.TryParse("Ctrl+Alt+K",
                    out modifiers, out key, out canonical, out _))
            {
                _settingsViewModel.SetCommandPaletteStatus(parseError ?? "Invalid command palette shortcut.");
                return;
            }
            _settings.CommandPaletteHotkey = canonical;
            parseError ??= "Invalid shortcut — using Ctrl + Alt + K.";
        }
        else
        {
            _settings.CommandPaletteHotkey = canonical;
        }
        _paletteHotkeyId = _hotkeys.Register("Command palette", modifiers, key, ToggleCommandPalette);
        _registeredPaletteHotkey = canonical;
        if (_paletteHotkeyId == 0)
        {
            _settingsViewModel.SetCommandPaletteStatus(
                Infrastructure.AppDataPaths.IsPreview
                    ? "Shortcut inactive in preview mode."
                    : $"Unavailable — {Infrastructure.HotkeyParser.Display(canonical)} may be used by another app.");
        }
        else
        {
            _settingsViewModel.SetCommandPaletteStatus(parseError ?? "");
        }
    }

    private void ToggleCommandPalette()
    {
        if (_paletteWindow is { IsVisible: true, IsPriming: false, IsDismissing: false })
        {
            _paletteWindow.Dismiss();
            return;
        }
        _ = OpenCommandPaletteAsync();
    }

    private void PrepareCommandPalette()
    {
        if (_paletteWindow is not null || _islandViewModel is null || _timerViewModel is null || _media is null || _audio is null || _timerAlarm is null) return;
        _paletteRunner ??= new CommandRunner(_islandViewModel, _media, _audio, _timerAlarm, _timerViewModel, this, clipboard: _clipboard);
        _islandViewModel.AttachAssistantControls(new AssistantIslandControls(_paletteRunner, async () =>
        {
            if (_startupService is not null && _settings is not null && _startupService.IsEnabled() != _settings.LaunchOnStartup)
            { _startupService.SetEnabled(_settings.LaunchOnStartup); _settings.LaunchOnStartup = _startupService.IsEnabled(); }
            await SaveAndApplyAsync();
            _settingsViewModel?.RefreshRuntimePreferences();
        }));
        _paletteViewModel ??= new CommandPaletteViewModel(_paletteRunner, _settings, () => _settingsService!.SaveAsync(_settings!));
        _paletteWindow = new CommandPaletteWindow(_paletteViewModel, () => _settings?.ShowIslandInScreenshots ?? false);
        new System.Windows.Interop.WindowInteropHelper(_paletteWindow).EnsureHandle();
        _paletteWindow.Measure(new System.Windows.Size(_paletteWindow.Width, double.PositiveInfinity));
        _paletteWindow.WarmLayout();
        _paletteWindow.StartPresentationMaintenance();
    }
    private async Task OpenCommandPaletteAsync(string? initialQuery = null)
    {
        if (_islandWindow is null || _islandViewModel is null || _timerViewModel is null ||
            _media is null || _audio is null || _timerAlarm is null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PrepareCommandPalette();
        var window = _paletteWindow!;
        await window.PrimePresentationAsync();
        window.PrepareForShow();
        window.PresentFromIsland(_islandWindow.PaletteAnchorBounds, _islandWindow.PaletteWorkArea, _islandViewModel.IsReducedMotion);
        window.Activate();
        window.FocusQuery();
        if (initialQuery is not null) { _paletteViewModel!.Query = initialQuery; await _paletteViewModel.ExecuteAsync(); }
        _ = Dispatcher.BeginInvoke(() => _log?.Debug($"Palette presented: elapsedMs={watch.Elapsed.TotalMilliseconds:0}."), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void CancelAssistantResponse()
    { if (Dispatcher.CheckAccess()) _islandViewModel?.CancelQ(); else Dispatcher.BeginInvoke(() => _islandViewModel?.CancelQ()); }
    void ICommandPaletteHost.OpenChat(string? question, bool useScreen) => Dispatcher.BeginInvoke(async () =>
    { await _islandViewModel!.StartChatAsync(question, useScreen); _islandWindow?.FocusAssistantComposer(); });
    void ICommandPaletteHost.ShowSettingsSearch(string query) => Dispatcher.BeginInvoke(() => { ShowSettings(); _settingsWindow?.OpenSearch(query); });
    void ICommandPaletteHost.RecenterIsland() => Recenter();
    Task ICommandPaletteHost.OpenClipboardAsync() => ShowClipboardAsync();
    Task ICommandPaletteHost.OpenCommandPanelAsync(string command) => OpenCommandPaletteAsync(command);
    void ICommandPaletteHost.ShowNotificationHistory() => Dispatcher.BeginInvoke(() => _islandWindow?.ShowHistory());
    async Task ICommandPaletteHost.SetNotificationsAsync(bool enabled)
    { _settings!.ShowNotifications = enabled; await SaveAndApplyAsync(); }
    void ICommandPaletteHost.ShowSettings() => Dispatcher.BeginInvoke(ShowSettings);
    void ICommandPaletteHost.ShowScreenshotTool() => Dispatcher.BeginInvoke(async () =>
    {
        // Let the palette finish its existing dismissal morph before opening the capture UI.
        await Task.Delay(200);
        if (!_isShuttingDown) _screenshots?.Open();
    });

    void ICommandPaletteHost.ShowTimerPanel() => Dispatcher.BeginInvoke(() =>
    {
        _islandWindow?.ShowTimerPanel();
        _islandWindow?.FocusTimerPanel();
    });

    void ICommandPaletteHost.ShowAlarmPanel() => Dispatcher.BeginInvoke(() =>
    {
        if (_islandWindow is null) return;
        _islandWindow.ShowTimerPanel();
        _islandWindow.ShowAlarmTab();
        _islandWindow.FocusTimerPanel();
    });

    void ICommandPaletteHost.ShowStopwatchPanel() => Dispatcher.BeginInvoke(() =>
    {
        if (_islandWindow is null) return;
        _islandWindow.ShowTimerPanel();
        _islandWindow.ShowStopwatchTab();
        _islandWindow.FocusTimerPanel();
    });

    void ICommandPaletteHost.OpenQ(string? question, QMode mode, bool compare) => Dispatcher.BeginInvoke(async () =>
    {
        if (_islandViewModel is null || _islandWindow is null) return;
        await _islandViewModel.StartChatAsync();
        _islandViewModel.SetQMode(mode);
        _islandViewModel.QCompareEnabled = compare;
        if (question is not null) _islandWindow.PrefillQPrompt(question);
        else _islandWindow.FocusAssistantComposer();
    });

    private void RegisterQHotkey()
    {
        if (_hotkeys is null || _settings is null || _islandViewModel is null || _qScreen is null) return;
        _qShortcuts ??= new QShortcutService(_hotkeys, _log!, Dispatcher,
            () => _ = _islandViewModel.StartQAsync(_qScreen.LastForegroundTarget, _settings.QHotkeyShortcut));
        _qShortcuts.Configure(_settings.QEnabled
            ? QActivationPolicy.Resolve(_settings.QActivationKeys, _settings.QActivationHotkey)
            : QActivationShortcuts.None);
        _settingsViewModel?.SetQActivationStatus(_qShortcuts.Status);
    }

    private bool _liveSettingsApplied, _clipboardEnabled;
    private void ApplyLiveActivitySettings()
    {
        if (_settings is null) return;
        _media?.ConfigurePolling(_settings.MediaPollMilliseconds);
        _audio?.ConfigurePolling(_settings.AudioPollMilliseconds);
        _sysMon?.ConfigurePolling(_settings.SystemMonitorPollMilliseconds);
        _timerAlarm?.ConfigurePolling(_settings.TimerPollMilliseconds);
        _clock?.ConfigurePolling(_settings.ClockPollSeconds);
        _weather?.Configure(_settings.WeatherLocation, _settings.WeatherFahrenheit);
        _weather?.SetEnabled(_settings.ShowWeather);
        _weather?.ConfigurePolling(TimeSpan.FromMinutes(_settings.WeatherPollMinutes));
        _stocks?.Configure(_settings.StockSymbols);
        _stocks?.SetEnabled(_settings.ShowStocks);
        _stocks?.ConfigurePolling(TimeSpan.FromMinutes(_settings.StocksPollMinutes));
        if (_battery is not null)
        {
            _battery.LowThreshold = _settings.LowBatteryThreshold;
            _battery.WarningsEnabled = _settings.LowBatteryWarning;
            _battery.ConfigurePolling(TimeSpan.FromSeconds(_settings.BatteryPollSeconds));
        }
        _privacy?.ConfigurePolling(TimeSpan.FromSeconds(_settings.PrivacyPollSeconds));
        _theme?.ConfigurePolling(TimeSpan.FromSeconds(_settings.ThemePollSeconds));
        _airPods?.ConfigurePolling(TimeSpan.FromSeconds(_settings.AirPodsPollSeconds));
        if (_settings.ShowSystemMonitor || _settings.ShowRamInCompact) _sysMon?.Start();
        else _sysMon?.Stop();
        if (_settings.RealAudioSpectrum) _spectrum?.Start();
        else _spectrum?.Stop();
        _calendar?.ConfigurePolling(TimeSpan.FromMinutes(_settings.CalendarPollMinutes));
        if (_settings.ShowNextMeeting) _ = _calendar!.StartAsync(_liveSettingsApplied); else _calendar!.Stop();
        _notifications?.Configure(TimeSpan.FromSeconds(_settings.NotificationPollSeconds));
        if (_settings.ShowNotifications) _ = _notifications!.StartAsync(_liveSettingsApplied); else _notifications!.Stop();
        if (!_liveSettingsApplied || _clipboardEnabled != _settings.ShowClipboard) { _clipboardEnabled = _settings.ShowClipboard; _clipboard!.Configure(_clipboardEnabled); }
        _liveSettingsApplied = true;
    }

    // Use the same image-aware history from the island menu and command palette.
    private Task ShowClipboardAsync() => OpenCommandPaletteAsync("clipboard");

    // Pin the island open while any settings window is visible so live size/appearance edits are visible.
    private void UpdateKeepExpanded()
    {
        if (_islandViewModel is null) return;
        var keep = _settingsWindow?.IsVisible == true;
        _islandViewModel.KeepExpanded = keep;
        _islandWindow?.SetSettingsWindowOpen(keep);
        if (!keep) _islandViewModel.IsExpanded = false;
    }

    private void Recenter()
    {
        if (_islandWindow is null || _position is null || _settings is null) return;
        _islandWindow.ForceShow();
        _position.Recenter(_islandWindow, _settings);
        _settingsViewModel?.RefreshPositionControls();
        _ = _settingsService?.SaveAsync(_settings);
    }

    private void ApplyGlobalTheme()
    {
        if (_theme is null || _settings is null) return;
        var dark = _theme.IsDark(_settings.Theme);
        SetBrush("SettingsBackgroundBrush", dark ? "#11151C" : "#F3F5F9");
        SetBrush("SettingsHeaderBrush", dark ? "#181D26" : "#F9FBFF");
        SetBrush("SettingsCardBrush", dark ? "#1B222D" : "#FFFFFFFF");
        SetBrush("SettingsCardBorderBrush", dark ? "#354052" : "#16000000");
        SetBrush("SettingsTextBrush", dark ? "#F1F5FB" : "#172033");
        SetBrush("SettingsMutedBrush", dark ? "#9AA8BB" : "#68758A");
        SetBrush("SettingsInputBrush", dark ? "#242D3A" : "#FFFFFFFF");
        SetBrush("SettingsSubtleButtonBrush", dark ? "#303A49" : "#E5EAF1");
    }

    private void SetBrush(string key, string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Resources[key] = brush;
    }

    private void ShutdownApplication()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        _log?.Info("Application shutting down");
        _paletteWindow?.Close();
        _screenshots?.Dispose();
        _tray?.Dispose();
        _timerViewModel?.Dispose();
        _settingsWindow?.Close();
        _islandViewModel?.Dispose();
        _voice?.Dispose();
        _keyboardLocks?.Dispose();
        _qSession?.Dispose();
        _codexClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _qSpeech?.Dispose();
        _weather?.Dispose();
        _sysMon?.Dispose();
        _spectrum?.Dispose();
        _stocks?.Dispose();
        _calendar?.Dispose();
        _notifications?.Dispose();
        _notificationHistory?.FlushAsync().GetAwaiter().GetResult();
        _qShortcuts?.Dispose();
        _hotkeys?.Dispose();
        _showSettingsRegistration?.Unregister(null);
        _showSettingsSignal?.Dispose();
        _airPods?.Dispose();
        _privacy?.Dispose();
        _media?.Dispose();
        _audio?.Dispose();
        _battery?.Dispose();
        _clock?.Dispose();
        _timerAlarm?.Dispose();
        _theme?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Shutdown();
    }

    private void ToggleDefaultTimer()
    {
        if (_timerAlarm is null) return;
        var displayed = _islandViewModel?.DisplayTimer;
        if (displayed is { Phase: TimerPhase.Running }) _timerAlarm.PauseTimer(displayed.Id);
        else if (displayed is { Phase: TimerPhase.Paused }) _timerAlarm.ResumeTimer(displayed.Id);
        else _timerAlarm.StartTimer(TimeSpan.FromMinutes(10), "Shortcut timer");
    }

    private void ToggleFocus()
    {
        _islandViewModel?.ToggleFocusCommand.Execute(null);
        ApplySettings();
    }
}
