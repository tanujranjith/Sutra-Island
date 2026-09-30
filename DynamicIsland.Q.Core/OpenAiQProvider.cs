using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class OpenAiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("openai", "OpenAI · API key",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "gpt-6-luna", "https://api.openai.com/v1");

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["instructions"] = QPromptComposer.SystemPrompt(request),
            ["input"] = ResponsesInput(request),
            ["max_output_tokens"] = request.MaxResponseTokens,
            ["stream"] = true,
            ["store"] = false
        };
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (effort != "auto") body["reasoning"] = new { effort };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/responses")) { Content = Json(body) };
        AddBearer(message, credential);
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
            if (type is "response.output_text.delta" or "response.refusal.delta")
            {
                var delta = ProviderJson.String(root, "delta");
                if (delta.Length > 0) { sawText = true; yield return new QStreamEvent.Text(delta); }
                continue;
            }
            if (type is "error" or "response.failed" or "response.incomplete")
            {
                var error = ResponsesError(root, type);
                yield return new QStreamEvent.Failed(error);
                yield break;
            }
        }
        if (!sawText)
        {
            yield return new QStreamEvent.Failed("OpenAI completed without returning text.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }

    private static object[] ResponsesInput(QRequest request)
    {
        var input = new List<object>();
        foreach (var item in request.History.Where(item => item.Role is "user" or "assistant"))
            input.Add(new { role = item.Role, content = item.Content });
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
        {
            input.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "input_text", text = TextPrompt(request) },
                    new { type = "input_image", image_url = $"data:image/png;base64,{Convert.ToBase64String(image)}", detail = "auto" }
                }
            });
        }
        else input.Add(new { role = "user", content = TextPrompt(request) });
        return input.ToArray();
    }

    private static string ResponsesError(JsonElement root, string type)
    {
        if (ProviderJson.ErrorMessage(root) is { } direct) return Sanitize(direct);
        if (root.TryGetProperty("response", out var response) && ProviderJson.ErrorMessage(response) is { } responseError)
            return Sanitize(responseError);
        if (root.TryGetProperty("response", out response) && response.TryGetProperty("incomplete_details", out var details))
        {
            var reason = ProviderJson.String(details, "reason");
            if (reason.Length > 0) return $"OpenAI response was incomplete: {Sanitize(reason)}.";
        }
        return type == "response.incomplete" ? "OpenAI could not finish the response within the configured limits." : "OpenAI could not complete the response.";
    }
}
