using System.Text.RegularExpressions;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Parsers for command-palette arguments: durations ("1 hour 20 minutes"), seek
/// directions, and alarm times ("7:30 AM weekdays"). All deterministic — invalid input fails
/// with an explanatory message rather than guessing.</summary>
public static partial class CommandParsers
{
    [GeneratedRegex(@"(\d+)\s*(hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();

    [GeneratedRegex(@"^(?<h>\d{1,2}):(?<m>\d{2})\s*(?<ap>[AaPp][Mm])?\s*(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AlarmTimeRegex();

    public static bool TryParseDuration(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var matches = DurationTokenRegex().Matches(text);
        if (matches.Count == 0) return false;
        double totalSeconds = 0;
        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups[1].Value, out var amount) || amount < 0) return false;
            var unit = match.Groups[2].Value;
            totalSeconds += char.ToLowerInvariant(unit[0]) switch
            {
                'h' => amount * 3600d,
                'm' => amount * 60d,
                _ => amount,
            };
        }
        if (totalSeconds < 1) return false;
        value = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    /// <summary>"timer 10 minutes for laundry", "timer 1 hour 20 minutes", "timer 90s tea".
    /// Label comes from "for &lt;label&gt;" or any leftover non-numeric words.</summary>
    public static bool TryParseTimerArgument(string argument, out TimeSpan duration, out string? label)
    {
        duration = TimeSpan.Zero;
        label = null;
        var text = argument.Trim();
        if (text.Length == 0) return false;

        var durationPart = text;
        string? labelPart = null;
        var forIndex = text.IndexOf(" for ", StringComparison.OrdinalIgnoreCase);
        if (forIndex >= 0)
        {
            durationPart = text[..forIndex].Trim();
            labelPart = text[(forIndex + 5)..].Trim();
            if (labelPart.Length == 0) labelPart = null;
        }

        if (!TryParseDuration(durationPart, out duration)) return false;
        if (labelPart is not null)
        {
            label = labelPart;
            return true;
        }

        // No "for": any leftover words after stripping duration tokens become the label.
        var leftover = DurationTokenRegex().Replace(durationPart, " ");
        leftover = string.Join(' ', leftover.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (leftover.Length > 0)
        {
            if (leftover.Any(char.IsDigit)) return false;
            label = leftover;
        }
        return true;
    }

    public static bool TryParseSeek(string argument, out TimeSpan offset, out string? error)
    {
        offset = TimeSpan.Zero;
        error = null;
        var parts = argument.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Say a direction and duration, e.g. seek forward 10 seconds.";
            return false;
        }
        var sign = parts[0].ToLowerInvariant() switch
        {
            "forward" or "fwd" => 1,
            "back" or "backward" or "rewind" => -1,
            _ => 0,
        };
        if (sign == 0)
        {
            error = "Say forward or back, e.g. seek forward 10 seconds.";
            return false;
        }
        if (parts.Length < 2 || !TryParseDuration(parts[1], out var duration))
        {
            error = "Add a duration, e.g. seek forward 10 seconds.";
            return false;
        }
        offset = TimeSpan.FromSeconds(sign * duration.TotalSeconds);
        return true;
    }

    public static bool TryParseAlarmTime(string argument, out int hour24, out int minute,
        out bool use24Hour, out AlarmRepeat repeat, out string? error)
    {
        hour24 = 0;
        minute = 0;
        use24Hour = true;
        repeat = AlarmRepeat.Once;
        error = null;
        var text = argument.Trim();
        if (text.Length == 0)
        {
            error = "Add a time, e.g. alarm 7:30 AM.";
            return false;
        }
        var match = AlarmTimeRegex().Match(text);
        if (!match.Success)
        {
            error = "Use a time like 7:30 AM or 19:30, e.g. alarm 7:30 AM weekdays.";
            return false;
        }
        if (!int.TryParse(match.Groups["h"].Value, out var hour) ||
            !int.TryParse(match.Groups["m"].Value, out var min) || min > 59)
        {
            error = "Minutes must be 00–59.";
            return false;
        }
        if (match.Groups["ap"].Success)
        {
            var amPm = match.Groups["ap"].Value;
            if (hour is < 1 or > 12)
            {
                error = "Hour must be 1–12 with AM/PM.";
                return false;
            }
            hour24 = hour % 12;
            if (amPm[0] is 'P' or 'p') hour24 += 12;
            use24Hour = false;
        }
        else
        {
            if (hour > 23)
            {
                error = "Hour must be 0–23.";
                return false;
            }
            hour24 = hour;
            use24Hour = true;
        }
        minute = min;

        var rest = match.Groups["rest"].Value.Trim();
        if (rest.Length == 0) return true;
        switch (rest.ToLowerInvariant())
        {
            case "weekdays": repeat = AlarmRepeat.Weekdays; return true;
            case "weekends": repeat = AlarmRepeat.Weekends; return true;
            case "daily" or "every day": repeat = AlarmRepeat.Daily; return true;
            case "once": repeat = AlarmRepeat.Once; return true;
            default:
                error = "Say weekdays, weekends, daily, or leave it off.";
                return false;
        }
    }
}
