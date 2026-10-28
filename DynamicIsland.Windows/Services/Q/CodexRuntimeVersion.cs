using System.Text.RegularExpressions;
namespace DynamicIsland.Windows.Services.Q;

public sealed record CodexRuntimeVersion(Version Core, string Value, bool IsPrerelease)
{
    private static readonly Regex Pattern = new(@"^v?(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public bool IsAtLeast(Version minimum) => Core > minimum || Core == minimum && !IsPrerelease;
    public static bool TryParse(string? text, out CodexRuntimeVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256) return false;
        var token = text.Trim(); var match = Pattern.Match(token);
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out var core)) return false;
        version = new(core, token.TrimStart('v'), match.Groups["pre"].Success); return true;
    }
    public static bool TryParseOutput(string? output, out CodexRuntimeVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(output) || output.Length > 16384) return false;
        if (TryParse(output, out version)) return true;
        var lines = output.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = lines.Where(line => line.StartsWith("codex-cli ", StringComparison.Ordinal)).ToArray();
        return candidates.Length == 1 && TryParse(candidates[0][10..], out version);
    }
}
