using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace DynamicIsland.Windows.Views;

// The content keeps its final measured size. Only the surrounding material and
// rounded reveal mask grow, so notification text never stretches during entry.
public sealed class NotificationMorphHost : Grid
{
    public bool ReleaseClipWhenSettled { get; set; }
    public bool BlendSurfaceWithContent { get; set; }
    public double ContentSurfaceOpacity { get; set; } = 1;
    public static readonly DependencyProperty RevealProgressProperty = DependencyProperty.Register(
        nameof(RevealProgress), typeof(double), typeof(NotificationMorphHost),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((NotificationMorphHost)owner).UpdateReveal()));

    private readonly RectangleGeometry _contentClip = new();
    private readonly TranslateTransform _contentOffset = new();
    private static readonly Brush Surface = FrozenBrush(Color.FromArgb(255, 29, 31, 37));
    private static readonly Pen Edge = new(FrozenBrush(Color.FromArgb(22, 255, 255, 255)), 1);

    public double RevealProgress
    {
        get => (double)GetValue(RevealProgressProperty);
        set => SetValue(RevealProgressProperty, value);
    }

    public NotificationMorphHost()
    {
        ClipToBounds = false;
        SizeChanged += (_, _) => UpdateReveal();
        Loaded += (_, _) => UpdateReveal();
    }

    internal Rect CurrentSurfaceBounds => Frame().Bounds;
    internal bool HasConnection => Frame().Neck > 0.001;

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static double Ease(double value) => 1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);
    private static double Mix(double start, double end, double amount) => start + (end - start) * amount;

    private (Rect Bounds, double Neck, double ContentOpacity) Frame()
    {
        var progress = Math.Clamp(RevealProgress, 0, 1);
        var seed = Ease(progress / (90d / 380d));
        var growth = Ease((progress - 90d / 380d) / (140d / 380d));
        var settle = Ease((progress - 230d / 380d) / (150d / 380d));
        var width = Mix(Math.Min(ActualWidth, Mix(32, 46, seed)), ActualWidth, growth);
        var height = Mix(Math.Min(ActualHeight, Mix(12, 22, seed)), ActualHeight, growth);
        var top = Mix(-20, -2, growth) + 2 * settle;
        return (new Rect((ActualWidth - width) / 2, top, width, height),
            1 - Ease((progress - 230d / 380d) / (110d / 380d)),
            Ease((progress - 150d / 380d) / (170d / 380d)));
    }

    private void UpdateReveal()
    {
        if (Children.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var frame = Frame();
        var child = Children[0];
        var radius = Math.Min(18, Math.Min(frame.Bounds.Width, frame.Bounds.Height) / 2);
        _contentClip.Rect = new Rect(frame.Bounds.X, 0, frame.Bounds.Width, frame.Bounds.Height);
        _contentClip.RadiusX = _contentClip.RadiusY = radius;
        _contentOffset.Y = frame.Bounds.Y;
        // A reusable palette can grow after device discovery or a command switch.
        // Its settled content must not retain the previous animation's reveal mask.
        child.Clip = ReleaseClipWhenSettled && RevealProgress >= 1 ? null : _contentClip;
        child.RenderTransform = _contentOffset;
        child.Opacity = frame.ContentOpacity;
        var settled = RevealProgress >= 1;
        IsHitTestVisible = settled;
        UseLayoutRounding = SnapsToDevicePixels = settled;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var frame = Frame();
        var radius = Math.Min(18, Math.Min(frame.Bounds.Width, frame.Bounds.Height) / 2);
        Geometry shape = new RectangleGeometry(frame.Bounds, radius, radius);
        if (frame.Neck > 0.001)
        {
            // The banner's settled top is 12 DIP below the shell. Start the neck
            // four DIP inside the shell; its higher Z order hides the joining seam.
            var center = ActualWidth / 2;
            var shoulder = Math.Min(34, frame.Bounds.Width / 2) * frame.Neck;
            var waist = Math.Min(15, shoulder) * frame.Neck;
            var bottom = frame.Bounds.Y + Math.Min(10, frame.Bounds.Height / 2);
            var bridge = new StreamGeometry();
            using (var path = bridge.Open())
            {
                path.BeginFigure(new Point(center - shoulder, -16), true, true);
                path.BezierTo(new Point(center - waist, -16), new Point(center - waist, bottom),
                    new Point(center - shoulder, bottom), true, false);
                path.LineTo(new Point(center + shoulder, bottom), true, false);
                path.BezierTo(new Point(center + waist, bottom), new Point(center + waist, -16),
                    new Point(center + shoulder, -16), true, false);
            }
            shape = Geometry.Combine(shape, bridge, GeometryCombineMode.Union, null);
        }
        if (BlendSurfaceWithContent)
        {
            // Keep a single material layer as the palette content emerges; at rest its
            // own translucent background supplies the surface, avoiding an opaque stack.
            var targetAlpha=Math.Clamp(ContentSurfaceOpacity,0,1);
            var childAlpha=targetAlpha*frame.ContentOpacity;
            var remainingAlpha=childAlpha>=1 ? 0 : (targetAlpha-childAlpha)/(1-childAlpha);
            drawingContext.PushOpacity(Math.Clamp(remainingAlpha,0,1));
        }
        drawingContext.DrawGeometry(Surface, Edge, shape);
        if (BlendSurfaceWithContent) drawingContext.Pop();
    }
}
