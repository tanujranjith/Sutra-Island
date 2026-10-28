using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _viewModel;
    private readonly Func<bool>? _showInScreenshots;
    private readonly Infrastructure.CommandPalette.PaletteCommandHistory _commandHistory=new();
    private bool _applyingHistory;
    private bool _syncingSelection;
    private Task? _primeTask;
    private System.Windows.Media.Imaging.RenderTargetBitmap? _warmBitmap;
    public bool IsPriming { get; private set; }
    public bool IsPresentationReady => _primeTask?.IsCompletedSuccessfully == true;

    public CommandPaletteWindow(CommandPaletteViewModel viewModel, Func<bool>? showInScreenshots = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _showInScreenshots = showInScreenshots;
        DataContext = _viewModel;

        // Keep the morph surface stable throughout its opening animation.
        _viewModel.CloseRequested += (_, _) => SafeClose();
        _viewModel.SuggestionsRefreshed += (_, _) => SyncListFromViewModel();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => { StopPresentationMaintenance(); _viewModel.ClosePanel(); _viewModel.PropertyChanged -= ViewModel_PropertyChanged; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { ResetPresentationMotion(); _viewModel.Reset(); } };
        Deactivated += (_, _) => SafeClose();
        SourceInitialized += (_, _) => ApplyCaptureAffinity();
        SizeChanged += (_, _) => { if (IsVisible && !IsPriming && _paletteWorkArea is not null) KeepOnScreen(); };
        Loaded += (_, _) =>
        {
            // Handle can be created after Loaded in some layered-window paths.
            ApplyCaptureAffinity();
            SyncListFromViewModel();
            if (!IsPriming) { QueryBox.Focus(); Keyboard.Focus(QueryBox); }
        };
    }

    // Same rules as the Island: exclude from Windows capture APIs unless the user opted the
    // Island into screenshots. Real acrylic stays off — layered (AllowsTransparency) windows
    // can't clip it to the rounded card, so the frosted wash is pure WPF (see XAML).
    private void ApplyCaptureAffinity()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        var affinity = _showInScreenshots?.Invoke() == true
            ? Interop.NativeMethods.WdaNone
            : Interop.NativeMethods.WdaExcludeFromCapture;
        Interop.NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);
    }

    private void SafeClose()
    {
        if (IsPriming) return;
        if (!IsVisible) return;
        Dismiss();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.HasPanel)
        {
            if (ToolsView.HasOpenDropdown) return;
            if (e.Key == Key.Escape) { e.Handled = true; _viewModel.ClosePanel(); }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase)
            { e.Handled = true; _ = ExecuteAsync(); }
            return;
        }
        if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { e.Handled = true; _ = _viewModel.ToggleFavoriteAsync(); return; }
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                SafeClose();
                break;
            case Key.Down:
                e.Handled = true;
                if(_commandHistory.IsBrowsing && _commandHistory.Next(out var newer)){RecallCommand(newer);break;}
                _viewModel.MoveSelection(+1);
                SyncListFromViewModel();
                break;
            case Key.Up:
                e.Handled = true;
                if((_commandHistory.IsBrowsing || string.IsNullOrWhiteSpace(_viewModel.Query)) && _commandHistory.Previous(_viewModel.Query,out var older)){RecallCommand(older);break;}
                _viewModel.MoveSelection(-1);
                SyncListFromViewModel();
                break;
            case Key.Enter:
                e.Handled = true;
                _ = ExecuteAsync();
                break;
        }
    }

    private void RecallCommand(string query)
    {
        _applyingHistory=true;
        try { _viewModel.Query=query; QueryBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty,query); QueryBox.CaretIndex=QueryBox.Text.Length; QueryBox.SelectionLength=0; }
        finally { _applyingHistory=false; }
    }

    private async System.Threading.Tasks.Task ExecuteAsync()
    {
        if(!_viewModel.HasPanel) _commandHistory.Remember(_viewModel.Query);
        if (_viewModel.SelectedSuggestion is { IsCalculation: true, CalculationValue: { } value })
        {
            try
            {
                System.Windows.Clipboard.SetText(value);
                _viewModel.ReportCalculationCopied();
            }
            catch
            {
                _viewModel.ReportCalculationCopyFailed();
            }
            if (IsVisible)
            {
                QueryBox.Focus();
                Keyboard.Focus(QueryBox);
            }
            return;
        }

        await _viewModel.ExecuteAsync();
        if (IsVisible)
        {
            FocusActiveControl();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(CommandPaletteViewModel.Query) && !_applyingHistory) _commandHistory.StopBrowsing();
        if (e.PropertyName != nameof(CommandPaletteViewModel.Panel)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            var center = Left + ActualWidth / 2;
            Width = _viewModel.PreferredPanelWidth;
            UpdateLayout(); Left = center - ActualWidth / 2;
            FocusActiveControl(); KeepOnScreen();
        });
    }
    private void FocusActiveControl()
    {
        if (_viewModel.IsToolPanel) { ToolsView.FocusPanel(); return; }
        System.Windows.Controls.Control target = _viewModel.IsVolumePanel ? VolumeSlider
            : _viewModel.IsSeekPanel ? SeekSlider : _viewModel.IsTimerPanel ? TimerMinutesBox : _viewModel.IsAlarmPanel ? AlarmTimeBox : QueryBox;
        target.Focus(); Keyboard.Focus(target);
        if (target is System.Windows.Controls.TextBox text) text.SelectAll();
    }
    private void KeepOnScreen()
    {
        var area = _paletteWorkArea ?? SystemParameters.WorkArea;
        Top = Math.Max(area.Top + 8, Math.Min(Top, area.Bottom - ActualHeight - 8));
        Left = Math.Max(area.Left + 8, Math.Min(Left, area.Right - ActualWidth - 8));
    }
    private void Back_Click(object sender, RoutedEventArgs e) => _viewModel.ClosePanel();
    private void VolumePreset_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var percent)) _viewModel.VolumePercent = percent; }
    private void Mute_Click(object sender, RoutedEventArgs e) => _viewModel.TogglePanelMute();
    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e) => _viewModel.BeginSeekDrag();
    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e) => _viewModel.EndSeekDrag();
    private void SeekSlider_LostCapture(object sender, System.Windows.Input.MouseEventArgs e) => _viewModel.EndSeekDrag();
    private void SeekSkip_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var seconds)) _viewModel.SkipSeek(seconds); }
    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string value }) _viewModel.TimerMinutes = value; }
    private async void PanelSubmit_Click(object sender, RoutedEventArgs e) => await _viewModel.SubmitPanelAsync();
    private async void PanelList_Click(object sender, RoutedEventArgs e) => await _viewModel.OpenPanelListAsync();

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
                if (current is System.Windows.Controls.Button) return;
        }
        var index = ItemIndexAt(e.GetPosition(SuggestionList));
        if (index < 0) return;
        _syncingSelection = true;
        _viewModel.SelectedIndex = index;
        _syncingSelection = false;
        _ = ExecuteAsync();
    }

    public void PrepareForShow()
    { if (_viewModel.HasPanel || _viewModel.Query.Length != 0 || _viewModel.HasStatus) _viewModel.Reset(); Width = 640; ApplyCaptureAffinity(); }
    public void WarmLayout()
    {
        // Measure and render the content offscreen. Measuring a hidden Window alone
        // skips its templates, list containers, and first-use text rendering work.
        if (Content is not FrameworkElement content) return;
        content.Measure(new System.Windows.Size(Width, 720));
        content.Arrange(new Rect(new System.Windows.Point(), content.DesiredSize));
        content.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
        if (_warmBitmap is null || _warmBitmap.PixelWidth != width || _warmBitmap.PixelHeight != height)
            _warmBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        _warmBitmap.Clear(); _warmBitmap.Render(content);
    }
    public Task PrimePresentationAsync() => _primeTask ??= PrimeCoreAsync();
    private async Task PrimeCoreAsync()
    {
        // Exercise WPF's first HWND presentation with a fully transparent,
        // non-activating surface. No visible window or input target is exposed.
        IsPriming = true;
        var opacity = Opacity; var activated = ShowActivated; var topmost = Topmost;
        var hitTest = IsHitTestVisible; var left = Left; var top = Top;
        Opacity = 0; ShowActivated = false; Topmost = false; IsHitTestVisible = false;
        Left = -32000; Top = -32000;
        try
        {
            Show();
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Hide();
        }
        finally
        { Hide(); Left = left; Top = top; Opacity = opacity; ShowActivated = activated; Topmost = topmost; IsHitTestVisible = hitTest; IsPriming = false; }
    }
    public void FocusQuery()
    { QueryBox.Focus(); Keyboard.Focus(QueryBox); }
    private async void Favorite_Click(object sender, RoutedEventArgs e)
    { if (sender is System.Windows.Controls.Button { DataContext: CommandPaletteSuggestion suggestion }) await _viewModel.ToggleFavoriteAsync(suggestion); e.Handled = true; }
    private int ItemIndexAt(System.Windows.Point position)
    {
        if (SuggestionList.InputHitTest(position) is not DependencyObject hit) return -1;
        var container = ItemsControl.ContainerFromElement(SuggestionList, hit) as ListBoxItem;
        if (container is null) return -1;
        return SuggestionList.ItemContainerGenerator.IndexFromContainer(container);
    }

    private void SuggestionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (SuggestionList.SelectedIndex < 0) return;
        _syncingSelection = true;
        _viewModel.SelectedIndex = SuggestionList.SelectedIndex;
        _syncingSelection = false;
    }

    private void SyncListFromViewModel()
    {
        _syncingSelection = true;
        var index = _viewModel.SelectedIndex;
        if (index >= 0 && index < SuggestionList.Items.Count)
        {
            SuggestionList.SelectedIndex = index;
            SuggestionList.ScrollIntoView(SuggestionList.Items[index]);
        }
        else
        {
            SuggestionList.SelectedIndex = -1;
        }
        _syncingSelection = false;
    }
}
