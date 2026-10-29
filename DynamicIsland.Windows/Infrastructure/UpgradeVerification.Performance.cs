using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using Drawing = System.Drawing;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    // Opt-in synthetic fixtures: no desktop capture, network, or provider requests.
    private static async Task VerifyPerformanceAsync(IslandViewModel vm, Action<bool, string> check, string output)
    {
        static byte[] Pixels(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
                source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            return pixels;
        }
        static BitmapSource PngRoundTrip(Drawing.Bitmap source)
        {
            using var encoded = new MemoryStream();
            source.Save(encoded, Drawing.Imaging.ImageFormat.Png);
            return ScreenshotTool.Decode(encoded.ToArray());
        }
        static Drawing.Bitmap Fixture(int width, int height)
        {
            var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[width * height * 4];
            new Random(3471).NextBytes(pixels);
            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        BitmapSource detached;
        byte[] expected;
        using (var fixture = Fixture(97, 65))
        {
            expected = Pixels(PngRoundTrip(fixture));
            detached = ScreenshotTool.CopyPixels(fixture);
            check(detached.IsFrozen && detached.PixelWidth == 97 && detached.PixelHeight == 65 && Pixels(detached).SequenceEqual(expected),
                "Direct screenshot copying preserves every BGRA pixel, alpha, and original dimensions");
            var rect = new Drawing.Rectangle(71, 38, 26, 27);
            var crop = ScreenshotTool.CopyPixels(fixture, rect);
            using var originalCrop = fixture.Clone(rect, Drawing.Imaging.PixelFormat.Format32bppArgb);
            check(crop.PixelWidth == rect.Width && crop.PixelHeight == rect.Height && Pixels(crop).SequenceEqual(Pixels(PngRoundTrip(originalCrop))),
                "A selected edge region retains only the exact crop pixels without a full-desktop source");
            using var graphics = Drawing.Graphics.FromImage(fixture);
            graphics.Clear(Drawing.Color.Magenta);
        }
        check(Pixels(detached).SequenceEqual(expected), "Screenshot pixels remain valid after the GDI source is modified and disposed");

        const int negativeWidth = 13, negativeHeight = 11, negativeStride = negativeWidth * 4;
        var memory = Marshal.AllocHGlobal(negativeStride * negativeHeight);
        BitmapSource negativeCopy;
        byte[] negativeExpected;
        try
        {
            var bytes = new byte[negativeStride * negativeHeight];
            new Random(952).NextBytes(bytes);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            using var fixture = new Drawing.Bitmap(negativeWidth, negativeHeight, -negativeStride, Drawing.Imaging.PixelFormat.Format32bppArgb,
                nint.Add(memory, negativeStride * (negativeHeight - 1)));
            negativeExpected = Pixels(PngRoundTrip(fixture));
            negativeCopy = ScreenshotTool.CopyPixels(fixture);
        }
        finally { Marshal.FreeHGlobal(memory); }
        check(Pixels(negativeCopy).SequenceEqual(negativeExpected), "Bottom-up GDI row layout copies correctly and releases its source allocation");

        static byte[] MaterialPixels(Brush brush)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, new Rect(0, 0, 240, 90));
            var image = new RenderTargetBitmap(240, 90, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            var bytes = new byte[240 * 90 * 4]; image.CopyPixels(bytes, 240 * 4, 0); return bytes;
        }
        var tint = new SolidColorBrush(Color.FromArgb(220, 25, 27, 32)); tint.Freeze();
        foreach (var inset in new[] { 0d, 1d / 128, .125, .49 })
        {
            var uncached = new SolidColorBrush(tint.Color);
            var cached = FrostedMaterial.WithOpaqueCaps(tint, inset);
            check(MaterialPixels(cached).SequenceEqual(MaterialPixels(FrostedMaterial.WithOpaqueCaps(uncached, inset))),
                $"Cached frost material is pixel-identical at inset {inset}");
            check(ReferenceEquals(cached, FrostedMaterial.WithOpaqueCaps(tint, inset)), "Repeated frost bindings reuse the same immutable material");
        }
        var equivalent = new SolidColorBrush(tint.Color); equivalent.Freeze();
        check(ReferenceEquals(FrostedMaterial.WithOpaqueCaps(tint, .125), FrostedMaterial.WithOpaqueCaps(equivalent, .125)),
            "Equivalent frozen tint brushes share the same bounded material cache");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var audioChanged = typeof(IslandViewModel).GetMethod("OnAudioChanged", flags)!;
        var timer = (DispatcherTimer)typeof(IslandViewModel).GetField("_visualizerTimer", flags)!.GetValue(vm)!;
        var clock = (Stopwatch)typeof(IslandViewModel).GetField("_visualizerClock", flags)!.GetValue(vm)!;
        var originalAudio = vm.Audio; var originalExpanded = vm.IsExpanded; var originalVisible = vm.VisualizerSurfaceVisible;
        var originalMusic = vm.Settings.ShowMusicVisualizer; var originalCompact = vm.Settings.ShowEqualizerInCompact;
        var originalConnectivity = vm.Settings.ShowConnectivity;
        var connectivityTimer = (DispatcherTimer)typeof(IslandViewModel).GetField("_connectivityTimer", flags)!.GetValue(vm)!;
        var frames = 0;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(vm.VisualizerBar0Height)) frames++; };
        vm.PropertyChanged += changed;
        try
        {
            vm.ClearQ(); vm.Settings.ShowMusicVisualizer = true; vm.Settings.ShowEqualizerInCompact = true;
            vm.VisualizerSurfaceVisible = true; vm.IsExpanded = true; vm.ApplySettings();
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available }]);
            check(!timer.IsEnabled && !clock.IsRunning, "Idle visualizer stops its dispatcher timer and stopwatch");
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available, ActiveAudioOutput = true }]);
            await WaitForSutraAsync(() => frames > 0);
            check(timer.IsEnabled && timer.Interval == TimeSpan.FromMilliseconds(33), "Visible playback retains the existing 33 ms visualizer cadence");
            vm.VisualizerSurfaceVisible = false;
            var before = frames; await Task.Delay(120);
            check(!timer.IsEnabled && !clock.IsRunning && frames == before, "Hidden visualizer performs no periodic UI animation updates");
            vm.VisualizerSurfaceVisible = true;
            await WaitForSutraAsync(() => frames > before);
            check(timer.IsEnabled && clock.IsRunning, "Visible visualizer resumes immediately with a fresh frame interval");
            vm.Settings.ShowMusicVisualizer = false; vm.ApplySettings();
            check(!timer.IsEnabled, "Disabling the expanded visualizer stops animation work");
            vm.IsExpanded = false;
            check(timer.IsEnabled, "Collapsing resumes the enabled compact equalizer");
            vm.Settings.ShowEqualizerInCompact = false; vm.ApplySettings();
            check(!timer.IsEnabled, "Disabling both visualizers leaves no high-frequency dispatcher timer");
            vm.Settings.ShowConnectivity = false; vm.ApplySettings();
            check(!connectivityTimer.IsEnabled, "Disabled connectivity widgets stop periodic adapter discovery");
            vm.Settings.ShowConnectivity = true; vm.ApplySettings();
            check(connectivityTimer.IsEnabled && connectivityTimer.Interval == TimeSpan.FromSeconds(vm.Settings.ConnectivityPollSeconds),
                "Re-enabled connectivity immediately resumes its configured refresh cadence");
        }
        finally
        {
            vm.PropertyChanged -= changed;
            vm.Settings.ShowMusicVisualizer = originalMusic; vm.Settings.ShowEqualizerInCompact = originalCompact;
            vm.Settings.ShowConnectivity = originalConnectivity;
            vm.IsExpanded = originalExpanded; vm.VisualizerSurfaceVisible = originalVisible;
            audioChanged.Invoke(vm, [null, originalAudio]); vm.ApplySettings();
        }

        await VerifyWidgetPerformanceAsync(check);

        // Microbenchmarks compare the old and new CPU paths on the same synthetic
        // pixels. They are diagnostic measurements, not FPS or total-RAM claims.
        static object Measure(int iterations, Func<object> action)
        {
            _ = action();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++) GC.KeepAlive(action());
            stopwatch.Stop();
            return new { Iterations = iterations, Milliseconds = stopwatch.Elapsed.TotalMilliseconds, ManagedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
        }
        using var benchmark = Fixture(1920, 1080);
        var originalMaterial = typeof(FrostedMaterial).GetMethod("CreateOpaqueCaps", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Brush, double, Brush>>();
        var metrics = new
        {
            CapturedAt = DateTimeOffset.Now, Environment.Version, PixelWidth = 1920, PixelHeight = 1080,
            PngRoundTrip = Measure(4, () => PngRoundTrip(benchmark)),
            DirectPixelCopy = Measure(4, () => ScreenshotTool.CopyPixels(benchmark)),
            UncachedMaterial = Measure(1000, () => originalMaterial(tint, .125)),
            CachedMaterial = Measure(1000, () => FrostedMaterial.WithOpaqueCaps(tint, .125))
        };
        File.WriteAllText(Path.Combine(output, "performance-metrics.json"), System.Text.Json.JsonSerializer.Serialize(metrics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        check(true, "Synthetic screenshot and frost allocation/timing measurements were recorded locally");
    }
}
