using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.Services.Q;

public sealed class CodexQProvider(CodexAppServerClient client) : IQProvider
{
    public QProviderInfo Info { get; } = new(
        "codex", "Codex · Subscription",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "gpt-6.1-sol", null);

    public async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken, string? baseUrl = null)
    {
        var models = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.Select(model => new QModelInfo(
            model.Id, model.DisplayName,
            QProviderCapabilities.Text | QProviderCapabilities.Streaming | (model.SupportsImages ? QProviderCapabilities.Images : QProviderCapabilities.None),
            model.IsDefault)).ToArray();
    }

    public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = BuildPrompt(request);
        if (request.WebSearch) text += "\nWhen current information is needed, use web search and cite sources as readable Markdown links with actual source URLs. Do not emit opaque citation reference IDs.";
        var answer = new System.Text.StringBuilder();
        yield return new QStreamEvent.Started();
        var sawText = false;
        await foreach (var delta in client.StreamSearchTurnAsync(request.Model, request.ReasoningEffort, text,
            request.IncludeImage ? request.ScreenContext?.PngBytes : null, request.WebSearch, cancellationToken).ConfigureAwait(false))
        {
            if (delta is QStreamEvent.Text chunk) { sawText = true; answer.Append(chunk.Value); }
            yield return delta;
        }
        if (request.WebSearch) foreach (var source in QWebSearch.MarkdownSources(answer.ToString())) yield return new QStreamEvent.Source(source);
        if (sawText) yield return new QStreamEvent.Completed();
        else yield return new QStreamEvent.Failed("Codex returned no answer.");
    }

    private static string BuildPrompt(QRequest request)
    {
        var parts = new List<string> { QPromptComposer.SystemPrompt(request) };
        foreach (var message in request.History)
            parts.Add($"{message.Role}: {message.Content}");
        parts.Add(QPromptComposer.ContextText(request.ScreenContext));
        parts.Add($"User request:\n{request.Prompt}");
        if (!string.IsNullOrWhiteSpace(request.CustomSystemPrompt)) parts.Add($"Additional instructions:\n{request.CustomSystemPrompt}");
        return string.Join("\n\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
