using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Windows.Views;
using Color = System.Windows.Media.Color;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyAssistantReplyMotionAsync(Action<bool,string> check, string output)
    {
        var reply = new AssistantReplyView { Active = true, Status = "Thinking…" };
        reply.Resources["QAccent"] = new SolidColorBrush(Color.FromRgb(131,191,255));
        reply.Resources["QText"] = System.Windows.Media.Brushes.White;
        var surface = new Border { Background = new SolidColorBrush(Color.FromRgb(18,25,33)), Padding = new Thickness(20), Child = reply };
        var window = new Window { Width = 520, SizeToContent = SizeToContent.Height, Content = surface, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        try
        {
            window.Show(); await Layout();
            check(reply.HasWorkingMotion && ((TextBlock)reply.FindName("StatusLabel")).Text == "Thinking", "Assistant reply retains its thinking label, shimmer and bouncing dots");
            await Task.Delay(100); await CaptureSutraAsync(window, output, "assistant-reply-thinking");
            var starts = reply.MotionStarts;
            reply.Status = "Responding…"; reply.Text = "Here is your answer as it arrives."; await Layout();
            check(reply.HasWorkingMotion && reply.MotionStarts == starts && reply.AnswerRevealCount == 1, "Thinking-to-responding changes the label without restarting the activity loop and reveals the first text once");
            reply.Text += " New text appears immediately, without artificial typing delays."; await Layout();
            check(reply.DisplayedText == reply.Text && reply.AnswerRevealCount == 1 && reply.MotionStarts == starts, "Streaming text updates immediately without replaying its entrance for every chunk");
            check(reply.HasCaretMotion && ((FrameworkElement)reply.FindName("StreamingCaret")).Visibility == Visibility.Visible, "Responding text ends with Sutra's blinking streaming caret");
            await Task.Delay(230); await CaptureSutraAsync(window, output, "assistant-reply-streaming");
            reply.Active = false; await Layout();
            await WaitForSutraAsync(() => ((FrameworkElement)reply.FindName("ActivityRow")).Height < .01);
            check(!reply.HasWorkingMotion && !reply.HasCaretMotion && ((FrameworkElement)reply.FindName("StreamingCaret")).Visibility == Visibility.Collapsed && ((TextBlock)reply.FindName("Answer")).Opacity == 1 && reply.DisplayedText == reply.Text, "Completing or stopping a reply ends activity and caret motion and preserves fully readable text");
            reply.Text = ""; reply.ReducedMotion = true; reply.Active = true; reply.Status = "Responding…"; reply.Text = "Reduced motion keeps the complete reply readable."; await Layout();
            check(!reply.HasWorkingMotion && !reply.HasCaretMotion && !((FrameworkElement)reply.FindName("ActivityRow")).HasAnimatedProperties && !((TextBlock)reply.FindName("Answer")).HasAnimatedProperties, "Reduced motion uses a static reply cue and caret with no activity or text entrance clocks");
            await CaptureSutraAsync(window, output, "assistant-reply-reduced");
            reply.ReducedMotion = false; await Layout(); check(reply.HasWorkingMotion, "Active reply motion resumes when normal motion is restored");
            window.Hide(); check(!reply.HasWorkingMotion && !reply.HasCaretMotion, "Hiding the assistant reply stops all repeating animations including its caret");
            window.Show(); await Layout(); check(reply.HasWorkingMotion, "Showing a still-active reply resumes its activity cue");
            reply.Active = false; reply.Text = "The provider reported an error. Try again."; await Layout();
            check(!reply.HasWorkingMotion && !reply.HasCaretMotion && reply.DisplayedText == reply.Text, "Error and cancellation text stay visible without a false responding animation");
        }
        finally { window.Close(); }
        check(!reply.HasWorkingMotion, "Closing the reply surface releases repeating animation clocks");
    }
}
