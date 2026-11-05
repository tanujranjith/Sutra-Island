using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;
using Canvas = System.Windows.Controls.Canvas;
using Brush = System.Windows.Media.Brush;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace DynamicIsland.Windows.Views;

/// <summary>
/// The compact voice orb hosted inside the compact island. It only presents: the view model's
/// <see cref="CompactVoicePresenter"/> decides the state and its timing. Motion is WPF animation on
/// transforms and opacity; only the voice bars poll the microphone level (≤30 Hz, while listening).
/// </summary>
public partial class CompactVoiceView : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(CompactVoiceState), typeof(CompactVoiceView), new PropertyMetadata(CompactVoiceState.Idle, (d, e) => ((CompactVoiceView)d).ApplyState((CompactVoiceState)e.OldValue, (CompactVoiceState)e.NewValue)));
    public static readonly DependencyProperty TranscriptProperty = DependencyProperty.Register(nameof(Transcript), typeof(string), typeof(CompactVoiceView), new PropertyMetadata("", (d, _) => ((CompactVoiceView)d).UpdateTranscript()));
    public static readonly DependencyProperty AnswerProperty = DependencyProperty.Register(nameof(Answer), typeof(string), typeof(CompactVoiceView), new PropertyMetadata("", (d, _) => ((CompactVoiceView)d).OnAnswerChanged()));
    public static readonly DependencyProperty ActionFeedbackProperty = DependencyProperty.Register(nameof(ActionFeedback), typeof(AssistantActionFeedback), typeof(CompactVoiceView), new PropertyMetadata(null, (d, _) => ((CompactVoiceView)d).OnActionChanged()));
    public static readonly DependencyProperty ActionFromLevelProperty = DependencyProperty.Register(nameof(ActionFromLevel), typeof(double?), typeof(CompactVoiceView), new PropertyMetadata(null));
    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(nameof(ErrorText), typeof(string), typeof(CompactVoiceView), new PropertyMetadata("", (d, e) => ((CompactVoiceView)d).ErrorMessageText.Text = (string?)e.NewValue ?? ""));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(CompactVoiceView), new PropertyMetadata(false));
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(nameof(Palette), typeof(ThemePalette), typeof(CompactVoiceView), new PropertyMetadata(ThemePalette.Dark, (d, _) => ((CompactVoiceView)d).RefreshTranscriptColor()));
    /// <summary>The compact island height the orb lays out against (the answer grows below it).</summary>
    public static readonly DependencyProperty BaseHeightProperty = DependencyProperty.Register(nameof(BaseHeight), typeof(double), typeof(CompactVoiceView), new PropertyMetadata(62d, (d, _) => ((CompactVoiceView)d).ApplyMetrics()));
    private static readonly DependencyProperty DisplayedLevelProperty = DependencyProperty.Register("DisplayedLevel", typeof(double), typeof(CompactVoiceView), new PropertyMetadata(0d, (d, e) => ((CompactVoiceView)d).RenderLevel((double)e.NewValue)));

    public CompactVoiceState State { get => (CompactVoiceState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Transcript { get => (string)GetValue(TranscriptProperty); set => SetValue(TranscriptProperty, value); }
    public string Answer { get => (string)GetValue(AnswerProperty); set => SetValue(AnswerProperty, value); }
    public AssistantActionFeedback? ActionFeedback { get => (AssistantActionFeedback?)GetValue(ActionFeedbackProperty); set => SetValue(ActionFeedbackProperty, value); }
    public double? ActionFromLevel { get => (double?)GetValue(ActionFromLevelProperty); set => SetValue(ActionFromLevelProperty, value); }
    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public ThemePalette Palette { get => (ThemePalette)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }
    public double BaseHeight { get => (double)GetValue(BaseHeightProperty); set => SetValue(BaseHeightProperty, value); }
    /// <summary>The microphone RMS level, 0..1.</summary>
    public Func<float>? LevelSource { get; set; }

    public event EventHandler? OpenInChatRequested;
    public event EventHandler? FixRequested;
    public event EventHandler? MascotClicked;

    internal bool HasLoopMotion { get; private set; }
    internal bool BarsRunning => _barsTimer?.IsEnabled == true;

    private Color TranscriptColor
    {
        get
        {
            var color = ThemeColor.Parse(State == CompactVoiceState.Thinking ? Palette.Muted : Palette.Text);
            return Color.FromRgb(color.R, color.G, color.B);
        }
    }
    private void RefreshTranscriptColor() => SetTranscriptColor(TranscriptColor, animate: false);
    private static readonly double[] BarWeights = [0.6, 0.85, 1, 0.85, 0.6];
    private readonly Rectangle[] _bars;
    private readonly Dictionary<UIElement, int> _fades = [];
    private readonly List<(Run Run, SolidColorBrush Brush)> _runs = [];
    private string[] _words = [];
    private DispatcherTimer? _barsTimer;
    private readonly double[] _barLevels = new double[5];
    private DateTime _barsStarted;
    private double _k = 62d / 110d, _barWidth = 4, _barPitch = 8.5, _barMax = 22;

    public CompactVoiceView()
    {
        InitializeComponent();
        _bars = [Bar0, Bar1, Bar2, Bar3, Bar4];
        SizeChanged += (_, _) => { LayoutSlider(); RenderLevel((double)GetValue(DisplayedLevelProperty)); };
        Unloaded += (_, _) => { StopLoops(); StopBarsTimer(); };
        ApplyMetrics();
    }

    // ===== Sizing: the mock is drawn on a 110-px-tall island; scale it to the real one. =====
    private void ApplyMetrics()
    {
        var h = Math.Max(38d, BaseHeight);
        _k = Math.Clamp(h / 110d, 0.35, 0.85);
        var k = _k;
        var mascot = Math.Max(26d, Math.Round(72 * k));
        MascotHost.Width = MascotHost.Height = mascot;
        var left = Math.Round(22 * k);
        Canvas.SetLeft(MascotHost, left);
        Canvas.SetTop(MascotHost, Math.Round((h - mascot) / 2));
        ErrorDot.Width = ErrorDot.Height = Math.Max(6, Math.Round(14 * k));
        _barWidth = Math.Max(3, Math.Round(7 * k * 2) / 2);
        _barPitch = Math.Max(6, 15 * k);
        _barMax = Math.Max(14, Math.Round(40 * k));
        var barsWidth = _barPitch * 4 + _barWidth;
        Bars.Width = barsWidth; Bars.Height = h;
        Bars.Margin = new Thickness(0, 0, Math.Round(40 * k), 0);
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i].Width = _barWidth; _bars[i].RadiusX = _bars[i].RadiusY = _barWidth / 2;
            Canvas.SetLeft(_bars[i], i * _barPitch);
            SetBarHeight(i, _barWidth);
        }
        var textLeft = left + mascot + Math.Round(23 * k);
        TextArea.Margin = new Thickness(textLeft, 0, Math.Round(40 * k), 0);
        ListenLayer.Height = ActionLayer.Height = ErrorLayer.Height = h;
        ListenLayer.Margin = new Thickness(0, 0, barsWidth + Math.Round(16 * k), 0);
        var transcript = Math.Max(11.5, 24 * k);
        Placeholder.FontSize = TranscriptText.FontSize = ShimmerText.FontSize = transcript;
        var answer = Math.Max(11.5, 23 * k);
        AnswerText.FontSize = answer;
        AnswerText.LineHeight = Math.Round(answer * 1.3);
        AnswerText.MaxHeight = AnswerText.LineHeight * 2 + 1;
        AnswerText.Margin = new Thickness(0, Math.Max(4, Math.Round(50 * k - AnswerText.LineHeight / 2)), 0, 0);
        OpenInChatLink.FontSize = Math.Max(10, 15 * k);
        OpenInChatLink.Margin = new Thickness(0, 0, 0, Math.Max(5, Math.Round(14 * k)));
        ActionIcon.FontSize = Math.Max(14, 30 * k);
        ActionIcon.Margin = new Thickness(0, 0, Math.Round(18 * k), 0);
        ActionLabel.FontSize = Math.Max(10.5, 17 * k);
        ActionDetail.FontSize = Math.Max(9.5, 14 * k);
        ActionDetail.Margin = new Thickness(Math.Round(10 * k), 0, 0, 0);
        ActionConfirm.FontSize = transcript;
        ActionValue.FontSize = Math.Max(15, 34 * k);
        ActionValue.Margin = new Thickness(Math.Round(24 * k), 0, 0, 0);
        SliderCanvas.Height = Math.Max(10, Math.Round(18 * k));
        SliderCanvas.Margin = new Thickness(0, Math.Round(14 * k), 0, 0);
        ErrorMessageText.FontSize = transcript;
        FixLink.FontSize = Math.Max(10.5, 16 * k);
        FixLink.Margin = new Thickness(Math.Round(12 * k), 0, 0, 0);
        LayoutSlider();
    }

    private void LayoutSlider()
    {
        var knob = SliderCanvas.Height;
        var track = Math.Max(4, Math.Round(knob * 0.3));
        SliderTrack.Height = SliderFill.Height = track;
        SliderTrack.CornerRadius = SliderFill.CornerRadius = new CornerRadius(track / 2);
        Canvas.SetTop(SliderTrack, (knob - track) / 2); Canvas.SetTop(SliderFill, (knob - track) / 2);
        SliderKnob.Width = SliderKnob.Height = knob;
        SliderTrack.Width = Math.Max(0, SliderCanvas.ActualWidth);
    }

    private void RenderLevel(double level)
    {
        var fraction = Math.Clamp(level / 100d, 0, 1);
        var width = Math.Max(0, SliderCanvas.ActualWidth);
        SliderTrack.Width = width;
        SliderFill.Width = width * fraction;
        Canvas.SetLeft(SliderKnob, Math.Clamp(width * fraction - SliderKnob.Width / 2, -SliderKnob.Width / 2, Math.Max(0, width - SliderKnob.Width / 2)));
        ActionValue.Text = $"{Math.Round(level):0}%";
        ActionIcon.Text = level <= 0 ? "\uE992" : CompactVoicePolicy.VolumeWaves(level) switch { 3 => "\uE995", 2 => "\uE994", _ => "\uE993" };
    }

    // ===== State =====
    private void ApplyState(CompactVoiceState from, CompactVoiceState to)
    {
        var motion = CompactVoiceMotion.For(to, ReducedMotion);
        StopLoops();
        if (to == CompactVoiceState.Idle) { ResetToIdle(); return; }
        Visibility = Visibility.Visible;
        var fadeMs = ReducedMotion ? 150 : 220;
        var entering = from is CompactVoiceState.Idle or CompactVoiceState.Exit && to != CompactVoiceState.Exit;
        if (entering) Enter();

        Mascot.ReducedMotion = ReducedMotion;
        Mascot.Desaturated = to == CompactVoiceState.Error;
        Mascot.Expression = to switch
        {
            CompactVoiceState.Answer or CompactVoiceState.Action => SutraMascotExpression.Happy,
            CompactVoiceState.Error => SutraMascotExpression.Flat,
            _ => SutraMascotExpression.Open
        };
        Mascot.State = to switch
        {
            CompactVoiceState.Listening => QRunState.Listening,
            CompactVoiceState.Thinking => QRunState.Thinking,
            _ => QRunState.Idle
        };
        ErrorDot.Visibility = to == CompactVoiceState.Error ? Visibility.Visible : Visibility.Collapsed;

        var listen = to is CompactVoiceState.Wake or CompactVoiceState.Listening or CompactVoiceState.Thinking;
        Fade(ListenLayer, listen ? 1 : 0, fadeMs);
        Fade(AnswerLayer, to == CompactVoiceState.Answer ? 1 : 0, fadeMs, to == CompactVoiceState.Answer && !ReducedMotion ? 120 : 0);
        Fade(ActionLayer, to == CompactVoiceState.Action ? 1 : 0, fadeMs);
        Fade(ErrorLayer, to == CompactVoiceState.Error ? 1 : 0, fadeMs);
        SetTranscriptColor(TranscriptColor, animate: !ReducedMotion);
        UpdateTranscript();

        if (to is CompactVoiceState.Wake or CompactVoiceState.Listening) StartBars();
        else if (from is CompactVoiceState.Wake or CompactVoiceState.Listening) FlattenBars();
        else Fade(Bars, 0, fadeMs);

        if (to == CompactVoiceState.Answer) StartTyping(); else ClearAnswer();
        if (to == CompactVoiceState.Action) ApplyAction();
        if (to == CompactVoiceState.Exit) Leave();

        // Loops: each runs only in its state, and never with reduced motion.
        if (motion.Ripples) { StartRipple(Ripple1, Ripple1Scale, 0); StartRipple(Ripple2, Ripple2Scale, 550); }
        if (motion.Orbit)
        {
            OrbitBox.BeginAnimation(OpacityProperty, Anim(1, 200));
            OrbitAngle.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(857)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        if (motion.Bob)
            MascotShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, 2, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        if (motion.Shimmer)
        {
            ShimmerText.BeginAnimation(OpacityProperty, Anim(1, 200));
            ShimmerShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        if (to == CompactVoiceState.Answer)
        {
            if (motion.Breathe)
            {
                foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                    MascotBreath.BeginAnimation(axis, new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(455)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                AnswerGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 0.95, TimeSpan.FromMilliseconds(455)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }
            else AnswerGlow.BeginAnimation(OpacityProperty, Anim(0.7, fadeMs));
        }
        HasLoopMotion = motion.Ripples || motion.Orbit || motion.Bob || motion.Shimmer || (to == CompactVoiceState.Answer && motion.Breathe);
    }

    private void Enter()
    {
        var reduced = ReducedMotion;
        Surface.BeginAnimation(OpacityProperty, Anim(1, reduced ? 150 : CompactVoiceTimings.ContentFade.TotalMilliseconds, from: Surface.Opacity));
        OrbContent.BeginAnimation(OpacityProperty, null); OrbContent.Opacity = 1;
        MascotHost.BeginAnimation(OpacityProperty, null);
        MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MascotShift.BeginAnimation(TranslateTransform.XProperty, null);
        if (reduced)
        {
            MascotScale.ScaleX = MascotScale.ScaleY = 1; MascotShift.X = 0; MascotHost.Opacity = 0;
            MascotHost.BeginAnimation(OpacityProperty, Anim(1, 200, from: 0));
            return;
        }
        // Starts 150 ms after the wake: fades in over 500 ms, slides 16 px from the left (cubic ease-out),
        // and scales 0.72 → 1.0 with a gentle back ease. It never drops in from above.
        MascotHost.Opacity = 0; MascotScale.ScaleX = MascotScale.ScaleY = 0.72; MascotShift.X = -16;
        var delay = CompactVoiceTimings.MascotDelay;
        MascotHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, CompactVoiceTimings.MascotFade) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        MascotShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(550)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, EasingFunction = back });
        MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, EasingFunction = back });
    }

    private void Leave()
    {
        var reduced = ReducedMotion;
        var mascotMs = reduced ? 200 : CompactVoiceTimings.ExitMascot.TotalMilliseconds;
        MascotHost.BeginAnimation(OpacityProperty, Anim(0, mascotMs, from: MascotHost.Opacity));
        if (!reduced)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var duration = CompactVoiceTimings.ExitMascot;
            MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.8, duration) { EasingFunction = ease });
            MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.8, duration) { EasingFunction = ease });
            MascotShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-12, duration) { EasingFunction = ease });
            Mascot.PlayBlink();
        }
        // The island's own content comes back from ~350 ms in.
        var begin = reduced ? 150 : CompactVoiceTimings.ExitContentReturn.TotalMilliseconds;
        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(reduced ? 200 : 380)) { BeginTime = TimeSpan.FromMilliseconds(begin), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        AnswerGlow.BeginAnimation(OpacityProperty, Anim(0, 200, from: AnswerGlow.Opacity));
    }

    private void ResetToIdle()
    {
        StopBarsTimer();
        foreach (var element in new UIElement[] { Surface, MascotHost, ListenLayer, AnswerLayer, ActionLayer, ErrorLayer, Bars, AnswerGlow, Placeholder, OpenInChatLink })
        { element.BeginAnimation(OpacityProperty, null); element.Opacity = 0; }
        ListenLayer.Visibility = AnswerLayer.Visibility = ActionLayer.Visibility = ErrorLayer.Visibility = Bars.Visibility = Visibility.Collapsed;
        MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MascotShift.BeginAnimation(TranslateTransform.XProperty, null);
        MascotScale.ScaleX = MascotScale.ScaleY = 0.72; MascotShift.X = -16;
        BeginAnimation(DisplayedLevelProperty, null);
        ClearAnswer();
        _runs.Clear(); _words = []; TranscriptText.Inlines.Clear(); ShimmerText.Text = "";
        Mascot.Expression = SutraMascotExpression.Open; Mascot.Desaturated = false; Mascot.State = QRunState.Idle;
        ErrorDot.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    private void StopLoops()
    {
        foreach (var (ring, scale) in new[] { (Ripple1, Ripple1Scale), (Ripple2, Ripple2Scale) })
        {
            ring.BeginAnimation(OpacityProperty, null); ring.Opacity = 0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        OrbitBox.BeginAnimation(OpacityProperty, null); OrbitBox.Opacity = 0;
        OrbitAngle.BeginAnimation(RotateTransform.AngleProperty, null);
        MascotShift.BeginAnimation(TranslateTransform.YProperty, null); MascotShift.Y = 0;
        ShimmerText.BeginAnimation(OpacityProperty, null); ShimmerText.Opacity = 0;
        ShimmerShift.BeginAnimation(TranslateTransform.XProperty, null);
        MascotBreath.BeginAnimation(ScaleTransform.ScaleXProperty, null); MascotBreath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MascotBreath.ScaleX = MascotBreath.ScaleY = 1;
        var glow = AnswerGlow.Opacity;
        AnswerGlow.BeginAnimation(OpacityProperty, null); AnswerGlow.Opacity = glow;
        if (State != CompactVoiceState.Answer) AnswerGlow.BeginAnimation(OpacityProperty, Anim(0, 200, from: glow));
        HasLoopMotion = false;
    }

    private void StartRipple(UIElement ring, ScaleTransform scale, int delayMs)
    {
        var delay = TimeSpan.FromMilliseconds(delayMs);
        var cycle = TimeSpan.FromMilliseconds(1100);
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0, cycle) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(axis, new DoubleAnimation(1, 1.5, cycle) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ===== Transcript =====
    private void UpdateTranscript()
    {
        var text = (Transcript ?? "").Trim();
        var listening = State is CompactVoiceState.Wake or CompactVoiceState.Listening or CompactVoiceState.Thinking;
        Fade(Placeholder, listening && text.Length == 0 && State != CompactVoiceState.Thinking ? 1 : 0, 160, State == CompactVoiceState.Wake && !ReducedMotion ? 300 : 0);
        var words = text.Length == 0 ? [] : text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var extends = _words.Length <= words.Length && _words.AsSpan().SequenceEqual(words.AsSpan(0, _words.Length));
        if (!extends || _runs.Count != _words.Length)
        {
            TranscriptText.Inlines.Clear(); _runs.Clear(); _words = [];
        }
        var color = TranscriptColor;
        // Fix the separator of the previous last word before appending.
        if (_runs.Count > 0 && words.Length > _runs.Count) _runs[^1].Run.Text = _words[^1] + " ";
        for (var i = _runs.Count; i < words.Length; i++)
        {
            var brush = new SolidColorBrush(color);
            var run = new Run(words[i] + (i < words.Length - 1 ? " " : "")) { Foreground = brush };
            TranscriptText.Inlines.Add(run);
            _runs.Add((run, brush));
            // Each new word fades in.
            if (!ReducedMotion && extends) brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        _words = words;
        ShimmerText.Text = string.Join(' ', words);
        // Keep the tail visible when the line overflows.
        Dispatcher.BeginInvoke(() => TranscriptScroll.ScrollToRightEnd(), DispatcherPriority.Loaded);
    }

    private void SetTranscriptColor(Color color, bool animate)
    {
        foreach (var (_, brush) in _runs)
        {
            if (brush.Color == color) continue;
            if (animate) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(260)));
            else { brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = color; }
        }
    }

    // ===== Answer =====
    private void OnAnswerChanged() { if (State == CompactVoiceState.Answer) StartTyping(); }

    private void StartTyping()
    {
        var text = (Answer ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length > 240) text = text[..240];
        AnswerText.BeginAnimation(System.Windows.Controls.TextBlock.TextProperty, null);
        OpenInChatLink.BeginAnimation(OpacityProperty, null); OpenInChatLink.Opacity = 0;
        var typing = CompactVoiceTimings.Typing(text, ReducedMotion);
        if (typing == TimeSpan.Zero)
        {
            AnswerText.Text = text;
            OpenInChatLink.BeginAnimation(OpacityProperty, Anim(1, 150, from: 0));
            return;
        }
        AnswerText.Text = "";
        var animation = new StringAnimationUsingKeyFrames { Duration = typing, FillBehavior = FillBehavior.HoldEnd };
        var perChar = typing.TotalMilliseconds / text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ' && i != text.Length - 1) continue;
            var end = i == text.Length - 1 ? text.Length : i;
            animation.KeyFrames.Add(new DiscreteStringKeyFrame(text[..end], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(end * perChar))));
        }
        AnswerText.BeginAnimation(System.Windows.Controls.TextBlock.TextProperty, animation);
        // "Open in chat" appears after the text finishes.
        OpenInChatLink.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = typing });
    }

    private void ClearAnswer()
    {
        // On exit keep what is on screen (the typed-out text) while the layer fades.
        var shown = AnswerText.Text;
        AnswerText.BeginAnimation(System.Windows.Controls.TextBlock.TextProperty, null);
        AnswerText.Text = State == CompactVoiceState.Exit ? shown : "";
    }

    // ===== System command =====
    private void OnActionChanged() { if (State == CompactVoiceState.Action) ApplyAction(); }

    private void ApplyAction()
    {
        if (ActionFeedback is not { } action) return;
        var level = CompactVoicePolicy.ShowsLevel(action);
        LevelRow.Visibility = ActionValue.Visibility = level ? Visibility.Visible : Visibility.Collapsed;
        ActionConfirm.Visibility = level ? Visibility.Collapsed : Visibility.Visible;
        if (!level)
        {
            BeginAnimation(DisplayedLevelProperty, null);
            ActionIcon.Text = action.Phase == AssistantActionPhase.Failed ? "\uE7BA" : AssistantActionView.Glyph(action.Command);
            ActionConfirm.Text = CompactVoicePolicy.ActionConfirmation(action);
            return;
        }
        ActionLabel.Text = CompactVoicePolicy.ActionLabel(action.Command);
        ActionDetail.Text = "Speakers · System output";
        var to = Math.Clamp(action.Level!.Value, 0, 100);
        var from = Math.Clamp(ActionFromLevel ?? to, 0, 100);
        Dispatcher.BeginInvoke(() =>
        {
            LayoutSlider();
            if (ReducedMotion) { BeginAnimation(DisplayedLevelProperty, null); SetValue(DisplayedLevelProperty, to); RenderLevel(to); return; }
            // The slider and the number move from the old level to what the system reports, over ~1 s.
            BeginAnimation(DisplayedLevelProperty, new DoubleAnimation(from, to, CompactVoiceTimings.ActionSettle) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }, DispatcherPriority.Loaded);
    }

    // ===== Voice bars =====
    private void SetBarHeight(int i, double height)
    {
        var bar = _bars[i];
        bar.Height = height;
        Canvas.SetTop(bar, (Bars.Height - height) / 2);
    }

    private void StartBars()
    {
        Fade(Bars, 1, 200);
        for (var i = 0; i < _bars.Length; i++) { _bars[i].BeginAnimation(HeightProperty, null); _barLevels[i] = 0; SetBarHeight(i, _barWidth); }
        if (_barsTimer is null)
        {
            _barsTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _barsTimer.Tick += (_, _) => TickBars();
        }
        _barsStarted = DateTime.UtcNow;
        _barsTimer.Start();
    }

    private void TickBars()
    {
        if (!IsVisible || State is not (CompactVoiceState.Wake or CompactVoiceState.Listening)) { StopBarsTimer(); return; }
        var level = Math.Clamp(LevelSource?.Invoke() ?? 0f, 0f, 1f);
        var t = (DateTime.UtcNow - _barsStarted).TotalSeconds;
        for (var i = 0; i < _bars.Length; i++)
        {
            var wobble = ReducedMotion ? 1 : 0.78 + 0.22 * Math.Sin(t * (7 + i * 1.7) + i);
            var target = Math.Clamp(level * BarWeights[i] * wobble * 1.25, 0, 1);
            _barLevels[i] += (target - _barLevels[i]) * (target > _barLevels[i] ? 0.55 : 0.25);
            SetBarHeight(i, _barWidth + (_barMax - _barWidth) * _barLevels[i]);
        }
    }

    private void StopBarsTimer() => _barsTimer?.Stop();

    /// <summary>When speech ends the bars flatten to dots, then fade out.</summary>
    private void FlattenBars()
    {
        StopBarsTimer();
        var duration = TimeSpan.FromMilliseconds(ReducedMotion ? 1 : 200);
        for (var i = 0; i < _bars.Length; i++)
        {
            var bar = _bars[i];
            Canvas.SetTop(bar, (Bars.Height - _barWidth) / 2);
            bar.BeginAnimation(HeightProperty, new DoubleAnimation(bar.ActualHeight > 0 ? bar.ActualHeight : bar.Height, _barWidth, duration) { FillBehavior = FillBehavior.Stop });
            bar.Height = _barWidth;
        }
        Fade(Bars, 0, 250, ReducedMotion ? 0 : 220);
    }

    // ===== Helpers =====
    private static DoubleAnimation Anim(double to, double ms, double? from = null) => from is double f
        ? new DoubleAnimation(f, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
        : new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    /// <summary>Crossfade one layer; a newer fade on the same element wins over a stale completion.</summary>
    private void Fade(UIElement element, double to, double ms, double beginMs = 0)
    {
        var generation = _fades.TryGetValue(element, out var g) ? g + 1 : 1;
        _fades[element] = generation;
        if (to > 0) element.Visibility = Visibility.Visible;
        else if (element.Visibility != Visibility.Visible) return;
        var current = element.Opacity;
        var animation = new DoubleAnimation(current, to, TimeSpan.FromMilliseconds(Math.Max(1, ms))) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to <= 0) animation.Completed += (_, _) => { if (_fades.TryGetValue(element, out var latest) && latest == generation) element.Visibility = Visibility.Collapsed; };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void Mascot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { MascotClicked?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    private void OpenInChat_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { OpenInChatRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    private void Fix_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { FixRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
}
