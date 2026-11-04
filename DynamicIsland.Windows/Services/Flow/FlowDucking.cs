namespace DynamicIsland.Windows.Services.Flow;

/// <summary>One app's audio session as Flow sees it when deciding what to turn down.</summary>
public sealed record FlowAudioSession(string Id, uint ProcessId, bool IsSystemSounds, bool Active, float Volume);

/// <summary>A session Flow turned down: what it was, and what Flow set it to.</summary>
public sealed record FlowDuckedSession(string Id, float Original, float Lowered);

/// <summary>
/// Pure rules for lowering other apps' audio while Sutra Flow records, and for putting it back.
/// Only playing sessions are touched (never Sutra Island itself or Windows system sounds), and a
/// session is restored only if its volume is still what Flow set, so a change the user made
/// while dictating is kept.
/// </summary>
public static class FlowDucking
{
    /// <summary>How close a session's volume must be to what Flow set for Flow to restore it.</summary>
    public const float Tolerance = 0.02f;

    /// <summary>The sessions to lower, to <paramref name="level"/> (0..1) of their current volume.</summary>
    public static IReadOnlyList<FlowDuckedSession> Plan(IEnumerable<FlowAudioSession> sessions, uint ownProcessId, float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        var plan = new List<FlowDuckedSession>();
        foreach (var s in sessions)
        {
            if (!s.Active || s.IsSystemSounds || s.ProcessId == ownProcessId || s.ProcessId == 0) continue;
            if (string.IsNullOrEmpty(s.Id) || s.Volume <= 0.001f) continue;
            if (plan.Any(p => p.Id == s.Id)) continue;
            var lowered = s.Volume * level;
            if (s.Volume - lowered < Tolerance) continue; // nothing worth changing
            plan.Add(new FlowDuckedSession(s.Id, s.Volume, lowered));
        }
        return plan;
    }

    /// <summary>The (session, volume) pairs to set back: only sessions still at the volume Flow set.</summary>
    public static IReadOnlyList<(string Id, float Volume)> Restore(IEnumerable<FlowDuckedSession> ducked, IReadOnlyDictionary<string, float> current)
    {
        var restore = new List<(string, float)>();
        foreach (var d in ducked)
        {
            if (!current.TryGetValue(d.Id, out var now)) continue;          // app closed
            if (Math.Abs(now - d.Lowered) > Tolerance) continue;            // the user changed it
            restore.Add((d.Id, d.Original));
        }
        return restore;
    }
}
