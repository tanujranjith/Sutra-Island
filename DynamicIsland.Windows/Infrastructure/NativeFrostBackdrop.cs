using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Windows.Interop;

namespace DynamicIsland.Windows.Infrastructure;

// Blur belongs to a separate, non-layered HWND. Applying acrylic directly to a WPF
// per-pixel window paints rectangular corner artifacts and can leave text unblurred.
internal sealed class NativeFrostBackdrop : IDisposable
{
    private readonly Window _owner;
    private readonly Func<(FrameworkElement Element,Rect Bounds,double Radius)> _surface;
    private readonly Action<bool,double> _setActive;
    private readonly Func<bool> _captureAllowed;
    private HwndSource? _source;
    private Rect _last;
    private double _lastRadius,_lastScale,_materialInset;
    private bool _lastCapture;
    private System.ComponentModel.DependencyPropertyDescriptor? _progressDescriptor;
    private DependencyObject? _progressSource;
    private bool _active,_disposed,_updating,_unavailable,_suspended,_settingsSuppressed;
    private NativeFrostBackdrop(Window owner,Func<(FrameworkElement,Rect,double)> surface,Action<bool,double> active,Func<bool> captureAllowed)
    {
        _owner=owner;_surface=surface;_setActive=active;_captureAllowed=captureAllowed;
        owner.IsVisibleChanged+=VisibilityChanged;owner.LocationChanged+=Changed;owner.SizeChanged+=Changed;
        owner.LayoutUpdated+=Changed;owner.Activated+=Activated;owner.Closed+=Closed;
        VisibilityChanged(owner,default);
    }
    public static NativeFrostBackdrop Attach(Window owner,Func<(FrameworkElement,Rect,double)> surface,Action<bool,double> active,Func<bool>? captureAllowed=null)
        => new NativeFrostBackdrop(owner,surface,active,captureAllowed??(()=>false));
    public void SetSuspended(bool suspended) { if(_suspended==suspended)return; _suspended=suspended; if(suspended)Hide(); else Update(); }
    public void SetSettingsSuppressed(bool suppressed) { if(_settingsSuppressed==suppressed)return; _settingsSuppressed=suppressed; if(suppressed)Hide(); else Update(); }
    private void VisibilityChanged(object? sender,DependencyPropertyChangedEventArgs e)
    {
        Update();
    }
    private void Changed(object? sender,EventArgs e)=>Update();
    private void Activated(object? sender,EventArgs e){_last=Rect.Empty;Update();}
    private void Closed(object? sender,EventArgs e)=>Dispose();
    private void Update() { if(_updating)return;_updating=true;try{UpdateCore();}finally{_updating=false;} }
    private void UpdateCore()
    {
        if(_disposed || _unavailable) return;
        if(_suspended || _settingsSuppressed){Hide();return;}
        if((AppDataPaths.IsPreview && Environment.GetEnvironmentVariable("ISLAND_VERIFY_BACKDROP")!="1") || !_owner.IsVisible || _owner.Opacity<=0)
        {Hide();return;}
        var (element,bounds,radius)=_surface();
        if(_progressSource is null && element is Views.NotificationMorphHost morph)
        {
            _progressSource=morph;_progressDescriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Views.NotificationMorphHost.RevealProgressProperty,typeof(Views.NotificationMorphHost));
            _progressDescriptor?.AddValueChanged(morph,Changed);
        }
        if(!element.IsVisible || element.Opacity<=0 || bounds.Width<1 || bounds.Height<1){Hide();return;}
        var ownerHandle=new WindowInteropHelper(_owner).Handle;if(ownerHandle==nint.Zero)return;
        try
        {
            var topLeft=element.PointToScreen(bounds.TopLeft);var bottomRight=element.PointToScreen(bounds.BottomRight);
            var panel=new Rect(topLeft,bottomRight);
            var scale=VisualTreeHelper.GetDpi(element).DpiScaleX;
            var cap=Math.Min((radius+2)*scale,Math.Max(0,panel.Width/2-1));
            var fraction=Math.Min(.49, Math.Ceiling(cap/panel.Width*128)/128);
            var rectangle=new Rect(panel.X+cap,panel.Y,panel.Width-2*cap,panel.Height);
            if(_source is null)
            {
                var parameters=new HwndSourceParameters("Sutra frosted backdrop") {WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x080000A0,Width=1,Height=1,PositionX=-32000,PositionY=-32000};
                _source=new HwndSource(parameters);_source.CompositionTarget.BackgroundColor=Colors.Transparent;
                _source.RootVisual=new DrawingVisual();_source.AddHook(Hook);
                var margins=new Margins {Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(_source.Handle,ref margins);
                if(!NativeMethods.SetAcrylic(_source.Handle,true,true)){_unavailable=true;DisposeSource();return;}
            }
            var capture=_captureAllowed();
            if(rectangle==_last && radius==_lastRadius && scale==_lastScale && capture==_lastCapture && _active)return;
            if (!_active || capture!=_lastCapture)
                NativeMethods.SetWindowDisplayAffinity(_source.Handle,capture?NativeMethods.WdaNone:NativeMethods.WdaExcludeFromCapture);
            _last=rectangle;_lastRadius=radius;_lastScale=scale;_lastCapture=capture;
            int width=(int)Math.Ceiling(rectangle.Width),height=(int)Math.Ceiling(rectangle.Height);
            // Insert directly below the foreground panel, never activate or consume input.
            if(!NativeMethods.SetWindowPos(_source.Handle,ownerHandle,(int)Math.Round(rectangle.X),(int)Math.Round(rectangle.Y),width,height,0x0010|0x0040)){_unavailable=true;DisposeSource();return;}
            if(!_active || Math.Abs(_materialInset-fraction)>.00001){_active=true;_materialInset=fraction;_setActive(true,fraction);}
        }
        catch(ExternalException){_unavailable=true;DisposeSource();}
        catch(InvalidOperationException){Hide();}
    }
    private static nint Hook(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {if(message==0x0084){handled=true;return -1;}return 0;}
    private void Hide(){if(_source is not null)ShowWindow(_source.Handle,0);_last=Rect.Empty;if(_active){_active=false;_setActive(false,0);}}
    private void DisposeSource(){Hide();_source?.Dispose();_source=null;}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;DisposeSource();
        if(_progressSource is not null)_progressDescriptor?.RemoveValueChanged(_progressSource,Changed);
        _owner.IsVisibleChanged-=VisibilityChanged;_owner.LocationChanged-=Changed;_owner.SizeChanged-=Changed;
        _owner.LayoutUpdated-=Changed;_owner.Activated-=Activated;_owner.Closed-=Closed;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")]private static extern int DwmExtendFrameIntoClientArea(nint hwnd,ref Margins margins);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint region);
}
