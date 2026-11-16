using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public abstract partial class HttpQProvider
{
    protected async IAsyncEnumerable<QStreamEvent> StreamResponsesAsync(QRequest request, string? credential, string? baseUrl,
        string fallback, string searchType = "web_search", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var input = request.History.Where(m => m.Role is "user" or "assistant").Select(m => (object)new { role = m.Role, content = m.Content }).ToList();
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            input.Add(new { role = "user", content = new object[] { new { type = "input_text", text = TextPrompt(request) }, new { type = "input_image", image_url = $"data:image/png;base64,{Convert.ToBase64String(image)}", detail = "auto" } } });
        else input.Add(new { role = "user", content = TextPrompt(request) });
        var body = new Dictionary<string, object?> { ["model"] = request.Model, ["instructions"] = QPromptComposer.SystemPrompt(request), ["input"] = input, ["max_output_tokens"] = request.MaxResponseTokens, ["stream"] = true, ["store"] = false };
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (request.WebSearch && Info.Id == "openai" && effort == "minimal") effort = "low";
        if (effort != "auto") body["reasoning"] = new { effort };
        if (request.WebSearch)
        {
            body["tools"] = new[] { new { type = searchType } };
            body["tool_choice"] = "auto";
            if (Info.Id == "openai") body["include"] = new[] { "web_search_call.action.sources" };
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(baseUrl, fallback, "/responses")) { Content = Json(body) };
        AddBearer(message, credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        var answer = new StringBuilder(); var searching = false;
        var seen = new HashSet<QWebSource>();
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(data); var root = document.RootElement;
            var type = ProviderJson.String(root, "type");
            if (type is "error" or "response.failed" or "response.incomplete")
            {
                var error = ProviderJson.ErrorMessage(root);
                if (root.TryGetProperty("response", out var nested))
                {
                    error ??= ProviderJson.ErrorMessage(nested);
                    if (nested.TryGetProperty("incomplete_details", out var details)) error ??= "Incomplete response: " + ProviderJson.String(details, "reason");
                }
                yield return new QStreamEvent.Failed(Sanitize(error ?? $"{Info.DisplayName} could not finish the response.")); yield break;
            }
            var itemType = root.TryGetProperty("item", out var item) ? ProviderJson.String(item, "type") : "";
            if (request.WebSearch && (type.Contains("web_search_call") || itemType is "web_search_call" or "deepseek_web_search_call" ||
                itemType == "custom_tool_call" && ProviderJson.String(item, "name").Contains("search", StringComparison.OrdinalIgnoreCase)))
            {
                var active = !type.EndsWith("completed") && !type.EndsWith(".done");
                searching |= active; yield return new QStreamEvent.Search(active);
            }
            if (type is "response.output_text.delta" or "response.refusal.delta")
            {
                var delta = ProviderJson.String(root, "delta");
                if (delta.Length > 0) { answer.Append(delta); yield return new QStreamEvent.Text(delta); }
            }
            if (request.WebSearch)
                foreach (var source in QWebSearch.ReadSources(root, answer.ToString()))
                    if (seen.Add(source)) yield return new QStreamEvent.Source(source);
        }
        if (searching) yield return new QStreamEvent.Search(false);
        if (request.WebSearch)
            foreach (var source in QWebSearch.MarkdownSources(answer.ToString())) if (seen.Add(source)) yield return new QStreamEvent.Source(source);
        if (answer.Length == 0) { yield return new QStreamEvent.Failed($"{Info.DisplayName} completed without returning text."); yield break; }
        yield return new QStreamEvent.Completed();
    }
}
