namespace DynamicIsland.Q.Core;

public sealed class OpenAiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("openai", "OpenAI · API key",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "gpt-6-luna", "https://api.openai.com/v1");

    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        CancellationToken cancellationToken) => StreamResponsesAsync(request with { WebSearch = request.WebSearch && QWebSearch.Supports(Info.Id, request.Model) }, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken: cancellationToken);
}
