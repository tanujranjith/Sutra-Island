using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Views;

/// <summary>Eye shapes. Open is the live, gazing default; Happy and Flat are used by the compact voice orb.</summary>
public enum SutraMascotExpression { Open, Happy, Flat }

public partial class SutraMascotView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(QRunState), typeof(SutraMascotView), new PropertyMetadata(QRunState.Idle, Changed));
    public static readonly DependencyProperty SpeakingProperty = DependencyProperty.Register(nameof(Speaking), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ShowThinkingBulbProperty = DependencyProperty.Register(nameof(ShowThinkingBulb), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ExpressionProperty = DependencyProperty.Register(nameof(Expression), typeof(SutraMascotExpression), typeof(SutraMascotView), new PropertyMetadata(SutraMascotExpression.Open, Changed));
    public static readonly DependencyProperty DesaturatedProperty = DependencyProperty.Register(nameof(Desaturated), typeof(bool), typeof(SutraMascotView), new PropertyMetadata(false, Changed));
    public QRunState State { get => (QRunState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public SutraMascotExpression Expression { get => (SutraMascotExpression)GetValue(ExpressionProperty); set => SetValue(ExpressionProperty, value); }
    /// <summary>Grey-blue artwork for an error.</summary>
    public bool Desaturated { get => (bool)GetValue(DesaturatedProperty); set => SetValue(DesaturatedProperty, value); }
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
    /// <summary>One blink now, independent of the idle blink loop (the compact voice exit).</summary>
    public void PlayBlink()
    {
        if (ReducedMotion || Expression != SutraMascotExpression.Open) return;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(220), FillBehavior = FillBehavior.Stop };
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }
    private static ImageSource? _greyArtwork;
    private void ApplyExpression()
    {
        var expression = Expression;
        OpenEyes.Visibility = expression == SutraMascotExpression.Open ? Visibility.Visible : Visibility.Collapsed;
        HappyEyes.Visibility = expression == SutraMascotExpression.Happy ? Visibility.Visible : Visibility.Collapsed;
        FlatEyes.Visibility = expression == SutraMascotExpression.Flat ? Visibility.Visible : Visibility.Collapsed;
        if (Desaturated && GreyArtwork.Source is null)
        {
            try
            {
                if (_greyArtwork is null && TryFindResource("SutraMascotArtwork") is System.Windows.Media.Imaging.BitmapSource art)
                {
                    var grey = new System.Windows.Media.Imaging.FormatConvertedBitmap(art, PixelFormats.Gray8, null, 0);
                    grey.Freeze();
                    _greyArtwork = grey;
                }
                GreyArtwork.Source = _greyArtwork;
            }
            catch (Exception) { /* the tint alone still reads as grey-blue */ }
        }
        GreyArtwork.Visibility = GreyTint.Visibility = Desaturated ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Update()
    {
        var oldX = Gaze.X; var oldY = Gaze.Y;
        StopMotion(); Blink.ScaleY = 1;
        ApplyExpression();
        CornerBulb.Visibility = ShowThinkingBulb && State is (QRunState.Capturing or QRunState.Thinking or QRunState.Streaming) ? Visibility.Visible : Visibility.Collapsed;
        var thinking = State is QRunState.Thinking or QRunState.Capturing;
        Gaze.X = thinking ? 12 : 0;
        Gaze.Y = thinking ? -12 : State == QRunState.Listening ? -6 : State == QRunState.Error ? 6 : 0;
        if (Expression != SutraMascotExpression.Open) { Gaze.X = Gaze.Y = 0; }
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
        if (Expression == SutraMascotExpression.Open) Blink.BeginAnimation(ScaleTransform.ScaleYProperty, CreateBlinkAnimation());
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(260);
        Gaze.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(oldX, Gaze.X, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Gaze.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(oldY, Gaze.Y, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (Speaking)
            Gaze.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(480)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }
}
