using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Flow;
using NAudio.Wave;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>
/// Sutra Assistant's ears, built on Sutra Flow's local Whisper model instead of the old
/// Windows recognizer. A light voice detector watches the microphone; each short burst of
/// speech is transcribed and checked for "Hey Sutra". Questions are recorded until about
/// 0.8 s of silence, then transcribed with the island's command words as hints.
/// </summary>
public sealed class WhisperVoiceInput : IDisposable
{
    private const int Rate = FlowRecorder.SampleRate;
    private const string WakeHint = "Hey Sutra. Hey Sutra, what's the weather? Hey Sutra, pause the music.";
    private const string CommandHint = "Pause. Unpause. Play. Resume. Skip. Next song. Previous song. Volume up. Set a timer for five minutes. What's on my calendar? Sutra Island.";
    private readonly LoggingService _log;
    private readonly WhisperEngine _engine;
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private WaveInEvent? _wakeMic;
    private VoiceSegmenter? _wakeSegmenter;
    private int _checking;
    private (string Text, DateTime At)? _stash;
    private volatile float _level;
    private bool _disposed;

    public WhisperVoiceInput(LoggingService log, WhisperEngine engine, AppSettings settings)
    { _log = log; _engine = engine; _settings = settings; }

    /// <summary>True once Flow's speech model is on disk. Until then the Windows recognizer is used.</summary>
    public bool IsReady => !_disposed && _engine.IsModelInstalled && WaveInEvent.DeviceCount > 0;
    /// <summary>Smoothed microphone level, 0..1, for the assistant's dots.</summary>
    public float Level => _level;
    public event Action? WakeDetected;

    private IReadOnlyList<string> Vocabulary() =>
        _settings.FlowVocabulary.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();

    private WaveInEvent OpenMic(Action<float[]> onFrame)
    {
        var wave = new WaveInEvent
        {
            DeviceNumber = FlowRecorder.DeviceNumberFor(_settings.FlowMicrophone),
            WaveFormat = new WaveFormat(Rate, 16, 1),
            BufferMilliseconds = 30,
            NumberOfBuffers = 4,
        };
        wave.DataAvailable += (_, e) =>
        {
            var count = e.BytesRecorded / 2;
            if (count == 0) return;
            var frame = new float[count];
            for (var i = 0; i < count; i++) frame[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            var target = (float)Math.Clamp((VoiceSegmenter.DbOf(frame) + 52) / 40, 0, 1);
            _level = target > _level ? _level + (target - _level) * 0.6f : _level + (target - _level) * 0.25f;
            try { onFrame(frame); } catch (Exception ex) { _log.Error("Sutra Assistant: voice frame failed.", ex); }
        };
        wave.RecordingStopped += (_, e) => { if (e.Exception is not null) _log.Error("Sutra Assistant: microphone stopped.", e.Exception); };
        try { wave.StartRecording(); }
        catch { wave.Dispose(); throw new InvalidOperationException("Microphone access is blocked. Check Windows privacy settings."); }
        return wave;
    }

    public void StartWake()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_wakeMic is not null) return;
            var segmenter = new VoiceSegmenter(Rate, TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(300))
            { ThresholdDb = WakePhrase.SensitivityThreshold(_settings.AssistantWakeSensitivity) };
            _wakeSegmenter = segmenter;
            _wakeMic = OpenMic(frame =>
            {
                // Sutra Flow is dictating: its words are not meant for the assistant.
                if (FlowRecorder.IsRecording) { segmenter.Reset(); return; }
                var utterance = segmenter.Push(frame);
                if (utterance is null || utterance.Length > Rate * 7) return;
                if (Interlocked.Exchange(ref _checking, 1) == 1) return; // still checking the last burst
                _ = Task.Run(() => CheckWakeAsync(utterance));
            });
        }
    }

    private async Task CheckWakeAsync(float[] utterance)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var text = await _engine.TranscribeAsync(utterance, Vocabulary(), limit.Token, hint: WakeHint).ConfigureAwait(false);
            var match = WakePhrase.Check(text, strict: string.Equals(_settings.AssistantWakeSensitivity, "low", StringComparison.OrdinalIgnoreCase));
            if (!match.Woke) return;
            _log.Info("Sutra Assistant: wake word heard.");
            lock (_gate) _stash = match.Command.Length > 0 ? (FlowTextCleaner.Basic(match.Command), DateTime.UtcNow) : null;
            WakeDetected?.Invoke();
        }
        catch (Exception ex) when (ex is OperationCanceledException or FlowException) { }
        catch (Exception ex) { _log.Error("Sutra Assistant: wake check failed.", ex); }
        finally { Interlocked.Exchange(ref _checking, 0); }
    }

    public void StopWake()
    {
        WaveInEvent? mic;
        lock (_gate) { mic = _wakeMic; _wakeMic = null; _wakeSegmenter = null; }
        if (mic is null) return;
        try { mic.StopRecording(); } catch { }
        mic.Dispose();
        _level = 0;
    }

    /// <summary>Records one question and returns its text, or null if nothing was said.</summary>
    public async Task<string?> DictateAsync(CancellationToken token)
    {
        lock (_gate)
        {
            // "Hey Sutra, pause the music" in one breath: the command came with the wake word.
            if (_stash is { } s && DateTime.UtcNow - s.At < TimeSpan.FromSeconds(8)) { _stash = null; return s.Text; }
            _stash = null;
        }
        StopWake();
        var segmenter = new VoiceSegmenter(Rate, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(200));
        var done = new TaskCompletionSource<float[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mic = OpenMic(frame =>
        {
            if (done.Task.IsCompleted) return;
            var utterance = segmenter.Push(frame);
            if (utterance is not null) done.TrySetResult(utterance);
            else if (!segmenter.InSpeech && segmenter.Elapsed > 8) done.TrySetResult(null); // nobody spoke
        });
        float[]? audio;
        try
        {
            using var registration = token.Register(() => done.TrySetCanceled(token));
            audio = await done.Task.ConfigureAwait(false);
        }
        finally
        {
            try { mic.StopRecording(); } catch { }
            mic.Dispose();
            _level = 0;
        }
        if (audio is null) return null;
        var raw = await Task.Run(() => _engine.TranscribeAsync(audio, Vocabulary(), token, hint: CommandHint), token).ConfigureAwait(false);
        var text = FlowTextCleaner.Basic(raw);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWake();
    }
}
