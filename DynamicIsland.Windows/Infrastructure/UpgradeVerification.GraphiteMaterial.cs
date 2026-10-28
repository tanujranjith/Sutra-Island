using System.Windows;
using Color=System.Windows.Media.Color;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using Border=System.Windows.Controls.Border;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyGraphiteMaterialAsync(IslandViewModel vm,IslandWindow island,TimerAlarmViewModel timerVm,
        MediaSessionService media,TimerAlarmService timers,SettingsService persistence,LoggingService log,NotificationHistoryService history,Action<bool,string> check,string output)
    {
        async Task Layout() => await island.Dispatcher.InvokeAsync(island.UpdateLayout,DispatcherPriority.ApplicationIdle);
        byte Capture(FrameworkElement element,string name,Color? backing=null)
        {
            element.UpdateLayout();var size=new Rect(0,0,element.ActualWidth,element.ActualHeight);var drawing=new DrawingVisual();
            using(var context=drawing.RenderOpen())
            {
                if(backing is Color color)
                {
                    context.DrawRectangle(new SolidColorBrush(color),null,size);
                    var pen=new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(100,130,125,145)),1);
                    for(var y=20d;y<size.Height;y+=28) context.DrawLine(pen,new System.Windows.Point(0,y),new System.Windows.Point(size.Width,y));
                }
                context.DrawRectangle(new VisualBrush(element) {ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(VisualTreeHelper.GetOffset(element).X,VisualTreeHelper.GetOffset(element).Y,size.Width,size.Height),Stretch=Stretch.Fill},null,size);
            }
            var image=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);image.Render(drawing);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);var sample=new byte[4];image.CopyPixels(new Int32Rect(15,90,1,1),sample,4,0);return sample[3];
        }
        vm.ClearQ();vm.KeepExpanded=true;vm.IsExpanded=true;vm.ApplySettings();island.ApplySettings();await Layout();
        var shell=(Border)island.FindName("GlassShell");
        check(vm.IslandBaseSurfaceBrush is SolidColorBrush {Color.A:255} && vm.PrimaryTextBrush is SolidColorBrush {Color.A:255},
            "Opaque fallback prevents sharp desktop text when native blur is unavailable");
        check(Capture(shell,"graphite-glass-island-alpha") ==255,"Rendered island fallback fully blocks background text");
        Capture(shell,"graphite-glass-island-light",Color.FromRgb(247,247,250));Capture(shell,"graphite-glass-island-dark",Color.FromRgb(30,27,42));
        var audio=new PaletteAudioFixture();var seek=new PaletteSeekFixture();var clipboard=new PaletteClipboardFixture();var outputs=new PaletteOutputFixture();
        var runner=new CommandRunner(vm,media,audio,timers,timerVm,new SutraPaletteHost(vm,island),seek,clipboard,outputs);
        var paletteVm=new CommandPaletteViewModel(runner,vm.Settings,()=>persistence.SaveAsync(vm.Settings));
        var palette=new CommandPaletteWindow(paletteVm,()=>true) {Opacity=0,ShowActivated=false};
        try
        {
            palette.PresentFromIsland(new Rect(300,0,188,38),new Rect(0,0,1920,1080),reducedMotion:true); await Task.Delay(80); palette.UpdateLayout();var surface=(Border)palette.FindName("PaletteSurface");
            check(surface.Background is LinearGradientBrush gradient && gradient.GradientStops.All(stop=>stop.Color.A ==255)
                && surface.CornerRadius.TopLeft==25,"Command palette shares opaque glass fallback and rounded shell");
            check(Capture((FrameworkElement)palette.Content,"graphite-glass-palette-alpha") ==255,"Settled palette fallback does not expose sharp background text");
            Capture((FrameworkElement)palette.Content,"graphite-glass-palette-light",Color.FromRgb(247,247,250));Capture((FrameworkElement)palette.Content,"graphite-glass-palette-dark",Color.FromRgb(30,27,42));
            palette.Hide();
            palette.PresentFromIsland(new Rect(300,0,188,38),new Rect(0,0,1920,1080),reducedMotion:true);
            paletteVm.Query="volume 37";
            await (Task)typeof(CommandPaletteWindow).GetMethod("ExecuteAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(palette,null)!;
            palette.Hide();palette.PrepareForShow();palette.PresentFromIsland(new Rect(300,0,188,38),new Rect(0,0,1920,1080),reducedMotion:true);
            var query=(System.Windows.Controls.TextBox)palette.FindName("QueryBox");
            void Key(System.Windows.Input.Key key) => typeof(CommandPaletteWindow).GetMethod("Window_PreviewKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(palette,[palette,new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(query)!,Environment.TickCount,key) {RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent}]);
            Key(System.Windows.Input.Key.Up);
            check(paletteVm.Query=="volume 37" && query.CaretIndex==query.Text.Length,"Up recalls the complete last command and arguments after reopening the palette");
            Key(System.Windows.Input.Key.Down);check(paletteVm.Query=="","Down exits command recall and restores the empty draft");
            paletteVm.Query="timer";Key(System.Windows.Input.Key.Up);check(paletteVm.Query=="timer","Up preserves suggestion navigation when a new query is being typed");
            palette.Hide();
            audio.SetMasterVolume(42);
            await VerifyCommandPanelsAsync(paletteVm,palette,audio,timers,check,output);
            await VerifySeekPanelAsync(paletteVm,palette,seek,check,output);
            await VerifyToolPanelsAsync(paletteVm,palette,clipboard,outputs,history,island,check,output);
            await VerifyPalettePresentationAsync(paletteVm,palette,log,check,output);
        }
        finally {palette.Close();}
    }
}
