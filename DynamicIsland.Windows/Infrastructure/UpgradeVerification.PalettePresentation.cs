using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyPalettePresentationAsync(CommandPaletteViewModel vm, CommandPaletteWindow window, LoggingService log, Action<bool,string> check, string output)
    {
        await window.PrimePresentationAsync(); window.PrepareForShow();
        window.StartPresentationMaintenance();
        var warms=window.HiddenWarmCount; window.WarmHiddenPresentation();
        check(window.HiddenWarmCount==warms+1 && !window.IsVisible,"Idle preparation renders the hidden palette without showing it or taking focus");
        var anchor=new Rect(800,20,320,40); var area=new Rect(0,0,1920,1080);
        var watch=Stopwatch.StartNew(); window.PresentFromIsland(anchor,area,false);
        var first=watch.Elapsed.TotalMilliseconds;
        check(window.Left==640 && window.Top==58 && window.PresentationProgress<1,"Palette begins its reveal centered below the island with the connecting neck inset");
        var morph=(NotificationMorphHost)window.FindName("PaletteMorph");
        var query=(System.Windows.Controls.TextBox)window.FindName("QueryBox"); var finalQueryWidth=query.ActualWidth;
        var clock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(380)).CreateClock(true);
        morph.ApplyAnimationClock(NotificationMorphHost.RevealProgressProperty,clock);
        clock.Controller!.Pause(); clock.Controller.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(190),System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
        check(morph.CurrentSurfaceBounds.Width<morph.ActualWidth && morph.HasConnection && query.ActualWidth==finalQueryWidth,"Mid-morph palette grows its material and connection without scaling or relaying out command text");
        await CaptureSutraAsync(window,output,"palette-morph-midpoint");
        window.PresentFromIsland(anchor,area,false); await WaitForSutraAsync(()=>window.PresentationProgress>=1);
        check(morph.IsHitTestVisible && Math.Abs(query.ActualWidth-finalQueryWidth)<=1,$"Finished palette reveal enables controls at their final layout (hitTest={morph.IsHitTestVisible}, width={query.ActualWidth:0.00}, initialWidth={finalQueryWidth:0.00})");
        await CaptureSutraAsync(window,output,"palette-morph-open");
        warms=window.HiddenWarmCount; window.WarmHiddenPresentation(); check(window.HiddenWarmCount==warms,"Maintenance skips the visible palette");
        window.Dismiss(); check(window.IsDismissing,"Hotkey dismissal retracts the palette");
        window.PresentFromIsland(anchor,area,false); await Task.Delay(210);
        check(window.IsVisible && !window.IsDismissing,"Reopening during dismissal cancels the old hide completion");
        window.Dismiss(); await WaitForSutraAsync(()=>!window.IsVisible);
        check(!vm.HasPanel && vm.Query=="","Dismissal clears stale query and panel state before the next hotkey");
        var slowSave=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Force managed cache pressure between uses, then exercise the same idle maintenance path.
        GC.Collect(); GC.WaitForPendingFinalizers(); window.WarmHiddenPresentation();
        watch.Restart(); window.PrepareForShow(); window.PresentFromIsland(anchor,area,true); var afterIdle=watch.Elapsed.TotalMilliseconds;
        check(window.PresentationProgress==1 && morph.IsHitTestVisible,"Reduced motion opens immediately after hidden cache preparation");
        window.Dismiss(); check(!window.IsVisible,"Reduced motion dismisses immediately");
        window.PrepareForShow(); window.PresentFromIsland(new Rect(-1400,80,240,45),new Rect(-1920,0,1920,1080),true);
        check(window.Left>=-1920 && window.Left+window.ActualWidth<=0,"Palette uses the island monitor work area including negative monitor coordinates"); window.Dismiss();
        check(first<300 && afterIdle<150,$"Palette synchronous presentation meets targets: initial={first:0.0}ms, after-cache-pressure={afterIdle:0.0}ms, hidden-warm={window.LastHiddenWarmMilliseconds:0.0}ms (excludes 380ms morph)");
        using var release=new ManualResetEventSlim(); var reads=0; var changed=0;
        using(var sensor=new PrivacySensorService(log,()=> { Interlocked.Increment(ref reads); release.Wait(TimeSpan.FromSeconds(3)); return new(["Fixture"],[]); }))
        {
            sensor.Changed+=(_,_)=>changed++;
            watch.Restart(); sensor.Start();
            await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Input);
            check(watch.Elapsed.TotalMilliseconds<150 && reads<=1,"A slow privacy registry scan does not block the input dispatcher");
            sensor.Dispose(); release.Set(); await Task.Delay(70);
            check(changed==0,"Disposed privacy polling does not publish late background results");
        }
    }
}
