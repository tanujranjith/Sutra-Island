using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyNotificationMotionAsync(AppSettings settings, IslandViewModel vm,
        IslandWindow window, Action<bool, string> check, string output)
    {
        var host = (NotificationMorphHost)window.FindName("NotifBanner");
        var card = (Border)window.FindName("NotifCard");
        var shell = (FrameworkElement)window.FindName("GlassShell");
        void Invoke(string name, params object?[] args) => typeof(IslandViewModel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);
        void Deliver(uint id) => Invoke("OnNotificationBatch", null, new NotificationInfo[]
        {
            new("Messages", $"Your file is ready {id}", "The export has finished.", id,
                DateTimeOffset.UtcNow, "motion.fixture")
        });
        void Capture(string name)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        // This scenario starts with a fresh queue, independently of command and voice fixtures.
        vm.ClearNotificationHistoryCommand.Execute(null); vm.DismissVisibleBanner();
        vm.ClearQ(); vm.KeepExpanded=false; window.CloseTimerPanel();
        settings.AutoHideFullscreen=false; window.Show();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        settings.AnimationIntensity = AnimationIntensity.Normal;
        vm.IsExpanded = false; vm.InteractionProtected = false;
        vm.ApplySettings(); window.ApplySettings();
        Deliver(1001); window.UpdateLayout();
        File.WriteAllText(Path.Combine(output,"notification-seed-state.txt"), System.Text.Json.JsonSerializer.Serialize(new { host.Visibility, window.IsLoaded, window.IsVisible, vm.IsAirPodsBannerActive, vm.BannerTitle, vm.BannerBody, host.RevealProgress, CardOpacity=card.Opacity, vm.ShowNotification, vm.ShowBanner, vm.FocusModeEnabled, vm.InteractionProtected, vm.HasUrgentAlert, settings.ShowNotifications, vm.IsReducedMotion }));
        check(host.Visibility == Visibility.Visible && host.RevealProgress < 0.1 && card.Opacity == 0,
            "Notification motion: begins as a text-free seed instead of flashing the final card");
        var shellSize = shell.RenderSize;
        var contentSize = card.RenderSize;
        var seedBounds = host.CurrentSurfaceBounds;
        check(seedBounds.Width < 50 && seedBounds.Y < -12 && host.HasConnection && !host.IsHitTestVisible,
            "Notification motion: seed overlaps the island and ignores clicks while opening");
        await Task.Delay(175); window.UpdateLayout();
        check(host.RevealProgress > 0 && host.RevealProgress < 1
            && host.CurrentSurfaceBounds.Width > seedBounds.Width && host.HasConnection,
            "Notification motion: real animation renders a growing connected intermediate shape");
        check(shell.RenderSize == shellSize && card.RenderSize == contentSize && card.RenderTransform is TranslateTransform,
            "Notification motion: island stays anchored and text retains its final measured size");
        await Task.Delay(300); window.UpdateLayout();
        check(host.RevealProgress == 1 && !host.HasConnection && host.IsHitTestVisible && card.Opacity == 1,
            "Notification motion: final card releases the connection and enables controls");
        check(Math.Abs(host.Margin.Top - shell.Margin.Top - shell.ActualHeight - 12) < 0.1
            && host.CurrentSurfaceBounds == new Rect(0, 0, host.ActualWidth, host.ActualHeight),
            "Notification motion: settles at full size with a 12 DIP gap");

        // Reproducible keyframe captures use the production geometry and masks.
        foreach (var (progress, name) in new[] { (0.12, "notification-seed"), (0.50, "notification-stretch"), (1d, "notification-settled") })
        {
            host.RevealProgress = progress; Capture(name);
        }
        vm.DismissCurrentNotificationCommand.Execute(null);
        Deliver(1002); await Task.Delay(60);
        vm.DismissCurrentNotificationCommand.Execute(null);
        check(host.Visibility == Visibility.Collapsed && !host.HasAnimatedProperties,
            "Notification motion: dismissing during entry removes the animation clock");
        Deliver(1003); await Task.Delay(450); window.UpdateLayout();
        check(host.Visibility == Visibility.Visible && host.RevealProgress == 1 && card.Opacity == 1,
            "Notification motion: a stale dismissed entrance cannot hide the next notification");
        vm.DismissCurrentNotificationCommand.Execute(null);
        settings.AnimationIntensity = AnimationIntensity.Reduced;
        vm.ApplySettings(); window.ApplySettings(); Deliver(1004); window.UpdateLayout();
        check(host.RevealProgress == 1 && !host.HasConnection && host.IsHitTestVisible && card.Opacity == 1,
            "Notification motion: reduced motion shows usable final content immediately");
        vm.DismissCurrentNotificationCommand.Execute(null);
        vm.ClearNotificationHistoryCommand.Execute(null);
    }
}
