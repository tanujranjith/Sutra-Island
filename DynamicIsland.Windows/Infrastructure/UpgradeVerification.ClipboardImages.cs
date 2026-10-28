using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyClipboardImagesAndCaretsAsync(CommandPaletteViewModel vm, CommandPaletteWindow window, PaletteClipboardFixture clipboard, IslandWindow island, Action<bool,string> check, string output)
    {
        var pixels=new byte[640*360*4];
        for(var y=0;y<360;y++) for(var x=0;x<640;x++) { var i=(y*640+x)*4; pixels[i]=(byte)(80+x/4); pixels[i+1]=(byte)(45+y/3); pixels[i+2]=(byte)(35+x/5); pixels[i+3]=255; }
        var source=BitmapSource.Create(640,360,96,96,PixelFormats.Bgra32,null,pixels,640*4);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var png=new MemoryStream(); encoder.Save(png);
        using var stream=new InMemoryRandomAccessStream(); await stream.WriteAsync(png.ToArray().AsBuffer()); stream.Seek(0);
        var decoded=await ClipboardService.ReadImagePreviewAsync("screenshot-fixture",stream);
        check(decoded.IsImage && decoded.ImageWidth==640 && decoded.ImageHeight==360 && decoded.PreviewWidth<=320 && decoded.PreviewHeight<=140 && decoded.PreviewBgra?.Length==decoded.PreviewWidth*decoded.PreviewHeight*4,"Windows image decoding creates a bounded thumbnail while retaining original image dimensions");
        clipboard.IncludeImage=true; clipboard.ImageEntry=decoded;
        window.PrepareForShow(); window.Show(); vm.Query="clipboard"; await vm.ExecuteAsync(); await vm.RefreshClipsAsync();
        await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        check(vm.Clips.Count==2 && vm.Clips.Any(c=>c.IsImage && c.HasThumbnail),"Clipboard history shows image previews alongside text entries");
        vm.ClipFilter="screenshot"; check(vm.Clips.Count==1 && vm.Clips[0].IsImage,"Screenshot search finds clipboard image entries");
        vm.CopyClip(vm.Clips[0]); check(clipboard.CopiedEntry?.Id==decoded.Id && clipboard.CopiedEntry.ImageWidth==640 && !vm.IsStatusError,"Copying an image selects its original history entry rather than copying the thumbnail or text");
        clipboard.FailCopy=true; vm.CopyClip(vm.Clips[0]); check(vm.IsStatusError && vm.StatusMessage.Contains("Refresh"),"A stale image copy reports refresh guidance without substituting clipboard text"); clipboard.FailCopy=false;
        vm.ClipFilter=""; await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var tools=(CommandToolsView)window.FindName("ToolsView");
        var list=(System.Windows.Controls.ListBox)tools.FindName("ClipboardList");
        File.WriteAllLines(Path.Combine(output,"clipboard-list-layout.txt"), VisualDescendants(list).OfType<FrameworkElement>().Prepend(list)
            .Select(element=>$"{element.GetType().Name} actual={element.ActualWidth:0.0}x{element.ActualHeight:0.0} desired={element.DesiredSize} visible={element.IsVisible} measure={element.IsMeasureValid}"));
        check(vm.Clips.Count==2 && list.Items.Count==2 && list.ActualHeight>100,$"Mixed clipboard rows remain rendered after filtering (vm={vm.Clips.Count}, items={list.Items.Count}, height={list.ActualHeight:0.0}, filter={vm.ClipFilter})");
        check(VisualDescendants(list).OfType<System.Windows.Controls.Image>().Any(image=>image.IsVisible && image.Source is not null && image.ActualHeight>0),"The clipboard image thumbnail has a rendered image surface");
        await CaptureSutraAsync(window,output,"clipboard-images");
        var stack=new System.Windows.Controls.StackPanel { Width=430, Margin=new Thickness(12) };
        var cases=new[] { ("Clipboard search",(Style)tools.FindResource("ToolSearchInput"),40d), ("Timer and alarm command",(Style)window.FindResource("PanelInput"),46d), ("Island timer",(Style)island.FindResource("TimerTextBox"),40d), ("Assistant composer",(Style)island.FindResource("QComposerTextBox"),48d), ("Standard settings field",(Style?)null,34d) };
        var fields=new List<(string Name,TextBox Field)>();
        foreach(var (name,style,height) in cases)
        {
            stack.Children.Add(new TextBlock {Text=name, Foreground=Brushes.LightGray, Margin=new Thickness(0,8,0,4)});
            var field=new TextBox { Style=style, Height=height, Text="", Foreground=Brushes.White, CaretBrush=Brushes.White };
            fields.Add((name,field)); stack.Children.Add(field);
        }
        var testWindow=new Window {Content=stack,SizeToContent=SizeToContent.WidthAndHeight,Background=Brushes.Black,Opacity=0,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            testWindow.Show(); await testWindow.Dispatcher.InvokeAsync(()=>testWindow.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach(var (name,field) in fields)
            {
                var empty=field.GetRectFromCharacterIndex(0); field.Text="Typing and pasting"; field.UpdateLayout(); var typed=field.GetRectFromCharacterIndex(3);
                check(!empty.IsEmpty && empty.Height>=field.FontSize*.85 && !typed.IsEmpty && typed.Height>=field.FontSize*.85,$"{name} retains a full-height caret for empty and typed text (empty={empty.Height:0.0}, typed={typed.Height:0.0}, font={field.FontSize:0.0})");
                field.Text="";
            }
            stack.LayoutTransform=new ScaleTransform(1.5,1.5); testWindow.UpdateLayout();
            check(fields.All(item=>item.Field.GetRectFromCharacterIndex(0).Height>=item.Field.FontSize*.85),"Text caret height survives scaled layouts");
            stack.LayoutTransform=Transform.Identity; testWindow.UpdateLayout(); await CaptureSutraAsync(testWindow,output,"text-field-carets");
        }
        finally { testWindow.Close(); }
        var releases=clipboard.Releases; window.Hide();
        check(vm.Clips.Count==0 && clipboard.Releases>releases,"Closing clipboard history releases previews and original-entry references"); clipboard.IncludeImage=false;
        check(new PaletteClip(new("no-preview","",true)).Detail.Contains("unavailable"),"Images without a decodable preview retain a usable image entry");
    }
}
