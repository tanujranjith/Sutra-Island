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
        var mascot = new SutraMascotView { Width = 32, Height = 32, ShowThinkingBulb = true, State = QRunState.Idle };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(mascot);
        row.Children.Add(new TextBlock { Text = "Sutra Assistant", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(14,0,0,0), VerticalAlignment = VerticalAlignment.Center });
        var window = new Window { Width = 310, SizeToContent = SizeToContent.Height, Content = new Border { Padding = new Thickness(18), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(9,15,22)), Child = row }, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        try
        {
            window.Show(); await Layout();
            check(mascot.IsBulbVisible && mascot.HasBulbMotion, "Response mascot keeps its flat lightbulb visible and gently floating while idle");
            var badge = (FrameworkElement)mascot.FindName("CornerBulb"); var point = badge.TranslatePoint(new System.Windows.Point(), mascot);
            check(badge.ActualWidth <= mascot.ActualWidth * .55 && point.X >= mascot.ActualWidth * .6 && point.Y < mascot.ActualHeight * .4 && point.Y + badge.ActualHeight < mascot.ActualHeight * .6,
                "The flat bulb floats just above and to the right of the response mascot");
            await CaptureSutraAsync(window, output, "mascot-response-flat-bulb");
            mascot.State = QRunState.Streaming; await Layout();
            check(mascot.IsBulbVisible && mascot.HasBulbMotion, "The response bulb keeps floating while a reply streams");
            mascot.ReducedMotion = true; await Layout();
            check(mascot.IsBulbVisible && !mascot.HasBulbMotion && !((TranslateTransform)mascot.FindName("BulbFloat")).HasAnimatedProperties, "Reduced motion keeps the response bulb still without animation clocks");
            await CaptureSutraAsync(window, output, "mascot-corner-bulb-reduced");
            mascot.ReducedMotion = false; await Layout(); window.Hide();
            check(!mascot.HasBulbMotion, "Hiding the assistant stops the floating bulb animation");
            window.Show(); await Layout(); check(mascot.HasBulbMotion, "Showing the assistant resumes the bulb float");
            mascot.State = QRunState.Cancelled; await Layout(); check(mascot.IsBulbVisible && mascot.HasBulbMotion, "Stopping generation keeps the response mascot and bulb in place");
            mascot.State = QRunState.Complete; await Layout(); check(mascot.IsBulbVisible && mascot.HasBulbMotion, "A completed reply keeps the response mascot and bulb in place");
            await CaptureSutraAsync(window, output, "mascot-response-bulb-complete");
        }
        finally { window.Close(); }
        check(!mascot.HasBulbMotion, "Closing the assistant releases corner-bulb animation clocks");
    }
}
