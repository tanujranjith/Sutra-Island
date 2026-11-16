using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using Button = System.Windows.Controls.Button;
using ThemeMode = DynamicIsland.Windows.Models.ThemeMode;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyVolumeSpacingAsync(IslandViewModel vm, TimerAlarmService timers,
        IslandWindow window, Action<bool, string> check, string output)
    {
        async Task Layout()
        {
            vm.ApplySettings(); window.ApplySettings();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        }
        void Capture(FrameworkElement element, string name)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 2),
                (int)Math.Ceiling(element.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        vm.Settings.AnimationIntensity = AnimationIntensity.Reduced;
        vm.Settings.Theme = ThemeMode.Graphite;
        vm.Settings.ShowVolume = true; vm.Settings.ShowConnectivity = true;
        vm.Settings.ShowWeather = true; vm.Settings.ShowClock = true; vm.Settings.ShowSystemMonitor = true;
        vm.Settings.ShowRamInCompact = true;
        vm.Settings.QuotePlacement = QuotePlacement.Off;
        typeof(IslandViewModel).GetMethod("OnAudioChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
            [null, new AudioState { Availability = AudioAvailability.Available, MasterVolumePercent = 6, OutputDeviceName = "Speakers (Realtek(R) Audio)" }]);
        vm.IsExpanded = true; vm.KeepExpanded = true;
        window.Show(); await Layout();
        var deck = (GraphiteIslandView)window.FindName("GraphiteDeck");
        var tile = (FrameworkElement)deck.FindName("GraphiteVolumeTile");
        var down = (Button)deck.FindName("GraphiteVolumeDownButton");
        var up = (Button)deck.FindName("GraphiteVolumeUpButton");
        var outputButton = (Button)deck.FindName("GraphiteOutputButton");
        check(ReferenceEquals(down.Command, vm.AdjustVolumeCommand) && (string)down.CommandParameter == "-2"
            && ReferenceEquals(up.Command, vm.AdjustVolumeCommand) && (string)up.CommandParameter == "2",
            "Volume buttons use the existing system volume command with opposite two-percent steps");
        foreach (var size in new[] { IslandSize.Compact, IslandSize.Normal, IslandSize.Large })
        {
            vm.Settings.IslandSize = size; await Layout();
            foreach (var button in new[] { down, up, outputButton })
            {
                var topLeft = button.TranslatePoint(new System.Windows.Point(), tile);
                check(button.IsVisible && button.IsEnabled && topLeft.X >= 0 && topLeft.Y >= 0
                    && topLeft.X + button.ActualWidth <= tile.ActualWidth - 8
                    && topLeft.Y + button.ActualHeight <= tile.ActualHeight,
                    $"{size} volume tile keeps {button.Name} fully visible with edge padding");
            }
            Capture(deck, $"volume-footer-{size}");
        }
        vm.Settings.IslandWidth = 188;
        vm.IsExpanded = false; vm.KeepExpanded = false;
        timers.StartTimer(TimeSpan.FromMinutes(5), "Spacing check");
        var timerId = timers.State.Timer.Id;
        var shell = (FrameworkElement)window.FindName("GlassShell");
        var clock = (TextBlock)window.FindName("CompactClockText");
        var orb = (FrameworkElement)window.FindName("CompactTimerOrb");
        var ring = (FrameworkElement)window.FindName("TimerOrbRing");
        foreach (var height in new[] { 38, 62, 90 })
        {
            vm.Settings.IslandHeight = height; await Layout();
            var clockRight = clock.TranslatePoint(new System.Windows.Point(clock.ActualWidth, 0), shell).X;
            check(clock.IsVisible && clock.DesiredSize.Width <= clock.ActualWidth + 1 && shell.ActualWidth - clockRight >= 15,
                $"Compact {height}px: time fits with at least 15px trailing inset");
            var shellOrigin = shell.TranslatePoint(new System.Windows.Point(), window);
            var orbOrigin = orb.TranslatePoint(new System.Windows.Point(), window);
            check(orb.IsVisible && Math.Abs(orbOrigin.Y + orb.ActualHeight / 2 - shellOrigin.Y - shell.ActualHeight / 2) < 1
                && orbOrigin.X - shellOrigin.X - shell.ActualWidth >= 11,
                $"Compact {height}px: timer stays centered beside the pill with a clear gap");
            var ringOrigin = ring.TranslatePoint(new System.Windows.Point(), orb);
            check(ringOrigin.X >= 6 && ringOrigin.Y >= 6 && ring.ActualWidth <= orb.ActualWidth - 12,
                $"Compact {height}px: timer ring remains inset inside its circular background");
            Capture(shell, $"compact-time-{height}");
            Capture(orb, $"timer-orb-{height}");
        }
        timers.CancelTimer(timerId);
    }
}
