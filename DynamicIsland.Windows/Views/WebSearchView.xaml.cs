using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Q.Core;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;

namespace DynamicIsland.Windows.Views;

public partial class WebSearchView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SearchingProperty = DependencyProperty.Register(nameof(Searching), typeof(bool), typeof(WebSearchView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty UsedProperty = DependencyProperty.Register(nameof(Used), typeof(bool), typeof(WebSearchView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(WebSearchView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(WebSearchView), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty NoticeTextProperty = DependencyProperty.Register(nameof(NoticeText), typeof(string), typeof(WebSearchView), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty SuggestionsHtmlProperty = DependencyProperty.Register(nameof(SuggestionsHtml), typeof(string), typeof(WebSearchView), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(nameof(Sources), typeof(IReadOnlyList<QWebSource>), typeof(WebSearchView), new PropertyMetadata(null, Changed));
    public bool Searching { get => (bool)GetValue(SearchingProperty); set => SetValue(SearchingProperty, value); }
    public bool Used { get => (bool)GetValue(UsedProperty); set => SetValue(UsedProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string NoticeText { get => (string)GetValue(NoticeTextProperty); set => SetValue(NoticeTextProperty, value); }
    public string SuggestionsHtml { get => (string)GetValue(SuggestionsHtmlProperty); set => SetValue(SuggestionsHtmlProperty, value); }
    public IReadOnlyList<QWebSource>? Sources { get => (IReadOnlyList<QWebSource>?)GetValue(SourcesProperty); set => SetValue(SourcesProperty, value); }
    private string _cardKey = "", _html = "";
    private bool _all;
    private bool _shimmering;
    public WebSearchView()
    {
        InitializeComponent(); Loaded += (_, _) => Update();
        IsVisibleChanged += (_, _) => UpdateMotion(); Unloaded += (_, _) => Stop();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var v = (WebSearchView)d; if (v.Layout is not null) v.Update(); }
    private void Stop() { Shimmer.BeginAnimation(TranslateTransform.XProperty, null); _shimmering = false; }
    private void UpdateMotion()
    {
        Globe.Active = Searching; Globe.ReducedMotion = ReducedMotion;
        if (!Searching || ReducedMotion || !IsLoaded || !IsVisible) { Stop(); return; }
        if (_shimmering) return;
        _shimmering = true;
        Shimmer.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(1.3, -1.3, TimeSpan.FromSeconds(2.2)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    private void Update()
    {
        Layout.Visibility = Searching || Used || !string.IsNullOrWhiteSpace(NoticeText) ? Visibility.Visible : Visibility.Collapsed;
        SearchRow.Visibility = Searching || Used ? Visibility.Visible : Visibility.Collapsed;
        Globe.Visibility = Searching ? Visibility.Visible : Visibility.Collapsed;
        DoneMark.Visibility = !Searching && Used ? Visibility.Visible : Visibility.Collapsed;
        SearchLabel.Text = Searching ? (Status.Contains("web", StringComparison.OrdinalIgnoreCase) ? Status : "Searching the web…") : !string.IsNullOrWhiteSpace(NoticeText) ? "Search needs attention" : "Search complete";
        DoneMark.Text = string.IsNullOrWhiteSpace(NoticeText) ? "✓" : "!";
        if (Searching && !ReducedMotion) SearchLabel.Foreground = ShimmerBrush;
        else SearchLabel.SetResourceReference(TextBlock.ForegroundProperty, Searching ? "QAccent" : "QMuted");
        var sources = (Sources ?? []).Where(s => QWebSearch.IsWebUrl(s.Url)).GroupBy(s => s.Url).Select(g => g.OrderByDescending(s => s.Title != s.Domain).First()).ToArray();
        SourceCount.Text = sources.Length > 0 ? $"{sources.Length} source{(sources.Length == 1 ? "" : "s")} found" : Searching ? "Finding relevant sources…" : "";
        var key = string.Join("|", sources.Select(s => s.Url + s.Title)) + _all;
        if (_cardKey != key)
        {
            _cardKey = key; SourceCards.Children.Clear();
            foreach (var (source, index) in sources.Take(_all ? 100 : 4).Select((s, i) => (s, i)))
            {
                var title = new TextBlock { Text = $"{index + 1}  {source.Title}", FontSize = 12, MaxWidth = 215, TextTrimming = TextTrimming.CharacterEllipsis };
                title.SetResourceReference(TextBlock.ForegroundProperty, "QText");
                var domain = new TextBlock { Text = source.Domain, FontSize = 10, Margin = new Thickness(14, 3, 0, 0) };
                domain.SetResourceReference(TextBlock.ForegroundProperty, "QMuted");
                var stack = new StackPanel(); stack.Children.Add(title); stack.Children.Add(domain);
                var card = new Button { Content = stack, ToolTip = source.Url, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 7), BorderThickness = new Thickness(1), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand };
                card.Style = (Style)FindResource("SourceCardStyle");
                card.SetResourceReference(Button.BackgroundProperty, "QCard"); card.SetResourceReference(Button.BorderBrushProperty, "QBorder");
                card.Click += (_, _) => OpenSource(source.Url);
                System.Windows.Automation.AutomationProperties.SetName(card, $"Source {index + 1}: {source.Title}");
                SourceCards.Children.Add(card);
            }
        }
        MoreSources.Visibility = sources.Length > 4 && !_all ? Visibility.Visible : Visibility.Collapsed;
        MoreSources.Content = $"Show all {sources.Length} sources";
        Notice.Text = NoticeText; Notice.Visibility = string.IsNullOrWhiteSpace(NoticeText) ? Visibility.Collapsed : Visibility.Visible;
        if (_html != SuggestionsHtml)
        {
            _html = SuggestionsHtml; SuggestionsHost.Child = null;
            SuggestionsHost.Visibility = string.IsNullOrWhiteSpace(_html) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(_html))
            {
                try
                {
                    // Render Google's supplied Search Suggestions HTML instead of reconstructing its branding.
                    var browser = new System.Windows.Controls.WebBrowser { Height = 76 };
                    browser.Navigating += (_, e) => { if (e.Uri is { Scheme: "http" or "https" } uri) { e.Cancel = true; OpenSource(uri.AbsoluteUri); } };
                    SuggestionsHost.Child = browser; browser.NavigateToString(_html);
                }
                catch { SuggestionsHost.Visibility = Visibility.Collapsed; }
            }
        }
        UpdateMotion();
    }
    private void MoreSources_Click(object sender, RoutedEventArgs e) { _all = true; Update(); }
    internal static void OpenSource(string url)
    {
        if (!QWebSearch.IsWebUrl(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
