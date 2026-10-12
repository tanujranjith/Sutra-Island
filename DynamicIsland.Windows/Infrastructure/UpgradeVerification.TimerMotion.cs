using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyTimerMotionAsync(AppSettings settings, IslandViewModel vm,
        IslandWindow window, Action<bool, string> check, string output)
    {
        var host = (Border)window.FindName("TimerPanelHost");
        var content = (Grid)window.FindName("TimerPanelContent");
        var orb = (FrameworkElement)window.FindName("CompactTimerOrb");
        var translate = (TranslateTransform)window.FindName("TimerPanelTranslate");
        var stableClip = host.Clip;
        var leaveTimer = (DispatcherTimer)typeof(IslandWindow)
            .GetField("_timerPanelLeaveTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var leaveInterval = leaveTimer.Interval;
        // Native fixtures have no pointer over the hidden window. Keep hover auto-close out of
        // motion assertions, while still running the actual production animation clocks.
        leaveTimer.Interval = TimeSpan.FromSeconds(30);
        void CaptureFrame(string name)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        try
        {
            foreach (var intensity in new[] { AnimationIntensity.Normal, AnimationIntensity.Expressive })
            {
                settings.AnimationIntensity = intensity;
                window.ApplySettings();
                window.ShowTimerPanel();
                check(Math.Abs(host.Width - orb.Width) < 1 && host.Opacity == 1,
                    $"Timer {intensity}: opening starts with a visible orb-sized shell (host={host.Width:0.0}, orb={orb.Width:0.0}, opacity={host.Opacity:0.0})");
                window.UpdateLayout();
                check(!host.UseLayoutRounding && !host.SnapsToDevicePixels
                    && content.CacheMode is BitmapCache { SnapsToDevicePixels: false },
                    $"Timer {intensity}: motion permits fractional layout and cached translation");
                var viewport = content.RenderSize;
                var previousWidth = host.Width;
                var previousHeight = host.Height;
                var previousOffset = translate.X;
                var sawOpeningFrame = false;
                for (var frame = 0; frame < 5; frame++)
                {
                    await Task.Delay(45); window.UpdateLayout();
                    check(host.Width >= previousWidth && host.Height >= previousHeight && translate.X <= previousOffset,
                        $"Timer {intensity} frame {frame}: continuous growth toward the panel");
                    sawOpeningFrame |= !host.IsHitTestVisible;
                    check(content.RenderSize == viewport && (host.IsHitTestVisible
                            ? content.CacheMode is null : content.CacheMode is BitmapCache),
                        $"Timer {intensity} frame {frame}: stable content viewport and correct cache lifecycle (size={content.RenderSize}, initial={viewport})");
                    check(host.Opacity == 1 && host.Clip is RectangleGeometry clip && clip.RadiusX > 0,
                        $"Timer {intensity} frame {frame}: opaque shell with rounded content clipping");
                    check(ReferenceEquals(host.Clip, stableClip),
                        $"Timer {intensity} frame {frame}: reuses the rounded clip during motion");
                    previousWidth = host.Width; previousHeight = host.Height; previousOffset = translate.X;
                    if (intensity == AnimationIntensity.Expressive) CaptureFrame($"timer-open-frame-{frame}");
                }
                await Task.Delay(250); window.UpdateLayout();
                check(sawOpeningFrame, $"Timer {intensity}: renders intermediate opening frames");
                check(host.IsHitTestVisible && content.Opacity == 1 && content.CacheMode is null,
                    $"Timer {intensity}: opening settles with usable controls and releases its cache");
                check(host.UseLayoutRounding && host.SnapsToDevicePixels,
                    $"Timer {intensity}: settled panel restores sharp pixel alignment");
                window.CloseTimerPanel();
                previousWidth = host.Width;
                var sawClosingFrame = false;
                for (var frame = 0; frame < 3; frame++)
                {
                    await Task.Delay(45); window.UpdateLayout();
                    // Rendering/capturing a frame can take longer than the remaining animation
                    // on a software compositor. A completed morph has already reset host.Width.
                    if (!vm.TimerEditorOpen) break;
                    sawClosingFrame = true;
                    check(host.Width <= previousWidth && host.Opacity == 1 && !orb.IsVisible,
                        $"Timer {intensity} close {frame}: smooth opaque shrink before orb handoff");
                    previousWidth = host.Width;
                    if (intensity == AnimationIntensity.Expressive) CaptureFrame($"timer-close-frame-{frame}");
                }
                await Task.Delay(200); window.UpdateLayout();
                check(sawClosingFrame, $"Timer {intensity}: renders intermediate closing frames");
                check(host.Visibility == Visibility.Collapsed && orb.IsVisible && !vm.TimerEditorOpen,
                    $"Timer {intensity}: closing hands directly back to the timer orb");
            }

            window.ShowTimerPanel(); await Task.Delay(80); window.UpdateLayout();
            var width = host.Width; var opacity = content.Opacity;
            window.CloseTimerPanel();
            check(Math.Abs(host.Width - width) < 1 && Math.Abs(content.Opacity - opacity) < 0.01,
                "Timer interruption: closing preserves the opening geometry and opacity");
            await Task.Delay(65); window.UpdateLayout();
            width = host.Width; opacity = content.Opacity;
            var offset = translate.X;
            window.ApplySettings();
            check(Math.Abs(host.Width - width) < 1, "Timer interruption: layout refresh preserves animated bounds");
            window.ShowTimerPanel();
            check(Math.Abs(host.Width - width) < 1 && Math.Abs(content.Opacity - opacity) < 0.01
                && Math.Abs(translate.X - offset) < 1,
                "Timer interruption: reopening continues from the current closing frame");
            await Task.Delay(450); window.UpdateLayout();
            check(host.IsHitTestVisible && vm.TimerEditorOpen, "Timer interruption: stale close callback cannot hide reopened panel");

            settings.AnimationIntensity = AnimationIntensity.Reduced;
            window.CloseTimerPanel(); window.ShowTimerPanel();
            check(host.IsHitTestVisible && content.Opacity == 1, "Timer reduced motion: opening settles immediately");
            window.CloseTimerPanel();
            check(host.Visibility == Visibility.Collapsed && !vm.TimerEditorOpen,
                "Timer reduced motion: closing settles immediately");
            check(host.UseLayoutRounding && host.SnapsToDevicePixels && ReferenceEquals(host.Clip, stableClip),
                "Timer reduced motion: retains pixel alignment and the reusable clip");
        }
        finally
        {
            leaveTimer.Stop(); leaveTimer.Interval = leaveInterval;
        }
    }
}
