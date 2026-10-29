using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Windows.Services;

namespace DynamicIsland.Windows.Infrastructure;

internal static class ScreenshotVerification
{
    public static async Task RunAsync()
    {
        var report=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.Add("PASS "+label);}
        Check(HotkeyParser.TryParse(new Models.AppSettings().ScreenshotHotkey,out var screenshotModifiers,out var screenshotKey,out _,out _) && (screenshotModifiers!=GlobalHotkeyService.SettingsModifiers || screenshotKey!=GlobalHotkeyService.SettingsKey),"The default Screenshot shortcut cannot collide with the registered Settings shortcut");
        var pixels=Enumerable.Repeat((byte)255,96*64*4).ToArray();
        var image=BitmapSource.Create(96,64,96,96,PixelFormats.Bgra32,null,pixels,96*4);image.Freeze();
        var attempts=0;var captureRequests=0;BitmapSource? copied=null;
        var preview=new ScreenshotPreview(_=>{captureRequests++;return Task.CompletedTask;},_=>{},bitmap=>{attempts++;if(attempts<3)throw new System.Runtime.InteropServices.COMException("fixture busy clipboard");copied=bitmap;}) {Opacity=0,ShowActivated=false};
        try
        {
            preview.Present(new Rect(100,10,180,40),new Rect(0,0,1200,800),true);
            Check(captureRequests==0&&attempts==0&&preview.ActualHeight<250,"The initial chooser is compact and does not capture or copy before a mode is chosen");
            preview.SetImage(image);
            preview.SetMode("region");
            var clipboard=ScreenshotPreview.ClipboardData(image);
            Check(clipboard.GetImage() is not null && clipboard.GetData("CanUploadToCloudClipboard") is System.IO.MemoryStream { Length:4 } && clipboard.GetData("CanIncludeInClipboardHistory") is System.IO.MemoryStream { Length:4 },"Screenshot clipboard data includes image pixels and excludes Windows cloud sync/history");
            preview.Present(new Rect(100,10,180,40),new Rect(0,0,1200,800),false);
            var deadline=DateTime.UtcNow.AddSeconds(4);
            while(preview.RevealProgress<1 && DateTime.UtcNow<deadline)await Task.Delay(20);
            preview.UpdateLayout();
            Check(preview.RevealProgress==1,"Screenshot preview uses the notification morph and settles fully");
            Check(ReferenceEquals(image,preview.RenderImage()),"Unannotated copying reuses frozen original pixels without an extra bitmap");
            preview.AnnotationCanvas.Strokes.Add(new Stroke(new StylusPointCollection(new[]{new StylusPoint(10,20),new StylusPoint(80,20)}),new DrawingAttributes {Color=Colors.Red,Width=6,Height=6}));
            var annotated=preview.RenderImage();
            Check(annotated.PixelWidth==96&&annotated.PixelHeight==64&&annotated.IsFrozen,"Annotations preserve full pixel dimensions and produce a frozen image");
            var sample=new byte[4];annotated.CopyPixels(new Int32Rect(40,20,1,1),sample,4,0);
            Check(sample[2]>200&&sample[1]<100,"Annotations are flattened into copied and saved image pixels");
            await preview.CopyAsync();Check(attempts==3&&copied is not null,"Busy clipboard writes retry and complete without recapturing");
            var capture=new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth),(int)Math.Ceiling(preview.ActualHeight),96,96,PixelFormats.Pbgra32);capture.Render((Visual)preview.Content);
            var captureDirectory=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(captureDirectory);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using(var file=System.IO.File.Create(System.IO.Path.Combine(captureDirectory,"screenshot-preview.png")))encoder.Save(file);
            preview.Present(new Rect(100,10,180,40),new Rect(0,0,1200,800),true);
            Check(preview.RevealProgress==1,"Reduced motion shows the usable screenshot preview immediately");
            preview.Hide();preview.Present(new Rect(-400,10,180,40),new Rect(-600,0,600,700),true);
            Check(preview.Left>=-600&&preview.Left+preview.Width<=0,"Preview placement supports a negative-coordinate monitor");
        }
        finally{preview.Close();}
        using(var background=new System.Drawing.Bitmap(100,80))
        using(var overlay=new RegionOverlay(new System.Drawing.Rectangle(-100,0,100,80),background) {Opacity=0})
        {
            var selection=overlay.SelectAsync();
            void Mouse(string name,int x,int y)=>typeof(System.Windows.Forms.Form).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(overlay,[new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,x,y,0)]);
            Mouse("OnMouseDown",80,60);Mouse("OnMouseMove",-20,-10);Mouse("OnMouseUp",-20,-10);
            Check(await selection==new System.Drawing.Rectangle(0,0,80,60),"Region selection supports reverse dragging and clamps outside-monitor coordinates");
        }
        var directory=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllLines(System.IO.Path.Combine(directory,"screenshot-checks.txt"),report);
    }
}
