using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace DynamicIsland.Windows.Services;

internal static class IslandUpdateService
{
    internal const string ApplyArgument = "--apply-island-update";
    private const string CleanupArgument = "--clean-island-update";
    private const string ExecutableAsset = "DynamicIsland.exe";
    private const string Repository = "tanujranjith/Sutra-Island";
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024;
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { UserAgent = { new("Sutra-Island", "1.0") } }
    };

    internal static Version CurrentVersion { get; } = typeof(App).Assembly.GetName().Version!;

    internal static async Task<IslandUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ReadRelease(release.RootElement, CurrentVersion);
    }

    internal static IslandUpdate? ReadRelease(JsonElement release, Version current)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;

        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var match = Regex.Match(tag, @"\Av(?<version>[0-9]+\.[0-9]+\.[0-9]+)(?:\+[0-9A-Za-z.-]+)?\z");
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed))
            throw new InvalidDataException("GitHub returned an invalid Island release version.");

        var version = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
        if (version <= current)
            return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != ExecutableAsset
                || asset.GetProperty("state").GetString() != "uploaded")
                continue;

            var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{ExecutableAsset}";
            var size = asset.GetProperty("size").GetInt64();
            if (asset.GetProperty("browser_download_url").GetString() == expectedUrl
                && size is > 0 and <= MaximumDownloadBytes
                && asset.TryGetProperty("digest", out var value)
                && value.GetString() is { Length: 71 } digest
                && digest.StartsWith("sha256:", StringComparison.Ordinal)
                && digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") == -1)
                return new IslandUpdate(version, new Uri(expectedUrl), size, Convert.FromHexString(digest.AsSpan(7)));
        }

        throw new InvalidDataException("The latest Island release does not have a verifiable Windows update yet.");
    }

    internal static async Task PrepareRestartAsync(IslandUpdate update, IProgress<int> progress)
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("Island's executable path is unavailable.");
        if (Path.GetFileNameWithoutExtension(target).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || FileVersionInfo.GetVersionInfo(target).OriginalFilename != "DynamicIsland.Windows.dll")
            throw new InvalidOperationException("Run the published Windows executable to install updates.");

        var token = Guid.NewGuid();
        var stage = StagePath(target, token);
        Directory.CreateDirectory(stage);
        var download = Path.Combine(stage, ExecutableAsset);

        try
        {
            await DownloadAsync(update, download, progress).ConfigureAwait(false);
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName(token));
            using var helper = Start(download, ApplyArgument, Environment.ProcessId.ToString(), token.ToString("N"));
            var accepted = await Task.Run(() =>
            {
                var timeout = Stopwatch.StartNew();
                while (!helper.HasExited && timeout.Elapsed < TimeSpan.FromSeconds(30))
                {
                    if (ready.WaitOne(100))
                        return true;
                }
                return false;
            }).ConfigureAwait(false);

            if (!accepted)
                throw new IOException("The update could not prepare to restart Island.");
        }
        catch
        {
            CleanStage(stage);
            throw;
        }
    }

    private static async Task DownloadAsync(IslandUpdate update, string path, IProgress<int> progress)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var cancellation = deadline.Token;
        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            var buffer = new byte[65536];
            long total = 0;
            int lastPercent = -1;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > update.Size)
                    throw new InvalidDataException("The update download is larger than expected.");

                await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                var percent = (int)(total * 100 / update.Size);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent);
                }
            }

            if (total != update.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), update.Sha256))
                throw new InvalidDataException("The update download failed its SHA-256 verification.");
        }

        var file = FileVersionInfo.GetVersionInfo(path);
        if (new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart) != update.Version)
            throw new InvalidDataException("The update executable does not match the advertised version.");
    }

    // The verified download doubles as the helper, so even a portable install can replace its own executable.
    internal static int Apply(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out var pid) || !Guid.TryParseExact(args[2], "N", out var token))
            return 2;

        string? target = null;
        string? backup = null;
        var parentExited = false;
        var replaced = false;
        try
        {
            using var parent = Process.GetProcessById(pid);
            target = parent.MainModule!.FileName;
            var stage = StagePath(target, token);
            var download = Path.Combine(stage, ExecutableAsset);
            if (!string.Equals(Environment.ProcessPath, download, StringComparison.OrdinalIgnoreCase))
                return 2;

            using var ready = EventWaitHandle.OpenExisting(ReadyEventName(token));
            var pending = Path.Combine(stage, "replacement.exe");
            File.Copy(download, pending);
            backup = Path.Combine(stage, "previous.exe");
            File.Copy(target, backup);
            ready.Set();
            parentExited = parent.WaitForExit(90_000);
            if (!parentExited)
                throw new IOException("Island did not close in time.");

            File.Move(pending, target, overwrite: true);
            replaced = true;
            using var restarted = Start(target, "--settings", CleanupArgument, token.ToString("N"));
            return 0;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            var message = $"Island could not finish the update. {ex.Message}";
            if (parentExited && target is not null)
            {
                try
                {
                    if (replaced)
                        File.Move(backup!, target, overwrite: true);
                    using var restored = Start(target, "--settings", CleanupArgument, token.ToString("N"));
                }
                catch (Exception restoreError)
                {
                    LogFailure(restoreError);
                    message += $"\n\nIsland could not restart. {restoreError.Message}";
                    if (File.Exists(backup))
                        message += $"\nThe previous executable is at {backup}.";
                }
            }
            System.Windows.MessageBox.Show(message, "Sutra Island update", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static void LogFailure(Exception error)
    {
        try
        {
            var log = new LoggingService();
            log.Error("Island update helper failed", error);
            log.FlushAsync().GetAwaiter().GetResult();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void CleanAfterRestart(string[] args)
    {
        var index = Array.IndexOf(args, CleanupArgument);
        if (index < 0 || index + 1 >= args.Length || !Guid.TryParseExact(args[index + 1], "N", out var token)
            || Environment.ProcessPath is not { } target)
            return;

        _ = Task.Run(async () =>
        {
            var stage = StagePath(target, token);
            for (var attempt = 0; attempt < 30 && Directory.Exists(stage); attempt++)
            {
                CleanStage(stage, removeBackup: true);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        });
    }

    private static string StagePath(string target, Guid token) =>
        Path.Combine(Path.GetDirectoryName(target)!, $".sutra-update-{token:N}");

    private static string ReadyEventName(Guid token) => $@"Local\SutraIsland.UpdateReady.{token:N}";

    private static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Windows could not start Island.");
    }

    private static void CleanStage(string stage, bool removeBackup = false)
    {
        try
        {
            File.Delete(Path.Combine(stage, ExecutableAsset));
            File.Delete(Path.Combine(stage, "replacement.exe"));
            if (removeBackup)
                File.Delete(Path.Combine(stage, "previous.exe"));
            Directory.Delete(stage);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
