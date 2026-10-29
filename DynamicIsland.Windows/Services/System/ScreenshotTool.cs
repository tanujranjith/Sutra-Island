using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Interop;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace DynamicIsland.Windows.Services;

/// <summary>Local pixel capture only: no OCR, provider calls, persistence, or upload.</summary>
internal sealed class ScreenshotTool(IslandWindow island, ScreenContextService screen, Func<bool> reduced) : IDisposable
{
    private ScreenshotPreview? _preview;
    private bool _busy, _disposed;
    private RegionOverlay? _selection;
    private readonly List<Window> _pins = [];

    public void Open()
    {
        if (_disposed || _busy) return;
        screen.RememberForeground(new System.Windows.Interop.WindowInteropHelper(island).Handle);
        _preview?.Close();
        Preview().Present(island.PaletteAnchorBounds, island.PaletteWorkArea, reduced());
    }
    private ScreenshotPreview Preview()
    {
        if (_preview is null)
        {
            _preview = new ScreenshotPreview(CaptureAsync, Pin);
            _preview.Closed += (_, _) => _preview = null;
        }
        return _preview;
    }
    public async Task CaptureAsync(string mode)
    {
        if (_busy || _disposed) return;
        _busy = true;
        var wasVisible = island.IsVisible;
        try
        {
            screen.RememberForeground(new System.Windows.Interop.WindowInteropHelper(island).Handle);
            var target = screen.LastForegroundTarget;
            _preview?.Close(); // Release the previous full-resolution image before another capture.
            // Hide our own surfaces before acquiring pixels, regardless of capture-affinity settings.
            island.Hide();
            foreach (var pin in _pins) pin.Hide();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(60);
            var bounds = Forms.SystemInformation.VirtualScreen;
            if (mode == "window")
            {
                if (target == nint.Zero || !NativeMethods.IsWindow(target) || !NativeMethods.GetWindowRect(target, out var rect))
                    throw new InvalidOperationException("No active window is available. Focus the window first, then try again.");
                bounds = Drawing.Rectangle.Intersect(bounds, new Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height));
            }
            if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 100_000_000)
                throw new InvalidOperationException("This capture is too large or outside the desktop. Try a smaller region.");
            BitmapSource bitmap;
            using (var background = await Task.Run(() => ScreenContextService.CaptureBitmap(new NativeMethods.Rect { Left=bounds.Left, Top=bounds.Top, Right=bounds.Right, Bottom=bounds.Bottom })))
            {
                if (_disposed) return;
                if (mode == "region")
                {
                    using var overlay = new RegionOverlay(bounds, background);
                    _selection = overlay;
                    var selected = await overlay.SelectAsync();
                    _selection = null;
                    if (selected is not { Width: > 1, Height: > 1 } region || _disposed) return;
                    // Copy only the selected pixels; a small crop must not retain the
                    // full multi-monitor desktop through a CroppedBitmap.Source.
                    bitmap = await Task.Run(() => CopyPixels(background, region));
                }
                else bitmap=await Task.Run(()=>CopyPixels(background));
            }
            if (_disposed) return;
            var preview = Preview(); preview.SetImage(bitmap); preview.SetMode(mode);
            await preview.CopyAsync();
            preview.Present(island.PaletteAnchorBounds, island.PaletteWorkArea, reduced());
        }
        catch (Exception ex)
        {
            if (!_disposed) { var preview=Preview();preview.SetStatus(ex is InvalidOperationException ? ex.Message : "Capture failed. Try again or choose a smaller region.");preview.Present(island.PaletteAnchorBounds,island.PaletteWorkArea,reduced()); }
        }
        finally
        {
            _selection = null;
            if (!_disposed) { if (wasVisible) island.Show(); foreach (var pin in _pins) pin.Show(); }
            _busy = false;
        }
    }
    internal static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    internal static BitmapSource CopyPixels(Drawing.Bitmap bitmap, Drawing.Rectangle? region = null)
    {
        var rect = region ?? new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(rect.Width * 4);
            BitmapSource image;
            if (data.Stride > 0)
            {
                // BitmapSource.Create copies the buffer into an OnLoad WIC bitmap.
                // No GDI handle, pointer, or full-desktop source survives this call.
                var size = checked((rect.Height - 1) * data.Stride + rowBytes);
                image = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null, data.Scan0, size, data.Stride);
            }
            else
            {
                // GDI can expose bottom-up rows. Normalize only that uncommon case.
                var pixels = new byte[checked(rowBytes * rect.Height)];
                for (var row = 0; row < rect.Height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(nint.Add(data.Scan0, checked(row * data.Stride)), pixels, row * rowBytes, rowBytes);
                image = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null, pixels, rowBytes);
            }
            image.Freeze();
            return image;
        }
        finally { bitmap.UnlockBits(data); }
    }
    private void Pin(BitmapSource bitmap)
    {
        // Pinned images retain full-resolution pixels. Bound their lifetime and count.
        var bytes=(long)bitmap.PixelWidth*bitmap.PixelHeight*4;
        const long budget=128L*1024*1024;
        if(bytes>budget){_preview?.SetStatus("This image is too large to pin. Save it or capture a smaller region.");return;}
        while(_pins.Count>0 && (_pins.Count>=3 || _pins.Sum(p=>(long)(p.Tag??0L))+bytes>budget))_pins[0].Close();
        var image=new Image { Source=bitmap, Stretch=Stretch.Uniform };
        var pin=new Window { Title="Pinned screenshot",Tag=bytes,Content=image,Width=Math.Min(600,bitmap.PixelWidth),Height=Math.Min(450,bitmap.PixelHeight),MinWidth=100,MinHeight=80,Topmost=true,ShowInTaskbar=false,Background=Brushes.Black };
        pin.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)pin.Close();};
        pin.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)pin.Close();else pin.DragMove();};
        pin.Closed+=(_,_)=>{image.Source=null;_pins.Remove(pin);};_pins.Add(pin);pin.Show();
    }
    public void Dispose() { _disposed=true;_selection?.Cancel();_preview?.Close();foreach(var pin in _pins.ToArray())pin.Close(); }
}

internal sealed class RegionOverlay : Forms.Form
{
    private readonly TaskCompletionSource<Drawing.Rectangle?> _result=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Drawing.Point? _start;
    private Drawing.Rectangle _region;
    public RegionOverlay(Drawing.Rectangle bounds, Drawing.Bitmap background)
    {
        FormBorderStyle=Forms.FormBorderStyle.None;StartPosition=Forms.FormStartPosition.Manual;AutoScaleMode=Forms.AutoScaleMode.None;
        Bounds=bounds;TopMost=true;ShowInTaskbar=false;KeyPreview=true;DoubleBuffered=true;Cursor=Forms.Cursors.Cross;
        BackgroundImage=background;BackgroundImageLayout=Forms.ImageLayout.None;
        MouseDown+=(_,e)=>{if(e.Button==Forms.MouseButtons.Right){Cancel();return;}if(e.Button==Forms.MouseButtons.Left){_start=e.Location;Capture=true;}};
        MouseMove+=(_,e)=>UpdateSelection(e.Location);
        MouseUp+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left && _start is not null){UpdateSelection(e.Location);_result.TrySetResult(_region.Width>1&&_region.Height>1?_region:null);Hide();}};
        KeyDown+=(_,e)=>{if(e.KeyCode==Forms.Keys.Escape){e.Handled=true;Cancel();}};
        FormClosed+=(_,_)=>_result.TrySetResult(null);
    }
    public Task<Drawing.Rectangle?> SelectAsync(){Show();Activate();return _result.Task;}
    public void Cancel(){_result.TrySetResult(null);Hide();}
    private void UpdateSelection(Drawing.Point point)
    {
        if(_start is not {} start)return;
        var next=Drawing.Rectangle.Intersect(ClientRectangle,Drawing.Rectangle.FromLTRB(Math.Min(start.X,point.X),Math.Min(start.Y,point.Y),Math.Max(start.X,point.X),Math.Max(start.Y,point.Y)));
        if(next==_region)return;
        using var dirty=new Drawing.Region(_region);dirty.Xor(next);
        foreach(var rect in new[]{_region,next})
        {
            if(rect.Width<=0||rect.Height<=0)continue;
            dirty.Union(new Drawing.Rectangle(rect.Left-2,rect.Top-2,rect.Width+4,4));
            dirty.Union(new Drawing.Rectangle(rect.Left-2,rect.Bottom-2,rect.Width+4,4));
            dirty.Union(new Drawing.Rectangle(rect.Left-2,rect.Top-2,4,rect.Height+4));
            dirty.Union(new Drawing.Rectangle(rect.Right-2,rect.Top-2,4,rect.Height+4));
        }
        _region=next;Invalidate(dirty);
    }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        using var shade=new Drawing.SolidBrush(Drawing.Color.FromArgb(95,0,0,0));
        using var outside=new Drawing.Region(ClientRectangle);if(_region.Width>0)outside.Exclude(_region);e.Graphics.FillRegion(shade,outside);
        using var outline=new Drawing.Pen(Drawing.Color.FromArgb(190,166,255),2);if(_region.Width>0)e.Graphics.DrawRectangle(outline,_region);
        e.Graphics.DrawString("Drag to capture · Esc / right-click to cancel",Drawing.SystemFonts.MessageBoxFont!,Drawing.Brushes.White,20,20);
    }
}

internal sealed class ScreenshotPreview : Window
{
    private readonly NotificationMorphHost _morph=new() { Margin=new Thickness(14) };
    private readonly InkCanvas _ink=new() { EditingMode=InkCanvasEditingMode.None };
    private readonly TextBlock _status=new() { Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8) };
    private BitmapSource? _image;
    private readonly List<Button> _imageButtons=[];
    private readonly Dictionary<string,Button> _modeButtons=[];
    private Button? _drawButton;
    private bool _copying;
    private readonly Action<BitmapSource> _copy;
    private readonly Viewbox _imageView;
    private readonly WrapPanel _actions;
    public ScreenshotPreview(Func<string,Task> capture,Action<BitmapSource> pin,Action<BitmapSource>? copy=null)
    {
        _copy=copy??CopyLocal;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/DynamicIsland.Windows;component/Views/ScreenshotResources.xaml",UriKind.Relative) });
        Title="Screenshot · Sutra Island";Width=580;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;
        var content=new StackPanel { Margin=new Thickness(16) };
        var header=new DockPanel();var close=Button("Close",()=>Close());close.Content=new TextBlock {Text="\uE711",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=12};close.Width=36;close.MinHeight=36;close.Padding=new Thickness(8);close.Background=Brushes.Transparent;close.BorderBrush=Brushes.Transparent;close.ToolTip="Close screenshot · Esc";DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        header.Children.Add(new TextBlock { Text="Screenshot",Foreground=Brushes.White,FontSize=17,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8) });content.Children.Add(header);
        var modes=new System.Windows.Controls.Primitives.UniformGrid {Columns=3};foreach(var mode in new[]{("Region","region"),("Active window","window"),("All screens","screen")}){var button=Button(mode.Item1,()=>{_ = capture(mode.Item2);});button.Style=(Style)FindResource("ScreenshotModeButton");_modeButtons.Add(mode.Item2,button);modes.Children.Add(button);}
        content.Children.Add(new Border { Background=new SolidColorBrush(Color.FromRgb(18,19,24)),CornerRadius=new CornerRadius(14),Padding=new Thickness(3),Margin=new Thickness(4,12,4,0),Child=modes });
        var view=new Viewbox { Stretch=Stretch.Uniform,MaxHeight=280,MaxWidth=510,Margin=new Thickness(4,12,4,4),Child=_ink,Visibility=Visibility.Collapsed };_imageView=view;content.Children.Add(view);
        _ink.DefaultDrawingAttributes=new DrawingAttributes { Color=Colors.OrangeRed,Width=5,Height=5,FitToCurve=true };
        var actions=new WrapPanel {Visibility=Visibility.Collapsed};_actions=actions;
        void Action(string title,Action action){var button=Button(title,action);button.IsEnabled=false;if(title=="Copy")button.Tag="primary";if(title=="Draw")_drawButton=button;_imageButtons.Add(button);actions.Children.Add(button);}
        Action("Copy",()=>{_ = CopyAsync();});Action("Save PNG",Save);Action("Draw",()=>{_ink.EditingMode=_ink.EditingMode==InkCanvasEditingMode.Ink?InkCanvasEditingMode.None:InkCanvasEditingMode.Ink;if(_drawButton is not null)_drawButton.Tag=_ink.EditingMode==InkCanvasEditingMode.Ink?"active":null;SetStatus(_ink.EditingMode==InkCanvasEditingMode.Ink?"Draw enabled · Copy, Save, or Pin to use your annotations.":"Drawing off.");});
        Action("Undo",()=>{if(_ink.Strokes.Count>0)_ink.Strokes.RemoveAt(_ink.Strokes.Count-1);});Action("Pin",()=>{try{pin(RenderImage());}catch(Exception){SetStatus("Could not pin this image. Try a smaller region.");}});content.Children.Add(actions);
        content.Children.Add(_status);SetStatus("Choose a capture mode. Images stay local; nothing is saved until you choose Save PNG.");
        var card=new Border { Background=new SolidColorBrush(Color.FromRgb(25,26,32)),CornerRadius=new CornerRadius(20),Child=content };
        _morph.Children.Add(new ScrollViewer { Content=card,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });Content=_morph;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        Closed+=(_,_)=>{_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_ink.Strokes.Clear();_ink.Background=null;_image=null;};
    }
    private Button Button(string text,Action click)
    {
        var content=new StackPanel {Orientation=System.Windows.Controls.Orientation.Horizontal};
        var glyph=text switch {"Copy"=>"\uE8C8","Save PNG"=>"\uE74E","Draw"=>"\uE70F","Undo"=>"\uE7A7","Pin"=>"\uE718",_=>""};
        if(glyph.Length>0)content.Children.Add(new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});
        content.Children.Add(new TextBlock {Text=text,VerticalAlignment=VerticalAlignment.Center});
        var button=new Button { Content=content,Style=(Style)FindResource("ScreenshotButton"),ToolTip=text };
        System.Windows.Automation.AutomationProperties.SetName(button,text);
        button.Click+=(_,_)=>click();return button;
    }
    public void SetMode(string mode){foreach(var entry in _modeButtons)entry.Value.Tag=entry.Key==mode?"active":null;}
    internal static System.Windows.DataObject ClipboardData(BitmapSource image)
    {
        var data=new System.Windows.DataObject();data.SetImage(image);
        data.SetData("CanIncludeInClipboardHistory",new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard",new MemoryStream(new byte[4]));
        return data;
    }
    private static void CopyLocal(BitmapSource image)=>System.Windows.Clipboard.SetDataObject(ClipboardData(image),true);
    public void SetStatus(string text)=>_status.Text=text;
    public void SetImage(BitmapSource image)
    {
        _image=image;_ink.Strokes.Clear();_ink.EditingMode=InkCanvasEditingMode.None;_ink.Width=image.PixelWidth;_ink.Height=image.PixelHeight;_ink.Background=new ImageBrush(image) { Stretch=Stretch.Fill };
        _imageView.Visibility=Visibility.Visible;_actions.Visibility=Visibility.Visible;
        foreach(var button in _imageButtons)button.IsEnabled=true;
        if(_drawButton is not null)_drawButton.Tag=null;
    }
    internal InkCanvas AnnotationCanvas => _ink;
    internal double RevealProgress => _morph.RevealProgress;
    internal BitmapSource RenderImage()
    {
        if(_image is null)throw new InvalidOperationException("Capture an image first.");
        if(_ink.Strokes.Count==0)return _image;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawImage(_image,new Rect(0,0,_image.PixelWidth,_image.PixelHeight));_ink.Strokes.Draw(dc);}
        var image=new RenderTargetBitmap(_image.PixelWidth,_image.PixelHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return image;
    }
    public async Task CopyAsync()
    {
        if(_image is null||_copying)return;_copying=true;
        try { var image=RenderImage();for(var attempt=0;attempt<5;attempt++){try{_copy(image);SetStatus($"Copied · {image.PixelWidth} × {image.PixelHeight} · local only");return;}catch(System.Runtime.InteropServices.COMException){await Task.Delay(50);}}SetStatus("Clipboard is busy. Your image is still here; choose Copy to retry."); }
        catch(Exception){SetStatus("Could not copy this image. Your preview is still available; try again or save it locally.");}
        finally {_copying=false;}
    }
    private async void Save()
    {
        try{var dialog=new Microsoft.Win32.SaveFileDialog { Filter="PNG image|*.png",FileName="Sutra screenshot "+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")+".png" };if(dialog.ShowDialog(this)!=true)return;var image=RenderImage();await Task.Run(()=>{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(dialog.FileName);encoder.Save(file);});SetStatus("Saved locally.");}
        catch(Exception){SetStatus("Could not save that file. Choose another location and try again.");}
    }
    public void Present(Rect anchor,Rect area,bool reduced)
    {
        Width=Math.Min(580,Math.Max(280,area.Width-16));MaxHeight=Math.Max(240,area.Height-16);
        Left=Math.Clamp(anchor.Left+anchor.Width/2-Width/2,area.Left,Math.Max(area.Left,area.Right-Width));Top=area.Top;
        _morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_morph.RevealProgress=reduced?1:0;
        Show();UpdateLayout();Top=Math.Clamp(anchor.Bottom-2,area.Top,Math.Max(area.Top,area.Bottom-ActualHeight));
        if(!reduced){var animation=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(380));animation.Completed+=(_,_)=>{_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_morph.RevealProgress=1;};_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,animation);}
    }
}
