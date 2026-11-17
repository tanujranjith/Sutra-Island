using DynamicIsland.Q.Core;
using NAudio.Wave;
using SherpaOnnx;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>Offline Kokoro speech, using the voice model installed alongside the app.</summary>
public sealed class LocalSpeechOutput : ISpeechOutput, IDisposable
{
    public static readonly IReadOnlyList<string> Voices = ["Heart · American", "Bella · American", "Sarah · American", "Michael · American", "Adam · American", "Emma · British", "George · British"];
    private static readonly int[] VoiceIds = [3, 2, 9, 16, 11, 21, 26];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _speaking;
    private OfflineTts? _engine;
    private bool _disposed;
    private const string ModelHash = "72C347230107EF8A31098EC4D468A605244BEEF5F1ED00DB97EFA13518800653";
    private const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2";

    public async Task SpeakAsync(string text, string voice, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync) { if (_disposed) { _gate.Release(); throw new ObjectDisposedException(nameof(LocalSpeechOutput)); } _speaking = current; }
        try
        {
            var token = current.Token;
            var plain = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", " Code is shown in the transcript. ");
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"[*#`_]", "");
            var index = Voices.ToList().IndexOf(voice);
            var id = VoiceIds[index < 0 ? 0 : index];
            var folder = await EnsureModelAsync(token).ConfigureAwait(false);
            await Task.Run(() => SpeakCore(plain, id, folder, token), token).ConfigureAwait(false);
        }
        finally { lock (_sync) { if (ReferenceEquals(_speaking, current)) _speaking = null; } _gate.Release(); }
    }

    private static async Task<string> EnsureModelAsync(CancellationToken token)
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "SutraVoice");
        if (File.Exists(Path.Combine(installed, "model.int8.onnx"))) return installed;
        var root = Path.Combine(Infrastructure.AppDataPaths.Root, "SutraVoice");
        var model = Path.Combine(root, "kokoro-int8-multi-lang-v1_0");
        if (File.Exists(Path.Combine(model, "model.int8.onnx"))) return model;
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException) { root = Path.Combine(AppContext.BaseDirectory, "SutraVoiceData"); Directory.CreateDirectory(root); model = Path.Combine(root, "kokoro-int8-multi-lang-v1_0"); }
        var archive = Path.Combine(root, "voice-download-" + Guid.NewGuid().ToString("N") + ".tar.bz2");
        var staging = Path.Combine(root, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            using var response = await http.GetAsync(ModelUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = File.Create(archive)) await input.CopyToAsync(output, token).ConfigureAwait(false);
            await using (var input = File.OpenRead(archive))
                if (!string.Equals(Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token).ConfigureAwait(false)), ModelHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("Local voice download failed verification.");
            Directory.CreateDirectory(staging);
            using var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo { FileName = "tar.exe", UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("-xf"); process.StartInfo.ArgumentList.Add(archive); process.StartInfo.ArgumentList.Add("-C"); process.StartInfo.ArgumentList.Add(staging);
            if (!process.Start()) throw new InvalidOperationException("Could not unpack the local voice model.");
            try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { try { process.Kill(); } catch { } throw; }
            if (process.ExitCode != 0 || !File.Exists(Path.Combine(staging, "kokoro-int8-multi-lang-v1_0", "model.int8.onnx"))) throw new InvalidOperationException("Could not unpack the local voice model.");
            Directory.Move(Path.Combine(staging, "kokoro-int8-multi-lang-v1_0"), model);
            return model;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException("Could not prepare local voice. Check your connection and folder permissions, then try again.", ex); }
        finally { try { File.Delete(archive); } catch { } try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    private void SpeakCore(string text, int voice, string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string FileIn(string name) => Path.Combine(folder, name);
        if (!File.Exists(FileIn("model.int8.onnx"))) throw new InvalidOperationException("Local voice model is missing. Reinstall Sutra Island to restore it.");
        _engine ??= new OfflineTts(new OfflineTtsConfig
        {
            Model = new OfflineTtsModelConfig
            {
                Provider = "cpu", NumThreads = Math.Clamp(Environment.ProcessorCount - 1, 1, 4),
                Kokoro = new OfflineTtsKokoroModelConfig
                {
                    Model = FileIn("model.int8.onnx"), Voices = FileIn("voices.bin"), Tokens = FileIn("tokens.txt"),
                    DataDir = FileIn("espeak-ng-data"), Lexicon = FileIn("lexicon-us-en.txt"), LengthScale = 1
                }
            }, MaxNumSentences = 1
        });
        var generated = _engine.GenerateWithCallback(text, 1f, voice, (_, _) => token.IsCancellationRequested ? 0 : 1);
        float[] samples;
        int sampleRate;
        try { token.ThrowIfCancellationRequested(); samples = generated.Samples; sampleRate = generated.SampleRate; }
        finally { generated.Dispose(); }
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[2 * i] = (byte)value; bytes[2 * i + 1] = (byte)(value >> 8);
        }
        using var stream = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(sampleRate, 16, 1));
        using var output = new WaveOutEvent();
        output.Init(stream); output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
        {
            if (token.WaitHandle.WaitOne(50)) { output.Stop(); token.ThrowIfCancellationRequested(); }
        }
    }

    public void StopSpeaking() { lock (_sync) _speaking?.Cancel(); }
    public void Dispose()
    {
        lock (_sync) { _disposed = true; _speaking?.Cancel(); }
        _ = Task.Run(async () => { await _gate.WaitAsync(); try { _engine?.Dispose(); _engine = null; } finally { _gate.Release(); } });
    }
}
