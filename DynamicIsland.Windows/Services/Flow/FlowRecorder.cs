using NAudio.Wave;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Records the default microphone as 16 kHz mono PCM (what Whisper expects) and
/// reports a smoothed loudness level about 33 times a second for the dot display.
/// </summary>
public sealed class FlowRecorder : IFlowAudio, IDisposable
{
    public const int SampleRate = FlowRecording.SampleRate;
    private const int MaxSeconds = 180;
    private readonly LoggingService _log;
    private readonly object _gate = new();
    private WaveInEvent? _wave;
    private Task _waveStopped = Task.CompletedTask;
    private List<float> _samples = new(SampleRate * 10);
    private float _peak;
    private float _level;

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
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wave.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) _log.Error("Sutra Flow: recording stopped with an error.", e.Exception);
                stopped.TrySetResult(true);
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
            _waveStopped = stopped.Task;
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
            if (!ReferenceEquals(sender, _wave) || _samples.Count > SampleRate * MaxSeconds) return;
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
        if (count == 0 || !ReferenceEquals(sender, _wave)) return;
        var rms = Math.Sqrt(sum / count);
        // Speech RMS sits roughly between 0.01 and 0.3; map it onto 0..1 on a log scale.
        var db = 20 * Math.Log10(Math.Max(rms, 1e-5));
        var target = (float)Math.Clamp((db + 52) / 40, 0, 1);
        _level = target > _level ? _level + (target - _level) * 0.6f : _level + (target - _level) * 0.25f;
    }

    /// <summary>
    /// Closes the microphone and returns what was recorded. Detaches the device and its samples
    /// first, so a later <see cref="Start"/> can never have its new device disposed by this call.
    /// </summary>
    public async Task<FlowRecording> StopAsync()
    {
        WaveInEvent? wave;
        Task stopped;
        lock (_gate)
        {
            wave = _wave;
            stopped = _waveStopped;
            _wave = null;
            IsRecording = false;
            _level = 0;
        }
        if (wave is not null)
        {
            try { wave.StopRecording(); } catch (Exception ex) { _log.Error("Sutra Flow: stop failed.", ex); }
            await Task.WhenAny(stopped, Task.Delay(400)).ConfigureAwait(false);
            wave.DataAvailable -= OnData;
            try { wave.Dispose(); } catch { }
        }
        lock (_gate) return new FlowRecording(_samples.ToArray(), _peak);
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
