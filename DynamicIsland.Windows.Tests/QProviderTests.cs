using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class QProviderTests
{
    private static readonly QScreenContext Screen = new("Terminal", "WindowsTerminal", 1200, 800,
        "visible text", [1, 2, 3], DateTimeOffset.UtcNow);

    [Fact]
    public async Task OpenAiUsesResponsesApiAndSendsSelectedModelEffortHistoryAndImage()
    {
        var handler = new RecordingHandler(_ => Sse("""
            {"type":"response.output_text.delta","delta":"answer"}
            """));
        var provider = new OpenAiQProvider(new HttpClient(handler));

        var result = await CollectAsync(provider, Request("gpt-5.6-luna", "high", true), "openai-key");

        Assert.Equal("answer", result.Text);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/responses", sent.Uri);
        Assert.Equal("Bearer openai-key", sent.Authorization);
        using var json = JsonDocument.Parse(sent.Body!);
        var root = json.RootElement;
        Assert.Equal("gpt-5.6-luna", root.GetProperty("model").GetString());
        Assert.Equal(8192, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("assistant", root.GetProperty("input")[0].GetProperty("role").GetString());
        Assert.Equal("input_image", root.GetProperty("input")[1].GetProperty("content")[1].GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task AnthropicUsesMessagesHeadersOutputConfigAndReportsStreamErrors()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1
            ? Sse("""{"type":"content_block_delta","delta":{"type":"text_delta","text":"Claude"}}""")
            : Sse("""{"type":"error","error":{"type":"overloaded_error","message":"Try again later"}}"""));
        var provider = new AnthropicQProvider(new HttpClient(handler));

        var ok = await CollectAsync(provider, Request("claude-sonnet-5", "xhigh", true), "anthropic-key");
        var failed = await CollectAsync(provider, Request("claude-sonnet-5", "low"), "anthropic-key");

        Assert.Equal("Claude", ok.Text);
        Assert.Contains("Try again later", failed.Failure);
        var sent = handler.Requests[0];
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.Uri);
        Assert.Equal("anthropic-key", sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal("xhigh", json.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("image", json.RootElement.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.False(json.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task GeminiUsesApiKeyHeaderCamelCaseSchemaAndSelectedThinkingLevel()
    {
        var handler = new RecordingHandler(_ => Sse("""
            {"candidates":[{"content":{"parts":[{"thought":true,"text":"hidden"},{"text":"Gemini"}]},"finishReason":"STOP"}]}
            """));
        var provider = new GeminiQProvider(new HttpClient(handler));

        var result = await CollectAsync(provider, Request("gemini-3.7-flash", "low", true), "gemini-key");

        Assert.Equal("Gemini", result.Text);
        var sent = Assert.Single(handler.Requests);
        Assert.Contains("/v1beta/models/gemini-3.7-flash:streamGenerateContent?alt=sse", sent.Uri);
        Assert.DoesNotContain("gemini-key", sent.Uri);
        Assert.Equal("gemini-key", sent.Headers["x-goog-api-key"]);
        using var json = JsonDocument.Parse(sent.Body!);
        var root = json.RootElement;
        Assert.Equal("low", root.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.Equal("model", root.GetProperty("contents")[0].GetProperty("role").GetString());
        Assert.Equal("image/png", root.GetProperty("contents")[1].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.False(root.GetProperty("generationConfig").TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task GroqUsesCurrentCompletionBudgetAndSelectedReasoningEffort()
    {
        var handler = new RecordingHandler(_ => ChatSse("Groq"));
        var provider = new GroqQProvider(new HttpClient(handler));

        var result = await CollectAsync(provider, Request("qwen/qwen3.8-27b", "medium", true), "groq-key");

        Assert.Equal("Groq", result.Text);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", sent.Uri);
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal(8192, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("medium", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(json.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task XaiUsesGrokModelSelectedEffortAndChatImageShape()
    {
        var handler = new RecordingHandler(_ => ChatSse("Grok"));
        var provider = new XaiQProvider(new HttpClient(handler));

        var result = await CollectAsync(provider, Request("grok-4.6", "xhigh", true), "xai-key");

        Assert.Equal("Grok", result.Text);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.x.ai/v1/chat/completions", sent.Uri);
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal("xhigh", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("image_url", json.RootElement.GetProperty("messages")[2].GetProperty("content")[1].GetProperty("type").GetString());
        Assert.False(json.RootElement.TryGetProperty("store", out _));
    }

    [Fact]
    public async Task DeepSeekUsesThinkingContractAndOnlySendsImagesToVisionModels()
    {
        var handler = new RecordingHandler(_ => ChatSse("DeepSeek"));
        var provider = new DeepSeekQProvider(new HttpClient(handler));

        await CollectAsync(provider, Request("deepseek-v4-flash", "high", true), "deepseek-key");
        await CollectAsync(provider, Request("deepseek-v4-flash-vision-exp", "none", true), "deepseek-key");

        using var textJson = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("enabled", textJson.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("high", textJson.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal(JsonValueKind.String, textJson.RootElement.GetProperty("messages")[2].GetProperty("content").ValueKind);
        using var visionJson = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("disabled", visionJson.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("image_url", visionJson.RootElement.GetProperty("messages")[2].GetProperty("content")[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenRouterUsesDiscoveredReasoningMetadataAndPreservesExplicitEffort()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json("""{"data":[{"id":"vendor/model","name":"Model","architecture":{"input_modalities":["text","image"]},"reasoning":{"mandatory":true,"default_enabled":true,"default_effort":"high","supported_efforts":["low","high"]}}]}""")
            : ChatSse("OpenRouter"));
        var provider = new OpenRouterQProvider(new HttpClient(handler));

        var result = await CollectAsync(provider, Request("vendor/model", "low"), "router-key");

        Assert.Equal("OpenRouter", result.Text);
        Assert.Equal(2, handler.Requests.Count);
        using var json = JsonDocument.Parse(handler.Requests[1].Body!);
        var reasoning = json.RootElement.GetProperty("reasoning");
        Assert.Equal("low", reasoning.GetProperty("effort").GetString());
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
        Assert.Equal(8192, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task OllamaUsesNativeApiNdjsonImagesAndCustomBaseUrl()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json("""{"models":[{"name":"qwen3:latest"}]}""")
            : Ndjson("""{"message":{"role":"assistant","content":"Local"},"done":false}""",
                """{"message":{"role":"assistant","content":""},"done":true}"""));
        var provider = new OllamaQProvider(new HttpClient(handler));

        var models = await provider.GetModelsAsync(null, CancellationToken.None, "http://127.0.0.1:11434/v1");
        var result = await CollectAsync(provider, Request("qwen3:latest", "medium", true), null,
            "http://127.0.0.1:11434/v1");

        Assert.Equal("qwen3:latest", Assert.Single(models).Id);
        Assert.Equal("Local", result.Text);
        Assert.Equal("http://127.0.0.1:11434/api/tags", handler.Requests[0].Uri);
        Assert.Equal("http://127.0.0.1:11434/api/chat", handler.Requests[1].Uri);
        using var json = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("medium", json.RootElement.GetProperty("think").GetString());
        Assert.Equal(8192, json.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal("AQID", json.RootElement.GetProperty("messages")[2].GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task HttpErrorsExposeSafeProviderDetailsRequestIdAndStatus()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"type":"rate_limit_error","message":"Slow down"}}""", Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("request-id", "req_123");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var provider = new AnthropicQProvider(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<ProviderApiException>(async () =>
            await provider.GetModelsAsync("secret", CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal("rate_limit_error", error.Code);
        Assert.Equal("req_123", error.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public void ProviderDefaultsAndEffortChoicesMatchCurrentContracts()
    {
        Assert.Equal("gpt-6-luna", new OpenAiQProvider().Info.DefaultModel);
        Assert.Equal("claude-sonnet-5-5", new AnthropicQProvider().Info.DefaultModel);
        Assert.Equal("gemini-3.7-flash", new GeminiQProvider().Info.DefaultModel);
        Assert.Equal("qwen/qwen3.8-27b", new GroqQProvider().Info.DefaultModel);
        Assert.Equal("grok-4.6", new XaiQProvider().Info.DefaultModel);
        Assert.Equal("deepseek-v4-flash", new DeepSeekQProvider().Info.DefaultModel);
        Assert.Contains("xhigh", QProviderPolicy.EffortOptions("xai", "grok-4.6"));
        Assert.DoesNotContain("minimal", QProviderPolicy.EffortOptions("openai", "gpt-5.6-luna"));
        Assert.DoesNotContain("minimal", QProviderPolicy.EffortOptions("gemini", "gemini-3.7-flash"));
        Assert.DoesNotContain("none", QProviderPolicy.EffortOptions("groq", "qwen/qwen3.8-27b"));
    }

    [Theory]
    [InlineData("gpt-6-luna", "none", "none")]
    [InlineData("gpt-6-luna", "minimal", "auto")]
    [InlineData("gpt-6.1-sol", "none", "auto")]
    [InlineData("gpt-6.1-sol", "xhigh", "xhigh")]
    [InlineData("gpt-6-astra", "max", "max")]
    public void NewOpenAiModelsNormalizeUnsupportedEfforts(string model, string requested, string expected)
    {
        Assert.Equal(expected, QProviderPolicy.NormalizeEffort("openai", model, requested));
    }

    [Theory]
    [InlineData("claude-sonnet-5-5", "xhigh", "xhigh")]
    [InlineData("claude-opus-5-5", "max", "max")]
    [InlineData("claude-haiku-4-5", "high", "auto")]
    [InlineData("claude-sonnet-4-6", "xhigh", "auto")]
    public void ClaudeEffortChoicesRespectModelSupport(string model, string requested, string expected)
    {
        Assert.Equal(expected, QProviderPolicy.NormalizeEffort("anthropic", model, requested));
    }

    [Fact]
    public void CurrentSuggestionsIncludeNewModelsAndPreserveManualSelection()
    {
        var openAi = QProviderPolicy.ModelSuggestions("openai", "custom-model-id");
        Assert.Contains("custom-model-id", openAi);
        Assert.Contains("gpt-6-luna", openAi);
        Assert.Contains("gpt-6.1-sol", openAi);
        Assert.Contains("gpt-6-astra", openAi);
        Assert.Contains("claude-sonnet-5-5", QProviderPolicy.ModelSuggestions("anthropic"));
        Assert.Contains("claude-opus-5-5", QProviderPolicy.ModelSuggestions("anthropic"));
    }

    private static QRequest Request(string model, string effort, bool image = false) =>
        new(QMode.Ask, "answer this", image ? Screen : null, [new QMessage("assistant", "earlier answer")],
            model, image, 8192, null, effort);

    private static async Task<(string Text, string? Failure)> CollectAsync(IQProvider provider, QRequest request,
        string? credential, string? baseUrl = null)
    {
        var text = new StringBuilder();
        string? failure = null;
        await foreach (var item in provider.StreamAsync(request, credential, baseUrl, CancellationToken.None))
        {
            if (item is QStreamEvent.Text chunk) text.Append(chunk.Value);
            if (item is QStreamEvent.Failed failed) failure = failed.Message;
        }
        return (text.ToString(), failure);
    }

    private static HttpResponseMessage Sse(params string[] events) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join("\n\n", events.Select(value => $"data: {value}")) + "\n\ndata: [DONE]\n\n",
            Encoding.UTF8, "text/event-stream")
    };

    private static HttpResponseMessage ChatSse(string text) =>
        Sse($$"""{"choices":[{"delta":{"content":"{{text}}"},"finish_reason":"stop"}]}""");

    private static HttpResponseMessage Ndjson(params string[] records) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join('\n', records) + "\n", Encoding.UTF8, "application/x-ndjson")
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed record SentRequest(string Method, string Uri, string? Body, string? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.ToString(), body,
                request.Headers.Authorization?.ToString(), request.Headers.ToDictionary(pair => pair.Key,
                    pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase)));
            return respond(request);
        }
    }
}
