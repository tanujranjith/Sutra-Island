using System.Drawing.Drawing2D;
using System.Text;
using DynamicIsland.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DynamicIsland.Windows.Services;

internal readonly record struct CaptureWindow(nint Handle, string Title, Drawing.Rectangle Bounds);

internal readonly record struct CaptureRequest(string Mode, Drawing.Rectangle Region);

/// <summary>
/// Full-desktop capture surface. The desktop is frozen before this window is shown,
/// then the overlay keeps the pointer interaction until a region or window is chosen.
/// Mode controls live in a separate owner window so they can use the Island's WPF
/// material while never appearing in the captured pixels.
/// </summary>
internal sealed class CaptureOverlay : Forms.Form
{
    internal const string RegionMode = "region";
    internal const string WindowMode = "window";
    internal const string ScreenMode = "screen";

    private readonly Drawing.Rectangle _bounds;
    private readonly Drawing.Rectangle _monitor;
    private readonly IReadOnlyList<CaptureWindow> _windows;
    private readonly uint _dpi;
    private readonly Drawing.Font _hintFont;
    private readonly Drawing.Font _labelFont;
    private readonly TaskCompletionSource<CaptureRequest?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Drawing.Point? _start;
    private Drawing.Rectangle _selection;
    private Drawing.Rectangle _hover;
    private string _hoverTitle = "";
    private string _mode = RegionMode;
    private bool _finished;

    /// <summary>Raised when this surface changes mode, including keyboard shortcuts.</summary>
    public event Action<string>? ModeChanged;

    /// <summary>Raised when Tab asks to move keyboard focus into the floating toolbar.</summary>
    public event Action? FocusToolbarRequested;

    public CaptureOverlay(Drawing.Rectangle bounds, Drawing.Bitmap background,
        IReadOnlyList<CaptureWindow>? windows = null)
    {
        _bounds = bounds;
        _windows = windows ?? [];
        _monitor = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        _dpi = CursorDpi();
        _hintFont = new Drawing.Font("Segoe UI Variable Text", Scale(14), Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel);
        _labelFont = new Drawing.Font("Segoe UI Variable Text", Scale(12), Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel);

        AutoScaleMode = Forms.AutoScaleMode.None;
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Drawing.Color.Black;
        BackgroundImage = background;
        BackgroundImageLayout = Forms.ImageLayout.None;
        Cursor = Forms.Cursors.Cross;
        Text = "Sutra Island capture";

        FormClosed += (_, _) =>
        {
            if (_finished) return;
            _finished = true;
            _result.TrySetResult(null);
        };
    }

    internal string ActiveMode => _mode;

    public Task<CaptureRequest?> SelectAsync()
    {
        if (_finished || IsDisposed) return Task.FromResult<CaptureRequest?>(null);
        Show();
        ApplyPhysicalBounds();
        NativeMethods.SetForegroundWindow(Handle);
        Activate();
        return _result.Task;
    }

    /// <summary>Applies a mode chosen from the WPF toolbar.</summary>
    public void SetMode(string mode)
    {
        if (_finished || mode == _mode) return;
        _mode = mode;
        _start = null;
        _selection = Drawing.Rectangle.Empty;
        _hover = Drawing.Rectangle.Empty;
        _hoverTitle = "";
        Cursor = mode == RegionMode ? Forms.Cursors.Cross : Forms.Cursors.Hand;
        Invalidate();
        ModeChanged?.Invoke(mode);
    }

    /// <summary>Enter or a second Full screen activation: capture the active mode without another choice.</summary>
    public void Confirm()
    {
        if (_finished) return;
        if (_mode == ScreenMode) CompleteFullScreen();
        else if (_mode == WindowMode && !_hover.IsEmpty) Complete(WindowMode, _hover);
    }

    public void Cancel()
    {
        if (_finished) return;
        _finished = true;
        Capture = false;
        _result.TrySetResult(null);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyPhysicalBounds();
    }

    protected override void OnDpiChanged(Forms.DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // A virtual-screen window can straddle monitors with different DPIs. Keep the
        // frozen desktop and every device coordinate at 1:1 physical pixels.
        ApplyPhysicalBounds();
        Invalidate();
    }

    private void ApplyPhysicalBounds()
    {
        if (!IsHandleCreated) return;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost,
            _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0);
    }

    private uint CursorDpi()
    {
        var cursor = Forms.Cursor.Position;
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point { X = cursor.X, Y = cursor.Y }, NativeMethods.MonitorDefaultToNearest);
        if (monitor != nint.Zero &&
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0 &&
            dpiX > 0)
            return dpiX;
        return 96;
    }

    private int Scale(int value) => CaptureOverlayGeometry.ScaleForDpi(value, _dpi);

    protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
    {
        if (HandleCaptureKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Forms.Message m)
    {
        // This form is hosted by the WPF dispatcher rather than Application.Run,
        // so ProcessCmdKey is not reliably reached for keys delivered to the
        // overlay itself. Handle the raw key message here as well so Escape,
        // Enter, Tab, and R/W/F always work regardless of where focus sits.
        if (m.Msg == 0x0100 && HandleCaptureKey((Forms.Keys)m.WParam.ToInt32()))
            return;
        base.WndProc(ref m);
    }

    private bool HandleCaptureKey(Forms.Keys keyData)
    {
        if (_finished) return true;
        if (keyData == Forms.Keys.Escape) { Cancel(); return true; }
        if (keyData == Forms.Keys.Enter) { Confirm(); return true; }
        if ((keyData & Forms.Keys.KeyCode) == Forms.Keys.Tab)
        {
            FocusToolbarRequested?.Invoke();
            return true;
        }
        if (keyData == Forms.Keys.R || keyData == Forms.Keys.D1) { SetMode(RegionMode); return true; }
        if (keyData == Forms.Keys.W || keyData == Forms.Keys.D2) { SetMode(WindowMode); return true; }
        if (keyData == Forms.Keys.F || keyData == Forms.Keys.D3) { SetMode(ScreenMode); return true; }
        return false;
    }

    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_finished) return;
        if (e.Button == Forms.MouseButtons.Right) { Cancel(); return; }
        if (e.Button != Forms.MouseButtons.Left) return;
        if (_mode == ScreenMode) { CompleteFullScreen(); return; }
        if (_mode == WindowMode)
        {
            UpdateHover(e.Location);
            if (!_hover.IsEmpty) Complete(WindowMode, _hover);
            return;
        }

        _start = e.Location;
        if (!_selection.IsEmpty)
        {
            var dirty = _selection;
            _selection = Drawing.Rectangle.Empty;
            Invalidate(Drawing.Rectangle.Inflate(dirty, Scale(4), Scale(4)));
        }
        Capture = true;
    }

    protected override void OnMouseMove(Forms.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_finished) return;
        if (_mode == RegionMode && _start is { } start)
        {
            var next = CaptureOverlayGeometry.NormalizeSelection(start, e.Location, ClientRectangle);
            if (next == _selection) return;
            _selection = next;
            // The outside shade is composited over the entire frozen desktop. A
            // clipped repaint can leave stale shade blocks behind as the clear
            // region moves, so redraw the full overlay for each changed selection.
            Invalidate();
        }
        else if (_mode == WindowMode)
        {
            UpdateHover(e.Location);
        }
    }

    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_finished || _mode != RegionMode || _start is null || e.Button != Forms.MouseButtons.Left) return;
        var region = CaptureOverlayGeometry.NormalizeSelection(_start.Value, e.Location, ClientRectangle);
        _start = null;
        Capture = false;
        if (region.Width > 2 && region.Height > 2)
        {
            _selection = region;
            Complete(RegionMode, region);
            return;
        }

        var old = _selection;
        _selection = Drawing.Rectangle.Empty;
        if (!old.IsEmpty) Invalidate(Drawing.Rectangle.Inflate(old, Scale(6), Scale(6)));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_finished || _mode != WindowMode || _hover.IsEmpty) return;
        var old = _hover;
        _hover = Drawing.Rectangle.Empty;
        _hoverTitle = "";
        Invalidate(Drawing.Rectangle.Inflate(old, Scale(6), Scale(6)));
    }

    private void UpdateHover(Drawing.Point clientPoint)
    {
        if (_finished || _mode != WindowMode)
            return;
        var screenPoint = new Drawing.Point(_bounds.Left + clientPoint.X, _bounds.Top + clientPoint.Y);
        CaptureWindow? hit = null;
        foreach (var window in _windows)
        {
            if (!window.Bounds.Contains(screenPoint)) continue;
            hit = window;
            break;
        }

        var next = hit is { } selected
            ? CaptureOverlayGeometry.ScreenToClient(selected.Bounds, _bounds)
            : Drawing.Rectangle.Empty;
        var title = hit?.Title ?? "";
        if (next == _hover && string.Equals(title, _hoverTitle, StringComparison.Ordinal)) return;

        var dirty = Drawing.Rectangle.Union(_hover, next);
        _hover = next;
        _hoverTitle = title;
        Invalidate(Drawing.Rectangle.Inflate(dirty, Scale(8), Scale(8)));
    }

    private void CompleteFullScreen()
        => Complete(ScreenMode, new Drawing.Rectangle(0, 0, BackgroundImage?.Width ?? ClientSize.Width,
            BackgroundImage?.Height ?? ClientSize.Height));

    private void Complete(string mode, Drawing.Rectangle region)
    {
        if (_finished) return;
        _finished = true;
        Capture = false;
        _result.TrySetResult(new CaptureRequest(mode, region));
        // Keep the frozen surface visible until the toolbar has finished its exit
        // animation; the owner (ScreenshotTool) disposes this window afterwards.
    }

    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (_mode == WindowMode && !_hover.IsEmpty)
        {
            ShadeOutside(graphics, _hover);
            DrawOutline(graphics, _hover, Drawing.Color.FromArgb(210, 185, 165, 253), Scale(2));
            DrawWindowTitle(graphics);
        }
        else if (_mode == RegionMode && !_selection.IsEmpty)
        {
            ShadeOutside(graphics, _selection);
            DrawOutline(graphics, _selection, Drawing.Color.FromArgb(210, 185, 165, 253), Scale(2));
            DrawSizeLabel(graphics);
        }
        else
        {
            using var shade = new Drawing.SolidBrush(Drawing.Color.FromArgb(_mode == ScreenMode ? 45 : 70, 0, 0, 0));
            graphics.FillRectangle(shade, ClientRectangle);
            if (_mode == ScreenMode)
            {
                var border = ClientRectangle;
                border.Inflate(-Scale(2), -Scale(2));
                DrawOutline(graphics, border, Drawing.Color.FromArgb(180, 185, 165, 253), Scale(3));
            }
        }

        DrawHint(graphics);
    }

    private void ShadeOutside(Drawing.Graphics graphics, Drawing.Rectangle clear)
    {
        using var shade = new Drawing.SolidBrush(Drawing.Color.FromArgb(110, 0, 0, 0));
        using var outside = new Drawing.Region(ClientRectangle);
        outside.Exclude(clear);
        graphics.FillRegion(shade, outside);
    }

    private void DrawOutline(Drawing.Graphics graphics, Drawing.Rectangle rect, Drawing.Color color, int width)
    {
        using var pen = new Drawing.Pen(color, width);
        var inset = Math.Max(1, width) / 2;
        graphics.DrawRectangle(pen, rect.X + inset, rect.Y + inset,
            Math.Max(0, rect.Width - inset * 2), Math.Max(0, rect.Height - inset * 2));
    }

    private void DrawSizeLabel(Drawing.Graphics graphics)
    {
        var text = $"{_selection.Width} × {_selection.Height}";
        var measured = graphics.MeasureString(text, _labelFont);
        var width = (int)Math.Ceiling(measured.Width) + Scale(16);
        var height = (int)Math.Ceiling(measured.Height) + Scale(8);
        if (_selection.Width < width + Scale(12) || _selection.Height < height + Scale(12)) return;

        var label = new Drawing.Rectangle(
            _selection.Right - width - Scale(8),
            _selection.Bottom - height - Scale(8), width, height);
        FillRounded(graphics, label, Scale(6), Drawing.Color.FromArgb(210, 18, 19, 24));
        using var brush = new Drawing.SolidBrush(Drawing.Color.White);
        graphics.DrawString(text, _labelFont, brush, label.X + Scale(8), label.Y + Scale(4));
    }

    private void DrawWindowTitle(Drawing.Graphics graphics)
    {
        var title = _hoverTitle.Trim();
        if (title.Length == 0) return;
        if (title.Length > 90) title = title[..87] + "…";
        var maxWidth = Math.Max(0, _hover.Width - Scale(20));
        if (maxWidth < Scale(60)) return;

        var measured = graphics.MeasureString(title, _labelFont);
        var width = Math.Min(maxWidth, (int)Math.Ceiling(measured.Width) + Scale(16));
        var height = (int)Math.Ceiling(measured.Height) + Scale(8);
        var label = new Drawing.Rectangle(_hover.Left + Scale(10), _hover.Top + Scale(10), width, height);
        FillRounded(graphics, label, Scale(6), Drawing.Color.FromArgb(220, 18, 19, 24));

        using var brush = new Drawing.SolidBrush(Drawing.Color.White);
        using var format = new Drawing.StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(title, _labelFont, brush,
            new Drawing.RectangleF(label.X + Scale(8), label.Y + Scale(4),
                Math.Max(1, label.Width - Scale(16)), Math.Max(1, label.Height - Scale(8))), format);
    }

    private void DrawHint(Drawing.Graphics graphics)
    {
        var text = _mode switch
        {
            WindowMode => "Click a window · R/W/F switch · Esc cancels",
            ScreenMode => "Click anywhere or press Enter to capture all screens · Esc cancels",
            _ => "Drag to select a region · R/W/F switch · Esc cancels"
        };

        var measured = graphics.MeasureString(text, _hintFont);
        var width = (int)Math.Ceiling(measured.Width) + Scale(24);
        var height = (int)Math.Ceiling(measured.Height) + Scale(12);
        var x = _monitor.Left + (_monitor.Width - width) / 2 - _bounds.Left;
        var y = _monitor.Bottom - height - Scale(24) - _bounds.Top;
        x = Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - width));
        y = Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - height));

        var label = new Drawing.Rectangle(x, y, width, height);
        FillRounded(graphics, label, Scale(8), Drawing.Color.FromArgb(205, 18, 19, 24));
        using var brush = new Drawing.SolidBrush(Drawing.Color.White);
        graphics.DrawString(text, _hintFont, brush, label.X + Scale(12), label.Y + Scale(6));
    }

    private static void FillRounded(Drawing.Graphics graphics, Drawing.Rectangle rect, int radius, Drawing.Color color)
    {
        radius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var brush = new Drawing.SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hintFont.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Top-level visible windows in z-order, used by the overlay's window mode.</summary>
internal static class CaptureWindowEnumerator
{
    public static IReadOnlyList<CaptureWindow> Enumerate(Drawing.Rectangle virtualScreen)
    {
        var windows = new List<CaptureWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
                NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == Environment.ProcessId) return true;

                var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
                if ((exStyle & NativeMethods.WsExTransparent) != 0) return true;
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                Drawing.Rectangle bounds;
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaExtendedFrameBounds,
                        out NativeMethods.Rect frame, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Rect>()) == 0
                    && frame.Width > 1 && frame.Height > 1)
                {
                    bounds = new Drawing.Rectangle(frame.Left, frame.Top, frame.Width, frame.Height);
                }
                else if (NativeMethods.GetWindowRect(hwnd, out var rect) && rect.Width > 1 && rect.Height > 1)
                {
                    bounds = new Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                }
                else return true;

                if (Drawing.Rectangle.Intersect(bounds, virtualScreen).Width < 2) return true;
                if (Drawing.Rectangle.Intersect(bounds, virtualScreen).Height < 2) return true;

                var title = new StringBuilder(512);
                NativeMethods.GetWindowText(hwnd, title, title.Capacity);
                windows.Add(new CaptureWindow(hwnd, title.ToString(), bounds));
                return true;
            }
            catch
            {
                return true;
            }
        }, nint.Zero);
        return windows;
    }
}
