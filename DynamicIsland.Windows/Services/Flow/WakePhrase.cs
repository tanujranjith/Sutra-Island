using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Decides whether a Whisper transcript is someone saying "Hey Sutra", allowing the near
/// misses Whisper produces for an unfamiliar name ("hey sutro", "hey sue tra", "a sutra").
/// Anything said after the wake word in the same breath is returned as the command.
/// </summary>
public static partial class WakePhrase
{
    public sealed record Match(bool Woke, string Command);

    private static readonly string[] Greetings = ["hey", "hay", "hei", "heh", "hi", "a", "eh", "ay", "okay", "ok", "yo"];
    private static readonly HashSet<string> KnownMisses = new(StringComparer.Ordinal)
    {
        "sutra", "sutras", "sutro", "sutta", "sudra", "sutter", "suter", "shutra", "sootra", "soutra", "suitra",
        "satra", "sutre", "sutrah", "zutra", "cetra", "sutera", "sutrah", "sultra", "sutura", "sitra",
    };

    /// <summary>The voice detector's threshold above the noise floor; lower hears quieter speech (more sensitive).</summary>
    public static double SensitivityThreshold(string? sensitivity) => sensitivity?.ToLowerInvariant() switch
    {
        "high" => 7,
        "low" => 14,
        _ => 10,
    };

    [GeneratedRegex(@"[^a-z0-9' ]")] private static partial Regex NotWord();

    /// <param name="strict">Strict needs a greeting before the name; loose also accepts the name alone first.</param>
    public static Match Check(string? transcript, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return new(false, "");
        var words = NotWord().Replace(transcript.ToLowerInvariant().Replace('-', ' '), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('\'')).Where(w => w.Length > 0).ToList();
        var original = transcript.Trim();
        // Only the start of the utterance counts, so "I read a sutra" mid-sentence doesn't wake it.
        for (var i = 0; i < Math.Min(3, words.Count); i++)
        {
            var greeting = Greetings.Contains(words[i]);
            var start = greeting ? i + 1 : i;
            if (!greeting && (strict || i > 0)) continue;
            if (start >= words.Count) break;
            var used = NameAt(words, start);
            if (used == 0) continue;
            return new(true, CommandAfter(original, start + used));
        }
        return new(false, "");
    }

    /// <summary>How many words starting at <paramref name="i"/> spell the name, or 0.</summary>
    private static int NameAt(IReadOnlyList<string> words, int i)
    {
        if (IsName(words[i])) return 1;
        if (i + 1 < words.Count && IsName(words[i] + words[i + 1])) return 2; // "sue tra", "su tra"
        return 0;
    }

    public static bool IsName(string word)
    {
        if (word.EndsWith("'s")) word = word[..^2];
        if (KnownMisses.Contains(word)) return true;
        if (word.Length < 4 || word.Length > 7 || word[0] is not ('s' or 'z' or 'c')) return false;
        return Distance(word, "sutra") <= 2 && word.Contains('t');
    }

    private static string CommandAfter(string original, int wordsUsed)
    {
        var parts = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        var index = 0;
        // Re-walk the original text so the command keeps its own casing and punctuation.
        while (index < parts.Length && count < wordsUsed)
        {
            var bare = NotWord().Replace(parts[index].ToLowerInvariant().Replace('-', ' '), " ").Trim();
            count += bare.Length == 0 ? 0 : bare.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            index++;
        }
        var rest = string.Join(' ', parts.Skip(index)).Trim().TrimStart(',', '.', '!', '?', ';', ':', '-').Trim();
        return rest.Length == 0 ? "" : char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
