using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

public sealed partial class ClipboardService
{
    // Captures deliberately opt out of Windows' persisted/cloud clipboard history.
    // Keep a bounded, process-local history so Island still shows its own captures,
    // including images over Windows clipboard history's size limit.
    private sealed record LocalScreenshot(ClipboardEntry Entry, BitmapSource Image, DateTimeOffset CopiedAt)
    {
        public long Bytes => (long)Image.PixelWidth * Image.PixelHeight * 4;
    }

    private readonly List<LocalScreenshot> _screenshots = [];
    private const long ScreenshotBudget = 128L * 1024 * 1024;
    private bool _rememberScreenshots = true;
    private int _screenshotLimit = 10;

    public void ConfigureScreenshotHistory(bool enabled, int limit)
    {
        if (_rememberScreenshots != enabled || _screenshotLimit != Math.Clamp(limit, 1, 30))
            _paletteReadGeneration++;
        _rememberScreenshots = enabled;
        _screenshotLimit = Math.Clamp(limit, 1, 30);
        if (!enabled) _screenshots.Clear();
        else if (_screenshots.Count > _screenshotLimit)
            _screenshots.RemoveRange(_screenshotLimit, _screenshots.Count - _screenshotLimit);
    }

    public void RememberScreenshot(BitmapSource image)
    {
        if (!_rememberScreenshots) return;
        try
        {
            // Recopying an unchanged capture moves it to the front rather than
            // retaining another reference and another row for the same pixels.
            _screenshots.RemoveAll(item => ReferenceEquals(item.Image, image));
            var scale = Math.Min(1d, Math.Min(320d / image.PixelWidth, 140d / image.PixelHeight));
            BitmapSource thumbnail = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
            thumbnail = new FormatConvertedBitmap(thumbnail, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[checked(thumbnail.PixelWidth * thumbnail.PixelHeight * 4)];
            thumbnail.CopyPixels(pixels, thumbnail.PixelWidth * 4, 0);
            var entry = new ClipboardEntry("sutra-screenshot-" + Guid.NewGuid().ToString("N"), "", true,
                pixels, thumbnail.PixelWidth, thumbnail.PixelHeight, image.PixelWidth, image.PixelHeight);
            var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
            while (_screenshots.Count > 0 && (_screenshots.Count >= _screenshotLimit || _screenshots.Sum(item => item.Bytes) + bytes > ScreenshotBudget))
                _screenshots.RemoveAt(_screenshots.Count - 1);
            // Always retain the latest allowed capture. A single unusually large
            // image can exceed the normal budget, but never accumulates with others.
            _screenshots.Insert(0, new(entry, image, DateTimeOffset.Now));
        }
        catch (Exception ex) { log.Debug($"Screenshot history unavailable: {ex.GetType().Name}"); }
    }

    private IReadOnlyList<ClipboardEntry> RecentScreenshots(int max) =>
        _screenshots.Take(Math.Clamp(max, 1, 30)).Select(item => item.Entry).ToArray();

    internal BitmapSource? ScreenshotOriginal(string id) => _screenshots.FirstOrDefault(item => item.Entry.Id == id)?.Image;
}
