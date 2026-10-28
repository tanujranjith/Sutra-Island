using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyDelayedOutputLayoutAsync(CommandPaletteViewModel vm, CommandPaletteWindow window, PaletteOutputFixture outputs, Action<bool,string> check, string output)
    {
        var area=new Rect(0,0,1920,1080); var anchor=new Rect(800,20,320,40);
        async Task Layout() => await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.ApplicationIdle);
        var tools=(CommandToolsView)window.FindName("ToolsView");
        bool Fits() => tools.TranslatePoint(new System.Windows.Point(0,tools.ActualHeight),window).Y<=window.ActualHeight-8;
        outputs.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.PrepareForShow(); window.PresentFromIsland(anchor,area,false);
        vm.Query="audio output"; await vm.ExecuteAsync();
        await WaitForSutraAsync(()=>window.PresentationProgress>=1); await Layout();
        check(vm.ToolsLoading && Fits() && window.ActualHeight>230,$"Audio output loading panel fits after opening morph (height={window.ActualHeight:0.0}, content={tools.ActualHeight:0.0}, loading={vm.ToolsLoading}, panel={vm.Panel}, visible={tools.IsVisible}, query={vm.Query}, selected={vm.SelectedSuggestion?.Name}, status={vm.StatusMessage})");
        await CaptureSutraAsync(window,output,"audio-output-loading");
        outputs.Pending.SetResult([new("speakers","Speakers · Realtek Audio",true),new("headphones","Headphones · Bluetooth",false)]);
        await WaitForSutraAsync(()=>!vm.ToolsLoading); outputs.Pending=null; await Layout();
        var list=(System.Windows.Controls.ListBox)tools.FindName("OutputList");
        var morph=(NotificationMorphHost)window.FindName("PaletteMorph");
        check(vm.Outputs.Count==2 && list.ActualHeight>60 && Fits() && ((System.Windows.Controls.Border)morph.Children[0]).Clip is null,
            $"Late output discovery expands the entire card beyond its old reveal mask (window={window.ActualHeight:0.0}, list={list.ActualHeight:0.0})");
        await CaptureSutraAsync(window,output,"audio-output-loaded");
        vm.OutputFilter="head"; await Layout(); check(vm.Outputs.Count==1 && list.ActualHeight>25 && Fits(),"Filtering audio outputs keeps the device row and footer visible");
        vm.OutputFilter=""; await Layout(); check(vm.Outputs.Count==2 && list.ActualHeight>60 && Fits(),"Clearing an audio output filter restores visible rows");
        vm.OutputFilter="no matching device"; await Layout(); check(vm.Outputs.Count==0 && Fits(),"Empty device search keeps refresh and status within the card");
        vm.ClosePanel(); vm.Query="volume"; await vm.ExecuteAsync(); await Layout();
        check(window.ActualHeight>180 && window.ActualHeight<400,"Switching from output to a compact command recalculates card height");
        vm.ClosePanel(); vm.Query="audio output"; await vm.ExecuteAsync(); await WaitForSutraAsync(()=>!vm.ToolsLoading); await Layout();
        check(vm.Outputs.Count==2 && list.ActualHeight>60 && Fits(),"Returning to audio output restores its full card");
        window.Hide();
    }
}
