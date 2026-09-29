using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _viewModel;
    private readonly Func<bool>? _showInScreenshots;
    private bool _syncingSelection;

    public CommandPaletteWindow(CommandPaletteViewModel viewModel, Func<bool>? showInScreenshots = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _showInScreenshots = showInScreenshots;
        DataContext = _viewModel;
        _viewModel.CloseRequested += (_, _) => SafeClose();
        _viewModel.SuggestionsRefreshed += (_, _) => SyncListFromViewModel();
        Deactivated += (_, _) => SafeClose();
        SourceInitialized += (_, _) => ApplyCaptureAffinity();
        Loaded += (_, _) =>
        {
            // Handle can be created after Loaded in some layered-window paths.
            ApplyCaptureAffinity();
            SyncListFromViewModel();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
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
        if (!IsVisible) return;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                SafeClose();
                break;
            case Key.Down:
                e.Handled = true;
                _viewModel.MoveSelection(+1);
                SyncListFromViewModel();
                break;
            case Key.Up:
                e.Handled = true;
                _viewModel.MoveSelection(-1);
                SyncListFromViewModel();
                break;
            case Key.Enter:
                e.Handled = true;
                _ = ExecuteAsync();
                break;
        }
    }

    private async System.Threading.Tasks.Task ExecuteAsync()
    {
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
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
        }
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var index = ItemIndexAt(e.GetPosition(SuggestionList));
        if (index < 0) return;
        _syncingSelection = true;
        _viewModel.SelectedIndex = index;
        _syncingSelection = false;
        _ = ExecuteAsync();
    }

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
