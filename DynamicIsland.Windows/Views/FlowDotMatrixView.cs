using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using DynamicIsland.Windows.Services.Flow;

namespace DynamicIsland.Windows.Views;

/// <summary>
/// Sutra Flow's dot-matrix display, drawn inside the island while dictating.
/// Listening: columns of lit dots ripple out from the center with your voice.
/// Released: the columns flatten into one row. Polishing: a diagonal shimmer sweeps
/// across. Pasted: the dots draw a green check. Error: a red X.
/// Only renders frames while visible, so it costs nothing when idle.
/// </summary>
public sealed class FlowDotMatrixView : FrameworkElement
{
    private const int Rows = 5;
    private static readonly Color Unlit = Color.FromRgb(0x30, 0x31, 0x38);
    private static readonly Color White = Color.FromRgb(0xF5, 0xF5, 0xFA);
    private static readonly Color Green = Color.FromRgb(0x78, 0xE1, 0xA0);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x7A, 0x7A);
    private static readonly Color Lavender = Color.FromRgb(0xB9, 0xA5, 0xFD);

    private FlowController? _controller;
    private FlowState _state = FlowState.Idle;
    private DateTime _stateAt = DateTime.UtcNow;
    private DateTime _shownAt = DateTime.UtcNow;
    private bool _rendering;
    private readonly float[] _history = new float[64];
    private int _historyHead;
    private float[] _seeds = [];
    private readonly Dictionary<Color, SolidColorBrush> _brushes = [];

    public bool ReducedMotion { get; set; }

    public void Attach(FlowController controller) => _controller = controller;

    /// <summary>Shows a state. Called by the island window on the UI thread.</summary>
    public void SetState(FlowState state)
    {
        if (state == _state) return;
        if (_state == FlowState.Idle) { _shownAt = DateTime.UtcNow; Array.Clear(_history); }
        _state = state;
        _stateAt = DateTime.UtcNow;
        UpdateRendering();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        IsVisibleChanged -= OnVisibleChanged;
        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateRendering();

    private void UpdateRendering()
    {
        var want = IsVisible && _state != FlowState.Idle;
        if (want == _rendering) { InvalidateVisual(); return; }
        _rendering = want;
        if (want) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
        InvalidateVisual();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_controller is not null && _state == FlowState.Listening)
        {
            _history[_historyHead] = _controller.Level;
            _historyHead = (_historyHead + 1) % _history.Length;
        }
        InvalidateVisual();
    }

    private float HistoryAt(int age)
    {
        var i = (_historyHead - 1 - age) % _history.Length;
        if (i < 0) i += _history.Length;
        return _history[i];
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 40 || h < 20) return;

        // Grid geometry: keep dots round and evenly spaced, centered in the island.
        var gridH = Math.Min(h - 20, 40);
        var pitchY = gridH / (Rows - 1);
        var pitchX = Math.Max(7.0, pitchY * 0.92);
        var cols = Math.Max(9, (int)((w - 44) / pitchX) + 1);
        if (cols % 2 == 0) cols--;
        var gridW = (cols - 1) * pitchX;
        var x0 = (w - gridW) / 2;
        var y0 = (h - gridH) / 2;
        var radius = Math.Min(pitchX, pitchY) * 0.23;
        if (_seeds.Length != cols * Rows)
        {
            var rng = new Random(7);
            _seeds = Enumerable.Range(0, cols * Rows).Select(_ => (float)rng.NextDouble()).ToArray();
        }

        var now = DateTime.UtcNow;
        var sinceShown = (now - _shownAt).TotalSeconds;
        var since = (now - _stateAt).TotalSeconds;
        var center = (cols - 1) / 2.0;
        var midRow = (Rows - 1) / 2.0;

        for (var c = 0; c < cols; c++)
        {
            var distFromCenter = Math.Abs(c - center);
            // Listening: each column shows the voice level from a moment ago, so sound ripples outward.
            var age = (int)Math.Round(distFromCenter * 1.6);
            var envelope = 1 - Math.Pow(distFromCenter / (center + 1), 2) * 0.55;
            var level = HistoryAt(age) * envelope;

            for (var r = 0; r < Rows; r++)
            {
                var seed = _seeds[c * Rows + r];
                // Dissolve in: dots appear in a scattered order over the first ~300 ms.
                var appear = ReducedMotion ? 1 : Smooth((sinceShown - seed * 0.22) / 0.12);
                if (appear <= 0) continue;

                var on = 0.0;
                var color = White;
                var grow = 0.0;
                var rowDist = Math.Abs(r - midRow);
                switch (_state)
                {
                    case FlowState.Listening:
                    {
                        var height = Math.Max(0.6, level * Rows * 1.15);
                        on = Math.Clamp(height / 2 + 0.5 - rowDist, 0, 1);
                        break;
                    }
                    case FlowState.Released:
                    {
                        var p = Smooth(since / 0.24);
                        var height = Math.Max(0.6, level * Rows * (1 - p)) * (1 - p) + p;
                        on = Math.Clamp(height / 2 + 0.5 - rowDist, 0, 1);
                        if (r == (int)midRow) on = Math.Max(on, 0.85 * p);
                        break;
                    }
                    case FlowState.Polishing:
                    {
                        var period = 1.15;
                        var phase = ReducedMotion ? 0.5 : (since % period) / period;
                        var pos = -4 + phase * (cols + 8);
                        var band = c + (r - midRow) * 1.4;
                        var shimmer = Math.Exp(-Math.Pow(band - pos, 2) / 5.0);
                        var baseline = r == (int)midRow ? 0.85 * (1 - Smooth(since / 0.35)) : 0;
                        on = Math.Max(baseline, shimmer);
                        grow = shimmer * 0.25;
                        break;
                    }
                    case FlowState.Pasted:
                    {
                        var index = CheckIndex(c, r, center);
                        if (index >= 0) { on = ReducedMotion ? 1 : Smooth((since - index * 0.045) / 0.12); color = Green; grow = 0.15 * on; }
                        break;
                    }
                    case FlowState.Error:
                    {
                        if (IsCross(c, r, center)) { on = ReducedMotion ? 1 : Smooth(since / 0.18); color = Red; grow = 0.1 * on; }
                        break;
                    }
                }

                on = Math.Round(on * 20) / 20;
                appear = Math.Round(appear * 10) / 10;
                var fill = Mix(Unlit, color, on);
                // Fade from the island background on the way in.
                fill = Color.FromArgb((byte)(255 * appear), fill.R, fill.G, fill.B);
                var rad = radius * (1 + grow);
                dc.DrawEllipse(Brush(fill), null, new Point(x0 + c * pitchX, y0 + r * pitchY), rad, rad);
            }
        }
    }

    // A check drawn on the 5-row grid: down-right stroke then a longer up-right stroke.
    private static int CheckIndex(int c, int r, double center)
    {
        var cx = (int)Math.Round(center);
        (int dc, int row)[] path = [(-3, 2), (-2, 3), (-1, 4), (0, 3), (1, 2), (2, 1), (3, 0)];
        for (var i = 0; i < path.Length; i++)
            if (c == cx + path[i].dc && r == path[i].row) return i;
        return -1;
    }

    private static bool IsCross(int c, int r, double center)
    {
        var dc = c - (int)Math.Round(center);
        return Math.Abs(dc) <= 2 && (dc == r - 2 || dc == 2 - r);
    }

    private SolidColorBrush Brush(Color color)
    {
        if (_brushes.TryGetValue(color, out var brush)) return brush;
        if (_brushes.Count > 512) _brushes.Clear();
        brush = new SolidColorBrush(color);
        brush.Freeze();
        _brushes[color] = brush;
        return brush;
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The lavender used for the island rim while Flow is active.</summary>
    public static Color RimColor => Lavender;
}
