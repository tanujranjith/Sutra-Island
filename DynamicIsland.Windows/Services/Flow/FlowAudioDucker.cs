using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using DynamicIsland.Windows.Interop;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Turns other apps' audio down while Sutra Flow records, so music doesn't bleed into the microphone,
/// and puts it back afterwards. Works per app (Windows' volume mixer), never on the master volume.
/// All Core Audio work runs on one background queue, so the key hook and UI never wait for it.
/// What was lowered is also written to disk until it is restored, so a crash can't leave music quiet.
/// </summary>
public sealed class FlowAudioDucker(LoggingService log) : IDisposable
{
    private const int FadeSteps = 6;
    private const int FadeStepMs = 20;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<FlowDuckedSession> _ducked = [];
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland.Windows", "SutraFlow", "ducked.json");

    /// <summary>Lowers playing apps to <paramref name="level"/> of their volume (0..1). Returns at once.</summary>
    public void Duck(float level) => _ = Run(() =>
    {
        if (_ducked.Count > 0) return;
        var sessions = Read(out var controls);
        try
        {
            _ducked = FlowDucking.Plan(sessions, (uint)Environment.ProcessId, level).ToList();
            if (_ducked.Count == 0) return;
            Save();
            Fade(controls, _ducked.Select(d => (d.Id, d.Original, d.Lowered)));
            log.Debug($"Sutra Flow: lowered {_ducked.Count} app(s) while listening.");
        }
        finally { ReleaseAll(controls); }
    });

    /// <summary>Puts back what <see cref="Duck"/> lowered, unless the user changed it meanwhile. Returns at once.</summary>
    public void Restore() => _ = Run(RestoreNow);

    /// <summary>Restores anything a previous run left lowered (call once at start-up).</summary>
    public void RecoverFromCrash() => _ = Run(() =>
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            _ducked = JsonSerializer.Deserialize<List<FlowDuckedSession>>(File.ReadAllText(StatePath)) ?? [];
        }
        catch { _ducked = []; }
        RestoreNow();
    });

    private void RestoreNow()
    {
        if (_ducked.Count == 0) { Clear(); return; }
        var sessions = Read(out var controls);
        try
        {
            var current = sessions.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Volume);
            var plan = FlowDucking.Restore(_ducked, current);
            Fade(controls, plan.Select(p => (p.Id, current[p.Id], p.Volume)));
        }
        finally { ReleaseAll(controls); }
        _ducked = [];
        Clear();
    }

    private async Task Run(Action work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(work).ConfigureAwait(false); }
        catch (COMException ex) { log.Debug($"Sutra Flow: audio ducking skipped (0x{ex.HResult:X8})."); }
        catch (Exception ex) { log.Error("Sutra Flow: audio ducking failed", ex); }
        finally { _gate.Release(); }
    }

    private static void Fade(Dictionary<string, ISimpleAudioVolume> controls, IEnumerable<(string Id, float From, float To)> changes)
    {
        var list = changes.Where(c => controls.ContainsKey(c.Id)).ToList();
        if (list.Count == 0) return;
        for (var step = 1; step <= FadeSteps; step++)
        {
            var t = (float)step / FadeSteps;
            foreach (var (id, from, to) in list)
            {
                try { controls[id].SetMasterVolume(Math.Clamp(from + (to - from) * t, 0f, 1f), nint.Zero); } catch { }
            }
            if (step < FadeSteps) Thread.Sleep(FadeStepMs);
        }
    }

    /// <summary>Reads every session on the default output device, keeping each one's volume control.</summary>
    private static List<FlowAudioSession> Read(out Dictionary<string, ISimpleAudioVolume> controls)
    {
        controls = new Dictionary<string, ISimpleAudioVolume>();
        var result = new List<FlowAudioSession>();
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            (device, manager) = CoreAudioFactory.ActivateDefault<IAudioSessionManager2>();
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) < 0 || control is null) continue;
                var keep = false;
                try
                {
                    if (control is not IAudioSessionControl2 c2 || control is not ISimpleAudioVolume volume) continue;
                    if (c2.GetSessionInstanceIdentifier(out var id) < 0 || string.IsNullOrEmpty(id)) continue;
                    c2.GetState(out var state);
                    c2.GetProcessId(out var pid);
                    var system = c2.IsSystemSoundsSession() == 0;
                    if (volume.GetMasterVolume(out var level) < 0) continue;
                    result.Add(new FlowAudioSession(id, pid, system, state == AudioSessionState.Active, level));
                    if (!controls.ContainsKey(id)) { controls[id] = volume; keep = true; }
                }
                catch { }
                finally { if (!keep) CoreAudioFactory.Release(control); }
            }
        }
        finally
        {
            CoreAudioFactory.Release(sessions);
            CoreAudioFactory.Release(manager);
            CoreAudioFactory.Release(device);
        }
        return result;
    }

    private static void ReleaseAll(Dictionary<string, ISimpleAudioVolume> controls)
    {
        foreach (var c in controls.Values) CoreAudioFactory.Release(c);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_ducked));
        }
        catch { }
    }

    private static void Clear()
    {
        try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
    }

    public void Dispose()
    {
        // Best effort, synchronously: never leave other apps quiet when the island exits.
        if (_gate.Wait(1500))
        {
            try { RestoreNow(); } catch { }
            finally { _gate.Release(); }
        }
    }
}
