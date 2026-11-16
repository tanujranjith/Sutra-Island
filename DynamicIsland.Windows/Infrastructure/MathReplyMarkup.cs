using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Infrastructure;

internal sealed record ReplyFormula(string Latex, bool Display);
internal sealed record MathReplyDocument(string Text, IReadOnlyDictionary<char, ReplyFormula> Formulas);

/// <summary>Protects completed math regions before the plain-text presentation pass.</summary>
internal static class MathReplyMarkup
{
    // WPF-Math lacks amsmath's outer \boxed command; draw its frame natively.
    public static (string Latex, bool Boxed) UnwrapBox(string latex)
    {
        var value = latex.Trim();
        var command = value.StartsWith(@"\boxed", StringComparison.Ordinal) ? @"\boxed"
            : value.StartsWith(@"\fbox", StringComparison.Ordinal) ? @"\fbox" : "";
        if (command.Length == 0) return (latex, false);
        var start = command.Length;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        if (start >= value.Length || value[start] != '{') return (latex, false);
        var depth = 0;
        for (var i = start; i < value.Length; i++)
        {
            if (i > 0 && value[i - 1] == '\\') continue;
            if (value[i] == '{') depth++;
            if (value[i] == '}' && --depth == 0)
                return i == value.Length - 1 ? (value[(start + 1)..i], true) : (latex, false);
        }
        return (latex, false);
    }
    // Code regions take precedence: dollar signs and TeX examples in code stay literal.
    private static readonly Regex Regions = new(
        @"(?<code>(?<fence>`{3,}|~{3,})[^\r\n]*\r?\n[\s\S]*?(?:\k<fence>|$)|(?<tick>`+)[^\r\n]*?\k<tick>)|(?<!\\)\\\[(?<display>[\s\S]*?)(?<!\\)\\\]|(?<!\\)\$\$(?<displayDollar>[\s\S]*?)(?<!\\)\$\$|(?<!\\)\\\((?<inline>[\s\S]*?)(?<!\\)\\\)|(?<![\\$])\$(?![\s$])(?<inlineDollar>[^\r\n]*?\S)(?<!\\)\$(?!\$)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    public static MathReplyDocument Prepare(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new("", new Dictionary<char, ReplyFormula>());
        var formulas = new Dictionary<char, ReplyFormula>();
        var output = new StringBuilder(raw.Length);
        var cursor = 0; var token = 0xE000;
        try
        {
            foreach (Match match in Regions.Matches(raw))
            {
                if (match.Groups["code"].Success) continue;
                var display = match.Groups["display"].Success || match.Groups["displayDollar"].Success;
                var latex = match.Groups["display"].Success ? match.Groups["display"].Value
                    : match.Groups["displayDollar"].Success ? match.Groups["displayDollar"].Value
                    : match.Groups["inline"].Success ? match.Groups["inline"].Value : match.Groups["inlineDollar"].Value;
                if (string.IsNullOrWhiteSpace(latex) || (match.Groups["inlineDollar"].Success && (char.IsWhiteSpace(latex[0]) || char.IsWhiteSpace(latex[^1])))) continue;
                while (token <= 0xF8FF && raw.Contains((char)token)) token++;
                if (token > 0xF8FF || formulas.Count >= 256) break;
                output.Append(raw, cursor, match.Index - cursor);
                var key = (char)token++;
                formulas[key] = new(latex.Trim(), display);
                output.Append(key);
                cursor = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException) { return new(ResponseTextFormatter.Format(raw), new Dictionary<char, ReplyFormula>()); }
        output.Append(raw, cursor, raw.Length - cursor);
        var text = output.ToString();
        foreach (var formula in formulas.Where(pair => pair.Value.Display))
            text = Regex.Replace(text, @"[ \t]*(?:\r?\n[ \t]*)*" + formula.Key + @"[ \t]*(?:\r?\n[ \t]*)*", "\n" + formula.Key + "\n");
        return new(ResponseTextFormatter.Format(text), formulas);
    }
}
