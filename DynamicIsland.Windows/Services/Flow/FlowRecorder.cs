using NAudio.Wave;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Records the default microphone as 16 kHz mono PCM (what Whisper expects) and
/// reports a smoothed loudness level about 33 times a second for the dot display.
///
/// Each <see cref="Start"/> creates its own <see cref="Take"/>: the device, its samples, and its
/// stop signal. Buffers are matched to their take, so a buffer that arrives while a take is
/// stopping still lands in that take, and a take can never touch another take's device.
/// </summary>
public sealed class FlowRecorder : IFlowAudio, IDisposable
{
    public const int SampleRate = FlowRecording.SampleRate;
    private const int MaxSeconds = 180;
    /// <summary>Keep listening briefly after release so the last syllable isn't cut off.</summary>
    private static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DrainLimit = TimeSpan.FromMilliseconds(500);

    private readonly LoggingService _log;
    private readonly object _gate = new();
    private Take? _current;
    private float _level;

    /// <summary>True while the microphone is open; the privacy indicator hides Island's own use.</summary>
    public static volatile bool IsRecording;

    /// <summary>0..1 loudness, smoothed. Read from any thread.</summary>
    public float Level => _level;

    public FlowRecorder(LoggingService log) => _log = log;

    private sealed class Take(WaveInEvent wave)
    {
        public readonly WaveInEvent Wave = wave;
        public readonly List<float> Samples = new(SampleRate * 10);
        public readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public float Peak;
    }

    /// <summary>Opens the microphone. Throws <see cref="FlowException"/> with a short reason on failure.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_current is not null) throw new InvalidOperationException("A recording is already running.");
            if (WaveInEvent.DeviceCount == 0) throw new FlowException("No microphone found");
            _level = 0;
            var wave = new WaveInEvent
            {
                DeviceNumber = -1, // WAVE_MAPPER: follows the Windows default input device
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 30,
                NumberOfBuffers = 3,
            };
            var take = new Take(wave);
            wave.DataAvailable += (_, e) => OnData(take, e);
            wave.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) _log.Error("Sutra Flow: recording stopped with an error.", e.Exception);
                take.Stopped.TrySetResult();
            };
            try
            {
                wave.StartRecording();
            }
            catch (Exception ex)
            {
                wave.Dispose();
                _log.Error("Sutra Flow: could not open the microphone.", ex);
                throw new FlowException("Microphone access blocked");
            }
            _current = take;
            IsRecording = true;
        }
    }

    private void OnData(Take take, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        if (count == 0) return;
        double sum = 0;
        float peak = 0;
        lock (take.Samples)
        {
            if (take.Samples.Count > SampleRate * MaxSeconds) return;
            for (var i = 0; i < count; i++)
            {
                var s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                take.Samples.Add(s);
                sum += s * s;
                var a = Math.Abs(s);
                if (a > peak) peak = a;
            }
            if (peak > take.Peak) take.Peak = peak;
        }
        if (!ReferenceEquals(take, _current)) return; // a stopping take still keeps its audio, but doesn't drive the dots
        var rms = Math.Sqrt(sum / count);
        // Speech RMS sits roughly between 0.01 and 0.3; map it onto 0..1 on a log scale.
        var db = 20 * Math.Log10(Math.Max(rms, 1e-5));
        var target = (float)Math.Clamp((db + 52) / 40, 0, 1);
        _level = target > _level ? _level + (target - _level) * 0.6f : _level + (target - _level) * 0.25f;
    }

    /// <summary>
    /// Records a short tail, stops the device, waits for its final buffers, and returns everything
    /// it captured. Only ever stops and disposes the take it detached.
    /// </summary>
    public async Task<FlowRecording> StopAsync()
    {
        Take? take;
        lock (_gate) take = _current;
        if (take is null) return new FlowRecording([], 0);

        await Task.Delay(Tail).ConfigureAwait(false);
        lock (_gate)
        {
            if (ReferenceEquals(_current, take)) _current = null;
            IsRecording = _current is not null;
            _level = 0;
        }
        try { take.Wave.StopRecording(); } catch (Exception ex) { _log.Error("Sutra Flow: stop failed.", ex); }
        // RecordingStopped fires after the capture thread has delivered its last buffer.
        if (await Task.WhenAny(take.Stopped.Task, Task.Delay(DrainLimit)).ConfigureAwait(false) != take.Stopped.Task)
            _log.Info("Sutra Flow: microphone was slow to stop; using what arrived.");
        try { take.Wave.Dispose(); } catch { }
        lock (take.Samples) return new FlowRecording(take.Samples.ToArray(), take.Peak);
    }

    public void Dispose()
    {
        Take? take;
        lock (_gate) { take = _current; _current = null; IsRecording = false; }
        if (take is null) return;
        try { take.Wave.StopRecording(); } catch { }
        try { take.Wave.Dispose(); } catch { }
    }
}
