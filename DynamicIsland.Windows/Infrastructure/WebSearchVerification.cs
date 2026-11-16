using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Views;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Grid = System.Windows.Controls.Grid;
using Orientation = System.Windows.Controls.Orientation;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;

namespace DynamicIsland.Windows.Infrastructure;

internal static class WebSearchVerification
{
    public static async Task RunAsync()
    {
        if (!AppDataPaths.IsPreview) throw new InvalidOperationException("Search verification needs an isolated preview directory.");
        var output = Path.Combine(AppDataPaths.Root, "captures"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool ok, string label) { checks.Add((ok ? "PASS " : "FAIL ") + label); File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); if (!ok) throw new InvalidOperationException(label); }
        var host = new StackPanel { Margin = new Thickness(32) };
        var window = new Window { Title = "Sutra web search verification", Width = 900, Height = 690, Background = new SolidColorBrush(Color.FromRgb(24, 25, 30)), Content = host, ShowInTaskbar = false };
        foreach (var (key, hex) in new[] { ("QText", "#F2F1F6"), ("QMuted", "#9B9BA9"), ("QAccent", "#BFA7FF"), ("QCard", "#24242D"), ("QBorder", "#353640") }) window.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        host.Children.Add(new TextBlock { Text = "Chat · web search", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(0, 0, 0, 16) });
        var reply = new AssistantReplyView { Active = true, Searching = true, SearchUsed = true, Status = "Searching the web…", Width = 800, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(reply);
        host.Children.Add(new TextBlock { Text = "Voice · compact island", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(0, 36, 0, 16) });
        var voice = new CompactVoiceView { Width = 420, Height = 84, BaseHeight = 62, Palette = ThemePalette.Dark, State = CompactVoiceState.Thinking, IsSearching = true, SearchStatus = "Searching the web…", Transcript = "Find the OpenAI web-search docs", HorizontalAlignment = HorizontalAlignment.Left,
            DataContext = new { QThemePalette = ThemePalette.Dark, IslandCornerRadius = 24d } };
        host.Children.Add(voice);
        window.Show(); await Pause(400);
        var activity = (WebSearchView)reply.FindName("WebActivity");
        var globe = (SearchGlobe)activity.FindName("Globe");
        var voiceGlobe = (SearchGlobe)voice.FindName("VoiceSearchGlobe");
        Check(globe.IsAnimating && voiceGlobe.IsAnimating, "Chat and voice globe animations run only while searching");
        Check(((Grid)voice.FindName("ListenLayer")).Opacity < .01, "Voice transcript fades out while the search row is visible");
        Check(((Grid)voice.FindName("SearchLayer")).Opacity > .99, "Compact voice shows the search row");
        Capture(window, Path.Combine(output, "searching.png"));
        reply.Sources = [new("https://developers.openai.com/api/docs/guides/tools-web-search", "Web search guide", CitedText: "Use the Responses API."), new("https://developers.openai.com/api/docs/models/gpt-6.1-terra", "Model support")];
        voice.SourceCount = 2;
        await Pause(100);
        Check(((WrapPanel)activity.FindName("SourceCards")).Children.Count == 2, "Source cards appear with two distinct sources");
        reply.RawText = "Use the Responses API."; reply.Text = "Use the Responses API.";
        reply.Searching = false; reply.Active = false; reply.Status = "Complete";
        voice.IsSearching = false; voice.Answer = "Use the Responses API with web search enabled."; voice.State = CompactVoiceState.Answer;
        await Pause(700);
        var answer = (TextBlock)reply.FindName("Answer");
        Check(answer.Inlines.OfType<Hyperlink>().Any(), "Cited answer text contains clickable inline links");
        Check(!globe.IsAnimating && !voiceGlobe.IsAnimating, "Search animation stops when the answer arrives");
        Capture(window, Path.Combine(output, "complete.png"));
        reply.Searching = true; reply.Active = true; reply.ReducedMotion = true;
        voice.State = CompactVoiceState.Thinking; voice.IsSearching = true; voice.ReducedMotion = true;
        // Reduced-motion changes take effect during an active search.
        await Pause(200);
        Check(!globe.IsAnimating && !voiceGlobe.IsAnimating, "Reduced motion keeps search glyphs static");
        reply.ReducedMotion = false; reply.Searching = false; reply.Searching = true;
        await Pause(100); Check(globe.IsAnimating, "Animation can restart after a new search");
        window.Close(); await Pause(100); Check(!globe.IsAnimating && !voiceGlobe.IsAnimating, "Unloading the views stops all search motion");
        var settings = new AppSettings { QWebSearchEnabled = true };
        Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!.QWebSearchEnabled, "Web-search preference survives settings serialization");
    }
    private static async Task Pause(int ms) { await Task.Delay(ms); await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    private static void Capture(FrameworkElement view, string path)
    {
        view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
}
