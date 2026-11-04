namespace DynamicIsland.Windows.Services.Flow;

/// <summary>Where Sutra Flow is in one dictation, as the island shows it.</summary>
public enum FlowState
{
    /// <summary>Nothing happening; the island shows its normal content.</summary>
    Idle,
    /// <summary>Push-to-talk is held and the microphone is recording.</summary>
    Listening,
    /// <summary>The key was released; the dots flatten while recording stops.</summary>
    Released,
    /// <summary>Transcribing and cleaning up the text.</summary>
    Polishing,
    /// <summary>The text was pasted into the focused app.</summary>
    Pasted,
    /// <summary>Something went wrong (no microphone, no speech model, nothing heard...).</summary>
    Error,
}

/// <summary>How much Sutra Flow tidies the raw transcript before pasting.</summary>
public enum FlowCleanupMode
{
    /// <summary>Paste exactly what Whisper heard.</summary>
    Off,
    /// <summary>Local rules: filler words, stutters, spoken punctuation, capitalization.</summary>
    Basic,
    /// <summary>Basic rules, then the Sutra Assistant provider rewrites it (falls back to Basic).</summary>
    Ai,
}


public sealed record FlowStatus(FlowState State, string Detail = "", int Words = 0);

/// <summary>One push-to-talk recording: 16 kHz mono samples plus the loudest sample.</summary>
public sealed record FlowRecording(float[] Samples, float Peak)
{
    public const int SampleRate = 16000;
    public double Seconds => Samples.Length / (double)SampleRate;

    private double? _voiced;
    /// <summary>Seconds of 30 ms frames loud enough to be speech. Used to tell real words from Whisper's guesses on silence.</summary>
    public double VoicedSeconds => _voiced ??= MeasureVoiced(Samples);

    private static double MeasureVoiced(float[] samples)
    {
        const int frame = SampleRate * 30 / 1000;
        var voiced = 0;
        for (var start = 0; start + frame <= samples.Length; start += frame)
        {
            double sum = 0;
            for (var i = start; i < start + frame; i++) sum += samples[i] * samples[i];
            if (Math.Sqrt(sum / frame) > 0.02) voiced++;
        }
        return voiced * 0.03;
    }
}

/// <summary>A failure with a short, user-facing reason for the island.</summary>
public sealed class FlowException(string message) : Exception(message);

/// <summary>
/// What happened when Flow tried to put text into the target app. The user's clipboard is left as
/// it was on every outcome but <see cref="Pasted"/>, and nothing was typed except with
/// <see cref="PartlyTyped"/>.
/// </summary>
public enum FlowPasteOutcome
{
    /// <summary>The text was delivered to the field that had focus when dictation started.</summary>
    Pasted,
    /// <summary>Focus moved to a different window, control, field, or tab.</summary>
    TargetChanged,
    /// <summary>The target runs as administrator and Windows would drop the keystrokes.</summary>
    Elevated,
    /// <summary>Windows refused the synthetic keystrokes.</summary>
    Blocked,
    /// <summary>Another app kept the clipboard locked.</summary>
    ClipboardBusy,
    /// <summary>Windows accepted only part of the typed text; <see cref="FlowPasteResult.Remaining"/> holds the rest.</summary>
    PartlyTyped,
    /// <summary>The clipboard couldn't be saved exactly, and the text has line breaks, which are unsafe to type (Enter can send or submit).</summary>
    MultilineNeedsClipboard,
}

/// <summary>The outcome of a paste and, after a partial typing, the text that didn't make it.</summary>
public readonly record struct FlowPasteResult(FlowPasteOutcome Outcome, string? Remaining = null)
{
    public static implicit operator FlowPasteResult(FlowPasteOutcome outcome) => new(outcome);
}

/// <summary>Pure bookkeeping for typing text as Unicode keystrokes (one key-down and one key-up per character).</summary>
public static class FlowTyping
{
    /// <summary>Characters that are typed: control characters (Enter, Tab) are never sent.</summary>
    public static string Typable(string text) => new(text.Where(c => !char.IsControl(c)).ToArray());

    /// <summary>
    /// Given how many keystrokes Windows accepted, the part of <paramref name="typable"/> that was not
    /// typed. A character whose key-down was accepted counts as typed (the app inserts it on key-down).
    /// </summary>
    public static string Remaining(string typable, int acceptedInputs)
    {
        var typed = Math.Clamp((acceptedInputs + 1) / 2, 0, typable.Length);
        return typable[typed..];
    }
}

/// <summary>The microphone side of a dictation.</summary>
public interface IFlowAudio
{
    float Level { get; }
    /// <summary>Opens the microphone. Throws <see cref="FlowException"/> with a short reason on failure.</summary>
    void Start();
    /// <summary>Closes the microphone opened by the matching <see cref="Start"/> and returns what it recorded.</summary>
    Task<FlowRecording> StopAsync();
    /// <summary>Drops the recording at once (a quick tap), so the next press can start immediately.</summary>
    void Abort() => _ = StopAsync();
}

/// <summary>Where keyboard focus is at one moment: top-level window, focused child window, and caret.</summary>
public sealed record FlowFocus(nint Window, nint Focus, nint CaretWindow, int CaretLeft, int CaretTop)
{
    /// <summary>
    /// True when <paramref name="other"/> is the same place. The focused child window and the caret
    /// are compared only when both readings have them (many apps draw their own caret).
    /// </summary>
    public bool SamePlaceAs(FlowFocus other)
    {
        if (Window == 0 || Window != other.Window) return false;
        if (Focus != 0 && other.Focus != 0 && Focus != other.Focus) return false;
        if (CaretWindow != 0 && other.CaretWindow != 0
            && (CaretWindow != other.CaretWindow || CaretLeft != other.CaretLeft || CaretTop != other.CaretTop)) return false;
        return true;
    }
}

/// <summary>
/// Where the user was dictating: focus readings at key-down and key-up, plus (when UI Automation
/// can tell) the focused element itself at both moments, such as a browser tab's text field.
/// </summary>
public sealed class FlowTarget
{
    public required FlowFocus AtPress { get; init; }
    public FlowFocus? AtRelease { get; set; }
    /// <summary>UI Automation id of the focused element at key-down (null if unknown).</summary>
    public Task<string?>? ElementAtPress { get; init; }
    /// <summary>UI Automation id of the focused element at key-up (null if unknown).</summary>
    public Task<string?>? ElementAtRelease { get; set; }

    /// <summary>
    /// Pure decision used before pasting: every focus reading must match, and every element id that
    /// is known must match the others. Unknown element ids (no UI Automation) don't block.
    /// </summary>
    public static bool Unchanged(FlowFocus press, FlowFocus? release, FlowFocus now, string? elementPress, string? elementRelease, string? elementNow)
    {
        if (!press.SamePlaceAs(now)) return false;
        if (release is not null && (!press.SamePlaceAs(release) || !release.SamePlaceAs(now))) return false;
        var known = new[] { elementPress, elementRelease, elementNow }.Where(e => e is not null).Distinct().Count();
        return known <= 1;
    }
}

/// <summary>The output side of a dictation.</summary>
public interface IFlowPaster
{
    /// <summary>Records where focus is when the key goes down.</summary>
    FlowTarget? CaptureTarget();
    /// <summary>Records the focused element again when the key comes up.</summary>
    void PinTarget(FlowTarget target);
    /// <summary>Pastes only if focus is still exactly where the dictation started.</summary>
    Task<FlowPasteResult> PasteAsync(string text, FlowTarget? target, CancellationToken token);
}
