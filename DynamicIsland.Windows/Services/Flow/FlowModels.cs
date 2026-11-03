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

/// <summary>What happened when Flow tried to put text into the target app.</summary>
public enum FlowPasteOutcome
{
    /// <summary>Ctrl+V was delivered to the window that had focus when the key was pressed.</summary>
    Pasted,
    /// <summary>Focus moved to a different window, so nothing was pasted; the text is on the clipboard.</summary>
    TargetChanged,
    /// <summary>The target runs as administrator and Windows would drop the keystrokes; the text is on the clipboard.</summary>
    Elevated,
    /// <summary>Windows refused the synthetic keystrokes; the text is on the clipboard.</summary>
    Blocked,
    /// <summary>Another app kept the clipboard locked; nothing was pasted or copied.</summary>
    ClipboardBusy,
}

/// <summary>The microphone side of a dictation.</summary>
public interface IFlowAudio
{
    float Level { get; }
    /// <summary>Opens the microphone. Throws <see cref="FlowException"/> with a short reason on failure.</summary>
    void Start();
    /// <summary>Closes the microphone opened by the matching <see cref="Start"/> and returns what it recorded.</summary>
    Task<FlowRecording> StopAsync();
}

/// <summary>
/// Where the user was when they started dictating: the top-level window, the focused child window,
/// and (when UI Automation can tell) the focused element itself, such as a browser tab's text field.
/// </summary>
public sealed class FlowTarget
{
    public nint Window { get; init; }
    public nint Focus { get; init; }
    /// <summary>UI Automation id of the focused element at key-down (null if unknown).</summary>
    public Task<string?>? ElementAtPress { get; init; }
    /// <summary>UI Automation id of the focused element at key-up (null if unknown).</summary>
    public Task<string?>? ElementAtRelease { get; set; }
}

/// <summary>The output side of a dictation.</summary>
public interface IFlowPaster
{
    /// <summary>Records where focus is when the key goes down.</summary>
    FlowTarget? CaptureTarget();
    /// <summary>Records the focused element again when the key comes up.</summary>
    void PinTarget(FlowTarget target);
    /// <summary>Pastes only if focus is still exactly where the dictation started.</summary>
    Task<FlowPasteOutcome> PasteAsync(string text, FlowTarget? target, CancellationToken token);
}
