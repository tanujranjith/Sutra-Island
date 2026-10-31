using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Windows.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;

namespace DynamicIsland.Windows.Views;

/// <summary>
/// Floating capture-mode toolbar. It is a separate WPF window owned by the WinForms
/// capture surface, so it always stays above the frozen desktop while keeping the
/// Island's graphite material and notification/palette reveal motion.
/// </summary>
public partial class CaptureToolbarWindow : Window
{
    private const int RevealMilliseconds = 380;
    private const int DismissMilliseconds = 170;

    private string _mode = CaptureOverlay.RegionMode;
    private bool _syncing;
    private bool _screenConfirmArmed;
    private bool _motionEnabled;
    private bool _dismissing;
    private int _generation;
    private Rect _workArea = SystemParameters.WorkArea;
    private TaskCompletionSource? _pendingDismiss;

    public event Action<string>? ModeRequested;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;

    public CaptureToolbarWindow()
    {
        InitializeComponent();
        SyncMode(_mode);
    }

    internal string ActiveMode => _mode;
    internal double RevealProgress => ToolbarMorph.RevealProgress;

    /// <summary>Shows the toolbar at the island's anchor and reveals it with the same 380 ms morphology.</summary>
    public void PresentFromIsland(Rect anchor, Rect workArea, bool reducedMotion)
    {
        _workArea = workArea;
        _motionEnabled = !reducedMotion;
        _dismissing = false;
        _generation++;
        ToolbarMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        ToolbarMorph.RevealProgress = reducedMotion ? 1 : 0;

        // Layout happens at zero opacity so the first frame cannot flash at a stale position.
        Opacity = 0;
        Show();
        UpdateLayout();
        Left = Math.Max(workArea.Left + 8,
            Math.Min(anchor.Left + anchor.Width / 2 - ActualWidth / 2, workArea.Right - ActualWidth - 8));
        Top = Math.Max(workArea.Top, anchor.Bottom - 2);
        KeepOnScreen();
        Opacity = 1;

        RegionButton.Focus();
        Keyboard.Focus(RegionButton);
        if (reducedMotion) return;

        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(RevealMilliseconds))
        {
            FillBehavior = FillBehavior.Stop
        };
        var generation = _generation;
        animation.Completed += (_, _) =>
        {
            if (generation != _generation || _dismissing || !IsVisible) return;
            ToolbarMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
            ToolbarMorph.RevealProgress = 1;
        };
        ToolbarMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, animation);
    }

    /// <summary>Moves keyboard focus from the overlay into the mode toolbar (Tab).</summary>
    public void FocusToolbar()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsVisible) return;
            Activate();
            var target = ModeButton(_mode);
            target.Focus();
            Keyboard.Focus(target);
        });
    }

    /// <summary>Mirrors a mode change made by the overlay without raising a second request.</summary>
    internal void SetMode(string mode)
    {
        if (mode == _mode) return;
        SyncMode(mode);
    }

    private void SyncMode(string mode)
    {
        _syncing = true;
        _mode = mode;
        _screenConfirmArmed = false;
        RegionButton.IsChecked = mode == CaptureOverlay.RegionMode;
        WindowButton.IsChecked = mode == CaptureOverlay.WindowMode;
        ScreenButton.IsChecked = mode == CaptureOverlay.ScreenMode;
        _syncing = false;
    }

    private RadioButton ModeButton(string mode) => mode switch
    {
        CaptureOverlay.WindowMode => WindowButton,
        CaptureOverlay.ScreenMode => ScreenButton,
        _ => RegionButton
    };

    private static string ModeOf(object sender) => sender switch
    {
        RadioButton { Name: nameof(WindowButton) } => CaptureOverlay.WindowMode,
        RadioButton { Name: nameof(ScreenButton) } => CaptureOverlay.ScreenMode,
        _ => CaptureOverlay.RegionMode
    };

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var mode = ModeOf(sender);
        if (mode == _mode) return;
        _mode = mode;
        _screenConfirmArmed = false;
        ModeRequested?.Invoke(mode);
    }

    /// <summary>
    /// The first click on Full screen selects the mode; a second click confirms it.
    /// RadioButton raises Checked before Click, so the first click leaves the guard off.
    /// </summary>
    private void Screen_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing || _mode != CaptureOverlay.ScreenMode) return;
        if (_screenConfirmArmed) ConfirmRequested?.Invoke();
        else _screenConfirmArmed = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ConfirmRequested?.Invoke();
            e.Handled = true;
            return;
        }

        var requested = e.Key switch
        {
            Key.R or Key.D1 => CaptureOverlay.RegionMode,
            Key.W or Key.D2 => CaptureOverlay.WindowMode,
            Key.F or Key.D3 => CaptureOverlay.ScreenMode,
            _ => null
        };
        if (requested is null) return;

        if (requested != _mode)
        {
            SyncMode(requested);
            ModeRequested?.Invoke(requested);
        }
        ModeButton(requested).Focus();
        e.Handled = true;
    }

    private void KeepOnScreen()
    {
        Top = Math.Max(_workArea.Top + 4, Math.Min(Top, _workArea.Bottom - ActualHeight - 4));
        Left = Math.Max(_workArea.Left + 4, Math.Min(Left, _workArea.Right - ActualWidth - 4));
    }

    /// <summary>Plays the 170 ms command-palette exit animation, then hides the toolbar.</summary>
    public Task DismissAsync()
    {
        if (_pendingDismiss is not null) return _pendingDismiss.Task;
        if (!IsVisible)
        {
            Hide();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDismiss = completion;

        void Complete()
        {
            Hide();
            completion.TrySetResult();
            if (ReferenceEquals(_pendingDismiss, completion)) _pendingDismiss = null;
        }

        if (!_motionEnabled)
        {
            Complete();
            return completion.Task;
        }

        _dismissing = true;
        var generation = ++_generation;
        var animation = new DoubleAnimation(ToolbarMorph.RevealProgress, 0,
            TimeSpan.FromMilliseconds(DismissMilliseconds)) { FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) =>
        {
            if (generation == _generation && _dismissing) Complete();
        };
        ToolbarMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, animation);
        return completion.Task;
    }
}
