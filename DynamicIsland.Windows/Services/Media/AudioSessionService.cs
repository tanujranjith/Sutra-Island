using System.Runtime.InteropServices;
using DynamicIsland.Windows.Interop;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

public sealed class AudioSessionService(LoggingService log) : IDisposable
{
    private const float AudibleThreshold = 0.008f;
    private readonly CancellationTokenSource _shutdown = new();
    private Thread? _thread;
    private readonly object _controlGate = new();

    public event EventHandler<AudioState>? Changed;
    public AudioState Current { get; private set; } = AudioState.Unknown;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(PollLoop) { IsBackground = true, Name = "DynamicIsland.CoreAudio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void SetMasterVolume(int percent)
        => ChangeVolume(percent, relative: false);

    public void AdjustMasterVolume(int delta)
        => ChangeVolume(delta, relative: true);

    private void ChangeVolume(int amount, bool relative)
    {
        lock (_controlGate)
        {
            IAudioEndpointVolume? endpoint = null;
            IMMDevice? device = null;
            try
            {
                (device, endpoint) = CoreAudioFactory.ActivateDefault<IAudioEndpointVolume>();
                Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var current));
                var percent = Math.Clamp((relative ? (int)Math.Round(current * 100) : 0) + amount, 0, 100);
                Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(percent / 100f, nint.Zero));
                Current = Current with { MasterVolumePercent = percent };
                Changed?.Invoke(this, Current);
            }
            catch (Exception ex) { log.Error("Unable to set master volume", ex); }
            finally { CoreAudioFactory.Release(endpoint); CoreAudioFactory.Release(device); }
        }
    }

    public void SetMuted(bool muted)
        => ChangeMute(muted);

    public void ToggleMuted() => ChangeMute(null);

    private void ChangeMute(bool? requested)
    {
        lock (_controlGate)
        {
            IAudioEndpointVolume? endpoint = null;
            IMMDevice? device = null;
            try
            {
                (device, endpoint) = CoreAudioFactory.ActivateDefault<IAudioEndpointVolume>();
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out var current));
                var muted = requested ?? !current;
                Marshal.ThrowExceptionForHR(endpoint.SetMute(muted, nint.Zero));
                Current = Current with { SystemMuted = muted };
                Changed?.Invoke(this, Current);
            }
            catch (Exception ex) { log.Error("Unable to update master mute", ex); }
            finally { CoreAudioFactory.Release(endpoint); CoreAudioFactory.Release(device); }
        }
    }

    /// <summary>Active output endpoints as (id, name) pairs, for the in-island device picker.</summary>
    public IReadOnlyList<(string Id, string Name)> GetOutputDevices()
    {
        try { return CoreAudioFactory.GetRenderEndpoints(); }
        catch (Exception ex) { log.Debug($"Enumerating output devices failed: {ex.Message}"); return []; }
    }

    /// <summary>Switches the system default output device to the given endpoint id.</summary>
    public void SetDefaultOutputDevice(string deviceId)
    {
        try { CoreAudioFactory.SetDefaultRenderDevice(deviceId); }
        catch (Exception ex) { log.Error("Unable to switch output device", ex); }
    }

    private void PollLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            AudioState next;
            lock (_controlGate)
            {
                next = ReadState();
                if (next != Current)
                {
                    Current = next;
                    Changed?.Invoke(this, next);
                }
            }

            // Core Audio enumeration is relatively expensive. Keep controls responsive while
            // audio is active, but back off when the machine is silent or audio is unavailable.
            var delay = next.ActiveAudioOutput ? 400 : 1000;
            _shutdown.Token.WaitHandle.WaitOne(delay);
        }
    }

    private AudioState ReadState()
    {
        IMMDevice? endpointDevice = null;
        IAudioEndpointVolume? endpointVolume = null;
        IAudioMeterInformation? endpointMeter = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            (endpointDevice, endpointVolume) = CoreAudioFactory.ActivateDefault<IAudioEndpointVolume>();
            endpointVolume.GetMasterVolumeLevelScalar(out var volume);
            endpointVolume.GetMute(out var systemMuted);

            (_, endpointMeter) = CoreAudioFactory.ActivateDefault<IAudioMeterInformation>();
            endpointMeter.GetPeakValue(out var endpointPeak);

            (_, manager) = CoreAudioFactory.ActivateDefault<IAudioSessionManager2>();
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
            sessions.GetCount(out var count);
            var active = 0;
            var audible = 0;
            var anyMuted = false;

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    if (sessions.GetSession(index, out control) < 0) continue;
                    if (control.GetState(out var state) < 0 || state != AudioSessionState.Active) continue;
                    active++;
                    if (control is ISimpleAudioVolume simple && simple.GetMute(out var sessionMuted) >= 0)
                        anyMuted |= sessionMuted;
                    if (control is IAudioMeterInformation meter && meter.GetPeakValue(out var peak) >= 0 && peak >= AudibleThreshold)
                        audible++;
                }
                catch { }
                finally { CoreAudioFactory.Release(control); }
            }

            return new AudioState
            {
                Availability = AudioAvailability.Available,
                MasterVolumePercent = (int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100),
                SystemMuted = systemMuted,
                AnySessionMuted = anyMuted,
                ActiveAudioOutput = !systemMuted && (endpointPeak >= AudibleThreshold || audible > 0),
                ActiveSessionCount = active,
                AudibleSessionCount = audible,
                OutputDeviceId = CoreAudioFactory.IdOf(endpointDevice),
                OutputDeviceName = CoreAudioFactory.FriendlyNameOf(endpointDevice)
            };
        }
        catch (COMException ex)
        {
            log.Debug($"CoreAudio unavailable: 0x{ex.HResult:X8}");
            return new AudioState { Availability = AudioAvailability.Unsupported };
        }
        catch (Exception ex)
        {
            log.Error("CoreAudio polling failed", ex);
            return AudioState.Unknown;
        }
        finally
        {
            CoreAudioFactory.Release(sessions);
            CoreAudioFactory.Release(manager);
            CoreAudioFactory.Release(endpointMeter);
            CoreAudioFactory.Release(endpointVolume);
            CoreAudioFactory.Release(endpointDevice);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _shutdown.Dispose();
    }
}
