using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Views;
using Brushes = System.Windows.Media.Brushes;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyAssistantActionMotionAsync(Action<bool,string> check,string output)
    {
        var view=new AssistantActionView();
        var surface=new Border {Background=Brushes.Black,Padding=new Thickness(14),Child=view};
        var window=new Window {Width=520,SizeToContent=SizeToContent.Height,Content=surface,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Opacity=0,ShowActivated=false,ShowInTaskbar=false};
        async Task Layout()=>await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.ApplicationIdle);
        try
        {
            view.Feedback=new(1,"Volume","10",AssistantActionPhase.Working);window.Show();await Layout();
            check(view.HasWorkingMotion && view.DisplayedTitle=="Adjusting volume","Visible device action shows a working orbit with an action-specific label");
            await Task.Delay(100);await CaptureSutraAsync(window,output,"assistant-action-working");
            view.Feedback=view.Feedback with {Phase=AssistantActionPhase.Succeeded,Message="Volume is now 10%.",Level=10};await Layout();
            check(!view.HasWorkingMotion && ((FrameworkElement)view.FindName("SuccessTick")).IsVisible,"Confirmed action stops its working loop and shows a completion tick");
            await WaitForSutraAsync(()=>Math.Abs(view.DisplayedLevel-.1)<.001);await Layout();
            check(Math.Abs(view.DisplayedLevel-.1)<.001 && view.DisplayedTitle=="Volume updated",$"Volume fill settles at the confirmed device level ({view.DisplayedLevel:0.000})");
            await CaptureSutraAsync(window,output,"assistant-action-success");
            view.Feedback=new(2,"SetOutput","headphones",AssistantActionPhase.Failed,"Device disconnected.");await Layout();
            check(!view.HasWorkingMotion && ((FrameworkElement)view.FindName("SuccessTick")).Visibility==Visibility.Collapsed && ((FrameworkElement)view.FindName("LevelTrack")).Visibility==Visibility.Collapsed,"Failed actions never display a success tick or fabricated volume level");
            await Task.Delay(240);await CaptureSutraAsync(window,output,"assistant-action-failed");
            view.Feedback=new(3,"Timer","5 minutes",AssistantActionPhase.Cancelled,"Action stopped.");await Layout();
            check(!view.HasWorkingMotion && view.DisplayedTitle=="Action stopped","Cancelled actions stop motion and show a distinct stopped state");
            view.ReducedMotion=true;view.Feedback=new(4,"Volume","80",AssistantActionPhase.Working);await Layout();
            check(!view.HasWorkingMotion,"Reduced motion shows a static working cue");
            view.Feedback=view.Feedback with {Phase=AssistantActionPhase.Succeeded,Level=80,Message="Volume is now 80%."};await Layout();
            check(Math.Abs(view.DisplayedLevel-.8)<.001 && !((ScaleTransform)((FrameworkElement)view.FindName("LevelFill")).RenderTransform).HasAnimatedProperties,"Reduced motion applies the confirmed level immediately without animation");
            await CaptureSutraAsync(window,output,"assistant-action-reduced");
            view.ReducedMotion=false;view.Feedback=new(5,"SetSetting","ShowCompactDate",AssistantActionPhase.Working);await Layout();window.Hide();
            check(!view.HasWorkingMotion,"Hidden action surfaces stop their repeating animation");
            window.Show();await Layout();check(view.HasWorkingMotion,"A still-running action resumes its working cue when visible");
            view.Feedback=new(6,"Volume","30",AssistantActionPhase.Succeeded,"Volume is now 30%.",30);
            view.Feedback=new(7,"Timer","10 minutes",AssistantActionPhase.Working);await Task.Delay(430);await Layout();
            check(view.HasWorkingMotion && view.DisplayedTitle=="Starting timer" && ((FrameworkElement)view.FindName("SuccessTick")).Visibility==Visibility.Collapsed,"A newer action replaces old completion motion without replaying a stale success");
            view.Feedback=null;await Layout();check(!view.HasWorkingMotion && view.DisplayedTitle=="","Clearing a conversation removes device feedback and motion");
        }
        finally {window.Close();}
        check(!view.HasWorkingMotion,"Closing the action surface releases its repeating animation");
    }
}
