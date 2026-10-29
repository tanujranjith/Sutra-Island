using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Interop;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DynamicIsland.Windows.Services.Q;

public sealed class ScreenContextService(LoggingService log) : IQScreenContextService
{
    public nint LastForegroundTarget { get; private set; }

    public void RememberForeground(nint excludedWindow)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != nint.Zero && foreground != excludedWindow && NativeMethods.IsWindow(foreground))
        {
            NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
            if (pid == Environment.ProcessId) return;
            LastForegroundTarget = foreground;
        }
    }

    public async Task<QScreenContext?> CaptureAsync(nint targetWindow, QCaptureMode mode, CancellationToken cancellationToken)
    {
        if (targetWindow == nint.Zero) { RememberForeground(nint.Zero); targetWindow = LastForegroundTarget; }
        if (targetWindow == nint.Zero || !NativeMethods.IsWindow(targetWindow)) return null;
        if (!NativeMethods.GetWindowRect(targetWindow, out var rect) || rect.Width <= 0 || rect.Height <= 0) return null;
        if (mode == QCaptureMode.ActiveMonitor)
        {
            var point = new System.Drawing.Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            var monitor = System.Windows.Forms.Screen.FromPoint(point).Bounds;
            rect = new NativeMethods.Rect { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom };
        }
        try
        {
            var png = await Task.Run(() => CapturePixels(rect), cancellationToken).ConfigureAwait(false);
            if (png is null) return null;
            string ocr;
            try { ocr = await RecognizeAsync(png, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log.Error("Q OCR was unavailable; continuing with the captured image", ex); ocr = string.Empty; }
            NativeMethods.GetWindowThreadProcessId(targetWindow, out var pid);
            var process = "Unknown window";
            try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            var titleBuffer = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(targetWindow, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.Length == 0 ? process : titleBuffer.ToString();
            return new QScreenContext(title, process, rect.Width, rect.Height, ocr, png, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Error("Q screen capture failed", ex);
            return null;
        }
    }

    internal static byte[]? CapturePixels(NativeMethods.Rect rect)
    {
        using var bitmap = CaptureBitmap(rect);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    // The caller owns the pixels. Local screenshots can use them directly without
    // encoding a full desktop PNG only to decode it again for selection/preview.
    internal static Bitmap CaptureBitmap(NativeMethods.Rect rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static async Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return string.Empty;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
    }
}
