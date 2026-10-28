using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyMascotMotionAsync(Action<bool,string> check, string output)
    {
        var mascot = new SutraMascotView { Width = 128, Height = 128 };
        var surface = new Border { Padding = new Thickness(20), Background = System.Windows.Media.Brushes.Black, Child = mascot };
        var window = new Window { Width = 168, SizeToContent = SizeToContent.Height, Content = surface, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        var blink = (ScaleTransform)mascot.FindName("Blink"); var gaze = (TranslateTransform)mascot.FindName("Gaze");
        try
        {
            window.Show(); await Layout();
            check(mascot.HasEyeMotion && mascot.EyeOpenness > .99, "The shared mascot opens with live eye animation and unchanged ribbon artwork");
            await CaptureSutraAsync(window, output, "mascot-eyes-open");
            var clock = (AnimationClock)SutraMascotView.CreateBlinkAnimation().CreateClock(true);
            blink.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock); clock.Controller!.Pause(); await Layout();
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(3.88), TimeSeekOrigin.BeginTime);
            check(mascot.EyeOpenness < .1, "Blink closes the eye layer without scaling or moving the mascot's ribbon");
            await CaptureSutraAsync(window, output, "mascot-eyes-blink");
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(4.02), TimeSeekOrigin.BeginTime);
            check(mascot.EyeOpenness > .99, "Blink returns both eyes to their full open shape");
            mascot.State = QRunState.Thinking; await Layout();
            await WaitForSutraAsync(() => Math.Abs(mascot.GazeX - 12) < .01 && Math.Abs(mascot.GazeY + 12) < .01);
            check(mascot.HasEyeMotion, "Thinking moves the mascot's gaze up while retaining its natural blink");
            await CaptureSutraAsync(window, output, "mascot-eyes-thinking");
            mascot.State = QRunState.Listening; await Layout();
            await WaitForSutraAsync(() => Math.Abs(mascot.GazeX) < .01 && Math.Abs(mascot.GazeY + 6) < .01);
            check(mascot.HasEyeMotion, "Listening uses an attentive centered eye expression");
            mascot.Speaking = true; await Layout(); check(gaze.HasAnimatedProperties && mascot.HasEyeMotion, "Voice playback gives the same mascot a subtle speaking expression");
            mascot.ReducedMotion = true; await Layout();
            check(!mascot.HasEyeMotion && !blink.HasAnimatedProperties && !gaze.HasAnimatedProperties && mascot.EyeOpenness == 1 && mascot.GazeX == 0 && mascot.GazeY == 0, "Reduced motion removes all mascot eye clocks and leaves an open neutral face");
            mascot.ReducedMotion = false; await Layout(); window.Hide();
            check(!mascot.HasEyeMotion && !blink.HasAnimatedProperties && !gaze.HasAnimatedProperties, "Hidden mascot surfaces release blinking and gaze animation clocks");
            window.Show(); await Layout(); check(mascot.HasEyeMotion, "Visible mascot resumes its eye expression after being hidden");
            mascot.State = QRunState.Idle; mascot.Speaking = false; mascot.ReducedMotion = true;
            foreach (var size in new[] { 26d, 32d, 40d })
            {
                mascot.Width = mascot.Height = size; await Layout();
                await CaptureSutraAsync(window, output, $"mascot-compact-{size:0}-open");
                var smallClock = (AnimationClock)SutraMascotView.CreateBlinkAnimation().CreateClock(true);
                blink.ApplyAnimationClock(ScaleTransform.ScaleYProperty, smallClock); smallClock.Controller!.Pause(); await Layout();
                smallClock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(3.88), TimeSeekOrigin.BeginTime);
                check(mascot.EyeOpenness < .1, $"Both eye layers close cleanly at the actual {size:0}-pixel icon size");
                await CaptureSutraAsync(window, output, $"mascot-compact-{size:0}-blink");
                smallClock.Controller.Remove(); blink.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
        finally { window.Close(); }
        check(!mascot.HasEyeMotion, "Closing the mascot surface stops its eye animation");
    }
}
