using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyMascotCornerBulbAsync(Action<bool,string> check, string output)
    {
        var mascot = new SutraMascotView { Width = 32, Height = 32, ShowThinkingBulb = true, State = QRunState.Thinking };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(mascot);
        row.Children.Add(new TextBlock { Text = "Sutra Assistant", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(14,0,0,0), VerticalAlignment = VerticalAlignment.Center });
        var window = new Window { Width = 310, SizeToContent = SizeToContent.Height, Content = new Border { Padding = new Thickness(18), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(9,15,22)), Child = row }, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        try
        {
            window.Show(); await Layout();
            check(mascot.IsBulbVisible && mascot.HasBulbMotion, "Thinking lights the mascot's animated corner bulb");
            var badge = (FrameworkElement)mascot.FindName("CornerBulb"); var point = badge.TranslatePoint(new System.Windows.Point(), mascot);
            check(point.X > mascot.ActualWidth / 2 && point.Y < 0 && point.Y + badge.ActualHeight < mascot.ActualHeight * .4, "The bulb sits at the mascot's upper-right corner above the eyes");
            await CaptureSutraAsync(window, output, "mascot-corner-bulb-thinking");
            mascot.State = QRunState.Streaming; await Layout();
            check(mascot.IsBulbVisible && mascot.HasBulbMotion, "The mascot corner bulb stays active while a reply streams");
            mascot.ReducedMotion = true; await Layout();
            check(mascot.IsBulbVisible && !mascot.HasBulbMotion && !((FrameworkElement)mascot.FindName("BulbGlow")).HasAnimatedProperties, "Reduced motion keeps a static lit corner bulb without pulse clocks");
            await CaptureSutraAsync(window, output, "mascot-corner-bulb-reduced");
            mascot.ReducedMotion = false; await Layout(); window.Hide();
            check(!mascot.HasBulbMotion, "Hiding the assistant stops corner-bulb animation");
            window.Show(); await Layout(); check(mascot.HasBulbMotion, "Showing a still-working assistant resumes its corner bulb");
            mascot.State = QRunState.Cancelled; await Layout(); check(!mascot.IsBulbVisible && !mascot.HasBulbMotion, "Stopping generation removes the mascot corner bulb");
            mascot.State = QRunState.Complete; await Layout(); check(!mascot.IsBulbVisible && !mascot.HasBulbMotion, "A completed reply leaves the mascot without a stale thinking bulb");
            await CaptureSutraAsync(window, output, "mascot-corner-bulb-complete");
        }
        finally { window.Close(); }
        check(!mascot.HasBulbMotion, "Closing the assistant releases corner-bulb animation clocks");
    }
}
