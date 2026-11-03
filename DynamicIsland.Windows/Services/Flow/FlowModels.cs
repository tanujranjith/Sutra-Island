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
