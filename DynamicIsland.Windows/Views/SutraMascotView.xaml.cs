using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Views;

public partial class SutraMascotView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(QRunState), typeof(SutraMascotView), new PropertyMetadata(QRunState.Idle, Changed));
    public static readonly DependencyProperty SpeakingProperty = DependencyProperty.Register(nameof(Speaking), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ShowThinkingBulbProperty = DependencyProperty.Register(nameof(ShowThinkingBulb), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public QRunState State { get => (QRunState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool Speaking { get => (bool)GetValue(SpeakingProperty); set => SetValue(SpeakingProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public bool ShowThinkingBulb { get => (bool)GetValue(ShowThinkingBulbProperty); set => SetValue(ShowThinkingBulbProperty, value); }
    internal bool HasBulbMotion { get; private set; }
    internal bool IsBulbVisible => CornerBulb.Visibility == Visibility.Visible;
    internal bool HasEyeMotion { get; private set; }
    internal double EyeOpenness => Blink.ScaleY;
    internal double GazeX => Gaze.X;
    internal double GazeY => Gaze.Y;

    public SutraMascotView()
    {
        InitializeComponent();
        Loaded += (_, _) => Update();
        IsVisibleChanged += (_, _) => { if (IsVisible) Update(); else StopMotion(); };
        Unloaded += (_, _) => StopMotion();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    { var view = (SutraMascotView)sender; if (view.Eyes is not null) view.Update(); }
    private void StopMotion()
    {
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Gaze.BeginAnimation(TranslateTransform.XProperty, null); Gaze.BeginAnimation(TranslateTransform.YProperty, null);
        HasEyeMotion = false;
        BulbScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); BulbScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BulbGlow.BeginAnimation(OpacityProperty, null); BulbRays.BeginAnimation(OpacityProperty, null);
        HasBulbMotion = false;
    }
    internal static DoubleAnimationUsingKeyFrames CreateBlinkAnimation()
    {
        // New native character motion: the source checkout contains no independent eye rig.
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.6), RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.8))));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(.08, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.88))));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.02))));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.6))));
        return blink;
    }
    private void Update()
    {
        var oldX = Gaze.X; var oldY = Gaze.Y;
        StopMotion(); Blink.ScaleY = 1;
        CornerBulb.Visibility = ShowThinkingBulb && State is (QRunState.Capturing or QRunState.Thinking or QRunState.Streaming) ? Visibility.Visible : Visibility.Collapsed;
        var thinking = State is QRunState.Thinking or QRunState.Capturing;
        Gaze.X = thinking ? 12 : 0;
        Gaze.Y = thinking ? -12 : State == QRunState.Listening ? -6 : State == QRunState.Error ? 6 : 0;
        if (ReducedMotion || !IsLoaded || !IsVisible) { Gaze.X = Gaze.Y = 0; return; }
        HasEyeMotion = true;
        if (IsBulbVisible)
        {
            HasBulbMotion = true;
            BulbGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, .8, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            BulbRays.BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                BulbScale.BeginAnimation(axis, new DoubleAnimation(.96, 1.04, TimeSpan.FromMilliseconds(1000)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, CreateBlinkAnimation());
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(260);
        Gaze.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(oldX, Gaze.X, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Gaze.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(oldY, Gaze.Y, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (Speaking)
            Gaze.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(480)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }
}
