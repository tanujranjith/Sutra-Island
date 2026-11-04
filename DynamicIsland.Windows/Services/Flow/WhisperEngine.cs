using System.Net.Http;
using System.Reflection;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// On-device speech-to-text with whisper.cpp (through Whisper.net).
/// The speech model (ggml-base.en, about 148 MB) is downloaded once into the app's
/// data folder instead of being bundled into the exe. The native whisper.cpp DLLs are
/// embedded in the exe and extracted next to the model on first use.
/// </summary>
public sealed class WhisperEngine : IDisposable
{
    public const string ModelFileName = "ggml-base.en.bin";
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
    private const long MinModelBytes = 100L * 1024 * 1024;
    private static readonly string[] NativeFiles = ["ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-whisper.dll", "whisper.dll"];

    private readonly LoggingService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private Task? _download;
    private double _downloadProgress = -1;

    public event EventHandler? ModelStatusChanged;

    public WhisperEngine(LoggingService log) => _log = log;

    public static string Folder => Path.Combine(Infrastructure.AppDataPaths.Root, "SutraFlow");
    public static string ModelPath => Path.Combine(Folder, "models", ModelFileName);
    public bool IsModelInstalled => File.Exists(ModelPath) && new FileInfo(ModelPath).Length >= MinModelBytes;
    public bool IsDownloading => _download is { IsCompleted: false };
    /// <summary>0..1 while downloading, otherwise -1.</summary>
    public double DownloadProgress => _downloadProgress;
    public string? LastDownloadError { get; private set; }

    public string ModelStatusText => IsDownloading
        ? $"Downloading speech model… {Math.Max(0, _downloadProgress) * 100:0}%"
        : IsModelInstalled ? "Whisper base.en · installed"
        : LastDownloadError is { } error ? $"Download failed: {error}"
        : "Speech model not installed (148 MB)";

    /// <summary>Starts the model download if it is missing. Safe to call repeatedly.</summary>
    public Task EnsureModelAsync()
    {
        if (IsModelInstalled) return Task.CompletedTask;
        lock (NativeFiles)
        {
            if (_download is { IsCompleted: false }) return _download;
            _download = DownloadAsync();
            return _download;
        }
    }

    private async Task DownloadAsync()
    {
        LastDownloadError = null;
        _downloadProgress = 0;
        Raise();
        var part = ModelPath + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SutraIsland/1.3 (+https://github.com/tanujranjith/Sutra-Island)");
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 147_964_211L;
            await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                var lastRaise = DateTime.UtcNow;
                int n;
                while ((n = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                    read += n;
                    _downloadProgress = Math.Min(0.999, read / (double)total);
                    if ((DateTime.UtcNow - lastRaise).TotalMilliseconds > 250) { lastRaise = DateTime.UtcNow; Raise(); }
                }
                if (read < MinModelBytes || (response.Content.Headers.ContentLength is long expected && read != expected))
                    throw new IOException("the download was incomplete");
            }
            File.Move(part, ModelPath, overwrite: true);
            _log.Info("Sutra Flow: speech model downloaded.");
        }
        catch (Exception ex)
        {
            LastDownloadError = ex is HttpRequestException ? "check your internet connection" : ex.Message;
            _log.Error("Sutra Flow: speech model download failed.", ex);
            try { if (File.Exists(part)) File.Delete(part); } catch { }
        }
        finally
        {
            _downloadProgress = -1;
            Raise();
        }
    }

    private void Raise() => ModelStatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Transcribes 16 kHz mono samples. The vocabulary nudges spelling of names and terms.</summary>
    /// <param name="expressive">Prompts Whisper with written-out hesitations so it keeps "uhh" and "ohhh" instead of dropping them.</param>
    public async Task<string> TranscribeAsync(float[] samples, IReadOnlyList<string> vocabulary, CancellationToken token, bool expressive = false)
    {
        if (!IsModelInstalled) throw new FlowException("Speech model still downloading");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var factory = GetFactory();
            var builder = factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 2, 8))
                .WithNoContext()
                .WithSingleSegment();
            var prompt = new List<string>();
            // Whisper copies the style of its prompt; a prompt full of hesitations makes it write them down.
            if (expressive) prompt.Add("Umm, uhhh... hmm. Ohhh, okay. Uh, I was like, ahhh, wow!");
            if (vocabulary.Count > 0) prompt.Add("Vocabulary: " + string.Join(", ", vocabulary.Take(60)) + ".");
            if (prompt.Count > 0) builder = builder.WithPrompt(string.Join(" ", prompt));
            await using var processor = builder.Build();
            // Whisper handles short clips poorly; pad to at least one second of silence.
            if (samples.Length < FlowRecorder.SampleRate)
                Array.Resize(ref samples, FlowRecorder.SampleRate);
            var parts = new List<string>();
            await foreach (var segment in processor.ProcessAsync(samples, token).ConfigureAwait(false))
                parts.Add(segment.Text);
            return FlowTextCleaner.StripWhisperArtifacts(string.Join(" ", parts));
        }
        finally { _gate.Release(); }
    }

    private WhisperFactory GetFactory()
    {
        if (_factory is not null) return _factory;
        ExtractNatives();
        try
        {
            _factory = WhisperFactory.FromPath(ModelPath);
        }
        catch (Exception ex)
        {
            _log.Error("Sutra Flow: whisper failed to load. whisper.cpp needs the Microsoft Visual C++ 2015-2022 Redistributable (x64), including vcomp140.dll.", ex);
            throw new FlowException("Install the Microsoft Visual C++ Redistributable (x64)");
        }
        _log.Info($"Sutra Flow: whisper loaded ({RuntimeOptions.LoadedLibrary}).");
        return _factory;
    }

    /// <summary>
    /// Whisper.net looks for natives in "&lt;dir&gt;\runtimes\win-x64". A single-file exe has no
    /// such folder, so the DLLs embedded at build time are written to the data folder and
    /// the loader is pointed there. Rewritten whenever the app version changes.
    /// </summary>
    private void ExtractNatives()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        var root = Path.Combine(Folder, "runtime", version);
        var target = Path.Combine(root, "runtimes", "win-x64");
        Directory.CreateDirectory(target);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in NativeFiles)
        {
            var path = Path.Combine(target, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0) continue;
            using var stream = assembly.GetManifestResourceStream("SutraFlow.Native.win-x64." + name)
                ?? throw new FlowException("Speech engine files are missing from this build");
            using var file = File.Create(path);
            stream.CopyTo(file);
        }
        RuntimeOptions.LibraryPath = Path.Combine(root, "whisper.dll");
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
