using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DynamicIsland.Windows.Infrastructure;

internal static class FrostedMaterial
{
    private const int CacheLimit = 128;
    private static readonly Dictionary<(Color Color, double Inset), Brush> CapsCache = new();
    private static readonly Queue<(Color Color, double Inset)> CacheOrder = new();
    // Acrylic can ignore an HWND's rounded region. Only its central rectangle is
    // used; opaque WPF end caps cover the rounded parts, including antialiased edges.
    public static Brush WithOpaqueCaps(Brush tint,double inset)
    {
        inset=Math.Clamp(inset,0,.49);
        if(inset<=0)return tint;
        // The island uses two immutable solid tints and quantized insets. Reuse
        // identical frozen materials across bindings and repeated morphs, bounded
        // independently of the number of expansions or custom theme changes.
        if (tint is SolidColorBrush { IsFrozen: true, Opacity: 1 } solid &&
            solid.Transform.Value.IsIdentity && solid.RelativeTransform.Value.IsIdentity)
        {
            var key = (solid.Color, inset);
            lock (CapsCache)
            {
                if (CapsCache.TryGetValue(key, out var cached)) return cached;
                var result = CreateOpaqueCaps(tint, inset);
                if (CapsCache.Count == CacheLimit) CapsCache.Remove(CacheOrder.Dequeue());
                CapsCache.Add(key, result);
                CacheOrder.Enqueue(key);
                return result;
            }
        }
        return CreateOpaqueCaps(tint, inset);
    }
    private static Brush CreateOpaqueCaps(Brush tint, double inset)
    {
        var opaque=tint.CloneCurrentValue();
        if(opaque is SolidColorBrush solid){var color=solid.Color;color.A=255;solid.Color=color;}
        else if(opaque is GradientBrush gradient)foreach(var stop in gradient.GradientStops){var color=stop.Color;color.A=255;stop.Color=color;}
        var drawing=new DrawingGroup();
        var rectangle=new RectangleGeometry(new Rect(0,0,1,1));
        drawing.Children.Add(new GeometryDrawing(tint,null,rectangle));
        var fade=Math.Min(.04,(.5-inset)/2);
        var mask=new LinearGradientBrush {StartPoint=new System.Windows.Point(0,0),EndPoint=new System.Windows.Point(1,0)};
        mask.GradientStops.Add(new GradientStop(Colors.White,0));
        mask.GradientStops.Add(new GradientStop(Colors.White,inset));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent,inset+fade));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent,1-inset-fade));
        mask.GradientStops.Add(new GradientStop(Colors.White,1-inset));
        mask.GradientStops.Add(new GradientStop(Colors.White,1));
        var caps=new DrawingGroup {OpacityMask=mask};
        caps.Children.Add(new GeometryDrawing(opaque,null,rectangle));
        drawing.Children.Add(caps);
        var brush=new DrawingBrush(drawing){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,1,1),Stretch=Stretch.Fill};brush.Freeze();return brush;
    }
}
