using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using DynamicIsland.Windows.Models;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
namespace DynamicIsland.Windows.Services;

public sealed partial class ClipboardService
{
    private int _paletteReadGeneration;
    private readonly Dictionary<string, ClipboardHistoryItem> _paletteEntries = [];
    public void ReleasePaletteEntries() { _paletteReadGeneration++; _paletteEntries.Clear(); }
    public async Task<IReadOnlyList<ClipboardEntry>> ReadPaletteAsync(int max)
    {
        var read = ++_paletteReadGeneration;
        try
        {
            var history = await WinClipboard.GetHistoryItemsAsync();
            if (read != _paletteReadGeneration) return [];
            if (history.Status != ClipboardHistoryItemsResultStatus.Success)
            {
                _paletteEntries.Clear();
                SetStatus(new(IntegrationState.PermissionRequired, "Enable clipboard history in Windows Settings.", "ms-settings:clipboard"));
                return [];
            }
            var entries = new List<ClipboardEntry>();
            var originals = new Dictionary<string, ClipboardHistoryItem>();
            foreach (var item in history.Items)
            {
                if (entries.Count >= Math.Clamp(max, 1, 30) || read != _paletteReadGeneration) break;
                try
                {
                    ClipboardEntry? entry = null;
                    if (item.Content.Contains(StandardDataFormats.Bitmap))
                    {
                        entry = new(item.Id, "", true);
                        try
                        {
                            var reference = await item.Content.GetBitmapAsync();
                            using var stream = await reference.OpenReadAsync();
                            entry = await ReadImagePreviewAsync(item.Id, stream);
                        }
                        catch (Exception ex) { log.Debug($"Clipboard image preview unavailable: {ex.GetType().Name}"); }
                    }
                    else if (item.Content.Contains(StandardDataFormats.Text))
                    {
                        var text = await item.Content.GetTextAsync();
                        if (!string.IsNullOrWhiteSpace(text)) entry = new(item.Id, text);
                    }
                    if (entry is not null) { entries.Add(entry); originals[item.Id] = item; }
                }
                catch (Exception ex) { log.Debug($"Clipboard item unavailable: {ex.GetType().Name}"); }
            }
            if (read != _paletteReadGeneration) return [];
            _paletteEntries.Clear(); foreach (var pair in originals) _paletteEntries.Add(pair.Key, pair.Value);
            SetStatus(new(IntegrationState.Ready, entries.Count == 0 ? "Clipboard history is empty" : "Ready"));
            return entries;
        }
        catch (Exception ex)
        {
            log.Debug($"Clipboard history unavailable: {ex.GetType().Name}");
            if (read == _paletteReadGeneration) { _paletteEntries.Clear(); SetStatus(new(IntegrationState.Error, "Clipboard history could not load. Refresh to retry.")); }
            return [];
        }
    }
    internal static async Task<ClipboardEntry> ReadImagePreviewAsync(string id, IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var originalWidth = decoder.PixelWidth; var originalHeight = decoder.PixelHeight;
        if (originalWidth == 0 || originalHeight == 0 || originalWidth > 32768 || originalHeight > 32768 || (ulong)originalWidth * originalHeight > 100_000_000)
            return new(id, "", true);
        var scale = Math.Min(1d, Math.Min(320d / originalWidth, 140d / originalHeight));
        var width = Math.Max(1u, (uint)Math.Round(originalWidth * scale));
        var height = Math.Max(1u, (uint)Math.Round(originalHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
        return new(id, "", true, pixels.DetachPixelData(), (int)width, (int)height, (int)originalWidth, (int)originalHeight);
    }
    public bool CopyEntry(ClipboardEntry entry)
    {
        if (!entry.IsImage) return CopyText(entry.Text);
        try
        {
            return _paletteEntries.TryGetValue(entry.Id, out var original)
                && WinClipboard.SetHistoryItemAsContent(original) == SetHistoryItemAsContentStatus.Success;
        }
        catch (Exception ex) { log.Debug($"Clipboard image copy failed: {ex.GetType().Name}"); return false; }
    }
}
