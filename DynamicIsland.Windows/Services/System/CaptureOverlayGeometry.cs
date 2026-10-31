using System.Drawing;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Pure geometry for the capture overlay. Kept free of WinForms/WPF types so the
/// multi-monitor and mixed-DPI rules can be verified without a desktop session.
/// All coordinates are physical screen pixels.
/// </summary>
internal static class CaptureOverlayGeometry
{
    public static Rectangle NormalizeSelection(Point start, Point end, Rectangle client)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom), client);
    }

    /// <summary>Clips a virtual-screen rectangle and converts it to bitmap client coordinates.</summary>
    public static Rectangle ScreenToClient(Rectangle screenRect, Rectangle virtualScreen)
    {
        var clipped = Rectangle.Intersect(screenRect, virtualScreen);
        if (clipped.Width <= 1 || clipped.Height <= 1) return Rectangle.Empty;
        return new Rectangle(clipped.X - virtualScreen.X, clipped.Y - virtualScreen.Y, clipped.Width, clipped.Height);
    }

    public static Point PlaceToolbar(Rectangle monitor, Size toolbar, int margin)
    {
        margin = Math.Max(0, margin);
        var leftLimit = monitor.Left + margin;
        var rightLimit = monitor.Right - toolbar.Width - margin;
        var x = rightLimit >= leftLimit
            ? Math.Clamp(monitor.Left + (monitor.Width - toolbar.Width) / 2, leftLimit, rightLimit)
            : monitor.Left + Math.Max(0, (monitor.Width - toolbar.Width) / 2);

        var topLimit = monitor.Top + margin;
        var bottomLimit = monitor.Bottom - toolbar.Height - margin;
        var y = bottomLimit >= topLimit
            ? topLimit
            : monitor.Top + Math.Max(0, (monitor.Height - toolbar.Height) / 2);

        return new Point(x, y);
    }

    /// <summary>Converts a 96-DPI design measurement to physical pixels for the supplied monitor DPI.</summary>
    public static int ScaleForDpi(int value, uint dpi)
        => Math.Max(1, (int)Math.Round(value * Math.Max(96u, dpi) / 96d));

    /// <summary>Returns the first (topmost) rectangle that contains the point, or -1.</summary>
    public static int HitTest(IReadOnlyList<Rectangle> orderedRects, Point point)
    {
        for (var index = 0; index < orderedRects.Count; index++)
        {
            if (orderedRects[index].Contains(point)) return index;
        }
        return -1;
    }
}
