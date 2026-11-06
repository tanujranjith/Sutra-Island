using System.Speech.Recognition;
using System.Speech.Synthesis;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>One Windows microphone owner shared by wake detection and bounded dictation.</summary>
public class WindowsVoicePlatform(LoggingService log) : IQSpeechInputService, IWakeWordInput, ISpeechOutput, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpeechRecognitionEngine? _wake;
    private SpeechSynthesizer? _speaker;
    private sealed record WakeConfiguration(WakeVoiceProfile? Profile, double Confidence, double CooldownSeconds, bool RequireTraining);
    private WakeConfiguration _wakeConfiguration = new(null, .86, 4, false);
    private readonly object _wakeDecisionGate = new();
    private long _lastWakeTick = -10000;
    private bool _disposed;
    private const string WakeGrammarName = "SutraWakePhrase";
    private static RecognizerInfo? Recognizer => SpeechRecognitionEngine.InstalledRecognizers()
        .FirstOrDefault(x => x.Culture.Name == "en-US") ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(x => x.Culture.TwoLetterISOLanguageName == "en");
    public bool IsAvailable { get { try { return !_disposed && Recognizer is not null; } catch { return false; } } }
    public event Action? WakeDetected;
    /// <summary>Interim dictation hypotheses, so the compact voice orb can show words as they're spoken.</summary>
    public event Action<string>? PartialTranscript;
    private volatile float _level;
    /// <summary>Microphone level (0..1) while dictating.</summary>
    public float Level => _level;
    public void ConfigureWakeProfile(WakeVoiceProfile? profile, double sensitivity = .86,
        double cooldownSeconds = 4, bool requireTraining = false) =>
        Volatile.Write(ref _wakeConfiguration, new WakeConfiguration(profile is { IsValid: true } ? profile : null,
            double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, .80, .97) : .86,
            double.IsFinite(cooldownSeconds) ? Math.Clamp(cooldownSeconds, 1, 15) : 4, requireTraining));
    public static IReadOnlyList<string> GetInstalledVoices()
    {
        try { using var speaker = new SpeechSynthesizer(); return speaker.GetInstalledVoices().Where(x => x.Enabled).Select(x => x.VoiceInfo.Name).ToArray(); }
        catch { return []; }
    }
    private static SpeechRecognitionEngine CreateRecognizer()
    {
        var info = Recognizer ?? throw new InvalidOperationException("Install an English speech language in Windows Settings → Time & language → Speech.");
        var engine = new SpeechRecognitionEngine(info);
        try { engine.SetInputToDefaultAudioDevice(); return engine; }
        catch { engine.Dispose(); throw; }
    }
    public async Task StartWakeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_wake is not null) return;
            _wake = await Task.Run(() =>
            {
                var engine = CreateRecognizer();
                try
                {
                    LoadWakeGrammars(engine);
                    engine.SpeechRecognized += WakeRecognized;
                    engine.RecognizeAsync(RecognizeMode.Multiple);
                    return engine;
                }
                catch { engine.Dispose(); throw; }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private void WakeRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _wake)) return;
        try
        {
            var configuration = Volatile.Read(ref _wakeConfiguration);
            var profile = configuration.Profile;
            if (configuration.RequireTraining && profile is null) return;
            var result = e.Result;
            if (!WakeDetectionPolicy.IsPhrase(result.Text, result.Confidence,
                result.Words.Select(x => (double)x.Confidence).ToArray(), result.Audio.Duration.TotalSeconds,
                configuration.Confidence)) return;
            var features = ExtractWakeFeatures(result);
            if (features is null || (profile is not null
                && !WakeDetectionPolicy.Matches(profile, features, result.Audio.Duration.TotalSeconds))) return;
            lock (_wakeDecisionGate)
            {
                var tick = Environment.TickCount64;
                if (tick - _lastWakeTick < configuration.CooldownSeconds * 1000) return;
                _lastWakeTick = tick;
            }
            WakeDetected?.Invoke();
        }
        catch (Exception ex) { log.Error("Wake phrase validation failed", ex); }
    }
    private static void LoadWakeGrammars(SpeechRecognitionEngine engine)
    {
        var grammar = new GrammarBuilder { Culture = engine.RecognizerInfo.Culture };
        grammar.Append("hey sutra");
        engine.LoadGrammar(new Grammar(grammar) { Name = WakeGrammarName });
        // With only one possible phrase, unrelated speech can be forced into that phrase.
        // A competing dictation grammar lets the recognizer classify ordinary speech instead.
        engine.LoadGrammar(new DictationGrammar { Name = "OtherSpeech" });
        engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(350);
        engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(600);
    }
    private static double[]? ExtractWakeFeatures(RecognitionResult result)
    {
        var format = result.Audio.Format;
        if (format.EncodingFormat != System.Speech.AudioFormat.EncodingFormat.Pcm) return null;
        using var audio = new MemoryStream();
        result.Audio.WriteToAudioStream(audio);
        return WakeDetectionPolicy.ExtractFeatures(audio.ToArray(), format.SamplesPerSecond, format.BitsPerSample, format.ChannelCount);
    }
    public async Task<WakeVoiceProfile> TrainWakeAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopWakeCore();
            using var engine = await Task.Run(CreateRecognizer, cancellationToken).ConfigureAwait(false);
            LoadWakeGrammars(engine);
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
            engine.BabbleTimeout = TimeSpan.FromSeconds(5);
            var samples = new List<double[]>();
            var durations = new List<double>();
            for (var attempt = 0; samples.Count < WakeDetectionPolicy.RequiredSamples && attempt < 15; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Example {samples.Count + 1} of 5 — say “Hey Sutra”, then pause.");
                RecognitionResult? result;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    try { result = await RecognizeOneAsync(engine, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { result = null; }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (result is null || !WakeDetectionPolicy.IsPhrase(result.Text, result.Confidence,
                    result.Words.Select(x => (double)x.Confidence).ToArray(), result.Audio.Duration.TotalSeconds, minimumConfidence: .82))
                {
                    progress?.Report("That was not a clear “Hey Sutra”. Try again after the next prompt.");
                    await Task.Delay(1300, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var features = ExtractWakeFeatures(result);
                if (features is null)
                {
                    progress?.Report("The microphone was too quiet or distorted. Adjust its position and try again.");
                    await Task.Delay(1600, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                samples.Add(features);
                durations.Add(result.Audio.Duration.TotalSeconds);
                progress?.Report($"Example {samples.Count} of 5 captured.");
                await Task.Delay(900, cancellationToken).ConfigureAwait(false);
            }
            return WakeDetectionPolicy.CreateProfile(samples, durations);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Error("Wake voice training failed", ex);
            throw new InvalidOperationException("Could not finish voice training. " + ex.Message, ex);
        }
        finally { _gate.Release(); }
    }
    private static async Task<RecognitionResult?> RecognizeOneAsync(SpeechRecognitionEngine engine, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<RecognitionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, RecognizeCompletedEventArgs e)
        {
            if (e.Error is not null) completion.TrySetException(e.Error);
            else if (e.Cancelled) completion.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
            else completion.TrySetResult(e.Result);
        }
        engine.RecognizeCompleted += Completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            engine.RecognizeAsync(RecognizeMode.Single);
            using var registration = cancellationToken.Register(() =>
            { try { engine.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } });
            // Await recognizer completion as well as cancellation before reusing the microphone.
            return await completion.Task.ConfigureAwait(false);
        }
        finally { engine.RecognizeCompleted -= Completed; }
    }
    public async Task StopWakeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { StopWakeCore(); }
        finally { _gate.Release(); }
    }
    private void StopWakeCore()
    {
        var wake = _wake; _wake = null;
        if (wake is null) return;
        wake.SpeechRecognized -= WakeRecognized;
        try { wake.RecognizeAsyncCancel(); } catch (InvalidOperationException) { }
        wake.Dispose();
    }
    public async Task<string?> DictateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopWakeCore();
            using var engine = await Task.Run(CreateRecognizer, cancellationToken).ConfigureAwait(false);
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
            engine.BabbleTimeout = TimeSpan.FromSeconds(8);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(800);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1200);
            engine.LoadGrammar(new DictationGrammar());
            engine.SpeechHypothesized += (_, e) => { var text = e.Result?.Text; if (!string.IsNullOrWhiteSpace(text)) PartialTranscript?.Invoke(text); };
            engine.AudioLevelUpdated += (_, e) => _level = Math.Clamp(e.AudioLevel / 100f, 0f, 1f);
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.RecognizeCompleted += (_, e) =>
            {
                if (e.Error is not null) completion.TrySetException(e.Error);
                else if (e.Cancelled) completion.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
                else completion.TrySetResult(e.Result?.Text);
            };
            engine.RecognizeAsync(RecognizeMode.Single);
            using var registration = cancellationToken.Register(() =>
            { try { engine.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } });
            try { return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally { _level = 0; }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log.Error("Windows dictation failed", ex); throw new InvalidOperationException("Check Windows microphone access and installed speech language. " + ex.Message, ex); }
        finally { _gate.Release(); }
    }
    public async Task SpeakAsync(string text, string voice, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopWakeAsync().ConfigureAwait(false);
        using var speaker = new SpeechSynthesizer();
        _speaker = speaker;
        try
        {
            speaker.SetOutputToDefaultAudioDevice();
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try { speaker.SelectVoice(voice); }
                catch (ArgumentException) { log.Info("Selected voice is no longer installed; using the Windows default voice."); }
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            speaker.SpeakCompleted += (_, e) =>
            { if (e.Error is not null) completion.TrySetException(e.Error); else if (e.Cancelled) completion.TrySetCanceled(); else completion.TrySetResult(); };
            cancellationToken.ThrowIfCancellationRequested();
            var plain = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", " Code is shown in the transcript. ");
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"[*#`_]", "");
            speaker.SpeakAsync(plain);
            using var registration = cancellationToken.Register(() => speaker.SpeakAsyncCancelAll());
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { if (ReferenceEquals(_speaker, speaker)) _speaker = null; }
    }
    public void StopSpeaking() { try { _speaker?.SpeakAsyncCancelAll(); } catch (ObjectDisposedException) { } }
    public void Dispose()
    { if (_disposed) return; _disposed = true; StopSpeaking(); _ = StopWakeAsync(); }
}
