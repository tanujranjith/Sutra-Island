using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.ComponentModel;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

public partial class SettingsWindow : Window
{
    private void GraphiteHeader_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private readonly IslandViewModel _islandViewModel;
    private readonly IslandWindow _islandWindow;
    private bool _previewExpanded;
    private bool _previewPaneCollapsed;

    public event EventHandler? OpenTimerRequested;

    public SettingsWindow(SettingsViewModel settingsViewModel, IslandViewModel islandViewModel, IslandWindow islandWindow)
    {
        InitializeComponent();
        Infrastructure.NativeFrostBackdrop.Attach(this,()=> (GraphiteSettingsShell,new Rect(GraphiteSettingsShell.RenderSize),25), (active,inset) => { var tint=new SolidColorBrush(System.Windows.Media.Color.FromArgb(active?(byte)225:(byte)255,23,24,29)); GraphiteSettingsShell.Background=active?Infrastructure.FrostedMaterial.WithOpaqueCaps(tint,inset):tint; },()=>true);
        SizeChanged += (_, _) => GraphiteSettingsShell.Clip = new RectangleGeometry(new Rect(0, 0, GraphiteSettingsShell.ActualWidth, GraphiteSettingsShell.ActualHeight), 25, 25);
        _islandViewModel = islandViewModel;
        _islandWindow = islandWindow;
        ExpandedPreviewSurface.Fill = new VisualBrush(_islandWindow.ExpandedPreviewVisual)
        { Stretch = Stretch.Fill };
        _islandWindow.ExpandedPreviewVisual.SizeChanged += ExpandedPreviewVisual_SizeChanged;
        Closed += (_, _) => _islandWindow.ExpandedPreviewVisual.SizeChanged -= ExpandedPreviewVisual_SizeChanged;
        DataContext = settingsViewModel;
        settingsViewModel.PropertyChanged += SettingsOnPropertyChanged;
        Closed += (_, _) => settingsViewModel.PropertyChanged -= SettingsOnPropertyChanged;
        IsVisibleChanged += (_, _) => { if (!IsVisible) QApiKeyBox.Clear(); };
        PreviewIslandData.DataContext = islandViewModel;
        PreviewIslandData.SizeChanged += PreviewIslandData_SizeChanged;
        Closed += (_, _) => PreviewIslandData.SizeChanged -= PreviewIslandData_SizeChanged;
        _islandViewModel.PropertyChanged += IslandPreviewOnPropertyChanged;
        Closed += (_, _) => _islandViewModel.PropertyChanged -= IslandPreviewOnPropertyChanged;
        SetPreviewExpanded(false, animate: false);
        ShowSection("content");
        UpdateNavigationFilter(string.Empty);
    }

    private void TogglePreviewPane_Click(object sender, RoutedEventArgs e)
    {
        _previewPaneCollapsed = !_previewPaneCollapsed;
        PreviewPane.Visibility = _previewPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
        PreviewColumn.Width = _previewPaneCollapsed ? new GridLength(0) : new GridLength(307);
        ShowPreviewButton.Visibility = _previewPaneCollapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompactPreview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewExpanded(false, animate: true);
    }

    private void ExpandedPreview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewExpanded(true, animate: true);
    }

    private void SetPreviewExpanded(bool expanded, bool animate)
    {
        if (_previewExpanded == expanded && animate) return;
        _previewExpanded = expanded;

        CompactPreviewButton.Background = expanded ? System.Windows.Media.Brushes.Transparent : BrushFrom("#362F42");
        ExpandedPreviewButton.Background = expanded ? BrushFrom("#362F42") : System.Windows.Media.Brushes.Transparent;
        CompactPreviewButton.Foreground = expanded ? BrushFrom("#9998A4") : System.Windows.Media.Brushes.White;
        ExpandedPreviewButton.Foreground = expanded ? System.Windows.Media.Brushes.White : BrushFrom("#9998A4");

        if (!animate)
        {
            var (width, height) = PreviewSize(expanded);
            PreviewPill.BeginAnimation(WidthProperty, null);
            PreviewPill.BeginAnimation(HeightProperty, null);
            PreviewPill.Width = width;
            PreviewPill.Height = height;
            CompactPreviewContent.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            ExpandedPreviewContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            CompactPreviewContent.Opacity = expanded ? 0 : 1;
            ExpandedPreviewContent.Opacity = expanded ? 1 : 0;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var (targetWidth, targetHeight) = PreviewSize(expanded);
        PreviewPill.BeginAnimation(WidthProperty, new DoubleAnimation(
            targetWidth, duration) { EasingFunction = ease });
        PreviewPill.BeginAnimation(HeightProperty, new DoubleAnimation(
            targetHeight, duration) { EasingFunction = ease });

        var showing = expanded ? ExpandedPreviewContent : CompactPreviewContent;
        var hiding = expanded ? CompactPreviewContent : ExpandedPreviewContent;
        showing.Visibility = Visibility.Visible;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fadeOut.Completed += (_, _) => hiding.Visibility = Visibility.Collapsed;
        hiding.BeginAnimation(OpacityProperty, fadeOut);
        showing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
        { BeginTime = TimeSpan.FromMilliseconds(80) });
    }

    private (double Width, double Height) PreviewSize(bool expanded)
    {
        if (!expanded)
            return (_islandViewModel.PreviewIslandWidth, _islandViewModel.PreviewIslandHeight);

        var source = _islandWindow.ExpandedPreviewVisual;
        var width = source.ActualWidth > 0 ? source.ActualWidth : 900d;
        var height = source.ActualHeight > 0 ? source.ActualHeight : 371d;
        return (width, height);
    }

    private void PreviewIslandData_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var (width, height) = PreviewSize(_previewExpanded);
        PreviewPill.BeginAnimation(WidthProperty, null);
        PreviewPill.BeginAnimation(HeightProperty, null);
        PreviewPill.Width = width;
        PreviewPill.Height = height;
    }

    private void ExpandedPreviewVisual_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_previewExpanded) return;
        var (width, height) = PreviewSize(true);
        PreviewPill.BeginAnimation(WidthProperty, null);
        PreviewPill.BeginAnimation(HeightProperty, null);
        PreviewPill.Width = width;
        PreviewPill.Height = height;
    }

    private void IslandPreviewOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IslandViewModel.PreviewIslandWidth)
            or nameof(IslandViewModel.PreviewIslandHeight))) return;
        var (width, height) = PreviewSize(_previewExpanded);
        PreviewPill.BeginAnimation(WidthProperty, null);
        PreviewPill.BeginAnimation(HeightProperty, null);
        PreviewPill.Width = width;
        PreviewPill.Height = height;
    }

    private void OpenTimer_Click(object sender, RoutedEventArgs e) => OpenTimerRequested?.Invoke(this, EventArgs.Empty);

    // ---- Command palette shortcut capture ----
    private bool _capturingPaletteHotkey;

    private void ChangePaletteHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturingPaletteHotkey = true;
        PaletteHotkeyButton.Content = "Press shortcut… Esc cancels";
        PaletteHotkeyButton.Foreground = BrushFrom("#69B5FF");
    }

    private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingPaletteHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return; // wait for the actual key
        if (key == Key.Escape)
        {
            EndPaletteHotkeyCapture();
            return;
        }
        var combo = Infrastructure.HotkeyParser.Format(key, Keyboard.Modifiers);
        if (combo is null)
        {
            if (DataContext is SettingsViewModel settings)
                settings.SetCommandPaletteStatus("Use Ctrl, Alt, or Shift plus a letter, digit, F1–F12, or Space.");
            return;
        }
        if (DataContext is SettingsViewModel vm) vm.CommandPaletteShortcut = combo;
        EndPaletteHotkeyCapture();
    }

    private void EndPaletteHotkeyCapture()
    {
        _capturingPaletteHotkey = false;
        PaletteHotkeyButton.Content = "Change";
        PaletteHotkeyButton.Foreground = System.Windows.Media.Brushes.White;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Never carry an unsaved credential into another provider's editor.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.QSelectedProvider)) QApiKeyBox.Clear();
    }

    private async void CustomThemeColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings) return;
        var picker = new ThemeColorPickerWindow(settings.CustomThemeColorHex) { Owner = this };
        if (picker.ShowDialog() != true) return;
        settings.CustomThemeColorHex = picker.SelectedColorHex;
        await settings.SaveAsync();
    }

    private void SaveQApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || string.IsNullOrWhiteSpace(QApiKeyBox.Password)) return;
        settings.QApiKey = QApiKeyBox.Password;
        QApiKeyBox.Clear();
    }

    private void RemoveQApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings) return;
        settings.QApiKey = "";
        QApiKeyBox.Clear();
    }

    public void OpenSearch(string query)
    {
        var entry = Infrastructure.CommandPalette.SettingsSearchIndex.All.FirstOrDefault(x => x.Property.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? Infrastructure.CommandPalette.SettingsSearchIndex.Search(query).FirstOrDefault();
        var property = entry?.Property ?? query;
        var pages = new (FrameworkElement Page, string Key)[] { (ContentPage, "content"), (AppearancePage, "appearance"), (PositionPage, "position"), (ActivitiesPage, "activities"), (PollingPage, "polling"), (QPage, "q"), (ShortcutsPage, "shortcuts"), (AdvancedPage, "advanced") };
        foreach (var (page, key) in pages)
        {
            foreach (var control in LogicalDescendants(page).OfType<FrameworkElement>())
            {
                var properties = new[] { System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, System.Windows.Controls.Primitives.Selector.SelectedItemProperty, System.Windows.Controls.Primitives.Selector.SelectedValueProperty, System.Windows.Controls.TextBox.TextProperty, System.Windows.Controls.Primitives.RangeBase.ValueProperty };
                if (!properties.Any(dp => System.Windows.Data.BindingOperations.GetBindingExpression(control, dp)?.ParentBinding.Path?.Path == property)) continue;
                SettingsSearchBox.Text = "";
                if (DataContext is SettingsViewModel settings) settings.SelectedSectionKey = key;
                ShowSection(key);
                Dispatcher.BeginInvoke(() =>
                {
                    control.BringIntoView(); control.Focus();
                    var border = LogicalAncestors(control).OfType<Border>().FirstOrDefault();
                    if (border is null) return;
                    var saved = border.BorderBrush; var thickness = border.BorderThickness;
                    border.BorderBrush = System.Windows.Media.Brushes.DodgerBlue; border.BorderThickness = new Thickness(2);
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (_, _) => { timer.Stop(); border.BorderBrush = saved; border.BorderThickness = thickness; };
                    timer.Start();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
        }
        SettingsSearchBox.Text = entry?.Name ?? query;
        SettingsSearchBox.Focus();
    }
    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in LogicalDescendants(child)) yield return nested; }
    }
    private static IEnumerable<DependencyObject> LogicalAncestors(DependencyObject child)
    { for (var parent = LogicalTreeHelper.GetParent(child); parent is not null; parent = LogicalTreeHelper.GetParent(parent)) yield return parent; }
    public void OpenQSettings()
    {
        if (DataContext is SettingsViewModel settings) settings.SelectedSectionKey = "q";
        ShowSection("q");
    }

    // Use an explicit click handler so Done always waits for the settings file to be written before
    // hiding this modeless window. The previous fire-and-forget command could leave the panel visible.
    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
            await settings.SaveAsync();
        Hide();
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { CommandParameter: string key }) return;
        if (DataContext is SettingsViewModel settings) settings.SelectedSectionKey = key;
        ShowSection(key);
    }

    private void NavigateFromOverview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        if (DataContext is SettingsViewModel settings) settings.SelectedSectionKey = key;
        ShowSection(key);
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings) settings.SearchText = SettingsSearchBox.Text;
        UpdateNavigationFilter(SettingsSearchBox.Text);
    }

    private void UpdateNavigationFilter(string? search)
    {
        var query = search?.Trim() ?? string.Empty;
        bool Matches(System.Windows.Controls.Primitives.ToggleButton item, params string[] keywords) => query.Length == 0
            || keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase)
                || query.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        OverviewTab.Visibility = Matches(OverviewTab, "overview") ? Visibility.Visible : Visibility.Collapsed;
        ContentTab.Visibility = Matches(ContentTab, "island", "content", "media", "album", "volume", "audio", "battery", "clock", "date", "timer", "equalizer", "ram") ? Visibility.Visible : Visibility.Collapsed;
        AppearanceTab.Visibility = Matches(AppearanceTab, "island", "appearance", "theme", "color", "colour", "accent", "animation", "font", "scale", "art") ? Visibility.Visible : Visibility.Collapsed;
        PositionTab.Visibility = Matches(PositionTab, "island", "position", "monitor", "offset", "display", "placement", "screen") ? Visibility.Visible : Visibility.Collapsed;
        ActivitiesTab.Visibility = Matches(ActivitiesTab, "widgets", "connections", "activities", "weather", "notifications", "notification", "integrations", "network", "wifi", "bluetooth", "stocks", "calendar", "meeting", "clipboard", "privacy", "countdown", "caps", "num", "lock") ? Visibility.Visible : Visibility.Collapsed;
        PollingTab.Visibility = Matches(PollingTab, "polling", "rate", "refresh", "interval", "frequency", "cadence", "window", "screen", "wifi", "wireless", "connectivity", "clock", "date", "media", "playback", "audio", "timer", "battery", "system monitor", "privacy", "theme", "airpods", "notifications", "calendar", "stocks", "weather") ? Visibility.Visible : Visibility.Collapsed;
        QTab.Visibility = Matches(QTab, "q", "assistant", "provider", "model", "api", "prompt", "response", "tokens", "screen", "voice", "speech", "wake", "hey sutra") ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsTab.Visibility = Matches(ShortcutsTab, "shortcuts", "behavior", "command palette", "startup", "hotkey", "hover", "click-through", "always on top", "expand") ? Visibility.Visible : Visibility.Collapsed;
        AdvancedTab.Visibility = Matches(AdvancedTab, "advanced", "developer", "backup", "profiles", "warnings", "debug", "import", "export", "reset", "volume warning", "battery warning") ? Visibility.Visible : Visibility.Collapsed;

        var showIslandGroup = query.Length == 0 || "island content appearance position".Contains(query, StringComparison.OrdinalIgnoreCase);
        IslandNavLabel.Visibility = showIslandGroup ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchEmpty.Visibility = new System.Windows.Controls.Primitives.ToggleButton[] { OverviewTab, ContentTab, AppearanceTab, PositionTab, ActivitiesTab, PollingTab, QTab, ShortcutsTab, AdvancedTab }
            .Any(item => item.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowSection(string key)
    {
        OverviewPage.Visibility = key == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ContentPage.Visibility = key == "content" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = key == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        PositionPage.Visibility = key == "position" ? Visibility.Visible : Visibility.Collapsed;
        ActivitiesPage.Visibility = key == "activities" ? Visibility.Visible : Visibility.Collapsed;
        PollingPage.Visibility = key == "polling" ? Visibility.Visible : Visibility.Collapsed;
        QPage.Visibility = key == "q" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = key == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = key == "advanced" ? Visibility.Visible : Visibility.Collapsed;

        OverviewTab.IsChecked = key == "overview";
        ContentTab.IsChecked = key == "content";
        AppearanceTab.IsChecked = key == "appearance";
        PositionTab.IsChecked = key == "position";
        ActivitiesTab.IsChecked = key == "activities";
        PollingTab.IsChecked = key == "polling";
        QTab.IsChecked = key == "q";
        ShortcutsTab.IsChecked = key == "shortcuts";
        AdvancedTab.IsChecked = key == "advanced";
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
