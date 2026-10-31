using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Services.Q;
using Microsoft.Win32;

namespace DynamicIsland.Windows.Views;

public partial class IslandWindow : Window
{
    private readonly IslandViewModel _viewModel;
    private readonly WindowPositionService _position;
    private readonly SettingsService _settingsService;
    private readonly LoggingService _log;
    private readonly TimerAlarmViewModel _timerViewModel;
    private readonly ScreenContextService _qScreen;
    private readonly HashSet<System.Windows.Controls.ContextMenu> _openIslandMenus = [];
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _timerPanelLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _sourceReady;
    private bool _initialLayoutStabilized;
    private bool _mainExpansionPending;
    private bool _dragging;
    private bool _dimmed;
    private bool _hiddenForFullscreen;
    private bool _hiddenForCapture;
    private bool _settingsWindowOpen;
    private bool _scrubbing;
    private bool _timerPanelOpen;
    private bool _timerPanelHostShown;
    private bool _timerPanelHostAnimating;
    private bool _timerPanelClosing;
    private int _timerPanelAnimationGeneration;
    private bool _suppressExpandedAnimation;
    private bool _liveTimerVisible;
    private bool _lastIsStatsStyle;
    private bool _lastShowAirPodsCard;
    private bool _lastShowWidgetsPanel;
    private bool _urgentAlertVisible;
    private bool _qFollowLatest = true;

    private const double TimerPanelNormalCornerRadius = 28d;
    private readonly RectangleGeometry _pillClip = new();
    private readonly RectangleGeometry _timerPanelClip = new();

    private double TimerPanelWidth => WindowSizingPolicy.BoundedDimension(930, AvailableWorkArea.Width);
    // The timer editor contains a complete timer card plus a stopwatch card. A 520-DIP shell
    // leaves the editor viewport just short of the first card on smaller displays, making the
    // panel look like it is cut off even though its scrollbar is active. Use the available screen
    // height more effectively while retaining a small safety margin for the taskbar/edge.
    private double TimerPanelHeight => WindowSizingPolicy.BoundedDimension(619, AvailableWorkArea.Height, 24);
    private (double Width, double Height) AvailableWorkArea => _position.AvailableSize(this, _viewModel.Settings);
    private const double LiveTimerExtraHeight = 86d;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ScreenshotRequested;
    public void RequestScreenshot() => ScreenshotRequested?.Invoke(this,EventArgs.Empty);
    internal void SetCaptureHidden(bool hidden) => _hiddenForCapture = hidden;
    private void ScreenshotButton_Click(object sender,RoutedEventArgs e)=>RequestScreenshot();
    public event EventHandler? OpenQSettingsRequested;
    public event EventHandler? OpenClipboardRequested;
    public event EventHandler? RecenterRequested;

    public FrameworkElement ExpandedPreviewVisual => GlassShell;

    public IslandWindow(IslandViewModel viewModel, TimerAlarmViewModel timerViewModel, WindowPositionService position, SettingsService settingsService, LoggingService log, ScreenContextService qScreen)
    {
        InitializeComponent();
        GlassShell.Clip = _pillClip;
        TimerPanelHost.Clip = _timerPanelClip;
        DataContext = _viewModel = viewModel;
        SyncNotificationBanner();
        ApplyQTheme();
        // The timer panel is opaque; keep the glass behind the main shell during its morph.
        _frostBackdrop = Infrastructure.NativeFrostBackdrop.Attach(this,() => (GlassShell,new Rect(GlassShell.RenderSize),GlassShell.CornerRadius.TopLeft), (active,inset) => { _viewModel.NativeFrostInset=inset; _viewModel.NativeFrostActive=active; },()=>_viewModel.Settings.ShowIslandInScreenshots);
        _timerViewModel = timerViewModel;
        TimerPanelContent.DataContext = timerViewModel;
        LiveTimerStrip.DataContext = timerViewModel;
        _liveTimerVisible = timerViewModel.ShowLiveTimer;
        LiveTimerStrip.Visibility = _liveTimerVisible ? Visibility.Visible : Visibility.Collapsed;
        _lastIsStatsStyle = viewModel.IsStatsStyle;
        _lastShowAirPodsCard = viewModel.ShowAirPodsCard;
        _lastShowWidgetsPanel = viewModel.ShowWidgetsPanel;
        _position = position;
        _settingsService = settingsService;
        _log = log;
        _qScreen = qScreen;
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_timerPanelOpen && OutputDevicePopup?.IsOpen != true && _openIslandMenus.Count == 0 && !GlassShell.IsMouseOver && !_dragging && !_viewModel.PinExpanded
                && QMenuButton?.ContextMenu?.IsOpen != true
                && QProviderSelector?.IsDropDownOpen != true && QModelSelector?.IsDropDownOpen != true
                && QEffortSelector?.IsDropDownOpen != true
                ) _viewModel.IsExpanded = false;
        };
        _timerPanelLeaveTimer.Tick += (_, _) => TryAutoCloseTimerPanel();
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            if (_viewModel.Settings.IdleDimming && !GlassShell.IsMouseOver) SetDimmed(true);
        };
        _fullscreenTimer.Tick += (_, _) => { _viewModel.InteractionProtected = _timerPanelOpen || OutputDevicePopup.IsOpen || _openIslandMenus.Count > 0 || IsKeyboardFocusWithin || Mouse.Captured is not null; _viewModel.VisualizerSurfaceVisible = IsVisible && !_hiddenForFullscreen && !_viewModel.IsQActive; CheckFullscreen(); CheckFollowScreen(); EnsureHealthy(); _qScreen.RememberForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle); };
        _fullscreenTimer.Start();
        SourceInitialized += (_, _) =>
        {
            _sourceReady = true;
            _position.ApplyWindowStyles(this, _viewModel.Settings, compact: true);
            ApplyCaptureAffinity();
            ApplyLayout(animate: false);
            ApplyFrost();
        };
        Loaded += (_, _) =>
        {
            ApplyLayout(animate: false);
            ApplyFrost();
            ApplyRoundedShellClip();
            UpdateQPromptComposer();
            // Re-fit the pill whenever the expanded content's size changes (live CPU/RAM/weather/title text).
            ExpandedContent.SizeChanged += (_, _) => UpdateAutoGrow();
            QContent.SizeChanged += (_, _) => UpdateAutoGrow();
            StatsExpandedContent.SizeChanged += (_, _) => UpdateAutoGrow();
            StatsOverlay.SizeChanged += (_, _) => UpdateAutoGrow();
            GlassShell.SizeChanged += (_, _) => { ApplyRoundedShellClip(); UrgentAlertStrip.Margin = new Thickness(12, GlassShell.ActualHeight + 12, 12, 0); UrgentAlertStrip.MaxWidth = Math.Max(1, Math.Min(650, ActualWidth - 24)); PositionNotifBannerUnderShell(); };
            TimerPanelHost.SizeChanged += (_, _) => ApplyRoundedTimerPanelClip();
            SyncUrgentAlertStrip(animate: false);
            // Let the first real render commit the compact HWND/content geometry before accepting
            // an expansion request. Without this warm-up, the first hover/click can race WPF's
            // initial measure and be applied as a layout snap instead of a visible morph.
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsLoaded) return;
                ApplyLayout(animate: false);
                UpdateLayout();
                _initialLayoutStabilized = true;
                SyncNotificationBanner();
                ApplyLayout(animate: false);
                _viewModel.SetBannerPresentationReady();
                var shouldExpand = _mainExpansionPending;
                _mainExpansionPending = false;
                if (shouldExpand && !_timerPanelOpen && !_viewModel.IsExpanded && GlassShell.IsMouseOver)
                    _viewModel.IsExpanded = true;
            }, DispatcherPriority.Render);
        };
        GotKeyboardFocus += (_, _) => _viewModel.InteractionProtected = true;
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => _viewModel.InteractionProtected = _timerPanelOpen || IsKeyboardFocusWithin || Mouse.Captured is not null);
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(ScheduleTimerPanelAutoClose);
        GotMouseCapture += (_, _) => _viewModel.InteractionProtected = true;
        LostMouseCapture += (_, _) => _viewModel.InteractionProtected = _timerPanelOpen || IsKeyboardFocusWithin;
        InputManager.Current.PreProcessInput += OnOutputPickerInput;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.NotificationGroupOpenRequested += OnNotificationGroupOpen;
        _timerViewModel.PropertyChanged += TimerViewModelOnPropertyChanged;
        _timerViewModel.ShowAlarmTabRequested += TimerViewModel_ShowAlarmTabRequested;
        SystemEvents.DisplaySettingsChanged += SystemEventsOnDisplaySettingsChanged;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => ApplyLayout(animate: false), DispatcherPriority.Loaded);
        SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
        Closed += (_, _) =>
        {
            InputManager.Current.PreProcessInput -= OnOutputPickerInput;
            SystemEvents.DisplaySettingsChanged -= SystemEventsOnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= SystemEventsOnPowerModeChanged;
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            _viewModel.NotificationGroupOpenRequested -= OnNotificationGroupOpen;
            _timerViewModel.PropertyChanged -= TimerViewModelOnPropertyChanged;
            _timerViewModel.ShowAlarmTabRequested -= TimerViewModel_ShowAlarmTabRequested;
            _fullscreenTimer.Stop();
            _idleTimer.Stop();
            _timerPanelLeaveTimer.Stop();
            _notificationAnimationGeneration++;
            NotifBanner.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        };
    }

    public void ApplySettings()
    {
        _fullscreenTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_viewModel.Settings.WindowPollSeconds, 1, 10));
        _position.ApplyWindowStyles(this, _viewModel.Settings, _viewModel.IsCompact);
        ApplyCaptureAffinity();
        ApplyLayout(animate: false);
        ApplyRoundedShellClip();
        ApplyFrost();
        EnsureHealthy();
    }

    // WPF Border.ClipToBounds clips to a rectangle, not to CornerRadius. Without an explicit
    // rounded geometry, the media artwork below Q can show through the transparent corner pixels.
    // Apply the same physical clip to the entire shell so every presentation layer shares one mask.
    private void ApplyRoundedShellClip()
    {
        var width = GlassShell.ActualWidth;
        var height = GlassShell.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var requested = _viewModel.IslandCornerRadius.TopLeft;
        var radius = Math.Clamp(requested, 0d, Math.Min(width, height) / 2d);
        UpdateRoundedClip(_pillClip, width, height, radius);
    }

    private void ApplyRoundedTimerPanelClip()
    {
        var width = TimerPanelHost.ActualWidth;
        var height = TimerPanelHost.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var radius = Math.Clamp(TimerPanelHost.CornerRadius.TopLeft, 0d, Math.Min(width, height) / 2d);
        UpdateRoundedClip(_timerPanelClip, width, height, radius);
    }

    private static void UpdateRoundedClip(RectangleGeometry clip, double width, double height, double radius)
    {
        // Keep one geometry attached to the compositor instead of allocating/replacing the
        // clip on every frame of a resize.
        clip.Rect = new Rect(0, 0, width, height);
        clip.RadiusX = clip.RadiusY = radius;
    }

    private static void SetSurfaceInMotion(FrameworkElement surface, bool moving)
    {
        // Pixel alignment keeps settled text sharp, but quantizes the slow tail of a morph
        // into visible steps. Permit fractional layout/translation only during the transition.
        surface.UseLayoutRounding = !moving;
        surface.SnapsToDevicePixels = !moving;
    }

    // Both surfaces use the same opening cadence and content handoff.
    private TimeSpan PillMorphDuration(bool expanded) => TimeSpan.FromMilliseconds(expanded
        ? (_viewModel.Settings.AnimationIntensity == AnimationIntensity.Expressive ? 440d : 360d)
        : (_viewModel.Settings.AnimationIntensity == AnimationIntensity.Expressive ? 300d : 230d));

    // A gentle, monotonic ease lets the island settle without a width overshoot or flash.
    private TimeSpan IslandMorphDuration(bool expanded) => TimeSpan.FromMilliseconds(expanded
        ? (_viewModel.Settings.AnimationIntensity == AnimationIntensity.Expressive ? 410d : 320d)
        : (_viewModel.Settings.AnimationIntensity == AnimationIntensity.Expressive ? 320d : 250d));
    private static IEasingFunction IslandMorphEase(bool expanded) => expanded
        ? new QuarticEase { EasingMode = EasingMode.EaseOut }
        : new CubicEase { EasingMode = EasingMode.EaseInOut };
    private static IEasingFunction PillMorphEase(bool expanded) => expanded
        ? new QuarticEase { EasingMode = EasingMode.EaseOut }
        : new CubicEase { EasingMode = EasingMode.EaseInOut };

    private void ApplyCaptureAffinity()
    {
        if (!_sourceReady) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        // Keep the Island visible in normal screenshots during the Q stabilization period. The
        // Q capture service still targets the remembered foreground window and never relies on
        // this affinity flag for context isolation.
        var affinity = _viewModel.Settings.ShowIslandInScreenshots
            ? Interop.NativeMethods.WdaNone
            : Interop.NativeMethods.WdaExcludeFromCapture;
        Interop.NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);
    }

    // Settings has its own live preview. The real transparent overlay must not cover it.
    public void SetSettingsWindowOpen(bool open)
    {
        _settingsWindowOpen = open;
        if (open) _frostBackdrop.SetSettingsSuppressed(true);
        if (_sourceReady)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero)
                Interop.NativeMethods.SetWindowPos(hwnd,
                    open ? Interop.NativeMethods.HwndNoTopmost : Interop.NativeMethods.HwndTopmost,
                    0, 0, 0, 0,
                    Interop.NativeMethods.SwpNoMove | Interop.NativeMethods.SwpNoSize | Interop.NativeMethods.SwpNoActivate);
        }
        if (!open) _frostBackdrop.SetSettingsSuppressed(false);
    }

    /// <summary>Force the island back into view (used by the tray "Recenter").</summary>
    public void ForceShow()
    {
        _fullscreenStreak = 0;
        _hiddenForFullscreen = false;
        Visibility = Visibility.Visible;
        GlassShell.BeginAnimation(OpacityProperty, null);
        GlassShell.Opacity = 1;
        _dimmed = false;
    }

    // Ensures no acrylic backdrop / window region is left applied. Real acrylic blur cannot be clipped
    // to the rounded pill on a layered (AllowsTransparency) window â€” it fills the whole rectangle and
    // reintroduces the square halo â€” so the frosting is done with WPF layers instead (see XAML).
    private void ApplyFrost()
    {
        if (!_sourceReady) return;
        _position.ApplyBackdropFrost(this, enable: false, _viewModel.IsDarkTheme);
    }

    private bool _lastQSurface, _lastQActive, _lastQComparison;
    private ThemePalette? _lastQPalette;
    private bool _lastQTranslucent;
    private double _lastQFrostInset;
    private Infrastructure.NativeFrostBackdrop _frostBackdrop = null!;
    private void ApplyQTheme()
    {
        var palette = _viewModel.QThemePalette;
        var translucent=_viewModel.Settings.Theme == DynamicIsland.Windows.Models.ThemeMode.Graphite && _viewModel.NativeFrostActive;
        if (palette == _lastQPalette && translucent == _lastQTranslucent && _lastQFrostInset==_viewModel.NativeFrostInset) return;
        _lastQFrostInset=_viewModel.NativeFrostInset;
        _lastQTranslucent=translucent;
        _lastQPalette = palette;
        var values = new Dictionary<string, string>
        {
            ["QPopupSurface"] = palette.Control, ["QSurface"] = palette.Surface, ["QCard"] = palette.Card, ["QControl"] = palette.Control,
            ["QHover"] = palette.Hover, ["QBorder"] = palette.Border, ["QText"] = palette.Text,
            ["QMuted"] = palette.Muted, ["QAccent"] = palette.Accent, ["QSelected"] = palette.Selected
        };
        foreach (var (key, hex) in values)
        {
            var color=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if(translucent) color.A = key switch { "QSurface" => 255, "QCard" => 96, "QControl" => 112, _ => color.A };
            System.Windows.Media.Brush brush = new SolidColorBrush(color);

            brush.Freeze(); Resources[key] = brush;
        }
    }
    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IslandViewModel.QThemePalette) or nameof(IslandViewModel.IsDarkTheme)) ApplyQTheme();
        if (e.PropertyName == nameof(IslandViewModel.QCanStop)) UpdateQPromptComposer();
        if (e.PropertyName == nameof(IslandViewModel.AccentBrush))
        {
            // Timer tab visuals are set in code because the active state is switched
            // imperatively. Reapply them when artwork changes so the open panel follows
            // the same adaptive accent as the island and timer orb.
            if (_timerPanelOpen)
            {
                SetTimerTabVisual(TimerTabButton, TimerTabContent.Visibility == Visibility.Visible);
                SetTimerTabVisual(AlarmTabButton, AlarmTabContent.Visibility == Visibility.Visible);
                SetTimerTabVisual(StopwatchTabButton, StopwatchTabContent.Visibility == Visibility.Visible);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsExpanded))
        {
            if (_suppressExpandedAnimation) return;
            _position.ApplyWindowStyles(this, _viewModel.Settings, _viewModel.IsCompact);
            // Dynamic rows (AirPods, timers, quotes) can appear while the island is compact.
            // Recompute the host HWND before expanding; animating only the inner shell leaves the
            // transparent window at its old compact-state height and clips the bottom of the pill.
            ApplyLayout(animate: true);
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsStatsStyle) || e.PropertyName == nameof(IslandViewModel.IsAppleStyle))
        {
            // Hardened: only re-layout when the actual visual mode changed. Spurious
            // PropertyChanged("IsStatsStyle") from unrelated media/battery/audio updates
            // must NOT restart the pill animation (previously caused visible flicker).
            var isStats = _viewModel.IsStatsStyle;
            if (isStats == _lastIsStatsStyle) return;
            _lastIsStatsStyle = isStats;
            if (_timerPanelOpen) return;
            if (_viewModel.IsExpanded)
            {
                // Visual mode changes alter both the active content and its minimum safe height.
                // Re-run the real layout path immediately so Stats never inherits Apple dimensions.
                ApplyLayout(animate: true);
            }
            else
            {
                // Compact visual mode must update immediately without a full morph.
                ApplyVisualMode();
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsQActive) || e.PropertyName == nameof(IslandViewModel.ShowQSurface) || e.PropertyName == nameof(IslandViewModel.QCompareEnabled))
        {
            if (_timerPanelOpen) return;
            if (_lastQSurface == _viewModel.ShowQSurface && _lastQActive == _viewModel.IsQActive && _lastQComparison == _viewModel.QCompareEnabled) return;
            _lastQSurface = _viewModel.ShowQSurface; _lastQActive = _viewModel.IsQActive; _lastQComparison = _viewModel.QCompareEnabled;
            ApplyVisualMode();
            ApplyLayout(animate: false);
            if (_viewModel.ShowQSurface && _viewModel.IsExpanded)
            {
                _qFollowLatest = true;
                Dispatcher.BeginInvoke(() =>
                {
                    QTranscriptScroll.ScrollToEnd();
                    QPromptBox.Focus();
                    Keyboard.Focus(QPromptBox);
                });
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.ShowAirPodsCard))
        {
            // AirPods battery/name properties are raised for every BLE packet. Only the card's
            // visibility changes the expanded layout; content updates are handled by bindings.
            var showAirPodsCard = _viewModel.ShowAirPodsCard;
            if (showAirPodsCard == _lastShowAirPodsCard) return;
            _lastShowAirPodsCard = showAirPodsCard;
            if (_timerPanelOpen) return;
            ApplyVisualMode();
            // Keep the transparent host ready even when the card connects while compact. The next
            // expansion must not inherit the smaller host height that was calculated at startup.
            ApplyLayout(animate: _viewModel.IsExpanded);
            Dispatcher.BeginInvoke(() => UpdateLiveWidgetsOverflow(LiveWidgetsScroller), DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(IslandViewModel.ShowWidgetsPanel))
        {
            var showWidgetsPanel = _viewModel.ShowWidgetsPanel;
            if (showWidgetsPanel == _lastShowWidgetsPanel) return;
            _lastShowWidgetsPanel = showWidgetsPanel;
            if (_timerPanelOpen) return;
            ApplyVisualMode();
            ApplyLayout(animate: _viewModel.IsExpanded);
            Dispatcher.BeginInvoke(() => UpdateLiveWidgetsOverflow(LiveWidgetsScroller), DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsAirPodsBannerActive))
        {
            if (_viewModel.IsAirPodsBannerActive) PlayAirPodsConnectionIntro();
            else ReturnFromAirPodsCompactBanner();
            SyncNotificationBanner();
        }
        else if (e.PropertyName == nameof(IslandViewModel.HasUrgentAlert))
        {
            // Timer completion notifications arrive on the same cadence as timer ticks. Defer
            // the visual transition to Render and guard the state so the entrance never restarts.
            Dispatcher.BeginInvoke(() => SyncUrgentAlertStrip(animate: true), DispatcherPriority.Render);
        }
        else if (e.PropertyName == nameof(IslandViewModel.ShowTimerOrb))
        {
            // The privacy dot must move with the timer activity orb instead of landing on top of it.
            if (!_timerPanelOpen && !_viewModel.IsExpanded)
            {
                var metrics = Metrics();
                UpdateTimerOrbLayout(metrics.cW, metrics.cH);
                UpdatePrivacyIndicatorLayout(metrics.cW, metrics.cH, animate: true);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.QState) ||
                 e.PropertyName == nameof(IslandViewModel.QShowInlineThinking) ||
                 e.PropertyName == nameof(IslandViewModel.QResponse) ||
                 e.PropertyName == nameof(IslandViewModel.QPromptText))
        {
            ScheduleQTranscriptScroll();
        }
        else if (e.PropertyName == nameof(IslandViewModel.PrivacySeq))
        {
            Dispatcher.BeginInvoke(PlayPrivacyDotIntro, DispatcherPriority.Render);
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsReducedMotion))
        {
            if (_viewModel.IsReducedMotion && NotifBanner.Visibility == Visibility.Visible)
                PlayNotificationIntro();
        }
        else if (e.PropertyName == nameof(IslandViewModel.BannerSeq))
        {
            // BannerSeq increments once per new Windows notification or volume warning,
            // so the entrance plays exactly once and never restarts mid-display.
            SyncNotificationBanner();
            if (NotifBanner.Visibility == Visibility.Visible && _lastAnimatedBannerSequence != _viewModel.BannerSeq)
                PlayNotificationIntro();
        }
        else if (e.PropertyName == nameof(IslandViewModel.ShowBanner))
        {
            // The banner hangs below the shell: re-anchor it and grow/shrink the HWND with it.
            SyncNotificationBanner();
            PositionNotifBannerUnderShell();
            ApplyLayout(animate: false);
        }
        else if (e.PropertyName == nameof(IslandViewModel.BannerApp) ||
                 e.PropertyName == nameof(IslandViewModel.BannerTitle) ||
                 e.PropertyName == nameof(IslandViewModel.BannerBody))
            SyncNotificationBanner();
    }

    private void TimerViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimerAlarmViewModel.ShowLiveTimer)) return;
        var visible = _timerViewModel.ShowLiveTimer;
        if (visible == _liveTimerVisible) return;

        _liveTimerVisible = visible;
        LiveTimerStrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (_viewModel.IsExpanded && !_timerPanelOpen)
            ApplyLayout(animate: true);
    }

    private int _notificationAnimationGeneration;
    private int _lastAnimatedBannerSequence = -1;

    private void SyncUrgentAlertStrip(bool animate)
    {
        var shouldShow = _viewModel.HasUrgentAlert;
        if (shouldShow == _urgentAlertVisible)
        {
            if (!shouldShow) UrgentAlertStrip.Visibility = Visibility.Collapsed;
            return;
        }

        _urgentAlertVisible = shouldShow;
        UrgentAlertStrip.BeginAnimation(UIElement.OpacityProperty, null);
        UrgentAlertScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        UrgentAlertScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        UrgentAlertTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        if (!shouldShow)
        {
            if (!animate || _viewModel.IsReducedMotion)
            {
                UrgentAlertStrip.Opacity = 0d;
                UrgentAlertStrip.Visibility = Visibility.Collapsed;
                return;
            }

            UrgentAlertStrip.Opacity = 1d;
            var fadeOut = new DoubleAnimation(0d, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (_, _) =>
            {
                if (_viewModel.HasUrgentAlert) return;
                UrgentAlertStrip.Visibility = Visibility.Collapsed;
                UrgentAlertScale.ScaleX = UrgentAlertScale.ScaleY = 1d;
                UrgentAlertTranslate.Y = 0d;
            };
            UrgentAlertStrip.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            UrgentAlertTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0d, -6d, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            return;
        }

        UrgentAlertStrip.Visibility = Visibility.Visible;
        if (!animate || _viewModel.IsReducedMotion)
        {
            UrgentAlertStrip.Opacity = 1d;
            UrgentAlertScale.ScaleX = UrgentAlertScale.ScaleY = 1d;
            UrgentAlertTranslate.Y = 0d;
            return;
        }

        UrgentAlertStrip.Opacity = 0d;
        UrgentAlertScale.ScaleX = UrgentAlertScale.ScaleY = 0.94d;
        UrgentAlertTranslate.Y = -10d;
        UrgentAlertStrip.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(190))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var reveal = TimeSpan.FromMilliseconds(260);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        UrgentAlertScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94d, 1d, reveal) { EasingFunction = ease });
        UrgentAlertScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94d, 1d, reveal) { EasingFunction = ease });
        UrgentAlertTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10d, 0d, reveal) { EasingFunction = ease });
    }

    private void PlayPrivacyDotIntro()
    {
        if (!_viewModel.ShowPrivacyInUse) return;

        if (_viewModel.IsExpanded)
        {
            PlayExpandedPrivacyActivityIntro();
            return;
        }

        PrivacyDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PrivacyDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (_viewModel.IsReducedMotion)
        {
            PrivacyDotScale.ScaleX = PrivacyDotScale.ScaleY = 1d;
            return;
        }

        var reveal = TimeSpan.FromMilliseconds(260);
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        PrivacyDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.25d, 1d, reveal) { EasingFunction = spring });
        PrivacyDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25d, 1d, reveal) { EasingFunction = spring });
    }

    private void PlayExpandedPrivacyActivityIntro()
    {
        ExpandedPrivacyActivity.BeginAnimation(UIElement.OpacityProperty, null);
        ExpandedPrivacyScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ExpandedPrivacyScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ExpandedPrivacyTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        if (_viewModel.IsReducedMotion)
        {
            ExpandedPrivacyActivity.Opacity = 1d;
            ExpandedPrivacyScale.ScaleX = ExpandedPrivacyScale.ScaleY = 1d;
            ExpandedPrivacyTranslate.X = 0d;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        ExpandedPrivacyActivity.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(180)));
        ExpandedPrivacyScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96d, 1d, duration) { EasingFunction = ease });
        ExpandedPrivacyScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96d, 1d, duration) { EasingFunction = ease });
        ExpandedPrivacyTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(12d, 0d, duration) { EasingFunction = ease });
    }

    private void PlayAirPodsConnectionIntro()
    {
        AirPodsImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        AirPodsImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        AirPodsImageTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        AirPodsImageTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        AirPodsCompactBanner.BeginAnimation(UIElement.OpacityProperty, null);
        AirPodsConnectionImage.BeginAnimation(UIElement.OpacityProperty, null);
        AirPodsImageScale.ScaleX = AirPodsImageScale.ScaleY = 0.82d;
        AirPodsImageTranslation.X = -6d;
        AirPodsImageTranslation.Y = 2d;
        AirPodsCompactBanner.Opacity = 0d;
        AirPodsConnectionImage.Opacity = 0d;

        if (_viewModel.IsReducedMotion || _viewModel.IsExpanded || _timerPanelOpen)
        {
            AirPodsImageScale.ScaleX = AirPodsImageScale.ScaleY = 1d;
            AirPodsImageTranslation.X = AirPodsImageTranslation.Y = 0d;
            AirPodsCompactBanner.Opacity = 1d;
            AirPodsConnectionImage.Opacity = 1d;
            return;
        }

        var compactWidth = Metrics().cW;
        var connectionWidth = Math.Clamp(compactWidth + 92d, 300d, 360d);
        GlassShell.BeginAnimation(WidthProperty, null);
        GlassShell.BeginAnimation(WidthProperty, new DoubleAnimation(connectionWidth, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
        });
        AirPodsCompactBanner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(160)));
        AirPodsConnectionImage.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(180)));
        AirPodsImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82d, 1d, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 }
        });
        AirPodsImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82d, 1d, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 }
        });
        AirPodsImageTranslation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-6d, 0d, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        AirPodsImageTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2d, 0d, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ReturnFromAirPodsCompactBanner()
    {
        if (_viewModel.IsExpanded || _timerPanelOpen) return;
        var compactWidth = Metrics().cW;
        GlassShell.BeginAnimation(WidthProperty, new DoubleAnimation(compactWidth, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // The notification banner must hang directly below the island instead of floating at a
    // fixed offset: track the shell's live height so it stays attached in compact and
    // expanded states (same pattern as UrgentAlertStrip).
    private void PositionNotifBannerUnderShell()
    {
        NotifBanner.Margin = new Thickness(0, GlassShell.Margin.Top + Math.Max(0d, GlassShell.ActualHeight) + 12d, 0, 0);
    }

    private void SyncNotificationBanner()
    {
        var app = _viewModel.BannerApp;
        var title = _viewModel.BannerTitle;
        var body = _viewModel.BannerBody;
        var visible = _viewModel.ShowBanner && !_viewModel.IsAirPodsBannerActive &&
                      (NotificationContent.HasVisibleText(title) || NotificationContent.HasVisibleText(body));
        // Set the actual text before exposing the card. A WPF binding refresh can otherwise
        // show a 60px card containing only the dismiss control for a frame or longer.
        NotifCard.MinWidth = _viewModel.IsLockBannerActive ? 180 : 300;
        NotifCard.MaxWidth = _viewModel.IsLockBannerActive ? 260 : 480;
        NotifContent.Margin = _viewModel.IsLockBannerActive ? new Thickness(0) : new Thickness(0, 0, 30, 0);
        NotifAppText.Visibility = _viewModel.IsLockBannerActive ? Visibility.Collapsed : Visibility.Visible;
        NotifBodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        NotifAppText.Text = visible ? app : string.Empty;
        NotifTitleText.Text = visible ? title : string.Empty;
        NotifBodyText.Text = visible ? body : string.Empty;
        var becomingVisible = visible && NotifBanner.Visibility != Visibility.Visible;
        if (!visible || becomingVisible)
        {
            _notificationAnimationGeneration++;
            NotifBanner.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
            NotifBanner.RevealProgress = 0d;
        }
        NotifBanner.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            NotifBanner.Measure(new System.Windows.Size(Math.Max(300, Width), double.PositiveInfinity));
            ApplyLayout(animate: false);
        }
        if (visible && _initialLayoutStabilized && (becomingVisible || _lastAnimatedBannerSequence != _viewModel.BannerSeq)) PlayNotificationIntro();
    }

    // Seed inside the anchored island, stretch downward, then retract the neck.
    // BannerSeq owns the entrance, so text refreshes never restart the animation.
    private void PlayNotificationIntro()
    {
        if (!_initialLayoutStabilized) return;
        PositionNotifBannerUnderShell();
        _log.Debug($"Banner entrance: seq={_viewModel.BannerSeq}, acceptedToRenderMs={_viewModel.BannerLatencyMilliseconds:0}.");
        _lastAnimatedBannerSequence = _viewModel.BannerSeq;
        var generation = ++_notificationAnimationGeneration;
        NotifBanner.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        if (_viewModel.IsReducedMotion)
        {
            NotifBanner.RevealProgress = 1d;
            return;
        }
        NotifBanner.RevealProgress = 0d;
        var reveal = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(380));
        reveal.Completed += (_, _) =>
        {
            if (generation != _notificationAnimationGeneration) return;
            NotifBanner.RevealProgress = 1d;
            NotifBanner.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        };
        NotifBanner.BeginAnimation(NotificationMorphHost.RevealProgressProperty, reveal);
    }

    // The window is a large, transparent, click-through canvas; only the centred pill is hit-testable
    // and animates. A generous fixed canvas lets the pill auto-grow to fit big text without resizing the
    // HWND (and the area around the pill stays click-through). (pillCompact, pillExpanded, window)
    // Quotes add a footer only when enabled. Keep the original canvas otherwise, because a transparent
    // WPF window has to redraw its whole canvas while the island animates.
    private const double CanvasW = 1200d, BaseCanvasH = 330d, QuoteFooterHeight = 56d;
    private const double QSurfaceExtraHeight = 220d;
    // The Stats dashboard is taller than the Apple activity deck (album art plus three
    // stacked tiles instead of one compact row). Reserve this space even when the user
    // turns auto-grow off so the bottom of the dashboard can never be clipped.
    private const double StatsDashboardExtraHeight = 64d;
    private (double cW, double cH, double eW, double eH, double winW, double winH) Metrics()
    {
        var quoteSpace = _viewModel.ShowQuoteInExpanded ? QuoteFooterHeight : 0d;
        var timerSpace = _liveTimerVisible ? LiveTimerExtraHeight : 0d;
        var statsSpace = _viewModel.IsStatsStyle ? StatsDashboardExtraHeight : 0d;
        var qSpace = _viewModel.ShowQSurface ? QSurfaceExtraHeight : 0d;
        // AirPods and widgets now share one 64-DIP accessory lane.
        var accessorySpace = _viewModel.ShowGraphiteAccessories ? 76d : 0d;
        // Compact dimensions are user-controlled and deliberately independent from the expanded
        // canvas.  Previously this method ignored IslandWidth/IslandHeight and used a preset for
        // both states, so adjusting the mini island could distort the expanded layout.
        var compactWidth = Math.Clamp(_viewModel.Settings.IslandWidth, 72d, 360d);
        var compactHeight = Math.Clamp(_viewModel.Settings.IslandHeight, 38d, 90d);
        if (_viewModel.QShowCompactComparison)
        {
            compactWidth = Math.Min(Math.Max(compactWidth, 480), Math.Max(72, AvailableWorkArea.Width - 24));
            compactHeight = Math.Max(compactHeight, 68);
        }
        var (expandedWidth, expandedHeight) = _viewModel.Settings.IslandSize switch
        {
            // The media header, transport controls, and the live-activity cards need 260px+
            // after their outer margins.  The smaller values clipped the entire bottom row,
            // making RAM/network/battery appear to have disappeared.
            IslandSize.Compact => (820d, 334d + quoteSpace + timerSpace + accessorySpace),
            IslandSize.Large => (1100d, 354d + quoteSpace + timerSpace + accessorySpace),
            _ => (990d, 334d + quoteSpace + timerSpace + accessorySpace)
        };
        if (_viewModel.ShowQSurface) { expandedWidth = 920; expandedHeight = 621; }
        if (_viewModel.QShowCompactComparison) { expandedWidth = Math.Max(expandedWidth, 1100); expandedHeight = Math.Max(expandedHeight, 700); }
        // The transparent HWND must be at least as tall as the pill, otherwise WPF clips
        // a correctly measured Stats dashboard at the canvas boundary.
        var windowHeight = Math.Max(BaseCanvasH + quoteSpace + timerSpace, expandedHeight + 40d);
                var available = AvailableWorkArea;
        expandedWidth = WindowSizingPolicy.BoundedDimension(expandedWidth, available.Width);
        expandedHeight = WindowSizingPolicy.BoundedDimension(expandedHeight, available.Height, 76);
        return (compactWidth, compactHeight, expandedWidth, expandedHeight, Math.Min(CanvasW, available.Width), Math.Min(available.Height, Math.Max(windowHeight, expandedHeight + 64)));
    }

    // When auto-grow is on, size the expanded pill to its content so nothing clips.
    private (double W, double H) ExpandedPillSize()
    {
        var m = Metrics();
        // Q has a fixed interaction frame. Letting the answer text determine the whole pill height
        // pushes the composer and footer below the shell after a long response. The response card
        // owns a ScrollViewer, so keep the prompt/actions/footer pinned in the reserved Q height.
        if (_viewModel.ShowQSurface) return (m.eW, m.eH);
        try
        {
            // Measure the content unconstrained so trimmed/async text reports its true natural size.
            // StatsOverlay lives inside ExpandedContent, so this measures the exact surface the
            // user sees in either visual style.  The old detached Stats grid is intentionally no
            // longer part of sizing or visibility decisions.
            var content = _viewModel.ShowQSurface ? QContent : ExpandedContent;
            content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = content.DesiredSize;
            // Auto-grow off keeps the configured width, but it must never suppress mandatory
            // vertical growth. Dynamic rows (AirPods, timer, quotes) can otherwise be arranged
            // below the fixed shell and get cut in half even though the host window is tall enough.
            var w = _viewModel.Settings.AutoGrowPill
                ? Math.Clamp(d.Width + 2, m.eW, Math.Max(m.eW, Math.Min(CanvasW - 24, AvailableWorkArea.Width - 24)))
                : m.eW;
            var h = WindowSizingPolicy.AntiClippingDimension(m.eH, d.Height + 2, m.winH - 12);
            return (WindowSizingPolicy.BoundedDimension(w, AvailableWorkArea.Width), WindowSizingPolicy.BoundedDimension(h, AvailableWorkArea.Height, 76));
        }
        catch { return (m.eW, m.eH); }
    }

    // Snap the expanded pill to fit its current content (called as content text changes size).
    private bool _inAutoGrow;
    private bool _expandAnimating;
    private int _pillAnimationGeneration;
    private bool _loggedAnimationStart;
    private bool _loggedAnimationCompletion;
    private void UpdateAutoGrow()
    {
        if (_inAutoGrow || _expandAnimating || !_viewModel.IsExpanded || !_viewModel.Settings.AutoGrowPill) return;
        _inAutoGrow = true;
        try
        {
            var (w, h) = ExpandedPillSize();
            if (Math.Abs(GlassShell.Width - w) > 0.5 || Math.Abs(GlassShell.Height - h) > 0.5)
            {
                // Clear any held open-animation values so the new size actually takes effect.
                GlassShell.BeginAnimation(WidthProperty, null);
                GlassShell.BeginAnimation(HeightProperty, null);
                GlassShell.Width = w;
                GlassShell.Height = h;
                SizeExpandedViewport(w, h);
            }
        }
        finally { _inAutoGrow = false; }
    }

    private void ApplyLayout(bool animate)
    {
        // Keep the expanded tree mounted so it can be measured, but do not let it render at full
        // opacity before AnimatePill has installed its fade clock. That one-frame reveal was the
        // flash seen at the very beginning of an expansion.
        var animateMain = animate && !_timerPanelOpen;
        var deferExpandedReveal = animateMain && !_viewModel.IsReducedMotion && _viewModel.IsExpanded;
        var deferCompactReveal = animateMain && !_viewModel.IsReducedMotion && !_viewModel.IsExpanded;
        ApplyVisualMode(deferExpandedReveal, deferCompactReveal);
        var m = Metrics();
        UpdateTimerOrbLayout(m.cW, m.cH);
        UpdatePrivacyIndicatorLayout(m.cW, m.cH, animate);
        // The timer panel carries no drop shadow; keep just enough edge room for its 8px
        // top offset so the panel never clips at the HWND boundary.
        const double timerEdgePadding = 12d;
        (double W, double H)? expandedBounds = null;
        (double W, double H) ExpandedOnce() => expandedBounds ??= ExpandedPillSize();
        var desiredWindowHeight = _timerPanelOpen ? Math.Max(m.winH, TimerPanelHeight + timerEdgePadding) : m.winH;
        var desiredWindowWidth = _timerPanelOpen ? Math.Max(m.winW, TimerPanelWidth + timerEdgePadding) : m.winW;
        if (NotifBanner.Visibility == Visibility.Visible && !_timerPanelOpen)
        {
            // The notification banner hangs below the shell: reserve room for it while visible
            // so the card never clips at the HWND edge (ShowBanner changes re-run ApplyLayout).
            var shellH = _viewModel.IsExpanded ? ExpandedOnce().H : m.cH;
            const double bannerReserve = 170d;
            desiredWindowHeight = Math.Max(desiredWindowHeight, GlassShell.Margin.Top + shellH + bannerReserve);
        }
        if (Math.Abs(Width - desiredWindowWidth) > 0.5 || Math.Abs(Height - desiredWindowHeight) > 0.5)
        {
            Width = desiredWindowWidth;
            Height = desiredWindowHeight;
        }
        // The transparent HWND stays anchored to the user's main-island position even while the
        // timer host is open. The timer surface is a sibling overlay; using its width here moves
        // the main island for TopLeft/TopRight layouts and makes the overlay look like its owner.
        var targetPillWidth = _viewModel.IsExpanded ? ExpandedOnce().W : m.cW;
        var targetPillHeight = _viewModel.IsExpanded ? ExpandedOnce().H : m.cH;
        _position.PositionInitial(this, _viewModel.Settings,
            _timerPanelOpen ? Math.Max(targetPillWidth, TimerPanelWidth) : targetPillWidth,
            _timerPanelOpen ? Math.Max(targetPillHeight, TimerPanelHeight) : targetPillHeight);
        // The timer host may be opening or closing while the main shell is changing state. Its
        // overlay owns that transition, so the main shell must settle synchronously and never
        // inherit the timer orb's geometry.
        AnimatePill(animateMain, targetPillWidth, targetPillHeight);
        ApplyTimerPanelHostLayout(animate);
    }

    private void UpdateTimerOrbLayout(double compactWidth, double compactHeight)
    {
        var size = Math.Clamp(compactHeight, 38d, 62d);
        CompactTimerOrb.Width = size;
        CompactTimerOrb.Height = size;
        var airPodsGap = _viewModel.IsAirPodsBannerActive ? 26d : 0d;
        TimerOrbTranslate.X = compactWidth / 2d + 8d + size / 2d + airPodsGap;
    }

    private void UpdatePrivacyIndicatorLayout(double compactWidth, double compactHeight, bool animate)
    {
        // Privacy belongs to the compact island/orb composition, never to the timer panel's
        // expanded bounds. Keeping this coordinate space stable prevents the dot from flying to
        // the panel's far edge and then returning when TimerEditorOpen changes the orb visibility.
        var (activeWidth, activeHeight) = _viewModel.IsExpanded
            ? ExpandedPillSize()
            : (compactWidth, compactHeight);

        // Follow the shell's trailing edge. Expanded mode keeps the orb near the top controls.
        var targetX = activeWidth / 2d + 12d;
        if (!_viewModel.IsExpanded && !_timerPanelOpen && _viewModel.ShowTimerOrb)
        {
            var timerOrbSize = Math.Clamp(compactHeight, 38d, 62d);
            var timerOrbRight = TimerOrbTranslate.X + timerOrbSize / 2d;
            // Place privacy to the right of the timer orb with a small optical gap.
            targetX = timerOrbRight + 6d + 9d;
        }
        var targetY = _viewModel.IsExpanded
            ? 12d
            : Math.Max(0d, (activeHeight - 18d) / 2d);

        PrivacyDotTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PrivacyDotTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        // Position changes are committed immediately. PrivacySeq owns the dot's entrance motion;
        // animating its location during every layout pass caused the visible far-right flicker.
        PrivacyDotTranslate.X = targetX;
        PrivacyDotTranslate.Y = targetY;
    }

    private void ApplyVisualMode(bool deferExpandedReveal = false, bool deferCompactReveal = false)
    {
        var apple = _viewModel.IsAppleStyle;
        var expanded = _viewModel.IsExpanded;
        var q = _viewModel.ShowQSurface;
        ExpandedContent.Margin = new Thickness(0);
        GraphiteDeck.Visibility = q ? Visibility.Collapsed : Visibility.Visible;
        // Give Q its own bounded layout viewport. Without an explicit height, the parent Grid can
        // arrange the response card at its natural text height and clip the prompt/footer below it.
        var qHeight = q ? Metrics().eH : double.NaN;
        QContent.Height = qHeight;
        QContent.MaxHeight = q ? qHeight : double.PositiveInfinity;
        QContent.VerticalAlignment = q ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        SetModeContent(CompactContent, !expanded && (apple || _viewModel.IsEdgePill || _viewModel.IsHolePunchMode || _viewModel.QShowCompactComparison), deferCompactReveal);
        // The full Apple activity deck is the stable shared expanded surface. The compact Stats
        // layout still provides the dense glanceable alternative, while this prevents Stats mode
        // from ever opening into an empty shell during rapid hover/settings transitions.
        // TimerPanelContent is deliberately not part of this tree. The timer has its own sibling
        // host so its transform and hit testing can never change the main island's geometry.
        ExpandedViewport.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Width = Math.Max(q ? 600 : 850, Metrics().eW);
        ExpandedContent.Height = q ? Metrics().eH : double.NaN;
        SetModeContent(ExpandedContent, expanded, deferExpandedReveal);
        SetModeContent(QContent, expanded && q);
        SetModeContent(StatsCompactContent, !apple && !_viewModel.IsEdgePill && !_viewModel.IsHolePunchMode && !expanded && !_viewModel.QShowCompactComparison, deferCompactReveal);
        SetModeContent(StatsExpandedContent, false);
        SetModeContent(StatsOverlay, false);
    }

    // Switching visual modes while the shell is expanded previously changed Visibility only. If an
    // in-flight transition had left the new content at opacity 0, the result was a large blank pill.
    // Reset the full presentation state together so either layout is always immediately usable.
    private static void SetModeContent(UIElement content, bool visible, bool deferReveal = false)
    {
        content.BeginAnimation(UIElement.OpacityProperty, null);
        content.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        content.Opacity = visible && !deferReveal ? 1d : 0d;
        content.IsHitTestVisible = visible && !deferReveal;
    }

    private void SetPillAnimationCache(bool enabled, UIElement compactContent)
    {
        SetSurfaceInMotion(GlassShell, enabled);
        // Keep expanded content as live vectors so it stays sharp throughout the morph.
        ExpandedContent.CacheMode = null;
        compactContent.CacheMode = enabled ? new BitmapCache() : null;
        if (!enabled)
        {
            CompactContent.CacheMode = null;
            StatsCompactContent.CacheMode = null;
        }
    }

    private Grid ActiveCompactContent => _viewModel.IsStatsStyle ? StatsCompactContent : CompactContent;
    private Grid ActiveExpandedContent => _viewModel.ShowQSurface ? QContent : ExpandedContent;

    // Lay out the expanded subtree once at its destination size. The shell's rounded clip
    // reveals it during the morph without resizing a ScrollViewer and all its children per frame.
    // Hidden scrollbars retain wheel/keyboard scrolling when a small monitor constrains the shell.
    private void SizeExpandedViewport(double width, double height)
    {
        ExpandedViewport.Width = width;
        ExpandedViewport.Height = height;
    }

    // The drop shadow is the most expensive part of each software-composited animation frame. Retain it
    // visually, but use WPF's lower-cost rendering mode until the zoom lands.
    private System.Windows.Media.Effects.DropShadowEffect? _animatedShellShadow;
    private System.Windows.Media.Effects.RenderingBias _savedShellShadowBias;
    private void UseFastPillShadow()
    {
        if (_animatedShellShadow is not null) return;
        if (GlassShell.Effect is not System.Windows.Media.Effects.DropShadowEffect shadow || shadow.IsFrozen) return;
        _animatedShellShadow = shadow;
        _savedShellShadowBias = shadow.RenderingBias;
        shadow.RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance;
    }

    private void RestorePillShadow()
    {
        if (_animatedShellShadow is null) return;
        _animatedShellShadow.RenderingBias = _savedShellShadowBias;
        _animatedShellShadow = null;
    }

    // Animate the clipped shell itself while the expanded viewport stays pinned at its destination
    // size. Scaling a transparent WPF Border exposed a full-width composition frame on some GPUs
    // before its transform clock was applied, which read as a distracting flash during expansion.
    private void AnimatePill(bool animate, double targetW, double targetH)
    {
        var generation = ++_pillAnimationGeneration;
        var expanded = _viewModel.IsExpanded;
        var reduced = _viewModel.Settings.AnimationIntensity == AnimationIntensity.Reduced;
        if (_viewModel.IsExpanded) SizeExpandedViewport(targetW, targetH);
        var compactContent = ActiveCompactContent;
        var expandedContent = ActiveExpandedContent;
        var currentWidth = Math.Max(1d, GlassShell.ActualWidth);
        var currentHeight = Math.Max(1d, GlassShell.ActualHeight);

        if (!animate || reduced)
        {
            _frostBackdrop.SetSuspended(false);
            SetPillAnimationCache(enabled: false, compactContent);
            GlassShell.BeginAnimation(WidthProperty, null);
            GlassShell.BeginAnimation(HeightProperty, null);
            GlassShell.BeginAnimation(UIElement.OpacityProperty, null);
            PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PillScale.ScaleX = PillScale.ScaleY = 1d;
            GlassShell.Width = targetW;
            GlassShell.Height = targetH;
            GlassShell.Opacity = 1d;
            ApplyVisualMode();
            _expandAnimating = false;
            RestorePillShadow();
            if (_viewModel.IsExpanded && !_timerPanelOpen) UpdateAutoGrow();
            return;
        }

        // Expansion covers the full compact-to-editor distance. Give it enough time for the
        // compositor to render a visibly fluid sequence of frames instead of making the first
        // few frames carry most of a 900x600 resize. Collapse stays quick and familiar.
        var duration = IslandMorphDuration(expanded);
        // Keep the shell dimensions monotonic. A BackEase on the actual width made the island
        // overshoot horizontally, producing the wide flash visible in the frame captures before
        // it settled back to the intended size. Apple-like motion here comes from a fast ease-out,
        // while the content fade provides the softer handoff.
        var ease = IslandMorphEase(expanded);

        _expandAnimating = true;
        _frostBackdrop.SetSuspended(true);
        SetPillAnimationCache(enabled: true, compactContent);
        UseFastPillShadow();
        if (!_loggedAnimationStart)
        {
            _loggedAnimationStart = true;
            _log.Info($"Real island animation started: expanded={expanded}, from={GlassShell.ActualWidth:0.#}x{GlassShell.ActualHeight:0.#}, target={targetW:0.#}x{targetH:0.#}, duration={duration.TotalMilliseconds:0}ms");
        }

        // Freeze an interrupted morph at its currently rendered bounds, then continue from there.
        // This keeps rapid pointer reversals smooth without a scale reset or a full-width flash.
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillScale.ScaleX = PillScale.ScaleY = 1d;
        GlassShell.BeginAnimation(WidthProperty, null);
        GlassShell.BeginAnimation(HeightProperty, null);
        GlassShell.BeginAnimation(UIElement.OpacityProperty, null);
        GlassShell.Width = currentWidth;
        GlassShell.Height = currentHeight;
        GlassShell.Opacity = 1d;
        var widthAnimation = new DoubleAnimation(targetW, duration) { EasingFunction = ease };
        var heightAnimation = new DoubleAnimation(targetH, duration) { EasingFunction = ease };
        heightAnimation.Completed += (_, _) => FinishPillMorph(generation, targetW, targetH, expanded);
        GlassShell.BeginAnimation(WidthProperty, widthAnimation);
        GlassShell.BeginAnimation(HeightProperty, heightAnimation);
        // Begin compositing the glass once the shell has covered most of its travel. Keeping
        // the first part opaque avoids expensive backdrop resizes during the fastest growth.
        if (expanded)
        {
            const double handoffFraction = 0.6;
            var frostHandoff = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * handoffFraction) };
            frostHandoff.Tick += (_, _) =>
            {
                frostHandoff.Stop();
                if (generation == _pillAnimationGeneration && _expandAnimating && _viewModel.IsExpanded)
                    _frostBackdrop.SetSuspended(false);
            };
            frostHandoff.Start();
        }

        if (expanded)
        {
            // Preserve a compact visual while the rounded clip begins widening, then hand off
            // promptly to the expanded surface. This avoids a black or full-width flash.
            compactContent.Visibility = Visibility.Visible;
            compactContent.Opacity = 1d;
            compactContent.IsHitTestVisible = false;
            // Fade the main expanded surface after the shell starts widening. The timer overlay
            // has its own animation and is never mounted in this tree.
            ExpandedContent.Opacity = 0d;
            ExpandedContent.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.62))
            {
                BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.12),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ExpandedContent.BeginAnimation(UIElement.OpacityProperty, fade);
            compactContent.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.25))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            // Keep the expanded surface mounted while the shell narrows. Switching to the compact
            // tree before the animation starts makes collapse look like a clipped replacement.
            // Cross-fade the compact tree near the end, then ApplyVisualMode commits the compact
            // layout after the shell reaches its final bounds.
            ExpandedViewport.Visibility = Visibility.Visible;
            ExpandedContent.Visibility = Visibility.Visible;
            ExpandedContent.Opacity = 1d;
            ExpandedContent.IsHitTestVisible = false;
            expandedContent.Visibility = Visibility.Visible;
            expandedContent.Opacity = 1d;
            expandedContent.IsHitTestVisible = false;
            compactContent.Visibility = Visibility.Visible;
            compactContent.Opacity = 0d;
            compactContent.IsHitTestVisible = false;

            // Reveal the compact island only near the end of the shrink. Showing it at the
            // beginning makes the timer panel look like it turns into a large normal island
            // before performing a second resize.
            var fadeDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.4);
            var expandedFade = new DoubleAnimation(0d, fadeDuration)
            {
                BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.4),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var compactFade = new DoubleAnimation(1d, fadeDuration)
            {
                BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.6),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            expandedContent.BeginAnimation(UIElement.OpacityProperty, expandedFade);
            compactContent.BeginAnimation(UIElement.OpacityProperty, compactFade);
        }
    }

    private void FinishPillMorph(int generation, double targetW, double targetH, bool expanded)
    {
        if (generation != _pillAnimationGeneration) return;
        SetPillAnimationCache(enabled: false, CompactContent);
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillScale.ScaleX = PillScale.ScaleY = 1d;
        GlassShell.BeginAnimation(WidthProperty, null);
        GlassShell.BeginAnimation(HeightProperty, null);
        GlassShell.BeginAnimation(UIElement.OpacityProperty, null);
        GlassShell.Width = targetW;
        GlassShell.Height = targetH;
        ApplyVisualMode();
        GlassShell.Opacity = 1d;
        _expandAnimating = false;
        _frostBackdrop.SetSuspended(false);
        RestorePillShadow();
        if (!_loggedAnimationCompletion)
        {
            _loggedAnimationCompletion = true;
            _log.Info($"Real island animation completed: expanded={expanded}, final={GlassShell.ActualWidth:0.#}x{GlassShell.ActualHeight:0.#}");
        }
        if (_viewModel.IsExpanded && !_timerPanelOpen) UpdateAutoGrow();
        LogAirPodsLayoutSnapshot();
    }

    private void ApplyTimerPanelHostLayout(bool animate)
    {
        var width = TimerPanelWidth;
        var height = TimerPanelHeight;
        if (!_timerPanelHostAnimating)
        {
            TimerPanelHost.Width = width;
            TimerPanelHost.Height = height;
        }
        TimerPanelContent.Width = width;
        TimerPanelContent.Height = height;
        TimerPanelContent.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        TimerPanelContent.VerticalAlignment = VerticalAlignment.Top;
        TimerTabContent.Height = AlarmTabContent.Height = StopwatchTabContent.Height = double.NaN;

        if (_timerPanelOpen)
        {
            if (!_timerPanelHostShown)
                BeginTimerPanelHostOpen(animate);
            else if (!_timerPanelHostAnimating && !_timerPanelClosing)
                SetTimerPanelHostSettled();
            return;
        }

        if (!_timerPanelHostShown)
            SetTimerPanelHostHidden();
    }

    private (double Width, double Height, double OffsetX) TimerPanelMorphAnchor()
    {
        if (_viewModel.DisplayTimer is not null)
        {
            var orbSize = Math.Clamp(CompactTimerOrb.Width, 38d, 62d);
            return (orbSize, orbSize, TimerOrbTranslate.X);
        }

        // With no timer activity there is no orb to receive the morph. Land on the visible
        // compact island instead, so the panel never closes into an empty spot beside it.
        return (Math.Max(1d, GlassShell.Width), Math.Max(1d, GlassShell.Height), 0d);
    }

    private void BeginTimerPanelHostOpen(bool animate)
    {
        var generation = ++_timerPanelAnimationGeneration;
        var reversing = _timerPanelClosing && _timerPanelHostShown;
        var currentWidth = TimerPanelHost.Width;
        var currentHeight = TimerPanelHost.Height;
        var currentTranslateX = TimerPanelTranslate.X;
        var currentContentOpacity = TimerPanelContent.Opacity;
        var currentHostOpacity = TimerPanelHost.Opacity;
        var currentCornerRadius = TimerPanelHost.CornerRadius;
        var (anchorWidth, anchorHeight, anchorOffsetX) = TimerPanelMorphAnchor();
        var targetWidth = Math.Max(1d, TimerPanelWidth);
        var targetHeight = Math.Max(1d, TimerPanelHeight);

        StopTimerPanelHostAnimations(preserveCurrent: reversing);
        _timerPanelClosing = false;
        _timerPanelHostShown = true;
        _timerPanelHostAnimating = true;
        TimerPanelHost.Visibility = Visibility.Visible;
        TimerPanelContent.Visibility = Visibility.Visible;
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelContent.Opacity = reversing ? currentContentOpacity : 0d;
        if (!reversing && animate && !_viewModel.IsReducedMotion)
        {
            // Prepare the editor at its fixed destination size before starting any clocks.
            // Otherwise the first hover spends the opening frames creating/measuring controls
            // and the first visible shell can already be most of the way to full size.
            TimerPanelHost.Opacity = 0d;
            TimerPanelHost.Width = targetWidth;
            TimerPanelHost.Height = targetHeight;
            TimerPanelHost.UpdateLayout();
        }
        // Keep the shell present from the first orb-sized frame, just like the main island.
        // Fading the entire host hid the beginning of the growth and exposed it already wide.
        TimerPanelHost.Opacity = reversing ? currentHostOpacity : 1d;
        TimerPanelHost.Width = reversing ? currentWidth : anchorWidth;
        TimerPanelHost.Height = reversing ? currentHeight : anchorHeight;
        TimerPanelHost.CornerRadius = reversing
            ? currentCornerRadius
            : new CornerRadius(Math.Min(anchorWidth, anchorHeight) / 2d);
        TimerPanelHost.IsHitTestVisible = false;
        TimerPanelContent.IsHitTestVisible = false;

        var startWidth = reversing ? currentWidth : anchorWidth;
        var startHeight = reversing ? currentHeight : anchorHeight;
        var startTranslateX = reversing ? currentTranslateX : anchorOffsetX;

        if (!animate || _viewModel.IsReducedMotion)
        {
            SetTimerPanelHostSettled();
            return;
        }

        // Cache the fixed destination content, not the shell whose bitmap changes every frame.
        SetSurfaceInMotion(TimerPanelHost, moving: true);
        TimerPanelContent.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
        TimerPanelTranslate.X = startTranslateX;
        var duration = PillMorphDuration(expanded: true);
        var ease = PillMorphEase(expanded: true);
        TimerPanelHost.BeginAnimation(WidthProperty,
            new DoubleAnimation(startWidth, targetWidth, duration) { EasingFunction = ease });
        TimerPanelTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(startTranslateX, 0d, duration) { EasingFunction = ease });
        var openAnimation = new DoubleAnimation(startHeight, targetHeight, duration) { EasingFunction = ease };
        openAnimation.Completed += (_, _) => FinishTimerPanelHostOpen(generation);
        TimerPanelHost.BeginAnimation(HeightProperty, openAnimation);
        TimerPanelHost.BeginAnimation(Border.CornerRadiusProperty,
            new CornerRadiusAnimation(
                TimerPanelHost.CornerRadius,
                new CornerRadius(TimerPanelNormalCornerRadius),
                duration) { EasingFunction = ease });
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(TimerPanelContent.Opacity, 1d, TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.56d))
            {
                BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.12d),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void FinishTimerPanelHostOpen(int generation)
    {
        if (generation != _timerPanelAnimationGeneration || !_timerPanelOpen || _timerPanelClosing) return;
        SetTimerPanelHostSettled();
        RefreshTimerPanelHoverAfterOpen();
        _log.Info("Timer panel orb morph opened");
    }

    private void RefreshTimerPanelHoverAfterOpen()
    {
        if (!_timerPanelOpen || _timerPanelClosing) return;

        if (IsPointerOverTimerPanelHost())
        {
            _timerPanelLeaveTimer.Stop();
        }
        else
        {
            ScheduleTimerPanelAutoClose();
        }
    }

    private void BeginTimerPanelHostClose()
    {
        if (!_timerPanelHostShown) return;

        var generation = ++_timerPanelAnimationGeneration;
        var currentWidth = TimerPanelHost.Width;
        var currentHeight = TimerPanelHost.Height;
        var currentTranslateX = TimerPanelTranslate.X;
        var currentContentOpacity = TimerPanelContent.Opacity;
        var currentHostOpacity = TimerPanelHost.Opacity;
        var currentCornerRadius = TimerPanelHost.CornerRadius;
        var (targetWidth, targetHeight, targetOffsetX) = TimerPanelMorphAnchor();
        StopTimerPanelHostAnimations(preserveCurrent: true);
        _timerPanelClosing = true;
        _timerPanelHostAnimating = true;
        TimerPanelHost.Visibility = Visibility.Visible;
        TimerPanelContent.Visibility = Visibility.Visible;
        TimerPanelHost.IsHitTestVisible = false;
        TimerPanelContent.IsHitTestVisible = false;
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelContent.Opacity = currentContentOpacity;
        TimerPanelHost.Opacity = currentHostOpacity;
        var duration = PillMorphDuration(expanded: false);
        if (_viewModel.IsReducedMotion)
        {
            FinishTimerPanelHostClose(generation);
            return;
        }

        SetSurfaceInMotion(TimerPanelHost, moving: true);
        TimerPanelContent.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
        TimerPanelTranslate.X = currentTranslateX;
        var ease = PillMorphEase(expanded: false);
        TimerPanelHost.BeginAnimation(WidthProperty,
            new DoubleAnimation(currentWidth, targetWidth, duration) { EasingFunction = ease });
        var closeAnimation = new DoubleAnimation(currentHeight, targetHeight, duration) { EasingFunction = ease };
        closeAnimation.Completed += (_, _) => FinishTimerPanelHostClose(generation);
        TimerPanelHost.BeginAnimation(HeightProperty, closeAnimation);
        TimerPanelTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(currentTranslateX, targetOffsetX, duration) { EasingFunction = ease });
        TimerPanelHost.BeginAnimation(Border.CornerRadiusProperty,
            new CornerRadiusAnimation(
                currentCornerRadius,
                new CornerRadius(Math.Min(targetWidth, targetHeight) / 2d),
                duration) { EasingFunction = ease });
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(currentContentOpacity, 0d, TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.42))
            {
                BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.4d),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        // Keep the shell opaque until it reaches the orb. The bound orb replaces it in the
        // completion callback, avoiding a blank gap followed by a suddenly reappearing circle.
    }

    private void FinishTimerPanelHostClose(int generation)
    {
        if (generation != _timerPanelAnimationGeneration || !_timerPanelClosing) return;
        SetTimerPanelHostHidden();
        _timerPanelOpen = false;
        _timerPanelClosing = false;
        _viewModel.TimerEditorOpen = false;
        _viewModel.InteractionProtected = IsKeyboardFocusWithin;
        _position.ApplyWindowStyles(this, _viewModel.Settings, compact: true);
        ApplyLayout(animate: false);
        _log.Info("Timer panel orb morph closed");
    }

    private void SetTimerPanelHostSettled()
    {
        StopTimerPanelHostAnimations(preserveCurrent: false);
        SetSurfaceInMotion(TimerPanelHost, moving: false);
        TimerPanelHost.Visibility = Visibility.Visible;
        TimerPanelContent.Visibility = Visibility.Visible;
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelContent.Opacity = 1d;
        TimerPanelHost.Opacity = 1d;
        TimerPanelHost.CacheMode = null;
        TimerPanelContent.CacheMode = null;
        TimerPanelHost.Width = TimerPanelWidth;
        TimerPanelHost.Height = TimerPanelHeight;
        TimerPanelTranslate.X = TimerPanelTranslate.Y = 0d;
        TimerPanelHost.IsHitTestVisible = true;
        TimerPanelContent.IsHitTestVisible = true;
        _timerPanelHostShown = true;
        _timerPanelHostAnimating = false;
    }

    private void SetTimerPanelHostHidden()
    {
        StopTimerPanelHostAnimations(preserveCurrent: false);
        SetSurfaceInMotion(TimerPanelHost, moving: false);
        TimerPanelHost.Visibility = Visibility.Collapsed;
        TimerPanelHost.Opacity = 0d;
        TimerPanelHost.CacheMode = null;
        TimerPanelContent.CacheMode = null;
        TimerPanelHost.IsHitTestVisible = false;
        TimerPanelContent.Visibility = Visibility.Collapsed;
        TimerPanelContent.IsHitTestVisible = false;
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelContent.Opacity = 0d;
        TimerPanelHost.Width = TimerPanelWidth;
        TimerPanelHost.Height = TimerPanelHeight;
        TimerPanelTranslate.X = TimerPanelTranslate.Y = 0d;
        _timerPanelHostShown = false;
        _timerPanelHostAnimating = false;
    }

    private void StopTimerPanelHostAnimations(bool preserveCurrent)
    {
        var width = TimerPanelHost.Width;
        var height = TimerPanelHost.Height;
        var translateX = TimerPanelTranslate.X;
        var translateY = TimerPanelTranslate.Y;
        var contentOpacity = TimerPanelContent.Opacity;
        var hostOpacity = TimerPanelHost.Opacity;
        var cornerRadius = TimerPanelHost.CornerRadius;
        TimerPanelHost.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelContent.BeginAnimation(UIElement.OpacityProperty, null);
        TimerPanelHost.BeginAnimation(WidthProperty, null);
        TimerPanelHost.BeginAnimation(HeightProperty, null);
        TimerPanelHost.BeginAnimation(Border.CornerRadiusProperty, null);
        TimerPanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        TimerPanelTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (preserveCurrent)
        {
            TimerPanelHost.Opacity = hostOpacity;
            TimerPanelHost.Width = width;
            TimerPanelHost.Height = height;
            TimerPanelTranslate.X = translateX;
            TimerPanelTranslate.Y = translateY;
            TimerPanelContent.Opacity = contentOpacity;
            TimerPanelHost.CornerRadius = cornerRadius;
        }
        else
        {
            TimerPanelHost.Opacity = 0d;
            TimerPanelHost.Width = TimerPanelWidth;
            TimerPanelHost.Height = TimerPanelHeight;
            TimerPanelTranslate.X = TimerPanelTranslate.Y = 0d;
            TimerPanelContent.Opacity = 0d;
            TimerPanelHost.CornerRadius = new CornerRadius(TimerPanelNormalCornerRadius);
        }
    }

    private sealed class CornerRadiusAnimation : AnimationTimeline
    {
        public CornerRadiusAnimation() { }

        public CornerRadiusAnimation(CornerRadius from, CornerRadius to, TimeSpan duration)
        {
            From = from;
            To = to;
            Duration = new Duration(duration);
        }

        public CornerRadius From { get; set; }
        public CornerRadius To { get; set; }
        public IEasingFunction? EasingFunction { get; set; }

        public override Type TargetPropertyType => typeof(CornerRadius);

        protected override Freezable CreateInstanceCore() => new CornerRadiusAnimation
        {
            From = From,
            To = To,
            Duration = Duration,
            EasingFunction = EasingFunction
        };

        public override object GetCurrentValue(
            object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            var progress = animationClock.CurrentProgress ?? 1d;
            progress = EasingFunction?.Ease(progress) ?? progress;
            return new CornerRadius(
                Lerp(From.TopLeft, To.TopLeft, progress),
                Lerp(From.TopRight, To.TopRight, progress),
                Lerp(From.BottomRight, To.BottomRight, progress),
                Lerp(From.BottomLeft, To.BottomLeft, progress));
        }

        private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);
    }

    private void LogAirPodsLayoutSnapshot()
    {
        if (!_viewModel.IsExpanded || !_viewModel.ShowAirPodsCard) return;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var cardOrigin = AirPodsCard.TranslatePoint(new System.Windows.Point(0, 0), GlassShell);
                var cardBottom = cardOrigin.Y + AirPodsCard.ActualHeight;
                _log.Info(
                    $"AirPods layout: window={ActualWidth:0.#}x{ActualHeight:0.#}, " +
                    $"shell={GlassShell.ActualWidth:0.#}x{GlassShell.ActualHeight:0.#}, " +
                    $"contentDesired={ExpandedContent.DesiredSize.Width:0.#}x{ExpandedContent.DesiredSize.Height:0.#}, " +
                    $"contentActual={ExpandedContent.ActualWidth:0.#}x{ExpandedContent.ActualHeight:0.#}, " +
                    $"cardY={cardOrigin.Y:0.#}, cardHeight={AirPodsCard.ActualHeight:0.#}, cardBottom={cardBottom:0.#}");
            }
            catch (InvalidOperationException)
            {
                // The visual may be detached if the user collapses the island during this render pass.
            }
        }, DispatcherPriority.Render);
    }

    private void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        _idleTimer.Stop();
        SetDimmed(false);
        if (!_timerPanelOpen && _viewModel.Settings.ExpandOnHover && !_viewModel.Settings.ClickThroughWhenCompact)
            RequestMainExpansion();
    }

    private void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_timerPanelOpen || OutputDevicePopup.IsOpen || _openIslandMenus.Count > 0) return;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, _viewModel.Settings.CollapseDelayMilliseconds));
        _collapseTimer.Start();
        // Don't dim when click-through is on — the pill can't receive the hover that un-dims it.
        if (_viewModel.Settings.IdleDimming && !_viewModel.Settings.ClickThroughWhenCompact) _idleTimer.Start();
    }

    // Safety net: the island must never get stuck invisible/off-screen. Runs every second.
    private void EnsureHealthy()
    {
        if (_hiddenForCapture) return;
        try
        {
            var legitimatelyHidden = _viewModel.Settings.AutoHideFullscreen && _hiddenForFullscreen;
            if (!legitimatelyHidden && Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            if (!_dimmed && GlassShell.Opacity < 0.99)
            {
                GlassShell.BeginAnimation(OpacityProperty, null);
                GlassShell.Opacity = 1;
            }
            // Re-assert top-most z-order. Windows silently drops a layered tool-window's top-most position
            // after a full-screen app, an explorer.exe restart, RDP/secure-desktop, or a display/GPU change
            // — the window stays "Visible" to WPF but renders behind everything (the "island vanished until
            // I hit Recenter" case). This heartbeat puts it back on top; SWP_NOMOVE/NOSIZE/NOACTIVATE keep
            // it cheap and non-disruptive (no reposition, no focus steal).
            if (!legitimatelyHidden && !_settingsWindowOpen && _viewModel.Settings.AlwaysOnTop) ReassertTopmost();
            EnsureOnScreen();
        }
        catch { }
    }

    private void ReassertTopmost()
    {
        if (!_sourceReady) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        Interop.NativeMethods.SetWindowPos(hwnd, Interop.NativeMethods.HwndTopmost, 0, 0, 0, 0,
            Interop.NativeMethods.SwpNoMove | Interop.NativeMethods.SwpNoSize | Interop.NativeMethods.SwpNoActivate);
    }

    private void EnsureOnScreen()
    {
        if (!_sourceReady) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !Interop.NativeMethods.GetWindowRect(hwnd, out var r)) return;
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;
        var minL = screens.Min(s => s.Bounds.Left); var maxR = screens.Max(s => s.Bounds.Right);
        var minT = screens.Min(s => s.Bounds.Top); var maxB = screens.Max(s => s.Bounds.Bottom);
        // Only act if the window has drifted completely off every monitor (e.g. a VDI resolution change).
        if (r.Right <= minL || r.Left >= maxR || r.Bottom <= minT || r.Top >= maxB)
            _position.PositionInitial(this, _viewModel.Settings);
    }

    // Idle dimming: fade the pill when it's been left alone, restore on hover.
    private void SetDimmed(bool dim)
    {
        var target = dim ? Math.Clamp(_viewModel.Settings.IdleOpacityPercent / 100.0, 0.2, 1.0) : 1.0;
        // Dimming the translucent shell also fades its protective material; keep it readable.
        if(_viewModel.Settings.Theme == DynamicIsland.Windows.Models.ThemeMode.Graphite) target=1;
        if (_dimmed == dim && Math.Abs(GlassShell.Opacity - target) < 0.01) return;
        _dimmed = dim;
        GlassShell.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // Auto-hide while a fullscreen app/game owns the foreground monitor.
    private int _fullscreenStreak;
    private void CheckFullscreen()
    {
        if (_hiddenForCapture) return;
        if (!_viewModel.Settings.AutoHideFullscreen)
        {
            _fullscreenStreak = 0;
            if (_hiddenForFullscreen) { _hiddenForFullscreen = false; Visibility = Visibility.Visible; }
            return;
        }
        bool fullscreen = false;
        try
        {
            var fg = Interop.NativeMethods.GetForegroundWindow();
            var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (fg != nint.Zero && fg != self && fg != Interop.NativeMethods.GetShellWindow() && !IsDesktopOrShell(fg))
            {
                // Never hide for one of our own windows, such as Settings.
                Interop.NativeMethods.GetWindowThreadProcessId(fg, out var pid);
                if (pid != (uint)Environment.ProcessId && Interop.NativeMethods.GetWindowRect(fg, out var r))
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
                    fullscreen = r.Left <= screen.Left && r.Top <= screen.Top
                              && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
                }
            }
        }
        catch { }

        // Debounce: require two consecutive detections before hiding (avoids transient misfires);
        // restore immediately the moment it's no longer fullscreen.
        _fullscreenStreak = fullscreen ? _fullscreenStreak + 1 : 0;
        var hide = _fullscreenStreak >= 2;
        if (hide != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = hide;
            Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        }
    }

    // The desktop ("Progman"/"WorkerW") and the taskbar fill the screen but must not count as fullscreen.
    private static bool IsDesktopOrShell(nint hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        Interop.NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    // Follow the monitor that owns the foreground window.
    private string _lastFollowDevice = "";
    private void CheckFollowScreen()
    {
        var s = _viewModel.Settings;
        var follow = s.FollowActiveScreen || s.PreferredMonitor.StartsWith("Active", StringComparison.OrdinalIgnoreCase);
        if (!follow || s.DefaultPosition == PositionMode.Manual) return;
        try
        {
            var fg = Interop.NativeMethods.GetForegroundWindow();
            if (fg == nint.Zero) return;
            var dev = System.Windows.Forms.Screen.FromHandle(fg).DeviceName;
            if (dev == _lastFollowDevice) return;
            _lastFollowDevice = dev;
            _position.PositionInitial(this, s);
        }
        catch { }
    }

    // Drag-to-scrub playback, with the same direct click behaviour as before.
    private void ProgressBar_SeekStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ActualWidth <= 0 || !_viewModel.CanSeek) return;
        _scrubbing = true;
        fe.CaptureMouse();
        SeekToPointer(fe, e.GetPosition(fe).X);
        e.Handled = true;
    }

    private void ProgressBar_SeekMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_scrubbing || sender is not FrameworkElement fe || fe.ActualWidth <= 0) return;
        SeekToPointer(fe, e.GetPosition(fe).X);
        e.Handled = true;
    }

    private void ProgressBar_SeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && _scrubbing && fe.ActualWidth > 0)
            SeekToPointer(fe, e.GetPosition(fe).X);
        EndScrub(sender as FrameworkElement);
        e.Handled = true;
    }

    private void ProgressBar_SeekCancel(object sender, System.Windows.Input.MouseEventArgs e) => EndScrub(sender as FrameworkElement);

    private void SeekToPointer(FrameworkElement track, double x)
    {
        var fraction = Math.Clamp(x / track.ActualWidth, 0, 1);
        if (_viewModel.SeekCommand.CanExecute(fraction)) _viewModel.SeekCommand.Execute(fraction);
    }

    private void EndScrub(FrameworkElement? track)
    {
        _scrubbing = false;
        if (track?.IsMouseCaptured == true) track.ReleaseMouseCapture();
    }

    // Click album art to open the source app.
    private void AlbumArt_Click(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Settings.ClickArtOpensApp && _viewModel.Media.HasSession)
        {
            _viewModel.OpenMediaAppCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Output-device row: opens an in-island picker listing active output endpoints; click one to switch default.
    private void OnOutputPickerInput(object sender, PreProcessInputEventArgs e)
    {
        if (!OutputDevicePopup.IsOpen || e.StagingItem.Input is not MouseButtonEventArgs mouse
            || mouse.RoutedEvent != Mouse.PreviewMouseDownEvent || mouse.ChangedButton != MouseButton.Left
            || mouse.ButtonState != MouseButtonState.Pressed
            || OutputDevicePopup.PlacementTarget is not FrameworkElement anchor || !anchor.IsVisible
            || !Interop.NativeMethods.GetCursorPos(out var cursor)) return;
        var point = anchor.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        if (point.X < 0 || point.Y < 0 || point.X >= anchor.ActualWidth || point.Y >= anchor.ActualHeight) return;
        OutputDevicePopup.IsOpen = false;
        mouse.Handled = true;
    }

    private void ToggleOutputDevicePopup(FrameworkElement? anchor)
    {
        if (OutputDevicePopup.IsOpen) { OutputDevicePopup.IsOpen = false; return; }
        _viewModel.RefreshOutputDevices();
        if (anchor is not null) OutputDevicePopup.PlacementTarget = anchor;
        OutputDevicePopup.IsOpen = true;
    }
    private void OutputDevice_Click(object sender, RoutedEventArgs e)
    {
        ToggleOutputDevicePopup(sender as FrameworkElement);
        e.Handled = true;
    }

    private void OutputDevicePopup_Opened(object? sender, EventArgs e)
    {
        _collapseTimer.Stop(); _idleTimer.Stop(); SetDimmed(false);
        _viewModel.InteractionProtected = true;
    }
    private void OutputDevicePopup_Closed(object? sender, EventArgs e)
    {
        _viewModel.InteractionProtected = _timerPanelOpen || IsKeyboardFocusWithin || Mouse.Captured is not null;
        if (!GlassShell.IsMouseOver && !_timerPanelOpen) Pill_MouseLeave(this, null!);
    }

    private void OutputDeviceItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
            _viewModel.SelectOutputDeviceCommand.Execute(id);
        OutputDevicePopup.IsOpen = false;
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => _viewModel.IsExpanded = false;

    // The More button owns its menu placement. Keeping this independent from drag handling prevents WPF
    // from treating the last pointer position as the popup anchor and detaching the menu from the island.
    private void IslandContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ContextMenu menu) _openIslandMenus.Add(menu);
        _collapseTimer.Stop(); _idleTimer.Stop(); SetDimmed(false);
        _viewModel.InteractionProtected=true;
    }
    private void IslandContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ContextMenu menu) _openIslandMenus.Remove(menu);
        _viewModel.InteractionProtected=_timerPanelOpen || OutputDevicePopup.IsOpen || _openIslandMenus.Count > 0 || IsKeyboardFocusWithin || Mouse.Captured is not null;
        if (_openIslandMenus.Count == 0 && !GlassShell.IsMouseOver) Pill_MouseLeave(this,null!);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu is not null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        }
    }

    private void RecenterMenu_Click(object sender, RoutedEventArgs e) => RecenterRequested?.Invoke(this, EventArgs.Empty);
    private void ClipboardMenu_Click(object sender, RoutedEventArgs e) => OpenClipboardRequested?.Invoke(this, EventArgs.Empty);
    private void OnNotificationGroupOpen(object? sender, EventArgs e) => NotificationHistoryPopup.IsOpen = true;
    private void NotificationHistoryMenu_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenNotificationHistoryCommand.Execute(null);
        NotificationHistoryPopup.IsOpen = true;
        e.Handled = true;
    }
    private void CollapseMenu_Click(object sender, RoutedEventArgs e) => _viewModel.IsExpanded = false;

    private void OpenNotification_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (ReferenceEquals(source, NotifDismissButton) || NotifDismissButton.IsAncestorOf(source)))
        {
            _log.Info("Notification X pressed; dismissing the current banner.");
            _viewModel.DismissCurrentNotificationCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (!_viewModel.ShowNotification) return;
        _viewModel.OpenCurrentNotificationCommand.Execute(null);
        e.Handled = true;
    }

    private void DismissNotification_Click(object sender, RoutedEventArgs e)
    {
        _log.Info("Notification X activated; dismissing the current banner.");
        _viewModel.DismissCurrentNotificationCommand.Execute(null);
        e.Handled = true;
    }

    private void OpenHistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: NotificationHistoryItem item })
            _viewModel.OpenNotificationCommand.Execute(item);
        e.Handled = true;
    }

    private void DismissHistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: NotificationHistoryItem item })
            _viewModel.DismissHistoryItemCommand.Execute(item);
        e.Handled = true;
    }

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_timerPanelOpen && _viewModel.IsCompact)
        {
            RequestMainExpansion();
            e.Handled = true;
        }
    }

    private void RequestMainExpansion()
    {
        if (_timerPanelOpen || !_viewModel.IsCompact) return;
        if (!_initialLayoutStabilized)
        {
            _mainExpansionPending = true;
            return;
        }
        _viewModel.IsExpanded = true;
    }

    private void DragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Settings.LockPosition) return;
        _dragging = true;
        try { DragMove(); }
        catch { }
        finally
        {
            _dragging = false;
            _position.CaptureManualPosition(this, _viewModel.Settings);
            _ = _settingsService.SaveAsync(_viewModel.Settings);
        }
        e.Handled = true;
    }

    private void ClipboardButton_Click(object sender, RoutedEventArgs e) => OpenClipboardRequested?.Invoke(this, EventArgs.Empty);
    private void JoinMeeting_Click(object sender, RoutedEventArgs e) { _viewModel.OpenMeetingCommand.Execute(null); e.Handled = true; }
    private void LiveWidgetsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableWidth <= 0d) return;
        scroller.ScrollToHorizontalOffset(
            Math.Clamp(scroller.HorizontalOffset - e.Delta, 0d, scroller.ScrollableWidth));
        e.Handled = true;
    }

    private void LiveWidgetsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller) return;
        UpdateLiveWidgetsOverflow(scroller);
    }

    private void AccessoryLane_SizeChanged(object sender, SizeChangedEventArgs e) => _viewModel.SetAccessoryLaneWidth(e.NewSize.Width);
    private void LiveWidgetsPreviousButton_Click(object sender, RoutedEventArgs e) { LiveWidgetsScroller.ScrollToHorizontalOffset(Math.Max(0, LiveWidgetsScroller.HorizontalOffset - 160)); e.Handled = true; }
    private void LiveWidgetsScroller_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        var target = e.Key switch { Key.Home => 0, Key.End => LiveWidgetsScroller.ScrollableWidth, Key.Left => LiveWidgetsScroller.HorizontalOffset - 160, _ => LiveWidgetsScroller.HorizontalOffset + 160 };
        LiveWidgetsScroller.ScrollToHorizontalOffset(Math.Clamp(target, 0, LiveWidgetsScroller.ScrollableWidth)); e.Handled = true;
    }
    private void LiveWidgetsScroller_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { if (e.NewFocus is FrameworkElement element) element.BringIntoView(); }
    private void LiveWidgetsMoreButton_Click(object sender, RoutedEventArgs e)
    {
        LiveWidgetsScroller.ScrollToHorizontalOffset(
            Math.Clamp(LiveWidgetsScroller.HorizontalOffset + 160d, 0d, LiveWidgetsScroller.ScrollableWidth));
        e.Handled = true;
    }

    private void UpdateLiveWidgetsOverflow(ScrollViewer scroller)
    {
        // Show the fade + chevron only while widget content is clipped on the right.
        var canScrollRight = scroller.ScrollableWidth > 0d &&
            scroller.HorizontalOffset < scroller.ScrollableWidth - 1d;
        LiveWidgetsPreviousButton.Visibility = scroller.HorizontalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
        var visibility = canScrollRight ? Visibility.Visible : Visibility.Collapsed;
        if (LiveWidgetsFade.Visibility != visibility) LiveWidgetsFade.Visibility = visibility;
        if (LiveWidgetsMoreButton.Visibility != visibility) LiveWidgetsMoreButton.Visibility = visibility;
    }

    private void TimerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTimerPanel();
        e.Handled = true;
    }

    private void TimerOrb_Click(object sender, MouseButtonEventArgs e)
    {
        ShowTimerPanel();
        e.Handled = true;
    }

    private void TimerOrb_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // The orb is a live activity affordance: hovering it should reveal the full
        // timer surface just like Apple's Dynamic Island, without requiring a click.
        if (!_timerPanelOpen) ShowTimerPanel();
    }

    private void TimerPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ScheduleTimerPanelAutoClose();
    }

    private void TimerPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_timerPanelOpen || _timerPanelClosing) return;
        _timerPanelLeaveTimer.Stop();
    }

    private void TimerOrb_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // The orb sits above the shell: hovering it opens the panel without the pointer
        // ever entering TimerPanelContent, so its leave must also arm the auto-close.
        ScheduleTimerPanelAutoClose();
    }

    private void ScheduleTimerPanelAutoClose()
    {
        if (!_timerPanelOpen || _timerPanelClosing)
        {
            _timerPanelLeaveTimer.Stop();
            return;
        }

        _timerPanelLeaveTimer.Stop();
        _timerPanelLeaveTimer.Start();
    }

    private void TryAutoCloseTimerPanel()
    {
        if (!_timerPanelOpen || _timerPanelClosing)
        {
            _timerPanelLeaveTimer.Stop();
            return;
        }

        // The panel is deliberately non-hit-testable during its morph. Never close it
        // based on the stale hover target that existed before the new surface settled.
        if (_timerPanelHostAnimating)
        {
            ScheduleTimerPanelAutoClose();
            return;
        }

        if (IsPointerOverTimerPanelHost())
        {
            _timerPanelLeaveTimer.Stop();
            return;
        }

        // During a click-open, the cursor can still be over the compact island that
        // launched this morph even when WPF has not delivered MouseEnter for the new
        // host yet. Treat that launch surface as hover and keep checking until it exits.
        if (CompactTimerOrb.IsMouseOver || IsPointerOverGlassShell())
        {
            ScheduleTimerPanelAutoClose();
            return;
        }

        if (Mouse.Captured is not null)
        {
            ScheduleTimerPanelAutoClose();
            return;
        }

        _timerPanelLeaveTimer.Stop();
        System.Windows.Input.Keyboard.ClearFocus();
        CloseTimerPanel();
    }

    private bool IsPointerOverTimerPanelHost()
    {
        if (TimerPanelHost.Visibility != Visibility.Visible || TimerPanelHost.ActualWidth <= 0 || TimerPanelHost.ActualHeight <= 0)
            return false;
        if (!Interop.NativeMethods.GetCursorPos(out var cursor)) return true;

        var point = TimerPanelHost.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.Y >= 0
            && point.X <= TimerPanelHost.ActualWidth
            && point.Y <= TimerPanelHost.ActualHeight;
    }

    private bool IsPointerOverGlassShell()
    {
        if (GlassShell.ActualWidth <= 0 || GlassShell.ActualHeight <= 0) return true;
        if (!Interop.NativeMethods.GetCursorPos(out var cursor)) return true;

        var point = GlassShell.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.Y >= 0
            && point.X <= GlassShell.ActualWidth
            && point.Y <= GlassShell.ActualHeight;
    }

    public void ShowTimerPanel()
    {
        if (_timerPanelClosing)
        {
            _timerPanelLeaveTimer.Stop();
            BeginTimerPanelHostOpen(animate: true);
            _timerPanelLeaveTimer.Start();
            return;
        }
        if (_timerPanelOpen)
        {
            RefreshTimerPanelHoverAfterOpen();
            return;
        }

        _collapseTimer.Stop();
        _timerPanelLeaveTimer.Stop();
        _idleTimer.Stop();
        SetDimmed(false);
        if (_viewModel.IsExpanded)
        {
            _suppressExpandedAnimation = true;
            _viewModel.IsExpanded = false;
            _suppressExpandedAnimation = false;
        }
        _viewModel.TimerEditorOpen = true;
        _viewModel.InteractionProtected = true;
        ShowTimerTab();
        // These view-model changes can synchronously raise ShowBanner and refresh layout.
        // Commit the open state afterwards so that refresh cannot settle the hidden timer
        // host at full size before the explicit animated layout below gets to open it.
        _timerPanelOpen = true;
        ForceShow();
        _position.ApplyWindowStyles(this, _viewModel.Settings, compact: false);
        ApplyLayout(animate: true);
        _timerPanelLeaveTimer.Start();
        _log.Info("In-island timer panel opened");
    }

    public void ToggleTimerPanel()
    {
        if (_timerPanelOpen) CloseTimerPanel();
        else ShowTimerPanel();
    }

    public void CloseTimerPanel()
    {
        _timerPanelLeaveTimer.Stop();
        if (!_timerPanelOpen || _timerPanelClosing) return;
        // Keep TimerEditorOpen true until the host has finished shrinking. This keeps the compact orb
        // hidden underneath the panel and lets the separate host land exactly on its orb geometry.
        BeginTimerPanelHostClose();
        _log.Info("In-island timer panel closed");
    }

    private void CloseTimerPanel_Click(object sender, RoutedEventArgs e)
    {
        CloseTimerPanel();
        e.Handled = true;
    }

    private void TimerTab_Click(object sender, RoutedEventArgs e)
    {
        ShowTimerTab();
        e.Handled = true;
    }

    private void AlarmTab_Click(object sender, RoutedEventArgs e)
    {
        ShowAlarmTab();
        e.Handled = true;
    }

    private void StopwatchTab_Click(object sender, RoutedEventArgs e)
    {
        ShowStopwatchTab();
        e.Handled = true;
    }

    private void TimerViewModel_ShowAlarmTabRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(ShowAlarmTab);
    }

    public void ShowAlarmTab()
    {
        TimerTabContent.Visibility = Visibility.Collapsed;
        AlarmTabContent.Visibility = Visibility.Visible;
        StopwatchTabContent.Visibility = Visibility.Collapsed;
        SetTimerTabVisual(TimerTabButton, false);
        SetTimerTabVisual(AlarmTabButton, true);
        SetTimerTabVisual(StopwatchTabButton, false);
        TimerHeaderIcon.Text = "\uE7B7";
        TimerFooterBorder.Visibility = Visibility.Visible;
        TimerHeaderTitle.Text = "Alarm";
        TimerHeaderSubtitle.Text = "Wake up. Do more.";
        TimerFooterText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TimerAlarmViewModel.AlarmStateText)));
    }

    public void ShowTimerTab()
    {
        TimerTabContent.Visibility = Visibility.Visible;
        AlarmTabContent.Visibility = Visibility.Collapsed;
        StopwatchTabContent.Visibility = Visibility.Collapsed;
        SetTimerTabVisual(TimerTabButton, true);
        SetTimerTabVisual(AlarmTabButton, false);
        SetTimerTabVisual(StopwatchTabButton, false);
        TimerHeaderIcon.Text = "\uE916";
        TimerFooterBorder.Visibility = Visibility.Collapsed;
        TimerHeaderTitle.Text = "Timer";
        TimerHeaderSubtitle.Text = "Set a duration. Stay focused.";
        TimerFooterText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TimerAlarmViewModel.TimerFooterText)));
    }

    public void ShowStopwatchTab()
    {
        TimerTabContent.Visibility = Visibility.Collapsed;
        AlarmTabContent.Visibility = Visibility.Collapsed;
        StopwatchTabContent.Visibility = Visibility.Visible;
        SetTimerTabVisual(TimerTabButton, false);
        SetTimerTabVisual(AlarmTabButton, false);
        SetTimerTabVisual(StopwatchTabButton, true);
        TimerHeaderIcon.Text = "\uE916";
        TimerFooterBorder.Visibility = Visibility.Visible;
        TimerHeaderTitle.Text = "Stopwatch";
        TimerHeaderSubtitle.Text = "Focus. Create. Get things done.";
        TimerFooterText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TimerAlarmViewModel.StopwatchText)));
    }

    private void SetTimerTabVisual(System.Windows.Controls.Button button, bool active)
    {
        button.Background = active
            ? _viewModel.AccentSoftBrush
            : System.Windows.Media.Brushes.Transparent;
        button.BorderBrush = active
            ? _viewModel.IslandDividerBrush
            : System.Windows.Media.Brushes.Transparent;
        button.Foreground = active
            ? System.Windows.Media.Brushes.White
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xB0, 0xCE));
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void GraphiteOutputRequested(object sender, RoutedEventArgs e)
    {
        ToggleOutputDevicePopup(sender as FrameworkElement);
    }

    // ---- Command palette integration ----

    /// <summary>Distance from the window top to the bottom of the glass shell (DIP), used to
    /// place the palette just below the island.</summary>
    public double ShellBottomDips => GlassShell.Margin.Top + GlassShell.ActualHeight;
    public Rect PaletteAnchorBounds
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var screen = GlassShell.PointToScreen(new System.Windows.Point());
            var origin = transform.Transform(screen);
            return new Rect(origin, GlassShell.RenderSize);
        }
    }
    public Rect PaletteWorkArea
    {
        get
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            return Rect.Transform(new Rect(area.X, area.Y, area.Width, area.Height), transform);
        }
    }

    public void FocusTimerPanel()
    {
        if (TimerPanelHost.Visibility != Visibility.Visible) return;
        TimerTabButton.Focus();
        Keyboard.Focus(TimerTabButton);
    }

    /// <summary>Fills the Q prompt without submitting, then hands focus to it.</summary>
    public void FocusAssistantComposer() => Dispatcher.BeginInvoke(() =>
    { QPromptBox.Focus(); Keyboard.Focus(QPromptBox); }, DispatcherPriority.Background);
    public void PrefillQPrompt(string? text)
    {
        QPromptBox.Text = text ?? string.Empty;
        UpdateQPromptComposer();
        Dispatcher.BeginInvoke(() =>
        {
            QPromptBox.Focus();
            Keyboard.Focus(QPromptBox);
        }, DispatcherPriority.Background);
    }

    private void QSettingsButton_Click(object sender, RoutedEventArgs e) => OpenQSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void RecenterButton_Click(object sender, RoutedEventArgs e) => RecenterRequested?.Invoke(this, EventArgs.Empty);

    private void QButton_Click(object sender, RoutedEventArgs e)
    {
        _qFollowLatest = true;
        _ = _viewModel.StartChatAsync();
        e.Handled = true;
    }

    private void QAsk_Click(object sender, RoutedEventArgs e) => _viewModel.SetQMode(DynamicIsland.Q.Core.QMode.Ask);
    private void QSay_Click(object sender, RoutedEventArgs e) => _viewModel.SetQMode(DynamicIsland.Q.Core.QMode.Say);
    private void QAllow_Click(object sender, RoutedEventArgs e) => _viewModel.AcceptQDisclosure();
    private async void QCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.QCanCopyResponse) return;
        _viewModel.CopyQResponse();
        if (sender is not System.Windows.Controls.Button button) return;
        var content = button.Content;
        button.Content = "Copied";
        await Task.Delay(1200);
        button.Content = content;
    }
    private void QStop_Click(object sender, RoutedEventArgs e) => _viewModel.StopAssistant();
    private async void QQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string prompt } || string.IsNullOrWhiteSpace(prompt)) return;
        _viewModel.SetQMode(DynamicIsland.Q.Core.QMode.Ask);
        QPromptBox.Text = prompt;
        await SubmitQPromptAsync();
    }

    private void ScheduleQTranscriptScroll()
    {
        if (!_sourceReady || !_qFollowLatest || !_viewModel.ShowQSurface) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_qFollowLatest) return;
            QTranscriptScroll.ScrollToEnd();
            QJumpToLatestButton.Visibility = Visibility.Collapsed;
        }, DispatcherPriority.Background);
    }

    private void QTranscriptScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        var atLatest = viewer.ScrollableHeight <= 1 || viewer.VerticalOffset >= viewer.ScrollableHeight - 12;
        if (e.VerticalChange != 0 && e.ExtentHeightChange == 0) _qFollowLatest = atLatest;
        if (e.ExtentHeightChange > 0 && _qFollowLatest)
            Dispatcher.BeginInvoke(viewer.ScrollToEnd, DispatcherPriority.Background);
        QJumpToLatestButton.Visibility = viewer.ScrollableHeight > 8 && !_qFollowLatest
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void QJumpToLatest_Click(object sender, RoutedEventArgs e)
    {
        _qFollowLatest = true;
        QTranscriptScroll.ScrollToEnd();
        QJumpToLatestButton.Visibility = Visibility.Collapsed;
    }

    private async void QRetry_Click(object sender, RoutedEventArgs e)
    {
        var prompt = _viewModel.QPromptText;
        if (string.IsNullOrWhiteSpace(prompt)) return;
        _qFollowLatest = true;
        QPromptBox.Clear();
        UpdateQPromptComposer();
        await _viewModel.SubmitQAsync(prompt);
    }

    private void QNew_Click(object sender, RoutedEventArgs e)
    {
        _qFollowLatest = true;
        QPromptBox.Clear();
        UpdateQPromptComposer();
        _viewModel.ClearQ();
        _ = _viewModel.StartChatAsync(newConversation: true);
    }
    private void QClose_Click(object sender, RoutedEventArgs e) => _viewModel.ClearQ();
    private async void QMic_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartVoiceAsync();
        QPromptBox.Focus();
    }
    private void QUseScreen_Click(object sender, RoutedEventArgs e) => _viewModel.QUseScreenNext = !_viewModel.QUseScreenNext;
    private async void QRead_Click(object sender, RoutedEventArgs e) => await _viewModel.ReadReplyAsync();
    public void ShowHistory()
    { _viewModel.OpenNotificationHistoryCommand.Execute(null); NotificationHistoryPopup.IsOpen = true; }
    private async void QSend_Click(object sender, RoutedEventArgs e) => await SubmitQPromptAsync();
    private void QSelector_DropDownClosed(object sender, EventArgs e)
    {
        if (_viewModel.ShowQSurface && !GlassShell.IsMouseOver)
        {
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, _viewModel.Settings.CollapseDelayMilliseconds));
            _collapseTimer.Start();
        }
    }

    internal static bool IsAssistantSubmitKey(Key key, ModifierKeys modifiers) =>
        key == Key.Enter && (modifiers & ModifierKeys.Shift) == 0;

    private async void QPromptBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await HandleAssistantEnterAsync(Keyboard.Modifiers, e.IsRepeat);
    }

    internal async Task HandleAssistantEnterAsync(ModifierKeys modifiers, bool isRepeat = false)
    {
        if (!IsAssistantSubmitKey(Key.Enter, modifiers))
        {
            if (QPromptBox.IsReadOnly) return;
            var insertion = QPromptBox.SelectionStart;
            QPromptBox.SelectedText = Environment.NewLine;
            QPromptBox.CaretIndex = insertion + Environment.NewLine.Length;
            QPromptBox.SelectionLength = 0;
            return;
        }
        if (!isRepeat) await SubmitQPromptAsync();
    }

    private void QPromptBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateQPromptComposer();

    private void UpdateQPromptComposer()
    {
        if (QPromptBox is null || QPromptPlaceholder is null || QSendButton is null) return;
        var hasPrompt = !string.IsNullOrWhiteSpace(QPromptBox.Text);
        QPromptPlaceholder.Visibility = hasPrompt ? Visibility.Collapsed : Visibility.Visible;
        QSendButton.IsEnabled = hasPrompt && !_viewModel.QCanStop;
    }

    private async Task SubmitQPromptAsync()
    {
        var prompt = QPromptBox.Text.Trim();
        if (prompt.Length == 0 || _viewModel.QCanStop) return;
        _qFollowLatest = true;
        QPromptBox.Clear();
        UpdateQPromptComposer();
        await _viewModel.SubmitQAsync(prompt);
    }

    private void SystemEventsOnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
        _position.PositionInitial(this, _viewModel.Settings));

    private void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(() =>
            {
                _position.ApplyWindowStyles(this, _viewModel.Settings, _viewModel.IsCompact);
                _position.PositionInitial(this, _viewModel.Settings);
            });
    }
}
