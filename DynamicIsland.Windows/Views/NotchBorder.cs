using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Windows.Views;

/// <summary>A shared shell and preview mask that morphs into the MacBook notch silhouette.</summary>
public sealed class NotchBorder : Border
{
    public static readonly DependencyProperty IsNotchProperty = DependencyProperty.Register(nameof(IsNotch), typeof(bool), typeof(NotchBorder), new PropertyMetadata(false, ModeChanged));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(NotchBorder), new PropertyMetadata(false));
    public static readonly DependencyProperty NotchProgressProperty = DependencyProperty.Register(nameof(NotchProgress), typeof(double), typeof(NotchBorder), new PropertyMetadata(0d, ShapeChanged));
    private static readonly DependencyProperty RevealProgressProperty = DependencyProperty.Register(
        "RevealProgress", typeof(double), typeof(NotchBorder),
        new PropertyMetadata(0d, ShapeChanged));

    private readonly RectangleGeometry _roundReveal = new();
    private Geometry? _shape;
    private Size _revealFrom;
    private Size _revealTo;
    private bool _revealing;
    private readonly GeometryDrawing _shellDrawing = new();

    internal Size VisibleSize { get; private set; }
    internal event EventHandler? VisibleSizeChanged;

    internal void BeginReveal(Size from, Size to, TimeSpan duration, IEasingFunction ease, Action completed)
    {
        BeginAnimation(RevealProgressProperty, null);
        SetValue(RevealProgressProperty, 0d);
        _revealFrom = from;
        _revealTo = to;
        _revealing = true;
        RefreshDrawingStyle();
        InvalidateVisual();
        UpdateShape();
        var animation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = ease };
        animation.Completed += (_, _) => completed();
        BeginAnimation(RevealProgressProperty, animation);
    }

    internal void EndReveal()
    {
        if (!_revealing) return;
        _revealing = false;
        InvalidateVisual();
        BeginAnimation(RevealProgressProperty, null);
        UpdateShape();
    }
    public bool IsNotch { get => (bool)GetValue(IsNotchProperty); set => SetValue(IsNotchProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public double NotchProgress { get => (double)GetValue(NotchProgressProperty); set => SetValue(NotchProgressProperty, value); }

    public NotchBorder() => SizeChanged += (_, _) => UpdateShape();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty) UpdateShape();
        if (e.Property == BackgroundProperty || e.Property == BorderBrushProperty || e.Property == BorderThicknessProperty)
            RefreshDrawingStyle();
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
    private static void ShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var shell = (NotchBorder)d;
        if (e.Property == NotchProgressProperty && ((double)e.OldValue <= 0) != ((double)e.NewValue <= 0))
        {
            shell.RefreshDrawingStyle();
            shell.InvalidateVisual();
        }
        shell.UpdateShape();
    }

    private void RefreshDrawingStyle()
    {
        _shellDrawing.Brush = Background;
        _shellDrawing.Pen = _revealing && NotchProgress <= 0 && BorderBrush is { } brush
            ? new Pen(brush, BorderThickness.Left * 2)
            : null;
    }
    private void UpdateShape()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = new Size(ActualWidth, ActualHeight);
        if (_revealing)
        {
            var progress = (double)GetValue(RevealProgressProperty);
            size = new Size(
                _revealFrom.Width + (_revealTo.Width - _revealFrom.Width) * progress,
                _revealFrom.Height + (_revealTo.Height - _revealFrom.Height) * progress);
        }

        var offset = (ActualWidth - size.Width) / 2;
        if (NotchProgress <= 0)
        {
            var radius = Math.Clamp(CornerRadius.TopLeft, 0, Math.Min(size.Width, size.Height) / 2);
            _roundReveal.Rect = new Rect(offset, 0, size.Width, size.Height);
            _roundReveal.RadiusX = _roundReveal.RadiusY = radius;
            _shape = _roundReveal;
        }
        else
        {
            _shape = CreateShape(size.Width, size.Height, CornerRadius.TopLeft, NotchProgress, offset);
        }
        Clip = _shape;
        _shellDrawing.Geometry = _shape;
        if (VisibleSize == size) return;
        VisibleSize = size;
        VisibleSizeChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (!_revealing && NotchProgress <= 0) { base.OnRender(dc); return; }
        dc.DrawDrawing(_shellDrawing);
    }
    internal static Geometry CreateShape(double width, double height, double cornerRadius, double progress, double offset = 0)
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
        if (offset != 0) shape.Transform = new TranslateTransform(offset, 0);
        shape.Freeze();
        return shape;
    }
}
