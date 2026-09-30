using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class AnthropicQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    private const string ApiVersion = "2023-06-01";

    public override QProviderInfo Info { get; } = new("anthropic", "Anthropic",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "claude-sonnet-5-5", "https://api.anthropic.com");

    public override async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,
        CancellationToken cancellationToken, string? baseUrl = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/v1/models"));
        AddAnthropicHeaders(request, credential);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = ProviderJson.String(item, "id");
                if (id.Length == 0) continue;
                var name = ProviderJson.String(item, "display_name");
                models.Add(new QModelInfo(id, name.Length == 0 ? id : name, Info.Capabilities,
                    string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxResponseTokens,
            ["system"] = QPromptComposer.SystemPrompt(request),
            ["messages"] = Messages(request),
            ["stream"] = true
        };
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (effort != "auto") body["output_config"] = new { effort };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/v1/messages")) { Content = Json(body) };
        AddAnthropicHeaders(message, credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();

        var sawText = false;
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var type = ProviderJson.String(root, "type");
            if (type == "error")
            {
                yield return new QStreamEvent.Failed(Sanitize(ProviderJson.ErrorMessage(root) ?? "Anthropic reported a streaming error."));
                yield break;
            }
            if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta) &&
                ProviderJson.String(delta, "type") == "text_delta")
            {
                var text = ProviderJson.String(delta, "text");
                if (text.Length > 0) { sawText = true; yield return new QStreamEvent.Text(text); }
            }
            if (type == "message_delta" && root.TryGetProperty("delta", out delta) &&
                ProviderJson.String(delta, "stop_reason") == "max_tokens")
            {
                yield return new QStreamEvent.Failed("Anthropic reached max_tokens before finishing. Increase the response budget or lower effort.");
                yield break;
            }
        }
        if (!sawText)
        {
            yield return new QStreamEvent.Failed("Anthropic completed without returning a text block.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }

    private static object[] Messages(QRequest request)
    {
        var messages = new List<object>();
        foreach (var item in request.History.Where(item => item.Role is "user" or "assistant"))
            messages.Add(new { role = item.Role, content = item.Content });
        var content = new List<object>();
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            content.Add(new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(image) } });
        content.Add(new { type = "text", text = TextPrompt(request) });
        messages.Add(new { role = "user", content });
        return messages.ToArray();
    }

    private static void AddAnthropicHeaders(HttpRequestMessage request, string? credential)
    {
        if (!string.IsNullOrWhiteSpace(credential)) request.Headers.TryAddWithoutValidation("x-api-key", credential);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
    }
}
