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
    private readonly IslandViewModel _islandViewModel;
    private readonly IslandWindow _islandWindow;
    private bool _previewPinnedExpanded;
    private bool _previewExpanded;

    public event EventHandler? OpenTimerRequested;

    public SettingsWindow(SettingsViewModel settingsViewModel, IslandViewModel islandViewModel, IslandWindow islandWindow)
    {
        InitializeComponent();
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
        _islandViewModel.PropertyChanged += IslandPreviewOnPropertyChanged;
        Closed += (_, _) => _islandViewModel.PropertyChanged -= IslandPreviewOnPropertyChanged;
        SetPreviewExpanded(false, animate: false);
        ShowSection("content");
    }

    private void CompactPreview_Click(object sender, RoutedEventArgs e)
    {
        _previewPinnedExpanded = false;
        SetPreviewExpanded(false, animate: true);
    }

    private void ExpandedPreview_Click(object sender, RoutedEventArgs e)
    {
        _previewPinnedExpanded = true;
        SetPreviewExpanded(true, animate: true);
    }

    private void Preview_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => SetPreviewExpanded(true, animate: true);

    private void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_previewPinnedExpanded) SetPreviewExpanded(false, animate: true);
    }

    private void SetPreviewExpanded(bool expanded, bool animate)
    {
        if (_previewExpanded == expanded && animate) return;
        _previewExpanded = expanded;

        CompactPreviewButton.Background = expanded ? System.Windows.Media.Brushes.Transparent : BrushFrom("#39414D");
        ExpandedPreviewButton.Background = expanded ? BrushFrom("#39414D") : System.Windows.Media.Brushes.Transparent;
        CompactPreviewButton.Foreground = expanded ? BrushFrom("#A7AFBA") : System.Windows.Media.Brushes.White;
        ExpandedPreviewButton.Foreground = expanded ? System.Windows.Media.Brushes.White : BrushFrom("#A7AFBA");

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
        if (!expanded) return (_islandViewModel.PreviewIslandWidth, _islandViewModel.PreviewIslandHeight);
        var source = _islandWindow.ExpandedPreviewVisual;
        var width = source.ActualWidth > 0 ? source.ActualWidth : 900d;
        var height = source.ActualHeight > 0 ? source.ActualHeight : 371d;
        var scale = Math.Min(400d / width, 300d / height);
        return (width * scale, height * scale);
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

    private void ShowSection(string key)
    {
        ContentPage.Visibility = key == "content" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = key == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        PositionPage.Visibility = key == "position" ? Visibility.Visible : Visibility.Collapsed;
        ActivitiesPage.Visibility = key == "activities" ? Visibility.Visible : Visibility.Collapsed;
        QPage.Visibility = key == "q" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = key == "advanced" ? Visibility.Visible : Visibility.Collapsed;

        ContentTab.IsChecked = key == "content";
        AppearanceTab.IsChecked = key == "appearance";
        PositionTab.IsChecked = key == "position";
        ActivitiesTab.IsChecked = key == "activities";
        QTab.IsChecked = key == "q";
        AdvancedTab.IsChecked = key == "advanced";
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
