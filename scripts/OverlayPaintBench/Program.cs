using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: OverlayPaintBench <DynamicIsland.Windows.dll> [output.json] [iterations]");
            return 2;
        }
        try
        {
            var assemblyPath = Path.GetFullPath(args[0]);
            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType("DynamicIsland.Windows.Services.CaptureOverlay", throwOnError: true)!;
            var desktop = SystemInformation.VirtualScreen;
            var scale = Math.Min(1d, Math.Sqrt(20_000_000d / Math.Max(1d, (double)desktop.Width * desktop.Height)));
            var width = Math.Max(640, (int)Math.Round(desktop.Width * scale));
            var height = Math.Max(480, (int)Math.Round(desktop.Height * scale));
            var bounds = new Rectangle(0, 0, width, height);
            var iterations = args.Length > 2 ? Math.Clamp(int.Parse(args[2]), 20, 300) : 80;
            using var background = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var drawing = Graphics.FromImage(background))
            {
                drawing.Clear(Color.FromArgb(255, 90, 110, 130));
                using var light = new SolidBrush(Color.FromArgb(255, 170, 180, 190));
                using var dark = new SolidBrush(Color.FromArgb(255, 35, 45, 65));
                for (var y = 0; y < height; y += 80)
                    for (var x = 0; x < width; x += 80)
                        drawing.FillRectangle((x / 80 + y / 80) % 2 == 0 ? light : dark, x, y, 80, 80);
            }
            using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(canvas);
            using var form = (Form)Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object?[] { bounds, background, null }, null)!;
            // Create a message target for Invalidated events, without displaying the form,
            // capturing the mouse, injecting input, or reading desktop pixels.
            _ = form.Handle;
            var paintBackground = type.GetMethod("OnPaintBackground", PrivateInstance)!;
            var paintForeground = type.GetMethod("OnPaint", PrivateInstance)!;
            var mouseMove = type.GetMethod("OnMouseMove", PrivateInstance)!;
            var startField = type.GetField("_start", PrivateInstance)!;
            Rectangle dirty = Rectangle.Empty;
            form.Invalidated += (_, e) => dirty = dirty.IsEmpty ? e.InvalidRect : Rectangle.Union(dirty, e.InvalidRect);

            double Paint(Rectangle clip)
            {
                clip = Rectangle.Intersect(bounds, clip);
                if (clip.IsEmpty) return 0;
                graphics.SetClip(clip);
                using var e = new PaintEventArgs(graphics, clip);
                var watch = Stopwatch.StartNew();
                paintBackground.Invoke(form, new object[] { e });
                paintForeground.Invoke(form, new object[] { e });
                graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
                return watch.Elapsed.TotalMilliseconds;
            }

            var initialPaint = Paint(bounds);
            startField.SetValue(form, new Point(width / 5, height / 5));
            mouseMove.Invoke(form, new object[] { new MouseEventArgs(MouseButtons.None, 0, width / 2, height / 2, 0) });
            for (var i = 0; i < 12; i++) Paint(bounds);
            var fullTimes = Enumerable.Range(0, iterations).Select(_ => Paint(bounds)).ToArray();

            var dirtyTimes = new List<double>();
            var handlerTimes = new List<double>();
            var fractions = new List<double>();
            var missingInvalidations = 0;
            for (var i = 0; i < iterations + 12; i++)
            {
                dirty = Rectangle.Empty;
                var x = width / 2 + i % 23 * 3;
                var y = height / 2 + i % 17 * 2;
                var watch = Stopwatch.StartNew();
                mouseMove.Invoke(form, new object[] { new MouseEventArgs(MouseButtons.None, 0, x, y, 0) });
                var handler = watch.Elapsed.TotalMilliseconds;
                if (dirty.IsEmpty) { missingInvalidations++; continue; }
                var clip = Rectangle.Intersect(bounds, dirty);
                var elapsed = Paint(clip);
                if (i < 12) continue;
                dirtyTimes.Add(elapsed);
                handlerTimes.Add(handler);
                fractions.Add((double)clip.Width * clip.Height / ((double)width * height));
            }
            var result = new
            {
                assemblyPath,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))),
                assemblyVersion = assembly.GetName().Version?.ToString(),
                virtualScreen = new { desktop.X, desktop.Y, desktop.Width, desktop.Height },
                benchmarkSize = new { width, height, scale },
                iterations,
                initialPaintMs = initialPaint,
                fullPaint = Summary(fullTimes),
                dragDirtyPaint = Summary(dirtyTimes),
                dragMouseHandler = Summary(handlerTimes),
                dragClipFraction = Summary(fractions),
                missingInvalidations,
                measurement = "Offscreen synthetic-pattern GDI paint CPU time: OnPaintBackground + OnPaint + synchronous Graphics.Flush. Excludes DWM presentation, WinForms double-buffer copying, input-queue delay, and hardware cursor latency. No window shown or real input injected."
            };
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            if (args.Length > 1)
            {
                var output = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, json);
            }
            Console.WriteLine(json);
            return dirtyTimes.Count > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static object Summary(IEnumerable<double> samples)
    {
        var sorted = samples.Order().ToArray();
        if (sorted.Length == 0) return new { count = 0, median = 0d, p95 = 0d, min = 0d, max = 0d };
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
        return new { count = sorted.Length, median = Percentile(.5), p95 = Percentile(.95), min = sorted[0], max = sorted[^1] };
    }
}
