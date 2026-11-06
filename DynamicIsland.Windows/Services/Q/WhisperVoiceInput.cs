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
    private const string CommandHint = "Pause. Unpause. Play. Resume. Skip. Next song. Previous song. Volume up. Set a timer for five minutes. What's on my calendar? Sutra Island.";
    private readonly LoggingService _log;
    private readonly WhisperEngine _engine;
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private readonly WakeSessionEpoch _wakeEpoch = new();
    private sealed record WakeConfiguration(WakeVoiceProfile? Profile, double CooldownSeconds, bool RequireTraining);
    private WakeConfiguration _wakeConfiguration = new(null, 4, false);
    private CancellationTokenSource? _wakeCancellation;
    private readonly CancellationTokenSource _lifetime = new();
    private long _lastWakeTick = -20000;
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

    public void ConfigureWakeProfile(WakeVoiceProfile? profile, double cooldownSeconds = 4, bool requireTraining = false)
    {
        lock (_gate)
        {
            Volatile.Write(ref _wakeConfiguration, new WakeConfiguration(profile is { IsValid: true } ? profile : null,
                double.IsFinite(cooldownSeconds) ? Math.Clamp(cooldownSeconds, 1, 15) : 4, requireTraining));
            // A settings change/disable invalidates a queued wake's one-breath command.
            // Ordinary StopWake -> Dictate still keeps that command for its handoff.
            _stash = null;
            if (_wakeSegmenter is not null)
                _wakeSegmenter.ThresholdDb = WakePhrase.SensitivityThreshold(_settings.AssistantWakeSensitivity);
        }
    }

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
            var epoch = _wakeEpoch.Next();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _wakeCancellation = cancellation;
            var wakeToken = cancellation.Token;
            var segmenter = new VoiceSegmenter(Rate, TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(300))
            { ThresholdDb = WakePhrase.SensitivityThreshold(_settings.AssistantWakeSensitivity) };
            _wakeSegmenter = segmenter;
            try { _wakeMic = OpenMic(frame =>
            {
                // Sutra Flow is dictating: its words are not meant for the assistant.
                if (FlowRecorder.IsRecording) { segmenter.Reset(); return; }
                var utterance = segmenter.Push(frame);
                if (utterance is null || utterance.Length > Rate * 7) return;
                if (Interlocked.Exchange(ref _checking, 1) == 1) return; // still checking the last burst
                _ = Task.Run(() => CheckWakeAsync(utterance, epoch, wakeToken));
            }); }
            catch { _wakeCancellation = null; cancellation.Dispose(); throw; }
        }
    }

    private async Task CheckWakeAsync(float[] utterance, int epoch, CancellationToken wakeToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(wakeToken);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            var configuration = Volatile.Read(ref _wakeConfiguration);
            if (configuration.RequireTraining && configuration.Profile is null) return;
            // A wake-word prompt can be hallucinated on background sounds. Let Whisper
            // transcribe freely, then validate its actual transcript and recorded audio.
            var text = await _engine.TranscribeAsync(utterance, [], limit.Token).ConfigureAwait(false);
            var match = WhisperWakePolicy.Check(text, utterance, Rate, configuration.Profile, configuration.RequireTraining);
            if (!match.Woke) return;
            lock (_gate)
            {
                // Transcription can complete after StopWake and even after a new
                // listener starts. Never deliver audio from an older session.
                if (_disposed || _wakeMic is null || !_wakeEpoch.IsCurrent(epoch)) return;
                // Settings may have changed while the model was transcribing.
                var currentConfiguration = Volatile.Read(ref _wakeConfiguration);
                if (!ReferenceEquals(configuration, currentConfiguration)) return;
                var tick = Environment.TickCount64;
                if (tick - _lastWakeTick < configuration.CooldownSeconds * 1000) return;
                _lastWakeTick = tick;
                _log.Info("Sutra Assistant: wake word heard.");
                _stash = match.Command.Length > 0 ? (FlowTextCleaner.Basic(match.Command), DateTime.UtcNow) : null;
                WakeDetected?.Invoke();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or FlowException) { }
        catch (Exception ex) { _log.Error("Sutra Assistant: wake check failed.", ex); }
        finally { Interlocked.Exchange(ref _checking, 0); }
    }

    public void StopWake()
    {
        WaveInEvent? mic;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _wakeEpoch.Next(); mic = _wakeMic; _wakeMic = null; _wakeSegmenter = null;
            cancellation = _wakeCancellation; _wakeCancellation = null;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (mic is null) return;
        try { mic.StopRecording(); } catch { }
        mic.Dispose();
        _level = 0;
    }

    public async Task<WakeVoiceProfile> TrainWakeAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady) throw new InvalidOperationException("Install Sutra Flow's local speech model before training with Whisper.");
        if (FlowRecorder.IsRecording) throw new InvalidOperationException("Finish Sutra Flow dictation before training the wake phrase.");
        StopWake();
        lock (_gate) _stash = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = lifetime.Token;
        var examples = new List<double[]>();
        var durations = new List<double>();
        for (var attempt = 0; examples.Count < WakeDetectionPolicy.RequiredSamples && attempt < 15; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (FlowRecorder.IsRecording) throw new InvalidOperationException("Finish Sutra Flow dictation before training the wake phrase.");
            progress?.Report($"Example {examples.Count + 1} of 5 — say “Hey Sutra”, then pause.");
            float[]? audio;
            string text = "";
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    audio = await CaptureTrainingUtteranceAsync(limit.Token).ConfigureAwait(false);
                    if (audio is not null) text = await _engine.TranscribeAsync(audio, [], limit.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { audio = null; }
            }
            token.ThrowIfCancellationRequested();
            if (audio is null || !WhisperWakePolicy.TryTrainingSample(text, audio, Rate, out var features, out var duration))
            {
                progress?.Report("That was not a clear “Hey Sutra” on its own. Wait for the next prompt and try again.");
                await Task.Delay(1400, token).ConfigureAwait(false);
                continue;
            }
            examples.Add(features); durations.Add(duration);
            progress?.Report($"Example {examples.Count} of 5 captured.");
            await Task.Delay(900, token).ConfigureAwait(false);
        }
        return WakeDetectionPolicy.CreateProfile(examples, durations);
    }

    private async Task<float[]?> CaptureTrainingUtteranceAsync(CancellationToken token)
    {
        var segmenter = new VoiceSegmenter(Rate, TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(300))
        { ThresholdDb = WakePhrase.SensitivityThreshold(_settings.AssistantWakeSensitivity) };
        var done = new TaskCompletionSource<float[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => done.TrySetCanceled(token));
        token.ThrowIfCancellationRequested();
        var mic = OpenMic(frame =>
        {
            if (done.Task.IsCompleted) return;
            if (FlowRecorder.IsRecording)
            {
                done.TrySetException(new InvalidOperationException("Sutra Flow is dictating. Finish it before training the wake phrase."));
                return;
            }
            var utterance = segmenter.Push(frame);
            if (utterance is not null) done.TrySetResult(utterance);
            else if (!segmenter.InSpeech && segmenter.Elapsed > 8) done.TrySetResult(null);
        });
        try { return await done.Task.ConfigureAwait(false); }
        finally
        {
            try { mic.StopRecording(); } catch { }
            mic.Dispose();
            _level = 0;
        }
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
        _lifetime.Cancel();
        StopWake();
    }
}
