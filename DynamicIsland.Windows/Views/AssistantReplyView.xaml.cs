using System.Windows.Documents;
using System.Text.RegularExpressions;
using DynamicIsland.Q.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Windows.Infrastructure;

namespace DynamicIsland.Windows.Views;

public partial class AssistantReplyView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty BodyFontSizeProperty = DependencyProperty.Register(nameof(BodyFontSize), typeof(double), typeof(AssistantReplyView), new PropertyMetadata(13d, TypographyChanged));
    public static readonly DependencyProperty BodyLineHeightProperty = DependencyProperty.Register(nameof(BodyLineHeight), typeof(double), typeof(AssistantReplyView), new PropertyMetadata(21d, TypographyChanged));
    public static readonly DependencyProperty LeadFontSizeProperty = DependencyProperty.Register(nameof(LeadFontSize), typeof(double), typeof(AssistantReplyView), new PropertyMetadata(17d, TypographyChanged));
    public double BodyFontSize { get => (double)GetValue(BodyFontSizeProperty); set => SetValue(BodyFontSizeProperty, value); }
    public double BodyLineHeight { get => (double)GetValue(BodyLineHeightProperty); set => SetValue(BodyLineHeightProperty, value); }
    public double LeadFontSize { get => (double)GetValue(LeadFontSizeProperty); set => SetValue(LeadFontSizeProperty, value); }
    private static void TypographyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (AssistantReplyView)d;
        if (view.Answer is null) return;
        view.Answer.FontSize = view.BodyFontSize;
        view.Answer.LineHeight = view.BodyLineHeight;
        view.RenderCitations();
    }
    public static readonly DependencyProperty SearchingProperty = DependencyProperty.Register(nameof(Searching), typeof(bool), typeof(AssistantReplyView), new PropertyMetadata(false, SearchChanged));
    public static readonly DependencyProperty SearchUsedProperty = DependencyProperty.Register(nameof(SearchUsed), typeof(bool), typeof(AssistantReplyView), new PropertyMetadata(false, SearchChanged));
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(nameof(Sources), typeof(IReadOnlyList<QWebSource>), typeof(AssistantReplyView), new PropertyMetadata(null, SearchChanged));
    public static readonly DependencyProperty SearchNoticeProperty = DependencyProperty.Register(nameof(SearchNotice), typeof(string), typeof(AssistantReplyView), new PropertyMetadata("", SearchChanged));
    public static readonly DependencyProperty SuggestionsHtmlProperty = DependencyProperty.Register(nameof(SuggestionsHtml), typeof(string), typeof(AssistantReplyView), new PropertyMetadata("", SearchChanged));
    public static readonly DependencyProperty RawTextProperty = DependencyProperty.Register(nameof(RawText), typeof(string), typeof(AssistantReplyView), new PropertyMetadata("", SearchChanged));
    public bool Searching { get => (bool)GetValue(SearchingProperty); set => SetValue(SearchingProperty, value); }
    public bool SearchUsed { get => (bool)GetValue(SearchUsedProperty); set => SetValue(SearchUsedProperty, value); }
    public IReadOnlyList<QWebSource>? Sources { get => (IReadOnlyList<QWebSource>?)GetValue(SourcesProperty); set => SetValue(SourcesProperty, value); }
    public string SearchNotice { get => (string)GetValue(SearchNoticeProperty); set => SetValue(SearchNoticeProperty, value); }
    public string SuggestionsHtml { get => (string)GetValue(SuggestionsHtmlProperty); set => SetValue(SuggestionsHtmlProperty, value); }
    public string RawText { get => (string)GetValue(RawTextProperty); set => SetValue(RawTextProperty, value); }
    private static void SearchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (AssistantReplyView)d;
        if (view.WebActivity is null) return;
        view.UpdateSearch(); view.RenderCitations(); view.Update(false);
    }
    private void UpdateSearch()
    {
        WebActivity.Searching = Searching; WebActivity.Used = SearchUsed; WebActivity.Sources = Sources;
        WebActivity.Status = Status; WebActivity.ReducedMotion = ReducedMotion; WebActivity.NoticeText = SearchNotice; WebActivity.SuggestionsHtml = SuggestionsHtml;
    }
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
    { var view = (AssistantReplyView)sender; if (view.StatusLabel is not null) { view.StatusLabel.Text = ((string?)e.NewValue ?? "").TrimEnd('…', '.'); view.UpdateSearch(); view.UpdateCaret(); } }
    private static void TextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (AssistantReplyView)sender; if (view.Answer is null) return;
        view.AnswerText.Text = (string?)e.NewValue ?? "";
        view.RenderCitations();
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
        UpdateSearch();
        var showThinking = Active && !Searching;
        ActivityRow.Height = showThinking ? 40 : 0; ActivityRow.Opacity = showThinking ? 1 : 0;
        var animate = IsLoaded && IsVisible && !ReducedMotion;
        if (transition && animate)
        {
            var duration = TimeSpan.FromMilliseconds(180);
            ActivityRow.BeginAnimation(HeightProperty, new DoubleAnimation(height, ActivityRow.Height, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            ActivityRow.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, ActivityRow.Opacity, duration) { FillBehavior = FillBehavior.Stop });
        }
        if (!showThinking || !animate)
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
    private void RenderCitations()
    {
        if (Answer is null) return;
        var caret = Answer.Inlines.OfType<InlineUIContainer>().FirstOrDefault(inline => ReferenceEquals(inline.Child, StreamingCaret));
        Answer.Inlines.Clear();
        var math = GetMathDocument();
        var value = math.Formulas.Count > 0 ? math.Text : Text ?? "";
        Answer.LineStackingStrategy = math.Formulas.Count > 0 ? LineStackingStrategy.MaxHeight : LineStackingStrategy.BlockLineHeight;
        var leadEnd = RawText.TrimStart().StartsWith("**", StringComparison.Ordinal) || Regex.IsMatch(RawText, @"^#{1,3}\s")
            ? value.IndexOf('\n') : -1;
        void AddStyledText(string text, int offset)
        {
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (!math.Formulas.TryGetValue(text[i], out var formula)) continue;
                if (i > start) AddPlainText(text[start..i], offset + start);
                AddFormula(text[i], formula);
                start = i + 1;
            }
            if (start < text.Length) AddPlainText(text[start..], offset + start);
        }
        void AddPlainText(string text, int offset)
        {
            if (leadEnd > 0 && offset < leadEnd)
            {
                var count = Math.Min(text.Length, leadEnd - offset);
                Answer.Inlines.Add(new Run(text[..count]) { FontSize = LeadFontSize, FontWeight = FontWeights.SemiBold });
                if (count < text.Length) Answer.Inlines.Add(new Run(text[count..].StartsWith("\n\n", StringComparison.Ordinal) ? text[(count + 1)..] : text[count..]));
            }
            else Answer.Inlines.Add(new Run(text));
        }
        var sources = (Sources ?? []).Where(s => QWebSearch.IsWebUrl(s.Url)).ToArray();
        if (sources.Length == 0)
        {
            AddStyledText(value, 0);
            if (caret is not null) Answer.Inlines.Add(caret);
            return;
        }
        var urls = sources.Select(s => s.Url).Distinct().ToArray();
        Hyperlink Link(string url, string label, string title)
        {
            var link = new Hyperlink(new Run(label)) { ToolTip = title + "\n" + url, NavigateUri = new Uri(url), Cursor = System.Windows.Input.Cursors.Hand };
            link.SetResourceReference(Hyperlink.ForegroundProperty, "QAccent");
            link.RequestNavigate += (_, e) => { WebSearchView.OpenSource(e.Uri.AbsoluteUri); e.Handled = true; };
            return link;
        }
        // Match cited content after display formatting; API offsets count Unicode code points.
        var markers = new SortedDictionary<int, List<QWebSource>>();
        foreach (var source in sources)
        {
            var quote = source.CitedText;
            if (string.IsNullOrEmpty(quote) && source.StartIndex is >= 0 && source.EndIndex > source.StartIndex)
            {
                var a = QWebSearch.Utf16Offset(RawText, source.StartIndex.Value); var b = QWebSearch.Utf16Offset(RawText, source.EndIndex!.Value);
                if (b > a && b <= RawText.Length) quote = RawText[a..b];
            }
            if (string.IsNullOrWhiteSpace(quote)) continue;
            quote = quote.Replace("**", "").Replace("__", "").Replace("\u0060", "");
            var at = value.IndexOf(quote, StringComparison.Ordinal);
            if (at < 0) continue;
            var offset = at + quote.Length;
            if (!markers.TryGetValue(offset, out var group)) markers[offset] = group = [];
            if (!group.Any(s => s.Url == source.Url)) group.Add(source);
        }
        var cursor = 0; var linked = new HashSet<string>();
        foreach (Match md in Regex.Matches(value, @"\[([^\]\r\n]+)\]\((https?://[^\s)]+)\)"))
        {
            if (!QWebSearch.IsWebUrl(md.Groups[2].Value)) continue;
            AddText(cursor, md.Index);
            var url = md.Groups[2].Value; Answer.Inlines.Add(Link(url, md.Groups[1].Value, md.Groups[1].Value)); linked.Add(url);
            cursor = md.Index + md.Length;
        }
        AddText(cursor, value.Length);
        if (linked.Count == 0 && !string.IsNullOrWhiteSpace(value))
        {
            Answer.Inlines.Add(new Run("\nSources: "));
            foreach (var url in urls) Answer.Inlines.Add(Link(url, $"[{Array.IndexOf(urls, url) + 1}] ", sources.First(s => s.Url == url).Title));
        }
        if (caret is not null) Answer.Inlines.Add(caret);
        void AddText(int start, int end)
        {
            var position = start;
            foreach (var marker in markers.Where(k => k.Key > start && k.Key <= end))
            {
                AddStyledText(value[position..marker.Key], position); position = marker.Key;
                foreach (var source in marker.Value)
                {
                    Answer.Inlines.Add(Link(source.Url, $" [{Array.IndexOf(urls, source.Url) + 1}]", source.Title)); linked.Add(source.Url);
                }
            }
            if (position < end) AddStyledText(value[position..end], position);
        }
    }

}
