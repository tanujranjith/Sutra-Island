using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;

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
    private sealed record WakeConfiguration(WakeVoiceProfile? Profile, double Confidence, double CooldownSeconds, bool RequireTraining);
    private WakeConfiguration _wakeConfiguration = new(null, .86, 4, false);
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        _whisper = whisper;
        var configuration = Volatile.Read(ref _wakeConfiguration);
        whisper.ConfigureWakeProfile(configuration.Profile, configuration.CooldownSeconds, configuration.RequireTraining);
        whisper.WakeDetected += () => WakeDetected?.Invoke();
    }

    public void ConfigureWakeProfile(WakeVoiceProfile? profile, double sensitivity = .86,
        double cooldownSeconds = 4, bool requireTraining = false)
    {
        var configuration = new WakeConfiguration(profile is { IsValid: true } ? profile : null,
            double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, .80, .97) : .86,
            double.IsFinite(cooldownSeconds) ? Math.Clamp(cooldownSeconds, 1, 15) : 4, requireTraining);
        Volatile.Write(ref _wakeConfiguration, configuration);
        _windows.ConfigureWakeProfile(configuration.Profile, configuration.Confidence, configuration.CooldownSeconds, configuration.RequireTraining);
        _whisper?.ConfigureWakeProfile(configuration.Profile, configuration.CooldownSeconds, configuration.RequireTraining);
    }

    public bool IsAvailable => UseWhisper || _windows.IsAvailable;

    public async Task StartWakeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (UseWhisper)
            {
                if (_wakeOnWindows) { await _windows.StopWakeAsync().ConfigureAwait(false); _wakeOnWindows = false; }
                _whisper!.StartWake();
                _wakeOnWhisper = true;
            }
            else
            {
                if (_wakeOnWhisper) { _whisper?.StopWake(); _wakeOnWhisper = false; }
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
            await StopWakeCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> DictateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopWakeCoreAsync().ConfigureAwait(false);
            return UseWhisper ? await _whisper!.DictateAsync(cancellationToken).ConfigureAwait(false)
                : await _windows.DictateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("Sutra Assistant: Whisper dictation failed.", ex);
            throw new InvalidOperationException(ex.Message, ex);
        }
        finally { _gate.Release(); }
    }

    public async Task<WakeVoiceProfile> TrainWakeAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopWakeCoreAsync().ConfigureAwait(false);
            return UseWhisper ? await _whisper!.TrainWakeAsync(progress, cancellationToken).ConfigureAwait(false)
                : await _windows.TrainWakeAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task StopWakeCoreAsync()
    {
        if (_wakeOnWhisper) { _whisper?.StopWake(); _wakeOnWhisper = false; }
        if (_wakeOnWindows) { await _windows.StopWakeAsync().ConfigureAwait(false); _wakeOnWindows = false; }
    }

    public Task SpeakAsync(string text, string voice, CancellationToken cancellationToken) => _windows.SpeakAsync(text, voice, cancellationToken);
    public void StopSpeaking() => _windows.StopSpeaking();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _whisper?.Dispose();
        _windows.Dispose();
    }
}
