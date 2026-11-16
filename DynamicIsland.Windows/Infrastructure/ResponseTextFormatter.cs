using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Infrastructure;

internal static class ResponseTextFormatter
{
    public static string Format(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return string.Empty;
        response = TextEncodingRepair.RepairUtf8ReadAsWindows1252(response);
        var cleaned = response.Replace("```", string.Empty, StringComparison.Ordinal)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"(?m)^\s{0,3}#{1,6}\s+", string.Empty);
        return MathMarkupFormatter.Format(cleaned);
    }
}
