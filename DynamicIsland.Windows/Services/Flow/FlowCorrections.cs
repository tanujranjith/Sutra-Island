using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Finds words you fixed by hand right after Sutra Flow pasted them ("Sutra Flow wrote Circ as
/// Cirk, you retyped it"), so they can be added to the dictionary. Pure: it compares the field's
/// text just after the paste with the same field a little later.
/// </summary>
public static partial class FlowCorrections
{
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-\.]*[\p{L}\p{N}]|[\p{L}\p{N}]")] private static partial Regex Word();

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "had", "her", "was", "one", "our", "out",
        "day", "get", "has", "him", "his", "how", "man", "new", "now", "old", "see", "two", "way", "who", "did", "its",
        "let", "put", "say", "she", "too", "use", "that", "with", "have", "this", "will", "your", "from", "they", "know",
        "want", "been", "good", "much", "some", "time", "very", "when", "come", "here", "just", "like", "long", "make",
        "many", "more", "only", "over", "such", "take", "than", "them", "well", "were", "what", "then", "there", "their",
    };

    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrEmpty(text) ? [] : Word().Matches(text).Select(m => m.Value.TrimEnd('.')).Where(w => w.Length > 0).ToList();

    /// <summary>
    /// Words to add: each one is new in <paramref name="after"/>, closely resembles a pasted word that
    /// disappeared from the field, and isn't a common word or already in the vocabulary.
    /// </summary>
    public static IReadOnlyList<string> Find(string pasted, string before, string after, IReadOnlyCollection<string> vocabulary)
    {
        var pastedWords = Words(pasted);
        if (pastedWords.Count == 0 || string.IsNullOrEmpty(after) || before == after) return [];
        var beforeSet = new HashSet<string>(Words(before), StringComparer.Ordinal);
        var afterWords = Words(after);
        var afterSet = new HashSet<string>(afterWords, StringComparer.Ordinal);
        var removed = pastedWords.Where(w => beforeSet.Contains(w) && !afterSet.Contains(w)).Distinct().ToList();
        if (removed.Count == 0 || removed.Count > 4) return []; // a rewrite, not a correction
        var added = afterWords.Where(w => !beforeSet.Contains(w)).Distinct().ToList();
        var known = new HashSet<string>(vocabulary, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var fix in added)
        {
            if (fix.Length < 3 || fix.Length > 40 || known.Contains(fix) || Common.Contains(fix) || fix.All(char.IsDigit)) continue;
            if (!removed.Any(old => Similar(old, fix))) continue;
            result.Add(fix);
            known.Add(fix);
        }
        return result;
    }

    /// <summary>A spelling fix, not a different word: close in letters, and not just a capitalization change of a common word.</summary>
    public static bool Similar(string old, string fix)
    {
        if (string.Equals(old, fix, StringComparison.Ordinal)) return false;
        var a = old.ToLowerInvariant();
        var b = fix.ToLowerInvariant();
        if (a == b) return char.IsUpper(fix[0]) && !Common.Contains(fix); // "sutra" → "Sutra"
        var distance = Distance(a, b);
        var limit = Math.Max(1, Math.Min(3, Math.Max(a.Length, b.Length) / 3));
        return distance <= limit && a[0] == b[0] || distance <= 1;
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
