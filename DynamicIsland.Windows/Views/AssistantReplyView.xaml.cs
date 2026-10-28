using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Windows.Views;

public partial class AssistantReplyView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(AssistantReplyView), new PropertyMetadata(false, StateChanged));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(AssistantReplyView), new PropertyMetadata(false, StateChanged));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(AssistantReplyView), new PropertyMetadata("", StatusChanged));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(AssistantReplyView), new PropertyMetadata("", TextChanged));
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    internal bool HasWorkingMotion { get; private set; }
    internal int MotionStarts { get; private set; }
    internal int AnswerRevealCount { get; private set; }
    internal string DisplayedText => AnswerText.Text;
    internal bool HasCaretMotion { get; private set; }
    private bool _revealed;

    public AssistantReplyView()
    {
        InitializeComponent();
        Loaded += (_, _) => Update(false);
        IsVisibleChanged += (_, _) => { if (IsVisible) Update(false); else StopAll(); };
        Unloaded += (_, _) => StopAll();
    }
    private static void StateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (AssistantReplyView)sender;
        if (e.Property == ActiveProperty && (bool)e.NewValue) view._revealed = false;
        if (view.ActivityRow is not null) view.Update(e.Property == ActiveProperty);
    }
    private static void StatusChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    { var view = (AssistantReplyView)sender; if (view.StatusLabel is not null) { view.StatusLabel.Text = ((string?)e.NewValue ?? "").TrimEnd('…', '.'); view.UpdateCaret(); } }
    private static void TextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (AssistantReplyView)sender; if (view.Answer is null) return;
        view.AnswerText.Text = (string?)e.NewValue ?? "";
        view.UpdateCaret();
        if (!view.Active || view._revealed || string.IsNullOrWhiteSpace(view.AnswerText.Text)) return;
        view._revealed = true;
        if (view.ReducedMotion || !view.IsLoaded || !view.IsVisible) return;
        view.AnswerRevealCount++;
        view.Answer.BeginAnimation(OpacityProperty, CreateEntryAnimation(0, 1));
        view.AnswerOffset.BeginAnimation(TranslateTransform.YProperty, CreateEntryAnimation(6, 0));
    }
    private static DoubleAnimationUsingKeyFrames CreateEntryAnimation(double from, double to)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(260), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new KeySpline(.22,.61,.36,1)));
        return animation;
    }
    private void Chip_SizeChanged(object sender, SizeChangedEventArgs e) => ChipClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    private void UpdateCaret()
    {
        if (StreamingCaret is null) return;
        var show = Active && !string.IsNullOrWhiteSpace(Text) && Status.StartsWith("Respond", StringComparison.OrdinalIgnoreCase);
        StreamingCaret.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || ReducedMotion || !IsLoaded || !IsVisible)
        { StreamingCaret.BeginAnimation(OpacityProperty, null); HasCaretMotion = false; return; }
        if (HasCaretMotion) return;
        HasCaretMotion = true;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1050), RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(525))));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1050))));
        StreamingCaret.BeginAnimation(OpacityProperty, blink);
    }
    private void StopLoop()
    {
        HasWorkingMotion = false;
        ChipSheen.BeginAnimation(TranslateTransform.XProperty, null); LabelShimmer.BeginAnimation(TranslateTransform.XProperty, null);
        foreach (var dot in new[] { Dot1, Dot2, Dot3 }) dot.BeginAnimation(OpacityProperty, null);
        foreach (var offset in new[] { DotOffset1, DotOffset2, DotOffset3 }) offset.BeginAnimation(TranslateTransform.YProperty, null);
        StreamingCaret.BeginAnimation(OpacityProperty, null); HasCaretMotion = false;
    }
    private void StopAll()
    {
        StopLoop();
        ActivityRow.BeginAnimation(HeightProperty, null); ActivityRow.BeginAnimation(OpacityProperty, null);
        Answer.BeginAnimation(OpacityProperty, null); AnswerOffset.BeginAnimation(TranslateTransform.YProperty, null);
    }
    private void Update(bool transition)
    {
        var height = ActivityRow.Height; var opacity = ActivityRow.Opacity;
        ActivityRow.BeginAnimation(HeightProperty, null); ActivityRow.BeginAnimation(OpacityProperty, null);
        ActivityRow.Height = Active ? 40 : 0; ActivityRow.Opacity = Active ? 1 : 0;
        var animate = IsLoaded && IsVisible && !ReducedMotion;
        if (transition && animate)
        {
            var duration = TimeSpan.FromMilliseconds(180);
            ActivityRow.BeginAnimation(HeightProperty, new DoubleAnimation(height, ActivityRow.Height, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            ActivityRow.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, ActivityRow.Opacity, duration) { FillBehavior = FillBehavior.Stop });
        }
        if (!Active || !animate)
        {
            StopLoop();
            StatusLabel.Foreground = TryFindResource("QMuted") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.LightSlateGray;
            Answer.BeginAnimation(OpacityProperty, null); AnswerOffset.BeginAnimation(TranslateTransform.YProperty, null);
            UpdateCaret();
            return;
        }
        UpdateCaret();
        if (HasWorkingMotion) return;
        HasWorkingMotion = true; MotionStarts++;
        StatusLabel.Foreground = StatusGradient;
        var sheen = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1900), RepeatBehavior = RepeatBehavior.Forever };
        sheen.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sheen.KeyFrames.Add(new SplineDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1140)), new KeySpline(.42,0,.58,1)));
        sheen.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        ChipSheen.BeginAnimation(TranslateTransform.XProperty, sheen);
        LabelShimmer.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(2, -2, TimeSpan.FromMilliseconds(2200)) { RepeatBehavior = RepeatBehavior.Forever });
        var dots = new[] { Dot1, Dot2, Dot3 }; var offsets = new[] { DotOffset1, DotOffset2, DotOffset3 };
        for (var i = 0; i < dots.Length; i++)
        {
            var delay = TimeSpan.FromMilliseconds(i * 180);
            dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            offsets[i].BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -2, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
    }
}
