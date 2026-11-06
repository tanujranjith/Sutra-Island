using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

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
        var attempts=0;BitmapSource? copied=null;
        var clipboardHistory=new ClipboardService(new LoggingService());
        var remembered=0;
        var preview=new ScreenshotPreview(_=>{},bitmap=>{attempts++;if(attempts<3)throw new System.Runtime.InteropServices.COMException("fixture busy clipboard");copied=bitmap;},bitmap=>{remembered++;clipboardHistory.RememberScreenshot(bitmap);}) {Opacity=0,ShowActivated=false};
        try
        {
            preview.Present(new Rect(100,10,180,40),new Rect(0,0,1200,800),true);
            Check(attempts==0&&preview.ActualHeight<250,"The post-capture preview is compact and does not copy before an image arrives");
            preview.SetImage(image);
            preview.SetMode(CaptureOverlay.RegionMode);
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
            Check(remembered==1,"Only a successful screenshot copy enters Island history, after clipboard retries finish");
            var history=await clipboardHistory.ReadPaletteAsync(30);
            var screenshot=history.SingleOrDefault(item=>item.Id.StartsWith("sutra-screenshot-",StringComparison.Ordinal));
            Check(screenshot is { IsImage:true, ImageWidth:96, ImageHeight:64, PreviewBgra:not null } && ReferenceEquals(clipboardHistory.ScreenshotOriginal(screenshot.Id),copied),"Copied annotations appear in Island clipboard history and retain their original full-resolution pixels");
            clipboardHistory.ReleasePaletteEntries();
            history=await clipboardHistory.ReadPaletteAsync(30);
            Check(history.Any(item=>item.Id==screenshot!.Id),"Closing and reopening clipboard history retains session screenshots independently of Windows history");
            clipboardHistory.RememberScreenshot(copied!);
            history=await clipboardHistory.ReadPaletteAsync(30);
            Check(history.Count(item=>item.Id.StartsWith("sutra-screenshot-",StringComparison.Ordinal))==1,"Recopying an unchanged screenshot does not duplicate the history row");
            for(var i=0;i<13;i++)
            {
                var next=BitmapSource.Create(96,64,96,96,PixelFormats.Bgra32,null,pixels,96*4);next.Freeze();clipboardHistory.RememberScreenshot(next);
            }
            history=await clipboardHistory.ReadPaletteAsync(30);
            Check(history.Count(item=>item.Id.StartsWith("sutra-screenshot-",StringComparison.Ordinal))==10,"Session screenshot history evicts old captures at its configured entry limit");
            clipboardHistory.ConfigureScreenshotHistory(true,3);
            history=await clipboardHistory.ReadPaletteAsync(30);
            Check(history.Count(item=>item.Id.StartsWith("sutra-screenshot-",StringComparison.Ordinal))==3,"Reducing screenshot history capacity immediately evicts older captures");
            clipboardHistory.ConfigureScreenshotHistory(false,10);
            clipboardHistory.RememberScreenshot(copied!);
            history=await clipboardHistory.ReadPaletteAsync(30);
            Check(!history.Any(item=>item.Id.StartsWith("sutra-screenshot-",StringComparison.Ordinal)),"Disabling screenshot history clears retained pixels and stops remembering new screenshots");
            var capture=new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth),(int)Math.Ceiling(preview.ActualHeight),96,96,PixelFormats.Pbgra32);capture.Render((Visual)preview.Content);
            var captureDirectory=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(captureDirectory);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using(var file=System.IO.File.Create(System.IO.Path.Combine(captureDirectory,"screenshot-preview.png")))encoder.Save(file);
            preview.Present(new Rect(100,10,180,40),new Rect(0,0,1200,800),true);
            Check(preview.RevealProgress==1,"Reduced motion shows the usable screenshot preview immediately");
            preview.Hide();preview.Present(new Rect(-400,10,180,40),new Rect(-600,0,600,700),true);
            Check(preview.Left>=-600&&preview.Left+preview.Width<=0,"Preview placement supports a negative-coordinate monitor");
        }
        finally{preview.Close();}
        var toolbar=new CaptureToolbarWindow {Opacity=0,ShowActivated=false,Topmost=false};
        try
        {
            // WPF may suspend clocks for a window wholly outside every real
            // monitor. Animate on the real work area; check negative placement
            // separately without depending on offscreen compositor frames.
            var toolbarWorkArea=SystemParameters.WorkArea;
            toolbar.PresentFromIsland(new Rect(toolbarWorkArea.Left+100,toolbarWorkArea.Top+20,180,40),toolbarWorkArea,false);
            var toolbarDeadline=DateTime.UtcNow.AddSeconds(4);
            while(toolbar.RevealProgress<1 && DateTime.UtcNow<toolbarDeadline)await Task.Delay(20);
            Check(toolbar.ActiveMode==CaptureOverlay.RegionMode,"The capture toolbar starts with Region selected");
            Check(toolbar.RevealProgress==1,"The capture toolbar settles after the notification-style reveal");
            toolbar.SetMode(CaptureOverlay.ScreenMode);
            Check(toolbar.ActiveMode==CaptureOverlay.ScreenMode,"Toolbar mode synchronization clearly switches the active capture mode");
            toolbar.SetMode(CaptureOverlay.WindowMode);
            Check(toolbar.ActiveMode==CaptureOverlay.WindowMode,"Toolbar synchronization returns to window mode without raising a second request");
            toolbar.SetMode(CaptureOverlay.RegionMode);
            toolbar.PresentFromIsland(new Rect(-1800,20,180,40),new Rect(-1920,0,1920,1080),true);
            Check(toolbar.Left>=-1920&&toolbar.Left+toolbar.ActualWidth<=0&&toolbar.RevealProgress==1,"The toolbar supports negative-coordinate placement independently of compositor animation timing");
        }
        finally{toolbar.Close();}
        using(var background=new System.Drawing.Bitmap(100,80))
        using(var overlay=new CaptureOverlay(new System.Drawing.Rectangle(-100,0,100,80),background,null) {Opacity=0})
        {
            Check(overlay.ActiveMode==CaptureOverlay.RegionMode,"The capture overlay starts in region mode without another click");
            var selection=overlay.SelectAsync();
            void Mouse(string name,int x,int y)=>typeof(System.Windows.Forms.Form).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(overlay,[new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,x,y,0)]);
            Mouse("OnMouseDown",80,60);Mouse("OnMouseMove",-20,-10);Mouse("OnMouseUp",-20,-10);
            var request=await selection;
            Check(request is { Mode:CaptureOverlay.RegionMode } && request.Value.Region==new System.Drawing.Rectangle(0,0,80,60),"Region selection supports reverse dragging and clamps outside-monitor coordinates");
        }
        using(var background=new System.Drawing.Bitmap(40,30))
        using(var overlay=new CaptureOverlay(new System.Drawing.Rectangle(0,0,40,30),background,null) {Opacity=0})
        {
            var selection=overlay.SelectAsync();
            overlay.Cancel();
            Check(await selection is null,"Escape-style cancellation resolves without a capture request");
        }
        await VerifyOverlayDirtyRenderingAsync(Check);
        var directory=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllLines(System.IO.Path.Combine(directory,"screenshot-checks.txt"),report);
    }

    private static async Task VerifyOverlayDirtyRenderingAsync(Action<bool,string> check)
    {
        const int width=960,height=720;
        using var background=new Drawing.Bitmap(width,height,Drawing.Imaging.PixelFormat.Format32bppArgb);
        var sourcePixels=new byte[width*height*4];
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var offset=(y*width+x)*4;
            sourcePixels[offset]=(byte)(30+(x*3+y)%190);
            sourcePixels[offset+1]=(byte)(25+(x+y*5)%200);
            sourcePixels[offset+2]=(byte)(45+(x*7+y*2)%180);
            sourcePixels[offset+3]=255;
        }
        var sourceData=background.LockBits(new Drawing.Rectangle(0,0,width,height),Drawing.Imaging.ImageLockMode.WriteOnly,Drawing.Imaging.PixelFormat.Format32bppArgb);
        try{System.Runtime.InteropServices.Marshal.Copy(sourcePixels,0,sourceData.Scan0,sourcePixels.Length);}
        finally{background.UnlockBits(sourceData);}

        var bounds=new Drawing.Rectangle(-800,-120,width,height);
        CaptureWindow[] windows=
        [
            new(11,"A long window title that must disappear when hover moves",new Drawing.Rectangle(-740,-60,390,280)),
            new(12,"Second window",new Drawing.Rectangle(-340,200,350,310)),
            new(13,"Clipped edge window",new Drawing.Rectangle(-900,-180,300,220)),
            new(14,"Very short window title",new Drawing.Rectangle(-120,450,240,12))
        ];
        using(var overlay=new CaptureOverlay(bounds,background,windows){Opacity=0,TopMost=false})
        {
            var result=overlay.SelectAsync();
            var dirty=new List<Drawing.Rectangle>();
            overlay.Invalidated+=(_,args)=>dirty.Add(Drawing.Rectangle.Intersect(args.InvalidRect,overlay.ClientRectangle));
            using var incremental=new Drawing.Bitmap(width,height,Drawing.Imaging.PixelFormat.Format32bppArgb);
            PaintOverlay(overlay,incremental,overlay.ClientRectangle);
            void Mouse(string method,int x,int y,Forms.MouseButtons button=Forms.MouseButtons.Left) =>
                typeof(Forms.Form).GetMethod(method,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
                    .Invoke(overlay,[new Forms.MouseEventArgs(button,1,x,y,0)]);
            void RepaintAndCompare(string label,bool partial=false)
            {
                var regions=dirty.Where(rect=>rect.Width>0&&rect.Height>0).ToArray();dirty.Clear();
                foreach(var region in regions)PaintOverlay(overlay,incremental,region);
                using var fresh=new Drawing.Bitmap(width,height,Drawing.Imaging.PixelFormat.Format32bppArgb);
                PaintOverlay(overlay,fresh,overlay.ClientRectangle);
                var equal=BitmapPixels(incremental).SequenceEqual(BitmapPixels(fresh));
                if(!equal)
                {
                    var output=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(output);
                    incremental.Save(System.IO.Path.Combine(output,"overlay-dirty-failure.png"),Drawing.Imaging.ImageFormat.Png);
                    fresh.Save(System.IO.Path.Combine(output,"overlay-full-expected.png"),Drawing.Imaging.ImageFormat.Png);
                }
                check(equal,label+" matches a fresh full render without stale shade, outlines, or labels");
                if(partial)check(regions.Length>0&&regions.All(rect=>(long)rect.Width*rect.Height<width*height/2),
                    "Small selection movement repaints bounded dirty regions instead of the full desktop");
            }

            Mouse("OnMouseDown",430,310);Mouse("OnMouseMove",800,620);
            RepaintAndCompare("A large forward drag on a negative-coordinate desktop");
            var clearPixel=incremental.GetPixel(500,400);var clearOriginal=background.GetPixel(500,400);
            var shadedPixel=incremental.GetPixel(20,300);var shadedOriginal=background.GetPixel(20,300);
            check(clearPixel==clearOriginal&&shadedPixel.R<shadedOriginal.R&&shadedPixel.G<shadedOriginal.G&&shadedPixel.B<shadedOriginal.B,
                "The selected region preserves original desktop pixels while its outside remains visibly dimmed");
            Mouse("OnMouseMove",790,610);RepaintAndCompare("A small drag update",partial:true);
            Mouse("OnMouseMove",460,340);RepaintAndCompare("Shrinking below the size-label threshold");
            Mouse("OnMouseMove",90,70);RepaintAndCompare("Reversing the drag across its anchor");
            Mouse("OnMouseMove",-80,-40);RepaintAndCompare("Dragging outside the top-left desktop bounds");
            Mouse("OnMouseMove",730,510);RepaintAndCompare("Returning from a clipped reverse selection");
            Mouse("OnMouseUp",431,311);RepaintAndCompare("Discarding a tiny release after a large drag");
            check(!result.IsCompleted,"A tiny release leaves the overlay available for a new region");

            overlay.SetMode(CaptureOverlay.WindowMode);RepaintAndCompare("Switching from region to window mode");
            Mouse("OnMouseMove",300,180,Forms.MouseButtons.None);RepaintAndCompare("Hovering a titled window");
            Mouse("OnMouseMove",850,575,Forms.MouseButtons.None);RepaintAndCompare("Hovering a window too short to contain its title");
            Mouse("OnMouseMove",300,180,Forms.MouseButtons.None);RepaintAndCompare("Moving from a short window without leaving title pixels below it");
            Mouse("OnMouseMove",600,450,Forms.MouseButtons.None);RepaintAndCompare("Moving to a differently titled window");
            Mouse("OnMouseMove",20,20,Forms.MouseButtons.None);RepaintAndCompare("Hovering a window clipped by the negative desktop origin");
            Mouse("OnMouseMove",920,690,Forms.MouseButtons.None);RepaintAndCompare("Leaving every window hover target");
            Mouse("OnMouseMove",300,180,Forms.MouseButtons.None);
            typeof(Forms.Form).GetMethod("OnMouseLeave",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
                .Invoke(overlay,[EventArgs.Empty]);
            RepaintAndCompare("Leaving the overlay erases the previous window title");
            overlay.SetMode(CaptureOverlay.ScreenMode);RepaintAndCompare("Switching to full-screen shade and outline");
            overlay.SetMode(CaptureOverlay.RegionMode);RepaintAndCompare("Returning to empty region mode");

            Mouse("OnMouseDown",440,330);
            for(var index=0;index<512;index++)Mouse("OnMouseMove",100+(index*17)%720,90+(index*11)%540);
            RepaintAndCompare("A burst of 512 pointer movements before a repaint");
            Mouse("OnMouseMove",777,555);RepaintAndCompare("The most recent pointer position after a movement burst");
            Mouse("OnMouseUp",-20,780);RepaintAndCompare("A release beyond the desktop after the last pointer event");
            var request=await result;
            check(request is {Mode:CaptureOverlay.RegionMode}&&request.Value.Region==new Drawing.Rectangle(0,330,440,390),
                "Rapid movement captures the exact final release coordinates, reverse direction, and desktop clipping");
            var captureDirectory=System.IO.Path.Combine(AppDataPaths.Root,"captures");System.IO.Directory.CreateDirectory(captureDirectory);
            incremental.Save(System.IO.Path.Combine(captureDirectory,"overlay-dirty-render.png"),Drawing.Imaging.ImageFormat.Png);
        }
        check(BitmapPixels(background).SequenceEqual(sourcePixels),
            "Overlay caches and disposal preserve the caller-owned frozen desktop pixels");
        using var secondOverlay=new CaptureOverlay(bounds,background,windows){Opacity=0,TopMost=false};
        using var reused=new Drawing.Bitmap(width,height,Drawing.Imaging.PixelFormat.Format32bppArgb);
        PaintOverlay(secondOverlay,reused,secondOverlay.ClientRectangle);
        check(BitmapPixels(reused).Any(value=>value!=0),"The same frozen desktop remains usable by a second overlay after cached scenes are disposed");
        var fullScreenResult=secondOverlay.SelectAsync();secondOverlay.SetMode(CaptureOverlay.ScreenMode);secondOverlay.Confirm();
        var fullScreen=await fullScreenResult;
        check(fullScreen is {Mode:CaptureOverlay.ScreenMode}&&fullScreen.Value.Region==new Drawing.Rectangle(0,0,width,height),
            "Full-screen confirmation returns physical bitmap bounds on a negative-coordinate desktop");
        using var windowOverlay=new CaptureOverlay(bounds,background,windows){Opacity=0,TopMost=false};
        var windowResult=windowOverlay.SelectAsync();windowOverlay.SetMode(CaptureOverlay.WindowMode);
        typeof(Forms.Form).GetMethod("OnMouseDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
            .Invoke(windowOverlay,[new Forms.MouseEventArgs(Forms.MouseButtons.Left,1,20,20,0)]);
        var selectedWindow=await windowResult;
        check(selectedWindow is {Mode:CaptureOverlay.WindowMode}&&selectedWindow.Value.Region==new Drawing.Rectangle(0,0,200,160),
            "Window capture clips negative screen coordinates into exact frozen-bitmap pixels");
    }

    private static void PaintOverlay(CaptureOverlay overlay,Drawing.Bitmap target,Drawing.Rectangle clip)
    {
        using var graphics=Drawing.Graphics.FromImage(target);graphics.SetClip(clip);
        using var args=new Forms.PaintEventArgs(graphics,clip);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(CaptureOverlay).GetMethod("OnPaintBackground",flags)!.Invoke(overlay,[args]);
        typeof(CaptureOverlay).GetMethod("OnPaint",flags)!.Invoke(overlay,[args]);
    }

    private static byte[] BitmapPixels(Drawing.Bitmap bitmap)
    {
        var rect=new Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height);
        var data=bitmap.LockBits(rect,Drawing.Imaging.ImageLockMode.ReadOnly,Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes=new byte[bitmap.Width*bitmap.Height*4];
            for(var row=0;row<bitmap.Height;row++)
                System.Runtime.InteropServices.Marshal.Copy(nint.Add(data.Scan0,row*data.Stride),bytes,row*bitmap.Width*4,bitmap.Width*4);
            return bytes;
        }
        finally{bitmap.UnlockBits(data);}
    }
}
