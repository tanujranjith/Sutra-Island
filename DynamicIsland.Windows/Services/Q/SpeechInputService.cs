using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>
/// Sutra Assistant's voice input and output. Listening ("Hey Sutra" and questions) uses Sutra
/// Flow's local Whisper model once it is installed, and falls back to the Windows recognizer
/// until then. Speaking always uses Windows text-to-speech.
/// </summary>
public sealed class SpeechInputService : IQSpeechInputService, IWakeWordInput, ISpeechOutput, IDisposable
{
    private readonly LoggingService _log;
    private readonly WindowsVoicePlatform _windows;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperVoiceInput? _whisper;
    private bool _wakeOnWhisper;
    private bool _wakeOnWindows;

    public static IReadOnlyList<string> GetInstalledVoices() => WindowsVoicePlatform.GetInstalledVoices();
    public event Action? WakeDetected;
    /// <summary>Which recognizer listens right now, for the settings page.</summary>
    public string EngineName => UseWhisper ? "Whisper (local, shared with Sutra Flow)" : "Windows speech recognition";
    public float Level => UseWhisper ? _whisper!.Level : _windows.Level;
    /// <summary>Interim words while dictating (the Windows recognizer provides them; Whisper reports the final text only).</summary>
    public event Action<string>? PartialTranscript;
    private bool UseWhisper => _whisper?.IsReady == true;

    public SpeechInputService(LoggingService log)
    {
        _log = log;
        _windows = new WindowsVoicePlatform(log);
        _windows.WakeDetected += () => WakeDetected?.Invoke();
        _windows.PartialTranscript += text => PartialTranscript?.Invoke(text);
    }

    /// <summary>Called once Sutra Flow (and its Whisper model) exists.</summary>
    public void AttachWhisper(WhisperVoiceInput whisper)
    {
        _whisper = whisper;
        whisper.WakeDetected += () => WakeDetected?.Invoke();
    }

    public bool IsAvailable => UseWhisper || _windows.IsAvailable;

    public async Task StartWakeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (UseWhisper)
            {
                if (_wakeOnWindows) { await _windows.StopWakeAsync().ConfigureAwait(false); _wakeOnWindows = false; }
                _whisper!.StartWake();
                _wakeOnWhisper = true;
            }
            else
            {
                await _windows.StartWakeAsync(cancellationToken).ConfigureAwait(false);
                _wakeOnWindows = true;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopWakeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_wakeOnWhisper) { _whisper?.StopWake(); _wakeOnWhisper = false; }
            if (_wakeOnWindows) { await _windows.StopWakeAsync().ConfigureAwait(false); _wakeOnWindows = false; }
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> DictateAsync(CancellationToken cancellationToken)
    {
        if (!UseWhisper) return await _windows.DictateAsync(cancellationToken).ConfigureAwait(false);
        try { return await _whisper!.DictateAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("Sutra Assistant: Whisper dictation failed.", ex);
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public Task SpeakAsync(string text, string voice, CancellationToken cancellationToken) => _windows.SpeakAsync(text, voice, cancellationToken);
    public void StopSpeaking() => _windows.StopSpeaking();

    public void Dispose()
    {
        _whisper?.Dispose();
        _windows.Dispose();
    }
}
