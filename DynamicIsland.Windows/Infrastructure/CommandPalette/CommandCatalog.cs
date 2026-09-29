namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public enum PaletteCommand
{
    Pause, Play, Next, Previous, Seek, Mute, Unmute,
    Ask, Say, Compare,
    Timer, PauseTimer, ResumeTimer, CancelTimer, ShowTimers,
    Alarm, CancelAlarm, ShowAlarms,
    StartStopwatch, PauseStopwatch, ResumeStopwatch, ResetStopwatch,
    FocusOn, FocusOff, Expand, Collapse, OpenSettings,
    Help
}

public sealed record CommandDefinition(
    PaletteCommand Id,
    string Name,
    string[] Aliases,
    string Description,
    string Example);

public sealed record CommandMatch(CommandDefinition Command, string Argument);

/// <summary>Deterministic command catalogue: the first word (or longest alias) identifies the
/// command; everything after it is the argument. No free-text interpretation.</summary>
public static class CommandCatalog
{
    public static readonly CommandDefinition[] All =
    [
        new(PaletteCommand.Pause, "pause", [], "Pause media playback", "pause"),
        new(PaletteCommand.Play, "play", ["resume"], "Play or resume media", "play"),
        new(PaletteCommand.Next, "next", ["next track"], "Skip to the next track", "next"),
        new(PaletteCommand.Previous, "previous", ["previous track", "prev"], "Skip to the previous track", "previous"),
        new(PaletteCommand.Seek, "seek", [], "Seek forward or back", "seek forward 10 seconds"),
        new(PaletteCommand.Mute, "mute", [], "Mute system audio", "mute"),
        new(PaletteCommand.Unmute, "unmute", [], "Unmute system audio", "unmute"),
        new(PaletteCommand.Ask, "ask", [], "Open Q with an optional question", "ask why is the sky blue"),
        new(PaletteCommand.Say, "say", [], "Open Q in voice mode", "say"),
        new(PaletteCommand.Compare, "compare", [], "Compare two options in Q", "compare train or drive"),
        new(PaletteCommand.Timer, "timer", [], "Start a timer or open timer controls", "timer 10 minutes for laundry"),
        new(PaletteCommand.PauseTimer, "pause timer", [], "Pause a running timer", "pause timer laundry"),
        new(PaletteCommand.ResumeTimer, "resume timer", [], "Resume a paused timer", "resume timer laundry"),
        new(PaletteCommand.CancelTimer, "cancel timer", [], "Cancel a timer", "cancel timer laundry"),
        new(PaletteCommand.ShowTimers, "show timers", ["timers"], "Open the timer list", "show timers"),
        new(PaletteCommand.Alarm, "alarm", [], "Set an alarm or open alarm setup", "alarm 7:30 AM weekdays"),
        new(PaletteCommand.CancelAlarm, "cancel alarm", [], "Cancel an alarm", "cancel alarm 7:30 AM"),
        new(PaletteCommand.ShowAlarms, "show alarms", ["alarms"], "Open the alarm list", "show alarms"),
        new(PaletteCommand.StartStopwatch, "start stopwatch", [], "Start the stopwatch", "start stopwatch"),
        new(PaletteCommand.PauseStopwatch, "pause stopwatch", [], "Pause the stopwatch", "pause stopwatch"),
        new(PaletteCommand.ResumeStopwatch, "resume stopwatch", [], "Resume the stopwatch", "resume stopwatch"),
        new(PaletteCommand.ResetStopwatch, "reset stopwatch", [], "Reset the stopwatch", "reset stopwatch"),
        new(PaletteCommand.FocusOn, "focus on", [], "Turn focus mode on", "focus on"),
        new(PaletteCommand.FocusOff, "focus off", [], "Turn focus mode off", "focus off"),
        new(PaletteCommand.Expand, "expand", [], "Expand the island", "expand"),
        new(PaletteCommand.Collapse, "collapse", [], "Collapse the island", "collapse"),
        new(PaletteCommand.OpenSettings, "open settings", ["settings"], "Open settings", "open settings"),
        new(PaletteCommand.Help, "help", ["show commands", "commands"], "Show available commands", "help"),
    ];

    // Shown when the input is empty — the most useful entry points first.
    public static readonly CommandDefinition[] DefaultSuggestions =
    [
        ById(PaletteCommand.Help),
        ById(PaletteCommand.Ask),
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
        return new CommandMatch(best, argument);
    }

    /// <summary>Filter for the suggestion list. Rank: full-input prefix beats first-word matches,
    /// exact beats prefix beats contains — so typing "pause ti" puts "pause timer" first.</summary>
    public static IReadOnlyList<CommandDefinition> Filter(string query)
    {
        var text = query.Trim();
        if (text.Length == 0) return DefaultSuggestions;
        var head = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var results = new List<(int Rank, CommandDefinition Command)>();
        foreach (var command in All)
        {
            var rank = int.MaxValue;
            foreach (var token in Tokens(command))
            {
                if (token.StartsWith(text, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 0);
                else if (token.Equals(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 1);
                else if (token.StartsWith(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 2);
                else if (head.Length >= 2 && token.Contains(head, StringComparison.OrdinalIgnoreCase)) rank = Math.Min(rank, 3);
            }
            if (rank != int.MaxValue) results.Add((rank, command));
        }
        return results
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Command)
            .ToArray();
    }
}
