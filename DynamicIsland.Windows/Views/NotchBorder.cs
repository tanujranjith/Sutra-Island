using System.Windows;
using Point = System.Windows.Point;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Windows.Views;

/// <summary>A shared shell and preview mask that morphs into the MacBook notch silhouette.</summary>
public sealed class NotchBorder : Border
{
    public static readonly DependencyProperty IsNotchProperty = DependencyProperty.Register(nameof(IsNotch), typeof(bool), typeof(NotchBorder), new PropertyMetadata(false, ModeChanged));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(NotchBorder), new PropertyMetadata(false));
    public static readonly DependencyProperty NotchProgressProperty = DependencyProperty.Register(nameof(NotchProgress), typeof(double), typeof(NotchBorder), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, ShapeChanged));
    private Geometry? _shape;
    public bool IsNotch { get => (bool)GetValue(IsNotchProperty); set => SetValue(IsNotchProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public double NotchProgress { get => (double)GetValue(NotchProgressProperty); set => SetValue(NotchProgressProperty, value); }

    public NotchBorder() => SizeChanged += (_, _) => UpdateShape();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty) UpdateShape();
    }
    private static void ModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var shell = (NotchBorder)d;
        var target = (bool)e.NewValue ? 1d : 0d;
        if (!shell.IsLoaded || shell.ReducedMotion)
        {
            shell.BeginAnimation(NotchProgressProperty, null);
            shell.NotchProgress = target;
        }
        else shell.BeginAnimation(NotchProgressProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
    private static void ShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NotchBorder)d).UpdateShape();
    private void UpdateShape()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _shape = CreateShape(ActualWidth, ActualHeight, CornerRadius.TopLeft, NotchProgress);
        Clip = _shape;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (NotchProgress <= 0) { base.OnRender(dc); return; }
        if (_shape is not null) dc.DrawGeometry(Background, null, _shape);
    }
    internal static Geometry CreateShape(double width, double height, double cornerRadius, double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var r = Math.Clamp(cornerRadius, 0, Math.Min(width, height) / 2);
        var shoulder = Math.Min(10, Math.Min(width / 8, height / 4));
        var inset = shoulder * p;
        var topRadius = r * (1 - p);
        var bottomRadius = r * (1 - p) + Math.Min(12, height / 4) * p;
        var topEndY = topRadius + inset;
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(topRadius, 0), true, true);
            g.LineTo(new Point(width - topRadius, 0), true, false);
            g.BezierTo(new Point(width - inset, 0), new Point(width - inset, topEndY * .448), new Point(width - inset, topEndY), true, false);
            g.LineTo(new Point(width - inset, height - bottomRadius), true, false);
            g.BezierTo(new Point(width - inset, height - bottomRadius * .448), new Point(width - inset - bottomRadius * .448, height), new Point(width - inset - bottomRadius, height), true, false);
            g.LineTo(new Point(inset + bottomRadius, height), true, false);
            g.BezierTo(new Point(inset + bottomRadius * .448, height), new Point(inset, height - bottomRadius * .448), new Point(inset, height - bottomRadius), true, false);
            g.LineTo(new Point(inset, topEndY), true, false);
            g.BezierTo(new Point(inset, topEndY * .448), new Point(inset, 0), new Point(topRadius, 0), true, false);
        }
        shape.Freeze();
        return shape;
    }
}
