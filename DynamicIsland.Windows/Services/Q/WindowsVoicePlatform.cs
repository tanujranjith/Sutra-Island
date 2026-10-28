using System.Speech.Recognition;
using System.Speech.Synthesis;
using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>One Windows microphone owner shared by wake detection and bounded dictation.</summary>
public class WindowsVoicePlatform(LoggingService log) : IQSpeechInputService, IWakeWordInput, ISpeechOutput, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpeechRecognitionEngine? _wake;
    private SpeechSynthesizer? _speaker;
    private bool _disposed;
    private static RecognizerInfo? Recognizer => SpeechRecognitionEngine.InstalledRecognizers()
        .FirstOrDefault(x => x.Culture.Name == "en-US") ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(x => x.Culture.TwoLetterISOLanguageName == "en");
    public bool IsAvailable { get { try { return !_disposed && Recognizer is not null; } catch { return false; } } }
    public event Action? WakeDetected;
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
                    var grammar = new GrammarBuilder { Culture = engine.RecognizerInfo.Culture };
                    grammar.Append("hey sutra");
                    engine.LoadGrammar(new Grammar(grammar));
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
    { if (!_disposed && e.Result.Confidence >= .70f && string.Equals(e.Result.Text, "hey sutra", StringComparison.OrdinalIgnoreCase)) WakeDetected?.Invoke(); }
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
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
