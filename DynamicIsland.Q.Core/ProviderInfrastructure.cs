using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class QProviderRegistry(IEnumerable<IQProvider> providers) : IQProviderRegistry
{
    public IReadOnlyList<IQProvider> Providers { get; } = providers.ToArray();
    public IQProvider? Find(string id) => Providers.FirstOrDefault(provider =>
        string.Equals(provider.Info.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ProviderApiException : HttpRequestException
{
    public ProviderApiException(string provider, string message, HttpStatusCode? statusCode = null,
        string? code = null, string? requestId = null, TimeSpan? retryAfter = null) : base(message, null, statusCode)
    {
        Provider = provider;
        Code = code;
        RequestId = requestId;
        RetryAfter = retryAfter;
    }

    public string Provider { get; }
    public string? Code { get; }
    public string? RequestId { get; }
    public TimeSpan? RetryAfter { get; }
}

public static class QProviderPolicy
{
    public static IReadOnlyList<string> ModelSuggestions(string? providerId, string? current = null)
    {
        var suggestions = providerId?.ToLowerInvariant() switch
        {
            "openai" => new[] { "gpt-6-luna", "gpt-6.1-sol", "gpt-6-astra" },
            "anthropic" => new[] { "claude-sonnet-5-5", "claude-opus-5-5", "claude-fable-5-1", "claude-haiku-4-5", "claude-sonnet-5" },
            "gemini" => new[] { "gemini-3.7-flash", "gemini-3.5-flash-lite", "gemini-3.1-pro-preview" },
            "groq" => new[] { "qwen/qwen3.8-27b", "qwen/qwen3.6-27b", "openai/gpt-oss-120b", "openai/gpt-oss-20b" },
            "xai" => new[] { "grok-4.6" },
            "openrouter" => new[] { "~openai/gpt-latest", "~anthropic/claude-sonnet-latest", "openrouter/auto", "openrouter/free" },
            "deepseek" => new[] { "deepseek-v4-flash", "deepseek-v4-pro", "deepseek-v4-flash-vision-exp" },
            "ollama" => new[] { "llama3.2-vision", "gemma3", "qwen3" },
            _ => Array.Empty<string>()
        };
        return new[] { current }.Concat(suggestions).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> EffortOptions(string? providerId, string? model = null) => providerId?.ToLowerInvariant() switch
    {
        "openai" when model?.StartsWith("gpt-6-luna", StringComparison.OrdinalIgnoreCase) == true =>
            ["auto", "none", "low", "medium", "high", "xhigh", "max"],
        "openai" when model?.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase) == true =>
            ["auto", "low", "medium", "high", "xhigh", "max"],
        "openai" when model?.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase) == true =>
            ["auto", "none", "low", "medium", "high", "xhigh", "max"],
        "openai" => ["auto", "none", "minimal", "low", "medium", "high", "xhigh", "max"],
        "anthropic" when model?.Contains("haiku", StringComparison.OrdinalIgnoreCase) == true => ["auto"],
        "anthropic" when model?.Contains("sonnet-4-6", StringComparison.OrdinalIgnoreCase) == true ||
            model?.Contains("opus-4-6", StringComparison.OrdinalIgnoreCase) == true => ["auto", "low", "medium", "high", "max"],
        "anthropic" => ["auto", "low", "medium", "high", "xhigh", "max"],
        "gemini" when model?.Contains("3.7", StringComparison.OrdinalIgnoreCase) == true => ["auto", "low", "medium", "high"],
        "gemini" => ["auto", "minimal", "low", "medium", "high"],
        "groq" when model?.Contains("qwen3.8", StringComparison.OrdinalIgnoreCase) == true ||
            model?.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase) == true =>
            ["auto", "low", "medium", "high"],
        "groq" => ["auto", "none", "low", "medium", "high"],
        "xai" => ["auto", "low", "medium", "high", "xhigh"],
        "openrouter" => ["auto", "none", "minimal", "low", "medium", "high", "xhigh", "max"],
        "deepseek" => ["auto", "none", "low", "high", "max"],
        "ollama" => ["auto", "none", "low", "medium", "high"],
        _ => ["auto"]
    };

    public static string NormalizeEffort(string? providerId, string? model, string? requested)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? "auto" : requested.Trim().ToLowerInvariant();
        var options = EffortOptions(providerId, model);
        return options.Contains(value, StringComparer.OrdinalIgnoreCase) ? value : "auto";
    }
}

internal static class ProviderJson
{
    public static string String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    public static string? ErrorMessage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            var message = String(error, "message");
            if (message.Length > 0) return message;
        }
        var direct = String(root, "message");
        return direct.Length > 0 ? direct : null;
    }

    public static string? ErrorCode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return null;
        var code = String(error, "code");
        if (code.Length > 0) return code;
        code = String(error, "type");
        return code.Length > 0 ? code : null;
    }

    public static IEnumerable<string> ChatText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) yield break;
        foreach (var choice in choices.EnumerateArray())
        {
            var container = choice.TryGetProperty("delta", out var delta) ? delta
                : choice.TryGetProperty("message", out var message) ? message : default;
            if (container.ValueKind != JsonValueKind.Object) continue;
            var text = String(container, "content");
            if (text.Length > 0) yield return text;
        }
    }

    public static bool HasChatReasoning(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return false;
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;
            if (String(delta, "reasoning").Length > 0 || String(delta, "reasoning_content").Length > 0 ||
                String(delta, "thinking").Length > 0) return true;
            if (delta.TryGetProperty("reasoning_details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0) return true;
        }
        return false;
    }
}

public abstract class HttpQProvider(HttpClient? httpClient = null) : IQProvider
{
    protected readonly HttpClient Http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    public abstract QProviderInfo Info { get; }
    public abstract IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        CancellationToken cancellationToken);

    public virtual async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,
        CancellationToken cancellationToken, string? baseUrl = null)
    {
        if (!Info.Capabilities.HasFlag(QProviderCapabilities.ModelDiscovery))
            return [new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true)];
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(baseUrl, Info.DefaultBaseUrl ?? string.Empty, "/models"));
        AddBearer(request, credential);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = ProviderJson.String(item, "id");
                if (id.Length == 0 || !IsUsableModel(id)) continue;
                models.Add(new QModelInfo(id, id, CapabilitiesForModel(id),
                    string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    protected virtual bool IsUsableModel(string id) =>
        !new[] { "embedding", "moderation", "whisper", "transcribe", "tts", "realtime", "image", "guard" }
            .Any(part => id.Contains(part, StringComparison.OrdinalIgnoreCase));

    protected virtual QProviderCapabilities CapabilitiesForModel(string id) => Info.Capabilities;

    protected static string Endpoint(string? baseUrl, string fallback, string suffix)
    {
        var root = (string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl!).TrimEnd('/');
        return root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? root : root + suffix;
    }

    protected static void AddBearer(HttpRequestMessage request, string? credential)
    {
        if (!string.IsNullOrWhiteSpace(credential))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    }

    protected static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    protected static string ContextPrompt(QRequest request)
    {
        var imageNote = request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 }
            ? " The request also contains an image of the active window."
            : string.Empty;
        return QPromptComposer.ContextText(request.ScreenContext) + imageNote;
    }

    protected static string TextPrompt(QRequest request) => $"{ContextPrompt(request)}\n\nUser request:\n{request.Prompt}";

    protected static object[] OpenAiChatMessages(QRequest request)
    {
        var messages = new List<object> { new { role = "system", content = QPromptComposer.SystemPrompt(request) } };
        foreach (var item in request.History.Where(item => item.Role is "user" or "assistant"))
            messages.Add(new { role = item.Role, content = item.Content });
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
        {
            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = TextPrompt(request) },
                    new { type = "image_url", image_url = new { url = $"data:image/png;base64,{Convert.ToBase64String(image)}", detail = "auto" } }
                }
            });
        }
        else messages.Add(new { role = "user", content = TextPrompt(request) });
        return messages.ToArray();
    }

    protected async IAsyncEnumerable<QStreamEvent> StreamChatCompletionsAsync(QRequest request, string? credential,
        string? baseUrl, string fallback, string maxTokenParameter,
        IReadOnlyDictionary<string, object?>? additionalBody,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = OpenAiChatMessages(request),
            ["stream"] = true,
            [maxTokenParameter] = request.MaxResponseTokens
        };
        if (additionalBody is not null)
            foreach (var pair in additionalBody) body[pair.Key] = pair.Value;

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(baseUrl, fallback, "/chat/completions")) { Content = Json(body) };
        AddBearer(message, credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();

        var sawText = false;
        var sawReasoning = false;
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (ProviderJson.ErrorMessage(root) is { } error)
            {
                yield return new QStreamEvent.Failed(Sanitize(error));
                yield break;
            }
            sawReasoning |= ProviderJson.HasChatReasoning(root);
            foreach (var text in ProviderJson.ChatText(root))
            {
                sawText = true;
                yield return new QStreamEvent.Text(text);
            }
            if (TryFinishReason(root, out var reason) && string.Equals(reason, "length", StringComparison.OrdinalIgnoreCase))
            {
                yield return new QStreamEvent.Failed("The provider reached the output-token limit before finishing. Increase the response budget or lower reasoning effort.");
                yield break;
            }
        }

        if (!sawText && sawReasoning)
        {
            yield return new QStreamEvent.Failed("The model used its response budget for reasoning but returned no final answer. Increase the response budget or lower reasoning effort.");
            yield break;
        }
        if (!sawText)
        {
            yield return new QStreamEvent.Failed("The provider completed without returning text.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }

    protected static async IAsyncEnumerable<string> SseDataAsync(HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var value = data.ToString().TrimEnd('\n');
                    data.Clear();
                    if (value.Length > 0 && value != "[DONE]") yield return value;
                }
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (data.Length > 0) data.Append('\n');
            data.Append(line[5..].TrimStart());
        }
        if (data.Length > 0)
        {
            var value = data.ToString().TrimEnd('\n');
            if (value.Length > 0 && value != "[DONE]") yield return value;
        }
    }

    protected static async IAsyncEnumerable<string> NdjsonAsync(HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, string provider,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? detail = null;
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            detail = ProviderJson.ErrorMessage(document.RootElement);
            code = ProviderJson.ErrorCode(document.RootElement);
        }
        catch (JsonException) { }
        detail = Sanitize(detail ?? response.ReasonPhrase ?? "Request failed");
        var requestId = Header(response, "request-id") ?? Header(response, "x-request-id") ?? Header(response, "cf-ray");
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var suffix = requestId is null ? string.Empty : $" (request {requestId})";
        throw new ProviderApiException(provider, $"{provider} returned {(int)response.StatusCode}: {detail}{suffix}",
            response.StatusCode, code, requestId, retryAfter);
    }

    protected static string Sanitize(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 300 ? clean : clean[..300];
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static bool TryFinishReason(JsonElement root, out string reason)
    {
        reason = string.Empty;
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return false;
        foreach (var choice in choices.EnumerateArray())
        {
            reason = ProviderJson.String(choice, "finish_reason");
            if (reason.Length > 0) return true;
        }
        return false;
    }
}
