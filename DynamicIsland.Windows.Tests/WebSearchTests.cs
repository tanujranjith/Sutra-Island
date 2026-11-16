using System.Net;
using System.Text;
using System.Text.Json;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WebSearchTests
{
    private static QRequest Request(string model, bool enabled = true) => new(QMode.Ask, "Find the official API docs", null, [], model, false, WebSearch: enabled);
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Auth)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
            Calls.Add((request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString()));
            return reply(request, body);
        }
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Sse(params object[] events) => new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\n\n", events.Select(e => "data: " + JsonSerializer.Serialize(e))) + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
    private static async Task<List<QStreamEvent>> Collect(IQProvider provider, QRequest request, string? key = "test-key")
    { var result = new List<QStreamEvent>(); await foreach (var item in provider.StreamAsync(request, key, null, CancellationToken.None)) result.Add(item); return result; }

    [Theory]
    [InlineData("openai", "gpt-6.1-terra", true)] [InlineData("openai", "gpt-4.1-nano", false)]
    [InlineData("anthropic", "claude-sonnet-5-5", true)] [InlineData("gemini", "gemini-3.7-flash", true)]
    [InlineData("groq", "qwen/qwen3.8-27b", false)] [InlineData("groq", "openai/gpt-oss-120b", true)]
    [InlineData("xai", "grok-4.6", true)] [InlineData("deepseek", "deepseek-v4-flash", false)]
    [InlineData("deepseek", "deepseek-flash", true)] [InlineData("deepseek", "deepseek-v4-pro", true)]
    [InlineData("openrouter", "any/model", true)] [InlineData("ollama", "qwen3", true)] [InlineData("codex", "gpt-6.1-sol", true)]
    public void SupportIsModelAware(string provider, string model, bool supported) => Assert.Equal(supported, QWebSearch.Supports(provider, model));

    [Theory] [InlineData("openai")] [InlineData("xai")] [InlineData("deepseek")]
    public async Task ResponsesSearchUsesHostedToolAndRetainsCitationOffsets(string id)
    {
        var handler = new Handler((_, _) => Sse(
            new { type = "response.output_item.added", item = new { type = "web_search_call" } },
            new { type = "response.output_text.delta", delta = "😀 Official docs" },
            new { type = "response.output_text.annotation.added", annotation = new { type = "url_citation", url = "https://example.com/docs", title = "Docs", start_index = 2, end_index = 15 } }));
        var http = new HttpClient(handler);
        IQProvider provider = id switch { "xai" => new XaiQProvider(http), "deepseek" => new DeepSeekQProvider(http), _ => new OpenAiQProvider(http) };
        var events = await Collect(provider, Request(id switch { "xai" => "grok-4.6", "deepseek" => "deepseek-v4-pro", _ => "gpt-6.1-terra" }));
        using var body = JsonDocument.Parse(handler.Calls.Single().Body);
        Assert.EndsWith("/responses", handler.Calls.Single().Url);
        Assert.Equal("web_search", body.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Contains(events, e => e is QStreamEvent.Search { Active: true });
        Assert.Contains(events, e => e is QStreamEvent.Source { Value.CitedText: "Official docs" });
    }

    [Fact]
    public async Task AnthropicSearchIncludesServerToolAndStreamsCitations()
    {
        var handler = new Handler((_, _) => Sse(
            new { type = "content_block_start", content_block = new { type = "server_tool_use", name = "web_search" } },
            new { type = "content_block_delta", delta = new { type = "text_delta", text = "Official docs." } },
            new { type = "content_block_delta", delta = new { type = "citations_delta", citation = new { type = "web_search_result_location", url = "https://example.com", title = "Official", cited_text = "Official docs." } } }));
        var events = await Collect(new AnthropicQProvider(new HttpClient(handler)), Request("claude-sonnet-5-5"));
        using var body = JsonDocument.Parse(handler.Calls.Single().Body);
        Assert.Equal("web_search_20250305", body.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Contains(events, e => e is QStreamEvent.Search { Active: true });
        Assert.Contains(events, e => e is QStreamEvent.Source { Value.Url: "https://example.com" });
    }

    [Fact]
    public async Task GeminiMetadataWithoutTextStillProducesCitationsAndSuggestions()
    {
        var handler = new Handler((_, _) => Sse(
            new { candidates = new[] { new { content = new { parts = new[] { new { text = "Official docs." } } } } } },
            new { candidates = new[] { new { groundingMetadata = new { groundingChunks = new[] { new { web = new { uri = "https://example.com", title = "Docs" } } }, groundingSupports = new[] { new { segment = new { text = "Official docs." }, groundingChunkIndices = new[] { 0 } } }, searchEntryPoint = new { renderedContent = "<div>Google Search suggestions</div>" } }, finishReason = "STOP" } } }));
        var events = await Collect(new GeminiQProvider(new HttpClient(handler)), Request("gemini-3.7-flash"));
        using var body = JsonDocument.Parse(handler.Calls.Single().Body);
        Assert.True(body.RootElement.GetProperty("tools")[0].TryGetProperty("googleSearch", out _));
        Assert.Contains(events, e => e is QStreamEvent.Source { Value.CitedText: "Official docs." });
        Assert.Contains(events, e => e is QStreamEvent.SearchSuggestions);
    }

    [Theory] [InlineData("groq")] [InlineData("deepseek")] [InlineData("openrouter")]
    public async Task ChatProvidersUseTheirOwnSearchConfiguration(string id)
    {
        var handler = new Handler((r, _) => r.Method == HttpMethod.Get ? Json("{\"data\":[]}") : Sse(new { choices = new[] { new { delta = new { content = "Docs", annotations = new[] { new { type = "url_citation", url_citation = new { url = "https://example.com", title = "Docs", start_index = 0, end_index = 4 } } } } } } }));
        var http = new HttpClient(handler);
        IQProvider p = id switch { "groq" => new GroqQProvider(http), "deepseek" => new DeepSeekQProvider(http), _ => new OpenRouterQProvider(http) };
        var events = await Collect(p, Request(id switch { "groq" => "openai/gpt-oss-120b", "deepseek" => "deepseek-flash", _ => "any/model" }));
        using var body = JsonDocument.Parse(handler.Calls.Last().Body);
        if (id == "openrouter") Assert.Equal("web", body.RootElement.GetProperty("plugins")[0].GetProperty("id").GetString());
        else Assert.Equal(id == "groq" ? "browser_search" : "deepseek_web_search", body.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Contains(events, e => e is QStreamEvent.Source);
    }

    [Fact]
    public async Task GroqCompoundBuffersExecutedToolsForSourceMetadata()
    {
        var handler = new Handler((_, _) => Json("""{"choices":[{"message":{"content":"See [Docs](https://example.com)","executed_tools":[{"type":"search","search_results":{"results":[{"url":"https://example.com","title":"Docs"}]}}]},"finish_reason":"stop"}]}"""));
        var events = await Collect(new GroqQProvider(new HttpClient(handler)), Request("groq/compound"));
        using var body = JsonDocument.Parse(handler.Calls.Single().Body);
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Contains("web_search", body.RootElement.GetProperty("compound_custom").GetRawText());
        Assert.Contains(events, e => e is QStreamEvent.Source { Value.Url: "https://example.com" });
        Assert.Contains(events, e => e is QStreamEvent.Completed);
    }

    [Fact]
    public async Task DeepSeekSearchEventsArriveBesideDelta()
    {
        var handler = new Handler((_, _) => Sse(
            new { choices = new[] { new { server_tool_event = new { type = "call_started", call = new { type = "deepseek_web_search" } }, delta = new { content = "" } } } },
            new { choices = new[] { new { delta = new { content = "Answer" } } } }));
        var events = await Collect(new DeepSeekQProvider(new HttpClient(handler)), Request("deepseek-flash"));
        Assert.Contains(events, e => e is QStreamEvent.Search { Active: true });
    }

    [Fact]
    public async Task OllamaUsesCloudKeyOnlyForSearchAndKeepsInferenceLocal()
    {
        var handler = new Handler((r, _) => r.RequestUri!.Host == "ollama.com" ? Json("{\"results\":[{\"url\":\"https://example.com\",\"title\":\"Docs\",\"content\":\"Reference material\"}]}") : new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"See [Docs](https://example.com)\"}}\n{\"done\":true}\n") });
        var events = await Collect(new OllamaQProvider(new HttpClient(handler)), Request("qwen3"));
        Assert.Equal("https://ollama.com/api/web_search", handler.Calls[0].Url);
        Assert.Equal("Bearer test-key", handler.Calls[0].Auth);
        Assert.StartsWith("http://localhost:11434/api/chat", handler.Calls[1].Url);
        Assert.Null(handler.Calls[1].Auth);
        Assert.Contains("untrusted reference material", handler.Calls[1].Body);
        Assert.Contains(events, e => e is QStreamEvent.Source);
    }

    [Fact]
    public async Task DisabledSearchDoesNotSendOpenAiTool()
    {
        var handler = new Handler((_, _) => Sse(new { type = "response.output_text.delta", delta = "Hello" }));
        var events = await Collect(new OpenAiQProvider(new HttpClient(handler)), Request("gpt-6.1-terra", false));
        Assert.DoesNotContain("web_search", handler.Calls.Single().Body);
        Assert.DoesNotContain(events, e => e is QStreamEvent.Search or QStreamEvent.Source);
    }

    [Fact]
    public async Task SessionClearsSearchOnCancellationAndOnNextTurn()
    {
        var provider = new SearchFixture(); using var session = new QSessionController(new QProviderRegistry([provider]));
        var searching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += s => { if (s.IsSearching) searching.TrySetResult(); };
        var turn = session.SubmitAsync("Search", QMode.Ask, "openai", "gpt-6.1-terra", null, null, false, webSearch: true);
        await searching.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.Cancel(); await turn;
        Assert.False(session.Snapshot.IsSearching); Assert.Equal(QRunState.Cancelled, session.Snapshot.State);
        provider.Wait = false;
        await session.SubmitAsync("Hello", QMode.Ask, "openai", "gpt-6.1-terra", null, null, false);
        Assert.False(session.Snapshot.SearchUsed); Assert.Null(session.Snapshot.Sources);
    }
    private sealed class SearchFixture : IQProvider
    {
        public bool Wait = true;
        public QProviderInfo Info => new("openai", "Fixture", QProviderCapabilities.Text, "gpt-6.1-terra");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? c, CancellationToken t, string? b = null) => Task.FromResult<IReadOnlyList<QModelInfo>>([]);
        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest r, string? c, string? b, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken t)
        {
            if (r.WebSearch) { yield return new QStreamEvent.Search(true); if (Wait) await Task.Delay(Timeout.Infinite, t); }
            yield return new QStreamEvent.Text("Answer"); yield return new QStreamEvent.Completed();
        }
    }

    [Fact]
    public void CodexSearchIsScopedToThreadAndCanBeDisabled()
    {
        using var on = JsonDocument.Parse(JsonSerializer.Serialize(CodexTurnStartRequest.CreateThread("model", @"C:\workspace", true)));
        using var off = JsonDocument.Parse(JsonSerializer.Serialize(CodexTurnStartRequest.CreateThread("model", @"C:\workspace", false)));
        Assert.Equal("live", on.RootElement.GetProperty("config").GetProperty("web_search").GetString());
        Assert.Equal("disabled", off.RootElement.GetProperty("config").GetProperty("web_search").GetString());
        var accumulator = new CodexTurnNotificationAccumulator("one");
        using var other = JsonDocument.Parse("{\"method\":\"item/started\",\"params\":{\"threadId\":\"two\",\"item\":{\"type\":\"webSearch\"}}}");
        Assert.Null(accumulator.Process(other.RootElement));
        using var own = JsonDocument.Parse("{\"method\":\"item/started\",\"params\":{\"threadId\":\"one\",\"item\":{\"type\":\"webSearch\"}}}");
        Assert.Equal(CodexTurnUpdateKind.Searching, accumulator.Process(own.RootElement)!.Kind);
    }

    [Theory] [InlineData("javascript:alert(1)")] [InlineData("file:///C:/secret.txt")] [InlineData("https://example.com", true)]
    public void SourcesOnlyOpenWebUrls(string url, bool expected = false) => Assert.Equal(expected, QWebSearch.IsWebUrl(url));
}
