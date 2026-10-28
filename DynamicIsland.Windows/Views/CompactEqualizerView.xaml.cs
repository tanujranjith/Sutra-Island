using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.Windows.Views;

public partial class CompactEqualizerView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(CompactEqualizerView), new PropertyMetadata(false, StateChanged));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(CompactEqualizerView), new PropertyMetadata(false, StateChanged));
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    private const double LaneWidth = 32.5;
    public CompactEqualizerView()
    {
        InitializeComponent();
        Loaded += (_, _) => Update(false);
        IsVisibleChanged += (_, _) => Update(false);
        Unloaded += (_, _) => Update(false);
    }
    private static void StateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((CompactEqualizerView)sender).Update(e.Property == ActiveProperty);
    private void Update(bool animate)
    {
        var width = Width; var opacity = Opacity;
        BeginAnimation(WidthProperty, null); BeginAnimation(OpacityProperty, null);
        var targetWidth = Active ? LaneWidth : 0;
        var targetOpacity = Active ? 1d : 0d;
        Width = targetWidth; Opacity = targetOpacity;
        if (!animate || ReducedMotion || !IsLoaded || !IsVisible) return;
        // Start from the current presentation so rapid reversals never jump or replay stale completions.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(Active ? 220 : 180);
        BeginAnimation(WidthProperty, new DoubleAnimation(width, targetWidth, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, targetOpacity, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
}
