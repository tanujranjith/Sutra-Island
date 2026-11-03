using NAudio.Wave;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Records the default microphone as 16 kHz mono PCM (what Whisper expects) and
/// reports a smoothed loudness level about 33 times a second for the dot display.
/// </summary>
public sealed class FlowRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int MaxSeconds = 180;
    private readonly LoggingService _log;
    private readonly object _gate = new();
    private WaveInEvent? _wave;
    private List<float> _samples = new(SampleRate * 10);
    private float _peak;
    private float _level;
    private TaskCompletionSource<bool>? _stopped;

    /// <summary>True while the microphone is open; the privacy indicator hides Island's own use.</summary>
    public static volatile bool IsRecording;

    /// <summary>0..1 loudness, smoothed. Read from any thread.</summary>
    public float Level => _level;

    public FlowRecorder(LoggingService log) => _log = log;

    /// <summary>Opens the microphone. Throws <see cref="FlowException"/> with a short reason on failure.</summary>
    public void Start()
    {
        lock (_gate)
        {
            StopDevice();
            _samples = new List<float>(SampleRate * 10);
            _peak = 0; _level = 0;
            if (WaveInEvent.DeviceCount == 0) throw new FlowException("No microphone found");
            var wave = new WaveInEvent
            {
                DeviceNumber = -1, // WAVE_MAPPER: follows the Windows default input device
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 30,
                NumberOfBuffers = 3,
            };
            wave.DataAvailable += OnData;
            wave.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) _log.Error("Sutra Flow: recording stopped with an error.", e.Exception);
                _stopped?.TrySetResult(true);
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
            _wave = wave;
            IsRecording = true;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        double sum = 0;
        float peak = 0;
        lock (_gate)
        {
            if (_samples.Count > SampleRate * MaxSeconds) return;
            for (var i = 0; i < count; i++)
            {
                var s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                _samples.Add(s);
                sum += s * s;
                var a = Math.Abs(s);
                if (a > peak) peak = a;
            }
            if (peak > _peak) _peak = peak;
        }
        if (count == 0) return;
        var rms = Math.Sqrt(sum / count);
        // Speech RMS sits roughly between 0.01 and 0.3; map it onto 0..1 on a log scale.
        var db = 20 * Math.Log10(Math.Max(rms, 1e-5));
        var target = (float)Math.Clamp((db + 52) / 40, 0, 1);
        _level = target > _level ? _level + (target - _level) * 0.6f : _level + (target - _level) * 0.25f;
    }

    /// <summary>Closes the microphone and returns what was recorded.</summary>
    public async Task<FlowRecording> StopAsync()
    {
        WaveInEvent? wave;
        lock (_gate)
        {
            wave = _wave;
            _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (wave is not null)
        {
            try { wave.StopRecording(); } catch (Exception ex) { _log.Error("Sutra Flow: stop failed.", ex); }
            await Task.WhenAny(_stopped.Task, Task.Delay(400)).ConfigureAwait(false);
        }
        lock (_gate)
        {
            StopDevice();
            _level = 0;
            return new FlowRecording(_samples.ToArray(), _peak);
        }
    }

    private void StopDevice()
    {
        if (_wave is not null)
        {
            _wave.DataAvailable -= OnData;
            try { _wave.Dispose(); } catch { }
            _wave = null;
        }
        IsRecording = false;
    }

    public void Dispose()
    {
        lock (_gate) StopDevice();
    }
}

public sealed record FlowRecording(float[] Samples, float Peak)
{
    public double Seconds => Samples.Length / (double)FlowRecorder.SampleRate;
}

/// <summary>A failure with a short, user-facing reason for the island.</summary>
public sealed class FlowException(string message) : Exception(message);
