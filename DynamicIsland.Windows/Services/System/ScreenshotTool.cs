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
    private CaptureOverlay? _overlay;
    private CaptureToolbarWindow? _toolbar;
    private bool _busy, _disposed;
    private readonly List<Window> _pins = [];

    /// <summary>
    /// Opens the full-desktop capture overlay immediately. Region selection is armed
    /// by default; nothing is copied or saved until the user chooses a region/window
    /// or confirms full-screen capture.
    /// </summary>
    public void Open()
    {
        if (_disposed || _busy || _overlay is not null) return;
        _ = OpenOverlayAsync();
    }

    private async Task OpenOverlayAsync()
    {
        _busy = true;
        var wasVisible = island.IsVisible;
        var previousForeground = NativeMethods.GetForegroundWindow();
        var restoreFocus = false;
        var fallbackFocus = nint.Zero;
        // Capture the anchor before hiding the Island; the toolbar grows from the
        // same neck point used by notifications and the command palette.
        var anchor = island.PaletteAnchorBounds;
        var workArea = island.PaletteWorkArea;
        _preview?.Close();
        CaptureToolbarWindow? toolbar = null;
        try
        {
            screen.RememberForeground(new System.Windows.Interop.WindowInteropHelper(island).Handle);
            fallbackFocus = screen.LastForegroundTarget;
            // Hide our own surfaces before acquiring pixels. The overlay is created from
            // this frozen snapshot, so neither the Island nor the toolbar can appear in it.
            island.SetCaptureHidden(true);
            island.Hide();
            foreach (var pin in _pins) pin.Hide();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(60);

            var bounds = Forms.SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 100_000_000)
                throw new InvalidOperationException("This desktop is too large to capture. Try a smaller region after changing display scaling.");

            BitmapSource? bitmap = null;
            var mode = CaptureOverlay.RegionMode;
            using (var background = await Task.Run(() => ScreenContextService.CaptureBitmap(new NativeMethods.Rect
                   {
                       Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom
                   })))
            {
                if (_disposed) return;

                var windows = CaptureWindowEnumerator.Enumerate(bounds);
                using var overlay = new CaptureOverlay(bounds, background, windows);
                _overlay = overlay;
                CaptureRequest? selection;
                try
                {
                    toolbar = new CaptureToolbarWindow();
                    _toolbar = toolbar;
                    toolbar.ModeRequested += overlay.SetMode;
                    toolbar.ConfirmRequested += overlay.Confirm;
                    toolbar.CancelRequested += overlay.Cancel;
                    overlay.ModeChanged += toolbar.SetMode;
                    overlay.FocusToolbarRequested += toolbar.FocusToolbar;

                    var selectionTask = overlay.SelectAsync();
                    // The owner relationship keeps the WPF toolbar above the topmost
                    // WinForms capture surface even after the overlay is clicked.
                    new System.Windows.Interop.WindowInteropHelper(toolbar).Owner = overlay.Handle;
                    toolbar.PresentFromIsland(anchor, workArea, reduced());
                    selection = await selectionTask;
                }
                finally
                {
                    _overlay = null;
                    if (toolbar is not null)
                    {
                        await toolbar.DismissAsync();
                        toolbar.Close();
                        if (ReferenceEquals(_toolbar, toolbar)) _toolbar = null;
                    }
                    overlay.Hide();
                }
                if (_disposed) return;
                if (selection is not { } request)
                {
                    // Escape, right-click, or closing the overlay: save and copy nothing.
                    restoreFocus = true;
                    return;
                }

                var crop = Drawing.Rectangle.Intersect(request.Region,
                    new Drawing.Rectangle(0, 0, background.Width, background.Height));
                if (crop.Width <= 1 || crop.Height <= 1)
                    throw new InvalidOperationException("That area is too small to capture. Drag a larger region.");

                mode = request.Mode;
                bitmap = await Task.Run(() => CopyPixels(background, crop));
                if (_disposed) return;
            }

            if (bitmap is null || _disposed) return;
            var preview = Preview();
            preview.SetImage(bitmap);
            preview.SetMode(mode);
            var copied = await preview.CopyAsync();
            preview.Present(anchor, workArea, reduced());
            if (copied) preview.ShowCopiedToast();
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                var preview = Preview();
                preview.SetStatus(ex is InvalidOperationException
                    ? ex.Message
                    : "Capture failed. Try again or choose a smaller region.");
                preview.Present(anchor, workArea, reduced());
            }
        }
        finally
        {
            _overlay = null;
            if (toolbar is not null)
            {
                try { toolbar.Close(); } catch { /* already closed with its owner */ }
                if (ReferenceEquals(_toolbar, toolbar)) _toolbar = null;
            }
            if (!_disposed)
            {
                island.SetCaptureHidden(false);
                var preview = _preview;
                if (preview is { IsVisible: true }) preview.BeginSurfaceRestore();
                if (wasVisible) island.Show();
                foreach (var pin in _pins) pin.Show();
                if (preview is { IsVisible: true }) preview.EndSurfaceRestore();
                if (restoreFocus) RestoreForeground(previousForeground, fallbackFocus);
            }
            else island.SetCaptureHidden(false);
            _busy = false;
        }
    }

    private static void RestoreForeground(nint previous, nint fallback)
    {
        var target = NativeMethods.IsWindow(previous) && NativeMethods.IsWindowVisible(previous)
            ? previous
            : fallback;
        if (target != nint.Zero && NativeMethods.IsWindow(target))
            NativeMethods.SetForegroundWindow(target);
    }

    private ScreenshotPreview Preview()
    {
        if (_preview is null)
        {
            _preview = new ScreenshotPreview(Pin);
            _preview.Closed += (_, _) => _preview = null;
        }
        return _preview;
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
    public void Dispose()
    {
        _disposed = true;
        _overlay?.Cancel();
        _toolbar?.Close();
        _preview?.Close();
        foreach (var pin in _pins.ToArray()) pin.Close();
    }
}

internal sealed class ScreenshotPreview : Window
{
    private readonly NotificationMorphHost _morph=new() { Margin=new Thickness(14) };
    private readonly InkCanvas _ink=new() { EditingMode=InkCanvasEditingMode.None };
    private readonly TextBlock _status=new() { Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8) };
    private readonly TextBlock _title=new() { Text="Screenshot",Foreground=Brushes.White,FontSize=17,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8) };
    private BitmapSource? _image;
    private readonly List<Button> _imageButtons=[];
    private Button? _drawButton;
    private bool _copying;
    private bool _suppressDismiss;
    private readonly Action<BitmapSource> _copy;
    private readonly Viewbox _imageView;
    private readonly WrapPanel _actions;
    private readonly System.Windows.Threading.DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(2600) };
    private Rect _presentationArea;
    private Window? _copyToast;
    public ScreenshotPreview(Action<BitmapSource> pin,Action<BitmapSource>? copy=null)
    {
        _copy=copy??CopyLocal;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/DynamicIsland.Windows;component/Views/ScreenshotResources.xaml",UriKind.Relative) });
        Title="Screenshot · Sutra Island";Width=580;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;
        var content=new StackPanel { Margin=new Thickness(16) };
        var header=new DockPanel();var close=Button("Close",()=>Close());close.Content=new TextBlock {Text="\uE711",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=12};close.Width=36;close.MinHeight=36;close.Padding=new Thickness(8);close.Background=Brushes.Transparent;close.BorderBrush=Brushes.Transparent;close.ToolTip="Close screenshot · Esc";DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        header.Children.Add(_title);content.Children.Add(header);
        var view=new Viewbox { Stretch=Stretch.Uniform,MaxHeight=280,MaxWidth=510,Margin=new Thickness(4,12,4,4),Child=_ink,Visibility=Visibility.Collapsed };_imageView=view;content.Children.Add(view);
        _ink.DefaultDrawingAttributes=new DrawingAttributes { Color=Colors.OrangeRed,Width=5,Height=5,FitToCurve=true };
        var actions=new WrapPanel {Visibility=Visibility.Collapsed};_actions=actions;
        void Action(string title,Action action){var button=Button(title,action);button.IsEnabled=false;if(title=="Copy")button.Tag="primary";if(title=="Draw")_drawButton=button;_imageButtons.Add(button);actions.Children.Add(button);}
        Action("Copy",()=>{_ = CopyAsync();});Action("Save PNG",Save);Action("Draw",()=>{_ink.EditingMode=_ink.EditingMode==InkCanvasEditingMode.Ink?InkCanvasEditingMode.None:InkCanvasEditingMode.Ink;if(_drawButton is not null)_drawButton.Tag=_ink.EditingMode==InkCanvasEditingMode.Ink?"active":null;SetStatus(_ink.EditingMode==InkCanvasEditingMode.Ink?"Draw enabled · Copy, Save, or Pin to use your annotations.":"Drawing off.");});
        Action("Undo",()=>{if(_ink.Strokes.Count>0)_ink.Strokes.RemoveAt(_ink.Strokes.Count-1);});Action("Pin",()=>{_suppressDismiss=true;try{pin(RenderImage());}catch(Exception){SetStatus("Could not pin this image. Try a smaller region.");}finally{Dispatcher.BeginInvoke(()=>_suppressDismiss=false,System.Windows.Threading.DispatcherPriority.Background);}});content.Children.Add(actions);
        content.Children.Add(_status);SetStatus("No capture yet.");
        var card=new Border { Background=new SolidColorBrush(Color.FromRgb(25,26,32)),CornerRadius=new CornerRadius(20),Child=content };
        _morph.Children.Add(new ScrollViewer { Content=card,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });Content=_morph;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        _autoClose.Tick+=(_,_)=>Close();
        _toastTimer.Tick+=(_,_)=>CloseCopiedToast();
        Deactivated+=(_,_)=>{if(!_suppressDismiss)Close();};
        Closed+=(_,_)=>{_autoClose.Stop();CloseCopiedToast();_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_ink.Strokes.Clear();_ink.Background=null;_image=null;};
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
    public void SetMode(string mode)
    {
        _title.Text = mode switch
        {
            CaptureOverlay.WindowMode => "Screenshot · Window",
            CaptureOverlay.ScreenMode => "Screenshot · Full screen",
            _ => "Screenshot · Region"
        };
    }
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
    internal void BeginSurfaceRestore() => _suppressDismiss = true;
    internal void EndSurfaceRestore()
    {
        Activate();
        Dispatcher.BeginInvoke(() => _suppressDismiss = false, System.Windows.Threading.DispatcherPriority.Background);
    }
    internal BitmapSource RenderImage()
    {
        if(_image is null)throw new InvalidOperationException("Capture an image first.");
        if(_ink.Strokes.Count==0)return _image;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawImage(_image,new Rect(0,0,_image.PixelWidth,_image.PixelHeight));_ink.Strokes.Draw(dc);}
        var image=new RenderTargetBitmap(_image.PixelWidth,_image.PixelHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return image;
    }
    public async Task<bool> CopyAsync()
    {
        if(_image is null||_copying)return false;_copying=true;
        try { var image=RenderImage();for(var attempt=0;attempt<5;attempt++){try{_copy(image);SetStatus($"Copied · {image.PixelWidth} × {image.PixelHeight} · local only");return true;}catch(System.Runtime.InteropServices.COMException){await Task.Delay(50);}}SetStatus("Clipboard is busy. Your image is still here; choose Copy to retry.");return false; }
        catch(Exception){SetStatus("Could not copy this image. Your preview is still available; try again or save it locally.");return false;}
        finally {_copying=false;}
    }
    internal void ShowCopiedToast()
    {
        CloseCopiedToast();
        var label=new TextBlock { Text="Screenshot copied to clipboard",Foreground=Brushes.White,FontSize=12.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center };
        var icon=new Border { Width=24,Height=24,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromRgb(185,165,253)),Child=new TextBlock { Text="\uE73E",FontFamily=new FontFamily("Segoe Fluent Icons"),Foreground=new SolidColorBrush(Color.FromRgb(35,30,49)),FontSize=14,FontWeight=FontWeights.Bold,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center } };
        var toast=new Window { Owner=this,Title="Screenshot copied",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Focusable=false,IsHitTestVisible=false,SizeToContent=SizeToContent.WidthAndHeight,Topmost=true,Content=new Border { Background=new SolidColorBrush(Color.FromRgb(35,36,43)),BorderBrush=new SolidColorBrush(Color.FromRgb(72,70,83)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(15),Padding=new Thickness(14,10,16,10),Child=new StackPanel { Orientation=System.Windows.Controls.Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Children={icon,new Border { Width=10,Background=Brushes.Transparent },label} } } };
        toast.Loaded+=(_,_)=>
        {
            var left=Left+ActualWidth-toast.ActualWidth-56;
            var top=Top+8;
            // Keep the toast in the preview header beside the title. On narrow
            // cards, use the footer so it does not cover the close button or image.
            if(left<Left+170){left=Left+12;top=Top+ActualHeight-toast.ActualHeight-8;}
            toast.Left=Math.Clamp(left,_presentationArea.Left,Math.Max(_presentationArea.Left,_presentationArea.Right-toast.ActualWidth));
            toast.Top=Math.Clamp(top,_presentationArea.Top,Math.Max(_presentationArea.Top,_presentationArea.Bottom-toast.ActualHeight));
        };
        _copyToast=toast;
        toast.Show();
        _toastTimer.Stop();_toastTimer.Start();
    }
    private void CloseCopiedToast()
    {
        _toastTimer.Stop();
        var toast=_copyToast;_copyToast=null;
        if(toast is not null)toast.Close();
    }
    private async void Save()
    {
        _suppressDismiss=true;_autoClose.Stop();
        try{var dialog=new Microsoft.Win32.SaveFileDialog { Filter="PNG image|*.png",FileName="Sutra screenshot "+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")+".png" };if(dialog.ShowDialog(this)!=true)return;var image=RenderImage();await Task.Run(()=>{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(dialog.FileName);encoder.Save(file);});SetStatus("Saved locally.");}
        catch(Exception){SetStatus("Could not save that file. Choose another location and try again.");}
        finally{_suppressDismiss=false;if(IsVisible)_autoClose.Start();}
    }
    public void Present(Rect anchor,Rect area,bool reduced)
    {
        _presentationArea=area;
        Width=Math.Min(580,Math.Max(280,area.Width-16));MaxHeight=Math.Max(240,area.Height-16);
        Left=Math.Clamp(anchor.Left+anchor.Width/2-Width/2,area.Left,Math.Max(area.Left,area.Right-Width));Top=area.Top;
        _morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_morph.RevealProgress=reduced?1:0;
        Show();UpdateLayout();Top=Math.Clamp(anchor.Bottom-2,area.Top,Math.Max(area.Top,area.Bottom-ActualHeight));
        _autoClose.Stop();_autoClose.Start();
        if(!reduced){var animation=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(380));animation.Completed+=(_,_)=>{_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,null);_morph.RevealProgress=1;};_morph.BeginAnimation(NotificationMorphHost.RevealProgressProperty,animation);}
    }
}
