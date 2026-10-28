using System.Windows;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyToolPanelsAsync(CommandPaletteViewModel vm, CommandPaletteWindow window, PaletteClipboardFixture clipboard, PaletteOutputFixture outputs, NotificationHistoryService history, IslandWindow islandWindow, Action<bool,string> check, string output)
    {
        async Task Layout() => await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        async Task Open(string query) { vm.ClosePanel(); vm.Query=query; window.Show(); await vm.ExecuteAsync(); await Layout(); }
        await Open("stopwatch"); await vm.SubmitPanelAsync(); await Task.Delay(90);
        vm.Stopwatch.StopwatchLapCommand.Execute(null);
        check(vm.IsStopwatchPanel && vm.Stopwatch.IsStopwatchRunning && vm.Stopwatch.Laps.Count==1,"Stopwatch panel starts the shared stopwatch and records laps");
        vm.ClosePanel(); check(vm.Stopwatch.IsStopwatchRunning,"Closing stopwatch panel preserves elapsed-time tracking");
        await Open("stopwatch"); await vm.SubmitPanelAsync(); check(!vm.Stopwatch.IsStopwatchRunning,"Reopening stopwatch can pause the same session");
        await CaptureSutraAsync(window,output,"command-stopwatch"); vm.Stopwatch.StopwatchResetCommand.Execute(null);
        check(vm.Stopwatch.Laps.Count==0,"Reset clears stopwatch laps");
        await Open("focus"); vm.Island.SetFocusMode(false); vm.FocusMinutes="0"; vm.StartPanelFocus();
        check(!vm.Island.FocusSessionActive && vm.IsStatusError,"Focus rejects zero duration");
        vm.FocusMinutes="25"; vm.StartPanelFocus(); vm.ClosePanel();
        check(vm.Island.FocusSessionActive && vm.Island.Settings.FocusModeEnabled,"Focus continues in quiet mode after closing the palette");
        await Open("focus"); await CaptureSutraAsync(window,output,"command-focus"); vm.Island.EndFocusSession();
        check(!vm.Island.FocusSessionActive && !vm.Island.Settings.FocusModeEnabled,"Ending focus restores the previous disabled setting");
        vm.Island.SetFocusMode(true); vm.Island.StartFocusSession(TimeSpan.FromSeconds(1)); await WaitForSutraAsync(()=>!vm.Island.FocusSessionActive);
        check(!vm.Island.FocusSessionActive && vm.Island.Settings.FocusModeEnabled,"Focus expiry preserves a previously enabled quiet mode"); vm.Island.SetFocusMode(false);
        await Open("clipboard"); await vm.RefreshClipsAsync();
        check(vm.Clips.Count==2,"Clipboard panel loads recent text on demand"); vm.ClipFilter="indented";
        check(vm.Clips.Count==1,"Clipboard search filters contents"); vm.CopyClip(vm.SelectedClip);
        check(clipboard.Copied=="  indented\ntext  ","Copy preserves clipboard whitespace and line breaks");
        vm.ClipFilter=""; await Layout(); await CaptureSutraAsync(window,output,"command-clipboard");
        clipboard.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously); var read=vm.RefreshClipsAsync(); vm.ClosePanel(); clipboard.Pending.SetResult([new("late", "late secret")]); await read; clipboard.Pending=null;
        check(vm.Clips.Count==0,"A late clipboard read cannot repopulate a closed panel");
        history.Clear(); for(var i=0;i<35;i++) history.Add(i%2==0?"Google Chrome":"Snipping Tool",i%2==0?$"Tab using {250+i} MB":"Screenshot copied to clipboard","A message preview with more detail.");
        await Open("notifications");
        check(vm.PanelNotifications.Count==35 && window.ActualHeight<590,"Notification command panel caps its height with a long history");
        vm.NotificationFilter="Snipping"; check(vm.PanelNotifications.Count==17,"Notification search filters by application");
        vm.RemoveHistoryItem(vm.PanelNotifications[0]); check(vm.PanelNotifications.Count==16 && history.Items.Count==34,"Removing a notification updates history and filtered list");
        var mirrored=vm.Island.Settings.ShowNotifications; await vm.ToggleMirroringAsync(); check(vm.Island.Settings.ShowNotifications!=mirrored,"Notification mirroring toggles through the host"); await vm.ToggleMirroringAsync();
        vm.NotificationFilter=""; await Layout(); await CaptureSutraAsync(window,output,"command-notifications");
        var popup=(System.Windows.Controls.Primitives.Popup)islandWindow.FindName("NotificationHistoryPopup");
        var card=(FrameworkElement)popup.Child; card.DataContext=vm.Island;
        card.Measure(new System.Windows.Size(380,double.PositiveInfinity)); card.Arrange(new Rect(card.DesiredSize)); card.UpdateLayout();
        check(card.ActualWidth==380 && card.ActualHeight<480,"Legacy notification popup remains compact with dozens of entries");
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(380,(int)Math.Ceiling(card.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(card);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,"notification-history.png"))) encoder.Save(file);
        vm.ClearPanelHistory(); check(vm.PanelNotifications.Count==0,"Clear notification history updates the panel immediately");
        await Open("convert"); vm.ConversionValue="2"; vm.ConversionFrom="m"; vm.ConversionTo="ft";
        check(vm.IsConvertPanel && vm.CanCopyConversion && vm.ConversionResult.Contains("ft"),"Bare convert opens a live unit converter");
        vm.ConversionCategory="Temperature"; vm.ConversionFrom="°C"; vm.ConversionTo="°F"; vm.ConversionValue="100";
        check(vm.ConversionResult.Contains("212"),"Conversion category updates compatible units and temperature offsets");
        vm.SwapConversion(); check(vm.ConversionFrom=="°F" && vm.ConversionTo=="°C","Swap reverses conversion units");
        vm.ConversionValue="bad"; check(!vm.CanCopyConversion && vm.ConversionError.Length>0,"Invalid conversion values disable copy with feedback"); vm.ConversionValue="72"; await Layout(); await CaptureSutraAsync(window,output,"command-convert");
        await Open("audio output"); await vm.RefreshOutputsAsync(); check(vm.Outputs.Count==2 && vm.Outputs.Count(d=>d.IsCurrent)==1,"Output panel marks exactly one current device");
        vm.OutputFilter="head"; check(vm.Outputs.Count==1,"Output devices can be searched"); await vm.SelectOutputAsync(vm.Outputs[0]);
        check(outputs.Current=="headphones" && vm.Outputs[0].IsCurrent,"Choosing an output switches and refreshes the current marker");
        vm.OutputFilter=""; outputs.Fail=true; await vm.SelectOutputAsync(vm.Outputs.First(d=>!d.IsCurrent));
        check(vm.IsStatusError && !vm.OutputSwitching,"Output switch failure keeps the panel usable and reports the problem"); outputs.Fail=false;
        await Layout(); await CaptureSutraAsync(window,output,"command-output"); window.Hide();
        check(vm.Outputs.Count==0,"Closing output panel releases its device list");
    }
    private sealed class PaletteClipboardFixture : IPaletteClipboard
    {
        public IntegrationStatus Status=>new(IntegrationState.Ready,"Ready");
        public string? Copied;
        public TaskCompletionSource<IReadOnlyList<ClipboardEntry>>? Pending;
        public bool IncludeImage, FailCopy; public ClipboardEntry? CopiedEntry; public int Releases;
        public ClipboardEntry ImageEntry = new("image", "", true, new byte[32*16*4], 32, 16, 1280, 640);
        public Task<IReadOnlyList<ClipboardEntry>> ReadPaletteAsync(int max)=>Pending?.Task??Task.FromResult<IReadOnlyList<ClipboardEntry>>(IncludeImage ? [new("text", "  indented\ntext  "), ImageEntry] : [new("text", "  indented\ntext  "),new("second", "A second clipboard item")]);
        public bool CopyText(string text) { Copied=text; return true; }
        public bool CopyEntry(ClipboardEntry entry) { if(FailCopy) return false; CopiedEntry=entry; return entry.IsImage || CopyText(entry.Text); }
        public void ReleasePaletteEntries() => Releases++;
    }
    private sealed class PaletteOutputFixture : IPaletteOutputControl
    {
        public string Current="speakers"; public bool Fail;
        public TaskCompletionSource<IReadOnlyList<AudioOutputDevice>>? Pending;
        public Task<IReadOnlyList<AudioOutputDevice>> GetPaletteOutputsAsync()=>Pending?.Task ?? Task.FromResult<IReadOnlyList<AudioOutputDevice>>([new("speakers","Speakers · Realtek Audio",Current=="speakers"),new("headphones","Headphones · Bluetooth",Current=="headphones")]);
        public Task SwitchPaletteOutputAsync(string id) { if(Fail) throw new InvalidOperationException("That output device disconnected. Refresh the list."); Current=id; return Task.CompletedTask; }
    }
}
