using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace DynamicIsland.Windows.Infrastructure;

internal static class FrostedMaterial
{
    // Acrylic can ignore an HWND's rounded region. Only its central rectangle is
    // used; opaque WPF end caps cover the rounded parts, including antialiased edges.
    public static Brush WithOpaqueCaps(Brush tint,double inset)
    {
        inset=Math.Clamp(inset,0,.49);
        if(inset<=0)return tint;
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
