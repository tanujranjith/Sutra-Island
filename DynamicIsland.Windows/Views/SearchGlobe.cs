using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace DynamicIsland.Windows.Views;

/// <summary>The single search glyph: slow meridians and a soft orbit, with no mascot badge.</summary>
public sealed class SearchGlobe : FrameworkElement
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(SearchGlobe), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(SearchGlobe), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register("Phase", typeof(double), typeof(SearchGlobe), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    internal bool IsAnimating { get; private set; }
    public SearchGlobe()
    {
        Width = Height = 26;
        Loaded += (_, _) => Update(); Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Update();
        System.Windows.Automation.AutomationProperties.SetName(this, "Web search");
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SearchGlobe)d).Update();
    private void Stop() { BeginAnimation(PhaseProperty, null); IsAnimating = false; }
    private void Update()
    {
        if (!Active || ReducedMotion || !IsLoaded || !IsVisible) { Stop(); return; }
        if (IsAnimating) return;
        IsAnimating = true;
        BeginAnimation(PhaseProperty, new DoubleAnimation(0, Math.PI * 2, TimeSpan.FromSeconds(4.2)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var k = Math.Min(ActualWidth, ActualHeight) / 26d;
        dc.PushTransform(new ScaleTransform(k, k));
        var c = new Point(13, 13); var phase = (double)GetValue(PhaseProperty);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(191, 167, 255)), 1.05);
        dc.DrawEllipse(null, pen, c, 8.7, 8.7);
        dc.DrawLine(pen, new Point(4.3, 13), new Point(21.7, 13));
        foreach (var y in new[] { 9.2, 16.8 }) dc.DrawEllipse(null, pen, new Point(13, y), 7.9, 1.5);
        for (var i = 0; i < 3; i++) dc.DrawEllipse(null, pen, c, Math.Max(.15, Math.Abs(Math.Sin(phase + i * Math.PI / 3)) * 8.7), 8.7);
        if (Active && !ReducedMotion)
        {
            for (var i = 12; i >= 0; i--)
            {
                var a = phase * 2 - i * .045; var opacity = (13 - i) / 13d;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(opacity * 220), 102, 220, 233)), null,
                    new Point(13 + 11.5 * Math.Cos(a), 13 + 11.5 * Math.Sin(a)), .5 + opacity * .6, .5 + opacity * .6);
            }
        }
        dc.Pop();
    }
}
