namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public enum PaletteCommand
{
    Pause, Play, Next, Previous, Seek, Mute, Unmute,
    Ask, Say, Compare,
    Timer, PauseTimer, ResumeTimer, CancelTimer, ShowTimers,
    Alarm, CancelAlarm, ShowAlarms,
    StartStopwatch, PauseStopwatch, ResumeStopwatch, ResetStopwatch,
    FocusOn, FocusOff, Expand, Collapse, OpenSettings,
    Help, Convert, Volume, OpenMedia, Recenter, Clipboard, NotificationHistory, NotificationsOn, NotificationsOff, RetryNotifications, DismissBanner, Chat, VoiceInput, StopAssistant, ReadReply, WakeOn, WakeOff, AskScreen, SearchSettings,
    Stopwatch, Focus, AudioOutput
}

public sealed record CommandDefinition(
    PaletteCommand Id,
    string Name,
    string[] Aliases,
    string Description,
    string Example)
{
    public string Category => Id switch
    {
        PaletteCommand.Volume or PaletteCommand.OpenMedia or PaletteCommand.Play or PaletteCommand.Pause or PaletteCommand.Next or PaletteCommand.Previous or PaletteCommand.Seek or PaletteCommand.Mute or PaletteCommand.Unmute => "Audio",
        PaletteCommand.Ask or PaletteCommand.Say or PaletteCommand.Compare or PaletteCommand.Chat or PaletteCommand.VoiceInput or PaletteCommand.StopAssistant or PaletteCommand.ReadReply or PaletteCommand.WakeOn or PaletteCommand.WakeOff or PaletteCommand.AskScreen => "Assistant",
        PaletteCommand.NotificationsOn or PaletteCommand.NotificationsOff or PaletteCommand.RetryNotifications or PaletteCommand.DismissBanner or PaletteCommand.NotificationHistory => "Notifications",
        PaletteCommand.SearchSettings or PaletteCommand.OpenSettings => "Settings",
        PaletteCommand.Convert or PaletteCommand.Stopwatch or PaletteCommand.Clipboard => "Tools",
        PaletteCommand.AudioOutput => "Audio",
        _ => "Island"
    };
}

public sealed record CommandMatch(CommandDefinition Command, string Argument);

/// <summary>Deterministic command catalogue: the first word (or longest alias) identifies the
/// command; everything after it is the argument. No free-text interpretation.</summary>
public static class CommandCatalog
{
    public static readonly CommandDefinition[] All =
    [
        new(PaletteCommand.Pause, "pause", [], "Pause media playback", "pause"),
        new(PaletteCommand.Play, "play", ["resume", "unpause"], "Play or resume media", "play"),
        new(PaletteCommand.Next, "next", ["next track"], "Skip to the next track", "next"),
        new(PaletteCommand.Previous, "previous", ["previous track", "prev"], "Skip to the previous track", "previous"),
        new(PaletteCommand.Seek, "seek", ["forward", "fwd", "back", "backward", "rewind"], "Scrub through the current track", "Enter to scrub · or forward 30 seconds"),
        new(PaletteCommand.Mute, "mute", [], "Mute system audio", "mute"),
        new(PaletteCommand.Unmute, "unmute", [], "Unmute system audio", "unmute"),
        new(PaletteCommand.Ask, "ask", [], "Ask Sutra a question, or open chat", "ask why is the sky blue"),
        new(PaletteCommand.Say, "say", [], "Suggest wording for what to say next", "say"),
        new(PaletteCommand.Compare, "compare", [], "Compare two options in Sutra", "compare train or drive"),
        new(PaletteCommand.Timer, "timer", [], "Start a timer or open timer controls", "timer 10 minutes for laundry"),
        new(PaletteCommand.PauseTimer, "pause timer", ["timer pause"], "Pause a running timer", "pause timer laundry"),
        new(PaletteCommand.ResumeTimer, "resume timer", ["timer resume"], "Resume a paused timer", "resume timer laundry"),
        new(PaletteCommand.CancelTimer, "cancel timer", ["timer cancel"], "Cancel a timer", "cancel timer laundry"),
        new(PaletteCommand.ShowTimers, "show timers", ["timers"], "Open the timer list", "show timers"),
        new(PaletteCommand.Alarm, "alarm", [], "Set an alarm or open alarm setup", "alarm 7:30 AM weekdays"),
        new(PaletteCommand.CancelAlarm, "cancel alarm", [], "Cancel an alarm", "cancel alarm 7:30 AM"),
        new(PaletteCommand.ShowAlarms, "show alarms", ["alarms"], "Open the alarm list", "show alarms"),
        new(PaletteCommand.Stopwatch, "stopwatch", ["sw"], "Elapsed time, laps, pause and reset", "Enter to open"),
        new(PaletteCommand.Focus, "focus", [], "Start a timed focus session", "Enter to focus"),
        new(PaletteCommand.AudioOutput, "audio output", ["output", "sound output"], "Choose the system output device", "Enter to choose"),
        new(PaletteCommand.StartStopwatch, "start stopwatch", [], "Start the stopwatch", "start stopwatch"),
        new(PaletteCommand.PauseStopwatch, "pause stopwatch", [], "Pause the stopwatch", "pause stopwatch"),
        new(PaletteCommand.ResumeStopwatch, "resume stopwatch", [], "Resume the stopwatch", "resume stopwatch"),
        new(PaletteCommand.ResetStopwatch, "reset stopwatch", [], "Reset the stopwatch", "reset stopwatch"),
        new(PaletteCommand.FocusOn, "focus on", [], "Turn focus mode on", "focus on"),
        new(PaletteCommand.FocusOff, "focus off", [], "Turn focus mode off", "focus off"),
        new(PaletteCommand.Expand, "expand", [], "Expand the island", "expand"),
        new(PaletteCommand.Collapse, "collapse", [], "Collapse the island", "collapse"),
        new(PaletteCommand.OpenSettings, "open settings", ["settings"], "Open settings", "open settings"),
        new(PaletteCommand.Volume, "volume", ["vol"], "Adjust system volume with a live slider", "Enter to adjust · or volume 50"),
        new(PaletteCommand.OpenMedia, "open media", [], "Open the current media app", "open media"),
        new(PaletteCommand.Recenter, "recenter", [], "Recenter the island", "recenter"),
        new(PaletteCommand.Clipboard, "clipboard", [], "Open clipboard history", "clipboard"),
        new(PaletteCommand.NotificationHistory, "notifications", ["notification history", "history"], "Review notifications and mirroring controls", "Enter to review"),
        new(PaletteCommand.NotificationsOn, "notifications on", [], "Enable Windows notification mirroring", "notifications on"),
        new(PaletteCommand.NotificationsOff, "notifications off", [], "Disable Windows notification mirroring", "notifications off"),
        new(PaletteCommand.RetryNotifications, "retry notifications", [], "Reconnect to Windows notifications", "retry notifications"),
        new(PaletteCommand.DismissBanner, "dismiss banner", [], "Dismiss the visible banner", "dismiss banner"),
        new(PaletteCommand.Chat, "chat", ["assistant"], "Open Sutra Assistant without screen capture", "chat"),
        new(PaletteCommand.VoiceInput, "voice", ["listen"], "Ask Sutra with your microphone", "voice"),
        new(PaletteCommand.StopAssistant, "stop assistant", [], "Stop dictation, response, and speech", "stop assistant"),
        new(PaletteCommand.ReadReply, "read reply", [], "Read the last answer aloud", "read reply"),
        new(PaletteCommand.WakeOn, "wake on", [], "Listen locally for Hey Sutra", "wake on"),
        new(PaletteCommand.WakeOff, "wake off", [], "Disable wake listening and release the mic", "wake off"),
        new(PaletteCommand.AskScreen, "ask screen", [], "Use the active window for this question", "ask screen explain this"),
        new(PaletteCommand.SearchSettings, "find setting", ["settings search"], "Search settings and highlight a control", "find setting compact date"),
        new(PaletteCommand.Help, "help", ["show commands", "commands"], "Show available commands", "help"),
        new(PaletteCommand.Convert, "convert", [], "Convert units; Enter copies the result", "12 in to ft"),
    ];

    // Shown when the input is empty — the most useful entry points first.
    public static readonly CommandDefinition[] DefaultSuggestions =
    [
        ById(PaletteCommand.Help),
        ById(PaletteCommand.Convert),
        ById(PaletteCommand.Chat),
        ById(PaletteCommand.VoiceInput),
        ById(PaletteCommand.Volume),
        ById(PaletteCommand.Stopwatch),
        ById(PaletteCommand.Focus),
        ById(PaletteCommand.Clipboard),
        ById(PaletteCommand.AudioOutput),
        ById(PaletteCommand.NotificationHistory),
        ById(PaletteCommand.Timer),
        ById(PaletteCommand.Alarm),
        ById(PaletteCommand.Pause),
        ById(PaletteCommand.OpenSettings),
        ById(PaletteCommand.Expand),
        ById(PaletteCommand.Collapse),
        ById(PaletteCommand.FocusOn),
        ById(PaletteCommand.FocusOff),
        ById(PaletteCommand.ShowTimers),
        ById(PaletteCommand.ShowAlarms),
    ];

    public static CommandDefinition ById(PaletteCommand id) =>
        All.First(c => c.Id == id);

    private static IEnumerable<string> Tokens(CommandDefinition command)
    {
        yield return command.Name;
        foreach (var alias in command.Aliases) yield return alias;
    }

    /// <summary>Longest name/alias that is the whole input or is followed by a space wins, so
    /// "pause timer laundry" resolves to the timer command with argument "laundry".</summary>
    public static CommandMatch? Match(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) return null;
        CommandDefinition? best = null;
        var bestLength = -1;
        string bestToken = "";
        foreach (var command in All)
        {
            foreach (var token in Tokens(command))
            {
                if (text.Length < token.Length) continue;
                if (!text.StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Length > token.Length && !char.IsWhiteSpace(text[token.Length])) continue;
                if (token.Length <= bestLength) continue;
                bestLength = token.Length;
                best = command;
                bestToken = token;
            }
        }
        if (best is null) return null;
        var argument = text.Length > bestToken.Length ? text[bestToken.Length..].Trim() : "";
        if (best.Id == PaletteCommand.Seek && !bestToken.Equals(best.Name, StringComparison.OrdinalIgnoreCase))
            argument = bestToken + (argument.Length == 0 ? "" : " " + argument);
        return new CommandMatch(best, argument);
    }

    /// <summary>Filter for the suggestion list. Rank: full-input prefix beats first-word matches,
    /// exact beats prefix beats contains — so typing "pause ti" puts "pause timer" first.</summary>
    public static IReadOnlyList<CommandDefinition> Filter(string query, IReadOnlyList<string>? favorites = null, IReadOnlyList<string>? recents = null)
    {
        var text = query.Trim();
        if (text.Length == 0) return (favorites ?? []).Concat(recents ?? [])
            .Select(id => Enum.TryParse<PaletteCommand>(id, out var parsed) ? All.FirstOrDefault(c => c.Id == parsed) : null)
            .OfType<CommandDefinition>().Concat(DefaultSuggestions).DistinctBy(c => c.Id).ToArray();
        var head = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var parsed = Match(text);
        var results = new List<(int Rank, CommandDefinition Command)>();
        foreach (var command in All)
        {
            var rank = int.MaxValue;
            foreach (var token in Tokens(command))
            {
                if (token.Equals(text, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, -1);
                else if (token.StartsWith(text, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 0);
                else if (token.Equals(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 1);
                else if (token.StartsWith(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 2);
                else if (head.Length >= 2 && token.Contains(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 3);
            }
            if (parsed?.Command.Id == command.Id) rank = -2;
            if (rank == int.MaxValue && $"{command.Description} {command.Category}".Contains(text, StringComparison.OrdinalIgnoreCase)) rank = 4;
            if (rank != int.MaxValue) results.Add((rank, command));
        }
        return results
            .OrderBy(r => r.Rank)
            .ThenBy(r => favorites?.Contains(r.Command.Id.ToString()) == true ? 0 : 1)
            .ThenBy(r => recents is null ? int.MaxValue : Enumerable.Range(0, recents.Count).FirstOrDefault(i => recents[i] == r.Command.Id.ToString(), int.MaxValue))
            .ThenBy(r => r.Command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Command)
            .ToArray();
    }
}
