using System.Reflection;
using ThemeMode = DynamicIsland.Windows.Models.ThemeMode;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyGraphiteAsync(IslandViewModel vm, TimerAlarmViewModel timerVm, IslandWindow window,
        SettingsService persistence, LoggingService log, DynamicIsland.Q.Core.IQProviderRegistry registry,
        Action<bool,string> check, string output)
    {
        async Task Layout() { await Task.Delay(160); await System.Windows.Application.Current.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); }
        void Capture(FrameworkElement element, string name)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element) { Stretch=Stretch.Fill }, null, new Rect(0,0,element.ActualWidth,element.ActualHeight)); bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        vm.Settings.Theme = ThemeMode.Graphite; vm.Settings.AnimationIntensity = AnimationIntensity.Reduced;
        vm.Settings.ShowConnectivity = true; vm.Settings.ShowSystemMonitor = true; vm.Settings.ShowClock = true; vm.Settings.ShowDate = true;
        vm.Settings.QuotePlacement = QuotePlacement.Off; vm.Settings.IslandSize = IslandSize.Normal; vm.Settings.IslandWidth = 188; vm.Settings.IslandHeight = 38;
        vm.ApplySettings(); vm.ClearQ(); vm.IsExpanded = true; vm.KeepExpanded = true;
        typeof(IslandViewModel).GetMethod("OnMediaChanged", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,
            [null, new MediaInfo { Title="GREECE (feat. Drake)", Artist="DJ Khaled, Drake", SourceAppName="Spotify", CanSeek=true, Duration=TimeSpan.FromMinutes(3), Position=TimeSpan.FromMinutes(1), PlaybackState=MediaPlaybackState.Paused }]);
        window.Show(); window.ApplySettings(); await Layout();
        var shellForTooltip=(FrameworkElement)window.FindName("GlassShell");
        var compactForTooltip=(FrameworkElement)window.FindName("CompactContent");
        check(shellForTooltip.ToolTip is null && compactForTooltip.ToolTip is string && vm.DateToolTip is not null,
            "Date tooltip stays on the compact island instead of covering the expanded dashboard");
        var deck = (FrameworkElement)window.FindName("GraphiteDeck");
        check(deck.IsVisible && deck.ActualWidth >= 800 && deck.ActualHeight >= 300, "Graphite expanded dashboard lays out at its intended size");
        Capture((FrameworkElement)window.FindName("GlassShell"), "graphite-island");
        var mediaDeck=(GraphiteIslandView)deck;
        var equalizer=(FrameworkElement)mediaDeck.FindName("GraphiteEqualizer");
        vm.Settings.ShowMusicVisualizer=true;vm.ApplySettings();
        typeof(IslandViewModel).GetMethod("OnAudioChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,
            [null,new AudioState { Availability=AudioAvailability.Available, ActiveAudioOutput=true }]);
        await Layout();check(equalizer.IsVisible,"Expanded graphite dashboard shows the visualizer when audio is active");
        vm.Settings.ShowMusicVisualizer=false;vm.ApplySettings();await Layout();check(!equalizer.IsVisible,"Expanded visualizer respects its setting");
        vm.Settings.ShowMusicVisualizer=true;vm.ApplySettings();
        var back=(System.Windows.Controls.Button)mediaDeck.FindName("GraphiteSeekBack");
        var forward=(System.Windows.Controls.Button)mediaDeck.FindName("GraphiteSeekForward");
        check(back.IsVisible && forward.IsVisible && back.IsEnabled && forward.IsEnabled
            && ReferenceEquals(back.Command,vm.SeekBackCommand) && ReferenceEquals(forward.Command,vm.SeekForwardCommand)
            && vm.SeekBackTooltip=="Back 30 seconds" && vm.SeekForwardTooltip=="Forward 30 seconds",
            "Graphite transport exposes enabled back/forward 30-second commands");
        typeof(IslandViewModel).GetMethod("OnAudioChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,
            [null,new AudioState { Availability=AudioAvailability.Available, MasterVolumePercent=36, OutputDeviceName="Fixture output" }]);
        vm.KeepExpanded=false; await Layout();
        var outputButton=(System.Windows.Controls.Button)mediaDeck.FindName("GraphiteOutputButton");
        outputButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var popup=(System.Windows.Controls.Primitives.Popup)window.FindName("OutputDevicePopup");
        var collapseTimer=(DispatcherTimer)typeof(IslandWindow).GetField("_collapseTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        typeof(IslandWindow).GetMethod("Pill_MouseLeave",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[deck,null]);
        check(popup.IsOpen && ReferenceEquals(popup.PlacementTarget,outputButton) && !collapseTimer.IsEnabled,
            "Audio popup stays anchored to its visible button and pointer leave cannot schedule collapse");
        var collapseInterval=collapseTimer.Interval; collapseTimer.Interval=TimeSpan.FromMilliseconds(100);collapseTimer.Start();
        await Task.Delay(300); await Layout();
        check(popup.IsOpen && vm.IsExpanded && vm.InteractionProtected,
            "Audio output popup keeps an unpinned island expanded beyond its collapse timeout");
        outputButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(!popup.IsOpen, "Clicking the output arrow again closes the picker");
        outputButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(popup.IsOpen, "Clicking the output arrow after closing reopens the picker");
        popup.IsOpen=false; collapseTimer.Stop();collapseTimer.Interval=collapseInterval;
        vm.KeepExpanded=true; vm.IsExpanded=true; window.ApplySettings();

        await VerifyGraphiteMenusAsync(vm,window,check,output,assistant:false);
        await vm.StartChatAsync("Set volume to 35 percent"); window.ApplySettings(); await Layout();
        check(((FrameworkElement)window.FindName("QProviderSelector")).ActualWidth > 100, "Assistant keeps a visible provider selector");
        Capture((FrameworkElement)window.FindName("QShell"), "graphite-assistant");
        await VerifyGraphiteMenusAsync(vm,window,check,output,assistant:true);
        await VerifyComposerKeysAsync(vm, window, check);
        vm.ClearQ(); timerVm.CustomHours="01"; timerVm.CustomMinutes="02"; timerVm.CustomSeconds="03";
        timerVm.StartCustomCommand.Execute(null);
        check(timerVm.ValidationMessage == "", "Timer accepts combined hours, minutes and seconds");
        timerVm.CustomHours="24"; timerVm.CustomMinutes="01"; timerVm.StartCustomCommand.Execute(null);
        check(timerVm.ValidationMessage != "", "Timer rejects combined durations over one day");
        timerVm.CustomHours="NaN"; timerVm.StartCustomCommand.Execute(null);
        check(timerVm.ValidationMessage != "", "Timer rejects non-finite duration fields");
        timerVm.CancelTimerCommand.Execute(null); timerVm.CustomHours="00"; timerVm.CustomMinutes="25"; timerVm.CustomSeconds="00"; timerVm.TimerLabel="Focus session"; timerVm.StartCustomCommand.Execute(null);
        timerVm.TimerLabel="Break"; timerVm.StartPresetCommand.Execute("5");
        // Headless fixtures have no pointer in the timer; isolate layout checks from hover dismissal.
        var leaveTimer=(DispatcherTimer)typeof(IslandWindow).GetField("_timerPanelLeaveTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var leaveInterval=leaveTimer.Interval; leaveTimer.Interval=TimeSpan.FromSeconds(30);
        window.ShowTimerPanel(); await Layout();
        var timerView=VisualDescendants(window).OfType<TimerListPanel>().Single();
        var scroller=(System.Windows.Controls.ScrollViewer)timerView.FindName("EditorScroller");
        var timerList=(System.Windows.Controls.ListBox)timerView.FindName("TimerItemsList");
        check(timerList.Items.Count >= 2 && timerList.ActualHeight >= 75, "Timer list displays multiple rows without clipping them to a single line");
        var startButton=VisualDescendants(timerView).OfType<System.Windows.Controls.Button>().Single(button=>ReferenceEquals(button.Command,timerVm.StartCustomCommand));
        check(startButton.TranslatePoint(new System.Windows.Point(0,startButton.ActualHeight),scroller).Y <= scroller.ViewportHeight+1, "Start timer remains fully visible without scrolling at the reference size");
        Capture((FrameworkElement)window.FindName("TimerPanelHost"), "graphite-timer");
        timerView.Width=600; timerView.UpdateLayout();
        var options=(FrameworkElement)timerView.FindName("OptionsPanel");
        check(System.Windows.Controls.Grid.GetRow(options)==3 && options.ActualWidth>300, "Narrow timer panel stacks the controls at a usable width");
        timerView.Width=double.NaN; timerView.UpdateLayout();
        window.CloseTimerPanel(); leaveTimer.Interval=leaveInterval;
        using var codex = new AsyncDisposeAdapter(new CodexAppServerClient(log:log));
        var editorVm = new SettingsViewModel(vm.Settings, persistence, new StartupService(log), vm.ApplySettings, ()=>{}, ()=>{}, new FakeSecrets(), registry, new CodexAccountCoordinator(codex.Client,log));
        var editor = new SettingsWindow(editorVm,vm,window) { Opacity=0, ShowActivated=false };
        try
        {
            editorVm.SelectedSectionKey="content"; editor.Show(); await Layout(); editor.UpdateLayout();
            check(editor.ActualWidth == 1260 && editor.ActualHeight == 740, "Settings uses the reference three-column window dimensions");
            Capture((FrameworkElement)editor.Content,"graphite-settings");
        }
        finally { editor.Close(); }
    }
}
