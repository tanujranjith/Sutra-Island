using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Coding helpers for Sutra Flow, platform-free so they can be unit-tested:
/// spoken casing ("camel case get user by id" → getUserById) in code editors, and
/// file tags ("at index dot tsx" → @index.tsx) in Cursor and Windsurf chat.
/// </summary>
public static partial class FlowCodeText
{
    private static readonly string[] Editors = ["code", "code - insiders", "cursor", "windsurf", "vscodium", "zed", "devenv", "rider64", "idea64", "pycharm64", "webstorm64"];
    private static readonly string[] TaggingEditors = ["cursor", "windsurf"];

    /// <summary>True when the process (name without .exe) is a code editor.</summary>
    public static bool IsCodeEditor(string? process) =>
        process is not null && Editors.Contains(process.Trim().ToLowerInvariant());

    /// <summary>True when the process is an editor whose AI chat takes @file tags.</summary>
    public static bool TakesFileTags(string? process) =>
        process is not null && TaggingEditors.Contains(process.Trim().ToLowerInvariant());

    /// <summary>
    /// "camel case user name" → userName, also snake, pascal, kebab, constant/screaming case.
    /// The name runs to the next punctuation mark, the word "end", or five words, whichever is first.
    /// </summary>
    public static string ApplySpokenCasing(string text) => Casing().Replace(text, m =>
    {
        var words = Word().Matches(m.Groups["words"].Value).Select(w => w.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return m.Value;
        var name = m.Groups["style"].Value.ToLowerInvariant() switch
        {
            "camel" => words[0] + string.Concat(words.Skip(1).Select(Cap)),
            "pascal" => string.Concat(words.Select(Cap)),
            "snake" => string.Join("_", words),
            "kebab" => string.Join("-", words),
            _ => string.Join("_", words).ToUpperInvariant(), // constant / screaming
        };
        return name;
    });

    /// <summary>"at index dot tsx" / "tag app.py" → @index.tsx / @app.py when the extension is a known file type.</summary>
    public static string TagFiles(string text) => FileTag().Replace(text, m =>
    {
        var name = Regex.Replace(m.Groups["name"].Value, @"\s+dot\s+", ".", RegexOptions.IgnoreCase).Replace(" ", "");
        var ext = m.Groups["ext"].Value.ToLowerInvariant();
        return "@" + name + "." + ext;
    });

    /// <summary>Splits an imported vocabulary file (one term per line, or comma, semicolon or tab separated; CSV quotes allowed).</summary>
    public static List<string> ParseVocabulary(string content) =>
        content.Split(['\r', '\n', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\'', ' '))
            .Where(t => t.Length is > 0 and <= 60 && !t.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Cap(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..];

    [GeneratedRegex(@"\b(?<style>camel|pascal|snake|kebab|constant|screaming)[\s-]?case\s+(?<words>[a-z0-9]+(?:\s+(?!end\b)[a-z0-9]+){0,4}?)(?=\s+end\b|\s*[,.!?;:\n]|\s*$)(?:\s+end\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex Casing();
    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex Word();
    [GeneratedRegex(@"\b(?:at|tag)\s+(?<name>[\w-]+(?:(?:\s+dot\s+|\.)[\w-]+)*?)(?:\s+dot\s+|\.)(?<ext>tsx|ts|jsx|js|mjs|py|cs|md|json|css|scss|html|rs|go|java|kt|cpp|c|h|hpp|txt|ya?ml|toml|xaml|sh|ps1|sql|swift|rb|php|vue|svelte)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FileTag();
}
