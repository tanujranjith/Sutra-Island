using System.IO;
using System.Net.Http;

namespace DynamicIsland.Windows.Services;

internal static class WorkspaceTransferService
{
    internal static bool TryDownloadUri(string? text, out Uri uri)
    {
        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var candidate) &&
            candidate.Scheme is "http" or "https" && candidate.Host.Length > 0 && candidate.UserInfo.Length == 0)
        {
            uri = candidate;
            return true;
        }
        uri = null!;
        return false;
    }

    internal static string SuggestedFileName(Uri uri)
    {
        var last = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? "");
        var name = new string(last.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.', '_');
        return name.Length is > 0 and <= 180 ? name : "download";
    }

    internal static async Task DownloadAsync(HttpClient client, Uri uri, string destination, IProgress<int?> progress, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await WriteAsync(source, response.Content.Headers.ContentLength, destination, progress, token).ConfigureAwait(false);
    }

    internal static async Task CopyAsync(string path, string destination, IProgress<int?> progress, CancellationToken token)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await WriteAsync(source, source.Length, destination, progress, token).ConfigureAwait(false);
    }

    private static async Task WriteAsync(Stream source, long? length, string destination, IProgress<int?> progress, CancellationToken token)
    {
        if (File.Exists(destination)) throw new IOException("A file with that name already exists in the destination folder.");
        var partial = Path.Combine(Path.GetDirectoryName(destination)!, $".sutra-{Guid.NewGuid():N}.part");
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                int? previous = length > 0 ? 0 : null;
                progress.Report(previous);
                var lastUpdate = Environment.TickCount64;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    written += read;
                    if (length is > 0 && Environment.TickCount64 - lastUpdate >= 125)
                    {
                        var percent = (int)Math.Clamp(written * 100d / length.Value, 0, 99);
                        if (previous != percent) progress.Report(previous = percent);
                        lastUpdate = Environment.TickCount64;
                    }
                }
                if (length is { } expected && written != expected) throw new IOException("The transfer ended before the expected file size was reached.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
