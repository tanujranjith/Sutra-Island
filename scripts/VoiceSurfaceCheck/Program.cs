using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Views;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["SutraMascotArtwork"] = new BitmapImage(new Uri("pack://application:,,,/DynamicIsland.Windows;component/Assets/sutra-assistant-logo.png"));
        foreach (var palette in new[] { ThemePalette.Dark, ThemePalette.Light, ThemePalette.Custom("#123D38") })
        foreach (var reduced in new[] { false, true })
        {
            var voice = new CompactVoiceView
            {
                Palette = palette, ReducedMotion = reduced, BaseHeight = 62,
                Answer = "This answer must hide all media behind the voice orb.",
                DataContext = new { IslandSurfaceBrush = new SolidColorBrush(Color.FromArgb(96, 16, 17, 20)), IslandCornerRadius = 22d }
            };
            var host = new Grid { Width = 320, Height = 84, Background = Brushes.Magenta };
            host.Children.Add(new TextBlock { Text = "MEDIA CONTENT UNDER THE ORB", Foreground = Brushes.Cyan });
            host.Children.Add(voice);
            foreach (var state in new[] { CompactVoiceState.Wake, CompactVoiceState.Listening, CompactVoiceState.Thinking, CompactVoiceState.Answer, CompactVoiceState.Action, CompactVoiceState.Error })
            {
                voice.State = state;
                AssertSurface(host, palette.Surface, $"{palette.Surface} reduced={reduced} state={state}");
            }
            voice.State = CompactVoiceState.Exit;
            voice.State = CompactVoiceState.Listening;
            Pump(650);
            AssertSurface(host, palette.Surface, "Wake interrupts exit without revealing media");
            voice.Palette = palette == ThemePalette.Light ? ThemePalette.Dark : ThemePalette.Light;
            AssertSurface(host, voice.Palette.Surface, "Theme changes while voice is active");
            voice.State = CompactVoiceState.Idle;
            host.Children.Clear();
        }
        Console.WriteLine($"PASS: {checks} WPF surface render checks (dark/light/custom, normal/reduced motion, exit interruption, live theme change).");
        app.Shutdown();
    }

    private static void AssertSurface(Grid host, string hex, string label)
    {
        host.Measure(new Size(320, 84));
        host.Arrange(new Rect(0, 0, 320, 84));
        host.UpdateLayout();
        var image = new RenderTargetBitmap(320, 84, 96, 96, PixelFormats.Pbgra32);
        image.Render(host);
        var expected = ThemeColor.Parse(hex);
        foreach (var point in new[] { new Int32Rect(315, 42, 1, 1), new Int32Rect(160, 82, 1, 1) })
        {
            var pixel = new byte[4];
            image.CopyPixels(point, pixel, 4, 0);
            if (pixel[3] != 255 || pixel[2] != expected.R || pixel[1] != expected.G || pixel[0] != expected.B)
                throw new Exception($"{label} at {point}: expected opaque {hex}; got BGRA={string.Join(',', pixel)}");
        }
        checks++;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
