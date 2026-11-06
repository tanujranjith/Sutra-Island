using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Infrastructure;

/// <summary>What the compact voice orb shows inside the island. Mapped from <see cref="AssistantVoiceState"/>
/// plus the Q session's reply and action feedback by <see cref="CompactVoicePresenter"/>.</summary>
public enum CompactVoiceState { Idle, Wake, Listening, Thinking, Answer, Action, Error, Exit }

/// <summary>Timings from docs/voice-orb (make.py's frame()). Shared by the presenter and the view.</summary>
public static class CompactVoiceTimings
{
    public static readonly TimeSpan ContentFade = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan MascotDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan MascotFade = TimeSpan.FromMilliseconds(500);
    /// <summary>The wake intro, after which the orb settles into Listening.</summary>
    public static readonly TimeSpan Wake = TimeSpan.FromMilliseconds(650);
    public static readonly TimeSpan Grow = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan AnswerHold = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ActionSettle = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ActionHold = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ErrorHold = TimeSpan.FromSeconds(4);
    /// <summary>The mascot leaves over 450 ms; the island content fades back in from 350 ms.</summary>
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan ExitMascot = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan ExitContentReturn = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan PerCharacter = TimeSpan.FromMilliseconds(22);
    public static readonly TimeSpan MaxTyping = TimeSpan.FromMilliseconds(2400);

    /// <summary>How long the answer takes to type out; reduced motion shows it at once.</summary>
    public static TimeSpan Typing(string? text, bool reducedMotion)
    {
        if (reducedMotion || string.IsNullOrEmpty(text)) return TimeSpan.Zero;
        var ms = Math.Min(MaxTyping.TotalMilliseconds, text.Length * PerCharacter.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}

/// <summary>Which motion the orb may run. Reduced motion keeps crossfades only.</summary>
public readonly record struct CompactVoiceMotion(bool SlideAndScale, bool Ripples, bool Orbit, bool Bob, bool Breathe, bool Shimmer, bool Typewriter, bool ShellMorph, bool Crossfade)
{
    public bool AnyMotion => SlideAndScale || Ripples || Orbit || Bob || Breathe || Shimmer || Typewriter || ShellMorph;

    public static CompactVoiceMotion For(CompactVoiceState state, bool reducedMotion)
    {
        if (reducedMotion || state == CompactVoiceState.Idle)
            return new(false, false, false, false, false, false, false, false, Crossfade: state != CompactVoiceState.Idle);
        return new(
            SlideAndScale: state is CompactVoiceState.Wake or CompactVoiceState.Exit,
            Ripples: state is CompactVoiceState.Listening,
            Orbit: state is CompactVoiceState.Thinking,
            Bob: state is CompactVoiceState.Thinking,
            Breathe: state is CompactVoiceState.Answer,
            Shimmer: state is CompactVoiceState.Thinking,
            Typewriter: state is CompactVoiceState.Answer,
            ShellMorph: true,
            Crossfade: true);
    }
}

/// <summary>Pure decisions about when compact voice applies and how it is sized and worded.</summary>
public static class CompactVoicePolicy
{
    /// <summary>"Compact voice mode" ships on.</summary>
    public const bool DefaultEnabled = true;

    /// <summary>
    /// A voice turn uses the compact orb only when the setting is on and the compact island is what the
    /// user sees. When the island is already expanded (or always expanded), or the provider disclosure still
    /// needs accepting in the panel, the turn keeps the original chat-panel behavior.
    /// </summary>
    public static bool UseCompact(bool settingEnabled, bool islandExpanded, bool alwaysExpanded, bool disclosureAccepted)
        => settingEnabled && !islandExpanded && !alwaysExpanded && disclosureAccepted;

    /// <summary>Whether a voice turn should expand the island into the chat panel (the old behavior).</summary>
    public static bool ExpandPanelForVoice(bool compactTurn, bool autoExpandSetting) => !compactTurn && autoExpandSetting;

    /// <summary>The orb owns its conversation until exit; the panel's timer must not clear a pinned answer.</summary>
    public static bool AllowPanelAutoClose(bool enabled, bool voiceBusy, bool compactActive, QRunState state)
        => enabled && !voiceBusy && !compactActive && state == QRunState.Complete;

    public static bool CanStopSpeech(CompactVoiceState state, bool speaking)
        => speaking && state is CompactVoiceState.Answer or CompactVoiceState.Action;

    /// <summary>The orb is ~38% wider than the compact island (500 → 690 in the mock) and keeps its height,
    /// with floors so small or hole-punch islands still fit the mascot and a line of text.</summary>
    public static (double Width, double Height) Size(double compactWidth, double compactHeight, bool tall)
    {
        var width = Math.Max(Math.Round(compactWidth * 1.38), 300d);
        var height = Math.Max(compactHeight, 56d);
        if (tall) height = Math.Max(Math.Round(height * 1.36), 80d); // 110 → 150 in the mock
        return (width, height);
    }

    /// <summary>A short, readable error line for the orb.</summary>
    public static string ErrorMessage(string? status, string? error)
    {
        var text = (error ?? "") + " " + (status ?? "");
        if (text.Contains("microphone", StringComparison.OrdinalIgnoreCase) || text.Contains("mic ", StringComparison.OrdinalIgnoreCase))
            return "Mic is blocked";
        if (text.Contains("speech", StringComparison.OrdinalIgnoreCase) && text.Contains("install", StringComparison.OrdinalIgnoreCase))
            return "Speech isn't set up";
        if (text.Contains("disclosure", StringComparison.OrdinalIgnoreCase)) return "Allow the assistant first";
        var first = (error ?? status ?? "").Trim();
        if (first.Length == 0) return "Voice unavailable";
        var stop = first.IndexOfAny(['.', '\n']);
        if (stop > 0) first = first[..stop];
        return first.Length > 48 ? first[..47].TrimEnd() + "…" : first;
    }

    /// <summary>Where "Fix" goes: a Windows settings URI, or null for Sutra Assistant settings.</summary>
    public static string? FixTarget(string? message)
    {
        if (message is null) return null;
        if (message.StartsWith("Mic", StringComparison.OrdinalIgnoreCase)) return "ms-settings:privacy-microphone";
        if (message.StartsWith("Speech", StringComparison.OrdinalIgnoreCase)) return "ms-settings:speech";
        return null;
    }

    /// <summary>Speaker glyph wave count for a volume level: 3 above 45%, 2 above 20%, otherwise 1.</summary>
    public static int VolumeWaves(double level) => level > 45 ? 3 : level > 20 ? 2 : 1;

    /// <summary>Whether a command shows the live slider row (it reports a level) or a one-line confirmation.</summary>
    public static bool ShowsLevel(AssistantActionFeedback? action)
        => action is { Phase: AssistantActionPhase.Succeeded, Level: double level } && double.IsFinite(level)
           && action.Command.Equals("Volume", StringComparison.OrdinalIgnoreCase);

    public static string ActionLabel(string command) => command.ToLowerInvariant() switch
    {
        "volume" => "Volume", "mute" => "Muted", "unmute" => "Unmuted", "setoutput" or "audiooutput" => "Audio output",
        "play" => "Playing", "pause" => "Paused", "next" => "Next track", "previous" => "Previous track", "seek" => "Seek",
        "timer" => "Timer", "alarm" => "Alarm", "stopwatch" or "startstopwatch" => "Stopwatch",
        "focuson" or "focussession" => "Focus on", "focusoff" or "endfocus" => "Focus off",
        "notificationson" => "Notifications on", "notificationsoff" => "Notifications off",
        _ => "Done"
    };

    /// <summary>The one-line confirmation for commands without a level.</summary>
    public static string ActionConfirmation(AssistantActionFeedback action)
    {
        if (action.Phase == AssistantActionPhase.Failed) return string.IsNullOrWhiteSpace(action.Message) ? "Couldn't do that" : Shorten(action.Message!);
        if (action.Phase == AssistantActionPhase.Cancelled) return "Stopped";
        var label = ActionLabel(action.Command);
        if (label != "Done" || string.IsNullOrWhiteSpace(action.Message)) return label;
        return Shorten(action.Message!);
    }

    private static string Shorten(string text)
    {
        text = text.Trim();
        return text.Length > 60 ? text[..59].TrimEnd() + "…" : text;
    }
}

/// <summary>
/// The compact voice orb's state machine. Single-threaded: the view model calls it on the UI thread.
/// It never runs timers itself; <see cref="Deadline"/> says how long the current state holds and
/// <see cref="DeadlineVersion"/> changes whenever that hold should restart, so the caller owns one timer.
/// </summary>
public sealed class CompactVoicePresenter
{
    private AssistantVoiceState _voice = AssistantVoiceState.Off;
    private long _actionSequence = -1;
    private long _staleSequence = -1;
    private bool _speaking;
    private bool _hovered;
    private bool _answerTyped;
    private bool _qTurnStarted;

    public CompactVoiceState State { get; private set; } = CompactVoiceState.Idle;
    public string Transcript { get; private set; } = "";
    public string Answer { get; private set; } = "";
    public string ErrorMessage { get; private set; } = "";
    public AssistantActionFeedback? Action { get; private set; }
    /// <summary>The level before the command ran, so the slider animates from what was there.</summary>
    public double? ActionFromLevel { get; private set; }
    public bool ReducedMotion { get; set; }
    public bool IsActive => State != CompactVoiceState.Idle;
    /// <summary>The island is widened (every visible state except the exit, which hands the size back).</summary>
    public bool IsWide => State is not (CompactVoiceState.Idle or CompactVoiceState.Exit);
    /// <summary>Only answers grow the island taller; commands and errors keep its height.</summary>
    public bool IsTall => State == CompactVoiceState.Answer;
    public bool IsHovered => _hovered;
    public int DeadlineVersion { get; private set; }
    public event Action<CompactVoiceState>? StateChanged;

    /// <summary>How long the current state holds before <see cref="Expire"/>; null holds until an event.</summary>
    public TimeSpan? Deadline => State switch
    {
        CompactVoiceState.Wake => CompactVoiceTimings.Wake,
        CompactVoiceState.Answer when _speaking || _hovered => null,
        CompactVoiceState.Answer => CompactVoiceTimings.AnswerHold + (_answerTyped ? TimeSpan.Zero : CompactVoiceTimings.Typing(Answer, ReducedMotion)),
        CompactVoiceState.Action when _speaking || _hovered || Action is null || Action.Phase == AssistantActionPhase.Working => null,
        CompactVoiceState.Action => CompactVoiceTimings.ActionSettle + CompactVoiceTimings.ActionHold,
        CompactVoiceState.Error when _hovered => null,
        CompactVoiceState.Error => CompactVoiceTimings.ErrorHold,
        CompactVoiceState.Exit => CompactVoiceTimings.Exit,
        _ => null
    };

    /// <summary>Feeds an <see cref="AssistantVoiceController"/> state change.</summary>
    /// <param name="compactAllowed">Whether a turn starting now may use the orb (see <see cref="CompactVoicePolicy.UseCompact"/>).</param>
    public void OnVoice(AssistantVoiceState voice, string? status, string? error, bool compactAllowed)
    {
        var previous = _voice;
        _voice = voice;
        switch (voice)
        {
            case AssistantVoiceState.Listening:
                _speaking = false;
                if (State is CompactVoiceState.Idle or CompactVoiceState.Exit)
                {
                    if (State == CompactVoiceState.Idle && !compactAllowed) return;
                    ClearTurn();
                    Go(CompactVoiceState.Wake);
                }
                else if (State != CompactVoiceState.Wake && State != CompactVoiceState.Listening)
                {
                    // A new wake while an answer, command, or error shows goes straight back to listening.
                    ClearTurn();
                    Go(CompactVoiceState.Listening);
                }
                break;
            case AssistantVoiceState.Thinking:
                if (State is CompactVoiceState.Wake or CompactVoiceState.Listening) Go(CompactVoiceState.Thinking);
                break;
            case AssistantVoiceState.Speaking:
                if (State == CompactVoiceState.Idle || State == CompactVoiceState.Exit) return; // read-aloud from the panel
                _speaking = true;
                if (State is CompactVoiceState.Wake or CompactVoiceState.Listening or CompactVoiceState.Thinking)
                {
                    if (Action is { Phase: not AssistantActionPhase.Working }) Go(CompactVoiceState.Action);
                    else Go(CompactVoiceState.Answer);
                }
                else Restart();
                break;
            case AssistantVoiceState.Error:
                _speaking = false;
                if (State is CompactVoiceState.Answer or CompactVoiceState.Action) { Restart(); return; }
                // A turn that fails before it reaches Listening (no recognizer, mic blocked) still came from a wake.
                if (State is CompactVoiceState.Idle or CompactVoiceState.Exit && (previous != AssistantVoiceState.WakeListening || !compactAllowed)) return;
                ErrorMessage = CompactVoicePolicy.ErrorMessage(status, error);
                Go(CompactVoiceState.Error);
                break;
            default: // Off or WakeListening: the turn ended.
                var wasSpeaking = _speaking;
                _speaking = false;
                if (State is CompactVoiceState.Wake or CompactVoiceState.Listening) Go(CompactVoiceState.Exit); // nothing heard, or cancelled
                else if (State == CompactVoiceState.Thinking) Go(CompactVoiceState.Exit); // cancelled before a reply
                else if (State is (CompactVoiceState.Answer or CompactVoiceState.Action) && wasSpeaking) { _answerTyped = true; Restart(); } // hold ~3 s after speech
                break;
        }
    }

    /// <summary>Feeds a Q session change. Only a voice turn the orb owns reacts.</summary>
    /// <param name="currentLevel">The system level right now, captured as the slider's start for a new command.</param>
    public void OnQ(QRunState state, string response, AssistantActionFeedback? action, double? currentLevel = null)
    {
        // Feedback that already existed before this turn reached Thinking belongs to an earlier request.
        if (State is not (CompactVoiceState.Thinking or CompactVoiceState.Answer or CompactVoiceState.Action))
        {
            if (action is not null) _staleSequence = Math.Max(_staleSequence, action.Sequence);
            return;
        }
        // SubmitAsync first republishes the previous snapshot with its Action cleared. A retained
        // Complete response is not this turn's answer until the new request has actually started.
        if (state is QRunState.Capturing or QRunState.Thinking or QRunState.Streaming) _qTurnStarted = true;
        if (!_qTurnStarted) return;
        if (action is not null && action.Sequence <= _staleSequence) action = null;
        if (action is not null)
        {
            if (action.Sequence != _actionSequence) { _actionSequence = action.Sequence; ActionFromLevel = currentLevel; }
            var changed = Action != action;
            Action = action;
            if (action.Phase == AssistantActionPhase.Working) { if (changed) Raise(); return; }
            if (State != CompactVoiceState.Action) Go(CompactVoiceState.Action);
            else if (changed) Restart();
            return;
        }
        if (state == QRunState.Complete && State == CompactVoiceState.Thinking && !string.IsNullOrWhiteSpace(response))
        {
            Answer = response.Trim();
            Go(CompactVoiceState.Answer);
        }
    }

    /// <summary>Live (interim) words while listening.</summary>
    public void OnPartialTranscript(string text)
    {
        if (State is not (CompactVoiceState.Wake or CompactVoiceState.Listening) || string.IsNullOrWhiteSpace(text)) return;
        var trimmed = text.Trim();
        if (trimmed == Transcript) return;
        Transcript = trimmed;
        Raise();
    }

    /// <summary>The final recognized prompt, as it is submitted.</summary>
    public void OnPrompt(string text)
    {
        if (!IsActive || State == CompactVoiceState.Exit || string.IsNullOrWhiteSpace(text)) return;
        Transcript = text.Trim();
        Raise();
    }

    /// <summary>The current state's hold ran out.</summary>
    public void Expire()
    {
        switch (State)
        {
            case CompactVoiceState.Wake:
                Go(_voice == AssistantVoiceState.Listening ? CompactVoiceState.Listening : CompactVoiceState.Exit);
                break;
            case CompactVoiceState.Answer or CompactVoiceState.Action or CompactVoiceState.Error:
                if (_hovered || (State is (CompactVoiceState.Answer or CompactVoiceState.Action) && _speaking)) return;
                Go(CompactVoiceState.Exit);
                break;
            case CompactVoiceState.Exit:
                ClearTurn();
                Go(CompactVoiceState.Idle);
                break;
        }
    }

    /// <summary>Hovering pins the orb open; leaving restarts the hold.</summary>
    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        if (State is CompactVoiceState.Answer or CompactVoiceState.Action or CompactVoiceState.Error)
        {
            if (!hovered) _answerTyped = true;
            Restart();
        }
    }

    /// <summary>Esc or an explicit dismissal: run the exit.</summary>
    public void Dismiss()
    {
        if (State is CompactVoiceState.Idle or CompactVoiceState.Exit) return;
        _speaking = false;
        Go(CompactVoiceState.Exit);
    }

    /// <summary>Drop the orb at once (the chat panel took over).</summary>
    public void Reset()
    {
        if (State == CompactVoiceState.Idle) return;
        _speaking = false; _hovered = false;
        ClearTurn();
        Go(CompactVoiceState.Idle);
    }

    private void ClearTurn()
    {
        Transcript = ""; Answer = ""; ErrorMessage = ""; Action = null; ActionFromLevel = null; _answerTyped = false; _qTurnStarted = false;
    }

    private void Go(CompactVoiceState next)
    {
        if (next == CompactVoiceState.Exit) _hovered = false;
        State = next;
        DeadlineVersion++;
        StateChanged?.Invoke(next);
    }

    private void Restart() { DeadlineVersion++; StateChanged?.Invoke(State); }
    private void Raise() => StateChanged?.Invoke(State);
}
