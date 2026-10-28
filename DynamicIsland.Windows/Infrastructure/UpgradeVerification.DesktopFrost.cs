using System.Windows;
using Brushes=System.Windows.Media.Brushes;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    private static bool IsDesktopBackdropVisible(nint hwnd)=>IsWindowVisible(hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);
    [System.Runtime.InteropServices.DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(nint process);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    private static async Task VerifyDesktopFrostAsync(IslandViewModel vm,IslandWindow island,string output)
    {
        var text=new StackPanel();
        for(var index=0;index<20;index++)text.Children.Add(new System.Windows.Controls.TextBlock {Text="SHARP BACKGROUND CONTENT — THIS SHOULD BE FROSTED — 0123456789",FontSize=22,Foreground=Brushes.Black,Margin=new Thickness(16,9,0,0)});
        var backing=new Window {Title="Sutra synthetic backdrop check",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=0,Top=0,Width=1800,Height=850,Background=Brushes.White,Content=text,ShowInTaskbar=false,ShowActivated=false,Topmost=true};
        try
        {
            backing.Show();backing.Activate();vm.Settings.AlwaysOnTop=true;island.Title="Sutra native frost verification";vm.ClearQ();vm.KeepExpanded=true;vm.IsExpanded=true;vm.ApplySettings();
            island.Opacity=1;island.Topmost=true;island.Show();island.ApplySettings();island.Left=90;island.Top=90;island.Activate();
            await Task.Delay(1800);island.UpdateLayout();
            if(Environment.GetEnvironmentVariable("ISLAND_VERIFY_OUTPUT_TOGGLE")=="1")
            {
                typeof(IslandViewModel).GetMethod("OnAudioChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(vm,
                    [null,new DynamicIsland.Windows.Models.AudioState { Availability=DynamicIsland.Windows.Models.AudioAvailability.Available, MasterVolumePercent=35 }]);
                island.UpdateLayout();await Task.Delay(100);
                var deck=(GraphiteIslandView)island.FindName("GraphiteDeck");
                var button=(FrameworkElement)deck.FindName("GraphiteOutputButton");
                var popup=(System.Windows.Controls.Primitives.Popup)island.FindName("OutputDevicePopup");
                if(!button.IsVisible || !Interop.NativeMethods.GetCursorPos(out var original))throw new InvalidOperationException("Output arrow or pointer unavailable");
                var point=button.PointToScreen(new System.Windows.Point(button.ActualWidth/2,button.ActualHeight/2));
                async Task ClickAt(int x,int y)
                {
                    SetCursorPos(x,y);await Task.Delay(80);mouse_event(2,0,0,0,0);await Task.Delay(60);mouse_event(4,0,0,0,0);await Task.Delay(180);
                }
                try
                {
                    await ClickAt((int)point.X,(int)point.Y);
                    if(!popup.IsOpen)throw new InvalidOperationException("Physical first click did not open the output picker");
                    await ClickAt((int)point.X,(int)point.Y);
                    if(popup.IsOpen)throw new InvalidOperationException("Physical second click reopened the output picker");
                    await ClickAt((int)point.X,(int)point.Y);
                    if(!popup.IsOpen)throw new InvalidOperationException("Physical third click did not reopen the output picker");
                    await ClickAt(20,800);
                    if(popup.IsOpen)throw new InvalidOperationException("Outside click did not dismiss the output picker");
                    File.WriteAllText(Path.Combine(output,"output-toggle-check.txt"),"PASS: real mouse open, close, reopen and outside dismissal");
                }
                finally {popup.IsOpen=false;SetCursorPos(original.X,original.Y);}
            }
            if(Environment.GetEnvironmentVariable("ISLAND_VERIFY_SETTINGS_STACK")=="1")
            {
                if(!vm.NativeFrostActive)throw new InvalidOperationException("Backdrop was not active before opening settings");
                for(int cycle=0;cycle<3;cycle++)
                {
                    island.SetSettingsWindowOpen(true);vm.IsExpanded=false;await Task.Delay(350);vm.IsExpanded=true;await Task.Delay(500);
                    var backdrop=typeof(IslandWindow).GetField("_frostBackdrop",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(island)!;
                    var source=typeof(NativeFrostBackdrop).GetField("_source",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(backdrop) as System.Windows.Interop.HwndSource;
                    if(vm.NativeFrostActive || (source is not null && IsDesktopBackdropVisible(source.Handle)))throw new InvalidOperationException("Island backdrop remained visible over Settings");
                    island.SetSettingsWindowOpen(false);await Task.Delay(300);
                    if(!vm.NativeFrostActive)throw new InvalidOperationException("Island backdrop did not resume after Settings closed");
                }
                File.WriteAllText(Path.Combine(output,"settings-stack-check.txt"),"PASS: backdrop hides during Settings and returns after close across three cycles");
            }
            if(Environment.GetEnvironmentVariable("ISLAND_CAPTURE_ISLAND_MORPH")=="1")
            {
                vm.Settings.AnimationIntensity=DynamicIsland.Windows.Models.AnimationIntensity.Normal;
                vm.KeepExpanded=false;vm.IsExpanded=false;await Task.Delay(700);
                vm.IsExpanded=true;
                for(int frame=0;frame<4;frame++)
                {
                    await Task.Delay(75);island.UpdateLayout();
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);
                    bitmap.Render((Visual)island.Content);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(output,$"island-morph-{frame}.png"));encoder.Save(file);
                }
                await Task.Delay(300);
            }
            if(Environment.GetEnvironmentVariable("ISLAND_MEASURE_IDLE_EXPAND")=="1")
            {
                vm.Settings.AnimationIntensity=DynamicIsland.Windows.Models.AnimationIntensity.Normal;
                vm.KeepExpanded=false;vm.IsExpanded=false;await Task.Delay(900);
                var shellTest=(FrameworkElement)island.FindName("GlassShell");
                var samples=new List<object>();
                for(var run=0;run<3;run++)
                {
                    if(run==0){GC.Collect();GC.WaitForPendingFinalizers();EmptyWorkingSet(GetCurrentProcess());await Task.Delay(500);}
                    var times=new List<double>();var sizes=new List<double>();var watch=System.Diagnostics.Stopwatch.StartNew();
                    EventHandler frame=(_,_)=>{times.Add(watch.Elapsed.TotalMilliseconds);sizes.Add(shellTest.ActualWidth);};
                    System.Windows.Media.CompositionTarget.Rendering+=frame;
                    double synchronous;
                    try {vm.IsExpanded=true;synchronous=watch.Elapsed.TotalMilliseconds;await Task.Delay(850);} finally {System.Windows.Media.CompositionTarget.Rendering-=frame;}
                    var moving=Enumerable.Range(1,Math.Max(0,times.Count-1)).Where(i=>sizes[i]>sizes[i-1]+.1).ToArray();
                    samples.Add(new {Run=run+1,SynchronousMs=synchronous,FirstMovingMs=moving.Length>0?times[moving[0]]:0,MovingFrames=moving.Length,MaxMovingGap=moving.Length>0?moving.Max(i=>times[i]-times[i-1]):0,EndWidth=sizes.LastOrDefault()});
                    vm.IsExpanded=false;await Task.Delay(700);
                }
                File.WriteAllText(Path.Combine(output,"idle-expansion-metrics.json"),System.Text.Json.JsonSerializer.Serialize(samples));
            }
            if(Environment.GetEnvironmentVariable("ISLAND_MEASURE_ANIMATION")=="1")
            {
                vm.Settings.AnimationIntensity=DynamicIsland.Windows.Models.AnimationIntensity.Normal;
                vm.KeepExpanded=false;vm.IsExpanded=false;await Task.Delay(1000);
                var shellTest=(FrameworkElement)island.FindName("GlassShell");
                var times=new List<double>();var sizes=new List<double>();double? frostFirst=null;var watch=System.Diagnostics.Stopwatch.StartNew();
                EventHandler frame=(_,_)=>{times.Add(watch.Elapsed.TotalMilliseconds);sizes.Add(shellTest.ActualWidth);if(vm.NativeFrostActive && frostFirst is null)frostFirst=watch.Elapsed.TotalMilliseconds;};
                System.Windows.Media.CompositionTarget.Rendering+=frame;
                try {vm.IsExpanded=true;await Task.Delay(900);} finally {System.Windows.Media.CompositionTarget.Rendering-=frame;}
                var moving=Enumerable.Range(1,times.Count-1).Where(i=>sizes[i]>sizes[i-1]+.1).Select(i=>times[i]-times[i-1]).ToArray();
                File.WriteAllText(Path.Combine(output,"animation-metrics.json"),System.Text.Json.JsonSerializer.Serialize(new {FrameCount=times.Count,MovingFrames=moving.Length,LongestGap=moving.Length>0?moving.Max():0,AverageGap=moving.Length>0?moving.Average():0,StartWidth=sizes.FirstOrDefault(),EndWidth=sizes.LastOrDefault(),FrostActive=vm.NativeFrostActive,FrostFirstMs=frostFirst}));
            }
            if(Environment.GetEnvironmentVariable("ISLAND_MEASURE_TIMER")=="1")
            {
                vm.Settings.AnimationIntensity=DynamicIsland.Windows.Models.AnimationIntensity.Normal;
                vm.KeepExpanded=false;vm.IsExpanded=false;await Task.Delay(700);
                var timers=(DynamicIsland.Windows.Services.TimerAlarmService)typeof(IslandViewModel).GetField("_timerAlarmService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
                timers.StartTimer(TimeSpan.FromMinutes(5),"Fixture");await Task.Delay(300);
                var host=(FrameworkElement)island.FindName("TimerPanelHost");
                var times=new List<double>();var sizes=new List<double>();var watch=System.Diagnostics.Stopwatch.StartNew();
                EventHandler frame=(_,_)=>{times.Add(watch.Elapsed.TotalMilliseconds);sizes.Add(host.ActualWidth);};
                System.Windows.Media.CompositionTarget.Rendering+=frame;
                try {island.ShowTimerPanel();await Task.Delay(900);} finally {System.Windows.Media.CompositionTarget.Rendering-=frame;}
                var moving=Enumerable.Range(1,times.Count-1).Where(i=>sizes[i]>sizes[i-1]+.1).Select(i=>times[i]-times[i-1]).ToArray();
                File.WriteAllText(Path.Combine(output,"timer-metrics.json"),System.Text.Json.JsonSerializer.Serialize(new {FrameCount=times.Count,MovingFrames=moving.Length,LongestGap=moving.Length>0?moving.Max():0,AverageGap=moving.Length>0?moving.Average():0,StartWidth=sizes.FirstOrDefault(),EndWidth=sizes.LastOrDefault()}));
                island.CloseTimerPanel();await Task.Delay(400);
            }
            var shell=(FrameworkElement)island.FindName("GlassShell");var origin=shell.PointToScreen(new System.Windows.Point());var corner=shell.PointToScreen(new System.Windows.Point(shell.ActualWidth,shell.ActualHeight));
            File.WriteAllText(Path.Combine(output,"desktop-ready.json"),System.Text.Json.JsonSerializer.Serialize(new {PID=Environment.ProcessId,vm.NativeFrostActive,X=origin.X,Y=origin.Y,Width=corner.X-origin.X,Height=corner.Y-origin.Y}));
            var deadline=DateTime.UtcNow.AddSeconds(60);
            while(DateTime.UtcNow<deadline && !File.Exists(Path.Combine(output,"desktop-finish.txt")))await Task.Delay(200);
        }
        finally {island.Hide();backing.Close();}
    }
}
