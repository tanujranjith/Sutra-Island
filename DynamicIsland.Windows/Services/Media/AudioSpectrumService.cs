using System.Runtime.InteropServices;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Interop;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Real audio spectrum from a WASAPI loopback capture of the default render device. Computes
/// broadband frequency-band levels (Hann-windowed FFT) to drive the island's visualiser bars.
/// Best-effort: if loopback can't initialise, <see cref="IsActive"/> stays false and the UI falls back
/// to the animated wave.
/// </summary>
public sealed class AudioSpectrumService(LoggingService log) : IDisposable
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
    public const int BandCount = SpectrumAnalyzer.BandCount;
    private const int Window = SpectrumAnalyzer.WindowSize;
    private const int HopSize = SpectrumAnalyzer.WindowSize / 4;
    private const int OverlapSize = SpectrumAnalyzer.WindowSize - HopSize;

    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _shutdown;
    private Thread? _thread;
    private readonly double[] _bands = new double[BandCount];
    private readonly float[] _buffer = new float[Window];
    private int _bufferPos;
    private double _loudness;
    private long _lastEmit;

    public event EventHandler<double[]>? BandsChanged;
    public bool IsActive { get; private set; }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_thread is not null) return;
            var shutdown = new CancellationTokenSource();
            _shutdown = shutdown;
            _thread = new Thread(() => Loop(shutdown))
            {
                IsBackground = true,
                Name = "DynamicIsland.Spectrum"
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        CancellationTokenSource? shutdown;
        lock (_lifecycleLock)
        {
            thread = _thread;
            shutdown = _shutdown;
            if (thread is null || shutdown is null) return;
            _thread = null;
            _shutdown = null;
            shutdown.Cancel();
        }
        // Do not dispose the token source while a stalled native call could still return
        // to the loop and access its token. A normal stop completes well within this bound.
        thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Loop(CancellationTokenSource shutdown)
    {
        var token = shutdown.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                CaptureOnce(token);
                if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    break;
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_thread, Thread.CurrentThread))
                {
                    _thread = null;
                    _shutdown = null;
                }
            }
            shutdown.Dispose();
        }
    }

    private void CaptureOnce(CancellationToken token)
    {
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var formatPtr = nint.Zero;
        try
        {
            (device, client) = CoreAudioFactory.ActivateDefault<IAudioClient>();
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out formatPtr));
            var format = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
            var subFormat = format.wFormatTag == 0xFFFE && format.cbSize >= 22
                ? Marshal.PtrToStructure<Guid>(formatPtr + 24)
                : Guid.Empty;
            var isFloat = format.wFormatTag == 3 || subFormat == IeeeFloatSubFormat;
            var isPcm = format.wFormatTag == 1 || subFormat == PcmSubFormat;
            var bitsPerSample = format.wBitsPerSample;
            var bytesPerSample = (bitsPerSample + 7) / 8;
            if ((!isFloat && !isPcm) ||
                (isFloat && bitsPerSample is not (32 or 64)) ||
                (isPcm && bitsPerSample is not (8 or 16 or 24 or 32)))
                throw new InvalidDataException($"Unsupported loopback sample format: tag {format.wFormatTag}, {bitsPerSample} bits.");

            const uint loopback = 0x00020000;
            Marshal.ThrowExceptionForHR(client.Initialize(0, loopback, 2_000_000, 0, formatPtr, nint.Zero));
            var captureIid = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(ref captureIid, out var captureObj));
            capture = (IAudioCaptureClient)captureObj;
            Marshal.ThrowExceptionForHR(client.Start());
            IsActive = true;

            var channels = Math.Max(1, (int)format.nChannels);
            var sampleRate = format.nSamplesPerSec;

            while (!token.IsCancellationRequested)
            {
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out var packetFrames));
                if (packetFrames == 0) { token.WaitHandle.WaitOne(12); continue; }

                while (packetFrames != 0)
                {
                    var hr = capture.GetBuffer(out var dataPtr, out var frames, out var flags, out _, out _);
                    if (hr < 0) break;
                    var silent = (flags & 0x2) != 0; // AUDCLNT_BUFFERFLAGS_SILENT
                    if (frames > 0 && (silent || dataPtr != nint.Zero))
                        Accumulate(dataPtr, (int)frames, channels, bytesPerSample, bitsPerSample, isFloat, silent, sampleRate);
                    capture.ReleaseBuffer(frames);
                    capture.GetNextPacketSize(out packetFrames);
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                log.Debug($"Audio loopback unavailable; retrying: {ex.Message}");
        }
        finally
        {
            IsActive = false;
            try { client?.Stop(); } catch { }
            if (formatPtr != nint.Zero) Marshal.FreeCoTaskMem(formatPtr);
            CoreAudioFactory.Release(capture);
            CoreAudioFactory.Release(client);
            CoreAudioFactory.Release(device);
        }
    }

    private unsafe void Accumulate(nint data, int frames, int channels, int bytesPerSample,
        int bitsPerSample, bool isFloat, bool silent, uint sampleRate)
    {
        var ptr = (byte*)data;
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var sampleByte = ptr + (f * channels + c) * bytesPerSample;
                var sample = silent ? 0 : isFloat
                    ? bytesPerSample == 4 ? *(float*)sampleByte : *(double*)sampleByte
                    : bitsPerSample switch
                    {
                        8 => (sampleByte[0] - 128) / 128.0,
                        16 => *(short*)sampleByte / 32768.0,
                        24 => DecodePcm24(sampleByte),
                        32 => *(int*)sampleByte / 2147483648.0,
                        _ => 0,
                    };
                sum += double.IsFinite(sample) ? sample : 0;
            }
            _buffer[_bufferPos++] = (float)(sum / channels);
            if (_bufferPos >= Window)
            {
                Analyze(sampleRate);
                // Retain 75% overlap so the spectrum updates every hop instead of jumping once
                // per non-overlapping window.
                Array.Copy(_buffer, HopSize, _buffer, 0, OverlapSize);
                _bufferPos = OverlapSize;
            }
        }
    }

    private static unsafe double DecodePcm24(byte* sample)
    {
        var value = sample[0] | (sample[1] << 8) | (sample[2] << 16);
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value / 8388608.0;
    }

    private void Analyze(uint sampleRate)
    {
        Span<double> instantaneous = stackalloc double[SpectrumAnalyzer.BandCount];
        var loudness = SpectrumAnalyzer.Analyze(_buffer, sampleRate, instantaneous);

        // Slow loudness envelope: loud music stays expressive, quiet music stays restrained.
        _loudness = Math.Max(loudness, _loudness * 0.99);
        var gain = VisualizerDynamics.MapLoudnessToGain(_loudness);

        for (var b = 0; b < BandCount; b++)
        {
            // Follow beat attacks and quiet gaps closely; slow envelope decay leaves every
            // bar raised through the next beat and hides the rhythm.
            var target = Math.Clamp(instantaneous[b] * gain, 0, 1);
            var smoothing = target > _bands[b] ? 0.82 : 0.46;
            _bands[b] += (target - _bands[b]) * smoothing;
        }

        var now = Environment.TickCount64;
        if (now - _lastEmit < 33) return; // Match the visualizer's display cadence.
        _lastEmit = now;
        BandsChanged?.Invoke(this, (double[])_bands.Clone());
    }

    public void Dispose()
    {
        Stop();
    }
}
