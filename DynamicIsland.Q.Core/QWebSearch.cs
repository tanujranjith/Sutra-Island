using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Q.Core;

public sealed record QWebSource(string Url, string Title, int? StartIndex = null, int? EndIndex = null, string? CitedText = null)
{
    public string Domain => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : Url;
}

public static class QWebSearch
{
    public static bool Supports(string? provider, string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        return provider?.ToLowerInvariant() switch
        {
            "openai" => (m.StartsWith("gpt-6") || m.StartsWith("gpt-5") || m.StartsWith("gpt-4.1") || m.StartsWith("gpt-4o") || m.StartsWith("o3")) && !m.Contains("nano") && !m.Contains("search-preview"),
            "anthropic" => m.StartsWith("claude-"),
            "gemini" => m.StartsWith("gemini-") && !m.Contains("1.5"),
            "groq" => m.StartsWith("openai/gpt-oss-") || m is "groq/compound" or "groq/compound-mini",
            "xai" => m.StartsWith("grok-4"),
            "deepseek" => m is "deepseek-flash" or "deepseek-v4-pro",
            "openrouter" or "ollama" or "codex" => true,
            _ => false
        };
    }

    public static string Description(string? provider, string? model) => provider?.ToLowerInvariant() switch
    {
        "groq" when !Supports(provider, model) => "Choose openai/gpt-oss-120b, openai/gpt-oss-20b, or a Groq Compound model for web search.",
        "deepseek" when !Supports(provider, model) => "Choose deepseek-flash or deepseek-v4-pro for DeepSeek web search.",
        "ollama" => "Searches each enabled question using Ollama’s web-search API. Save an Ollama API key below; your selected local model writes the answer.",
        "openrouter" => "OpenRouter’s web plugin searches each enabled question, using native search where available and Exa otherwise.",
        "anthropic" => "Claude can search when needed. Web search must also be enabled for your Anthropic organization.",
        "codex" => "Lets Codex search the live web when needed. Your account’s managed search restrictions still apply.",
        _ when !Supports(provider, model) => "The selected model does not advertise a supported web-search integration. Choose a supported model to use this setting.",
        _ => "Lets the selected model search when needed. Applies to chat and voice. Provider search charges may apply."
    };

    public static bool IsWebUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && !string.IsNullOrWhiteSpace(uri.Host);

    public static int Utf16Offset(string text, int codePoints)
    {
        var offset = 0;
        foreach (var rune in text.EnumerateRunes()) { if (codePoints-- <= 0) break; offset += rune.Utf16SequenceLength; }
        return offset;
    }

    public static IEnumerable<QWebSource> ReadSources(JsonElement value, string? text = null, int depth = 0)
    {
        if (depth > 12) yield break;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) foreach (var source in ReadSources(child, text, depth + 1)) yield return source;
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object) yield break;
        var url = ProviderJson.String(value, "url");
        if (url.Length == 0) url = ProviderJson.String(value, "uri");
        if (IsWebUrl(url))
        {
            var title = ProviderJson.String(value, "title");
            int? start = Number(value, "start_index"), end = Number(value, "end_index");
            var cited = ProviderJson.String(value, "cited_text");
            if (cited.Length == 0 && text is not null && start >= 0 && end > start)
            {
                var a = Utf16Offset(text, start.Value); var b = Utf16Offset(text, end.Value);
                if (b > a && b <= text.Length) cited = text[a..b];
            }
            yield return new(url, title.Length > 0 ? title : new Uri(url).Host, start, end, cited.Length > 0 ? cited : null);
        }
        // Follow only documented tool/citation containers, never arbitrary response strings.
        foreach (var key in new[] { "annotations", "annotation", "url_citation", "citation", "citations", "sources", "results", "content", "output", "message", "delta", "choices", "content_block", "action", "item", "response", "search_results", "browser_results", "executed_tools", "server_tool_calls", "calls", "call", "page", "web", "groundingChunks" })
            if (value.TryGetProperty(key, out var child)) foreach (var source in ReadSources(child, text, depth + 1)) yield return source;
    }

    public static IEnumerable<QWebSource> MarkdownSources(string text)
    {
        foreach (Match match in Regex.Matches(text, @"\[([^\]\r\n]+)\]\((https?://[^\s)]+)\)"))
            if (IsWebUrl(match.Groups[2].Value)) yield return new(match.Groups[2].Value, match.Groups[1].Value, CitedText: match.Value);
    }

    private static int? Number(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) ? number : null;
}
