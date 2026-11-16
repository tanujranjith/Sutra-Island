using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class GroqQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("groq", "Groq",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "qwen/qwen3.8-27b", "https://api.groq.com/openai/v1");

    protected override QProviderCapabilities CapabilitiesForModel(string id)
    {
        var capabilities = QProviderCapabilities.Text | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery;
        if (id.Contains("qwen3.6", StringComparison.OrdinalIgnoreCase) || id.Contains("qwen3.8", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("llama-4", StringComparison.OrdinalIgnoreCase)) capabilities |= QProviderCapabilities.Images;
        return capabilities;
    }

    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        CancellationToken cancellationToken)
    {
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (request.Model.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase) && effort is "none") effort = "low";
        var extra = new Dictionary<string, object?>();
        if (effort != "auto" && !request.Model.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase)) extra["reasoning_effort"] = effort;
        if (request.Model.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase))
            extra["compound_custom"] = new { tools = new { enabled_tools = request.WebSearch ? new[] { "web_search", "visit_website" } : Array.Empty<string>() } };
        else if (request.Model.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase))
        {
            extra["tools"] = request.WebSearch ? new[] { new { type = "browser_search" } } : Array.Empty<object>();
            if (!request.WebSearch) extra["tool_choice"] = "none";
        }
        return StreamChatCompletionsAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, "max_completion_tokens", extra, cancellationToken);
    }
}

public sealed class XaiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("xai", "xAI / Grok",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "grok-4.6", "https://api.x.ai/v1");

    protected override bool IsUsableModel(string id) => id.StartsWith("grok-", StringComparison.OrdinalIgnoreCase) &&
        !id.Contains("imagine", StringComparison.OrdinalIgnoreCase);

    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        CancellationToken cancellationToken)
    {
        if (request.WebSearch && QWebSearch.Supports(Info.Id, request.Model))
            return StreamResponsesAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken: cancellationToken);
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        var extra = new Dictionary<string, object?>();
        if (effort != "auto") extra["reasoning_effort"] = effort;
        return StreamChatCompletionsAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, "max_tokens", extra, cancellationToken);
    }
}

public sealed class DeepSeekQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("deepseek", "DeepSeek",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "deepseek-v4-flash", "https://api.deepseek.com");

    protected override bool IsUsableModel(string id) => id.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase);
    protected override QProviderCapabilities CapabilitiesForModel(string id) =>
        QProviderCapabilities.Text | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery |
        (id.Contains("vision", StringComparison.OrdinalIgnoreCase) ? QProviderCapabilities.Images : QProviderCapabilities.None);

    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        CancellationToken cancellationToken)
    {
        if (request.WebSearch && request.Model == "deepseek-v4-pro")
            return StreamResponsesAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken: cancellationToken);
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        var extra = new Dictionary<string, object?>();
        if (request.WebSearch && request.Model == "deepseek-flash") extra["tools"] = new[] { new { type = "deepseek_web_search" }, new { type = "deepseek_read_url" } };
        if (effort == "none") extra["thinking"] = new { type = "disabled" };
        else if (effort != "auto")
        {
            extra["thinking"] = new { type = "enabled" };
            extra["reasoning_effort"] = effort;
        }
        var supportsImage = request.Model.Contains("vision", StringComparison.OrdinalIgnoreCase);
        return StreamChatCompletionsAsync(request with { IncludeImage = request.IncludeImage && supportsImage }, credential,
            baseUrl, Info.DefaultBaseUrl!, "max_tokens", extra, cancellationToken);
    }
}

public sealed class OpenRouterQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    private sealed record ReasoningProfile(bool Mandatory, bool DefaultEnabled, string? DefaultEffort,
        string[] SupportedEfforts, bool SupportsMaxTokens);

    private readonly ConcurrentDictionary<string, ReasoningProfile> _reasoningProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _modelLoadGate = new(1, 1);
    private bool _profilesLoaded;

    public override QProviderInfo Info { get; } = new("openrouter", "OpenRouter",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "~openai/gpt-latest", "https://openrouter.ai/api/v1");

    public override async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,
        CancellationToken cancellationToken, string? baseUrl = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/models"));
        AddBearer(request, credential);
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
                var name = ProviderJson.String(item, "name");
                var capabilities = QProviderCapabilities.Text | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery;
                if (item.TryGetProperty("architecture", out var architecture) && architecture.TryGetProperty("input_modalities", out var modalities) &&
                    modalities.ValueKind == JsonValueKind.Array && modalities.EnumerateArray().Any(value => string.Equals(value.GetString(), "image", StringComparison.OrdinalIgnoreCase)))
                    capabilities |= QProviderCapabilities.Images;
                models.Add(new QModelInfo(id, name.Length == 0 ? id : name, capabilities,
                    string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
                if (TryReadReasoningProfile(item, out var profile)) _reasoningProfiles[id] = profile;
            }
        }
        _profilesLoaded = true;
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var profile = await GetReasoningProfileAsync(request.Model, credential, cancellationToken).ConfigureAwait(false);
        var extra = new Dictionary<string, object?>(BuildReasoningBody(profile, request.ReasoningEffort) ?? new Dictionary<string, object?>());
        if (request.WebSearch) extra["plugins"] = new[] { new { id = "web", max_results = 5 } };
        await foreach (var item in StreamChatCompletionsAsync(request, credential, baseUrl, Info.DefaultBaseUrl!,
            "max_completion_tokens", extra, cancellationToken).ConfigureAwait(false)) yield return item;
    }

    private async Task<ReasoningProfile?> GetReasoningProfileAsync(string model, string? credential, CancellationToken cancellationToken)
    {
        if (_reasoningProfiles.TryGetValue(model, out var profile)) return profile;
        if (!_profilesLoaded)
        {
            await _modelLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { if (!_profilesLoaded) await GetModelsAsync(credential, cancellationToken).ConfigureAwait(false); }
            finally { _modelLoadGate.Release(); }
        }
        return _reasoningProfiles.TryGetValue(model, out profile) ? profile : null;
    }

    private static bool TryReadReasoningProfile(JsonElement model, out ReasoningProfile profile)
    {
        profile = default!;
        if (!model.TryGetProperty("reasoning", out var reasoning) || reasoning.ValueKind != JsonValueKind.Object) return false;
        var mandatory = reasoning.TryGetProperty("mandatory", out var mandatoryValue) && mandatoryValue.ValueKind == JsonValueKind.True;
        var defaultEnabled = reasoning.TryGetProperty("default_enabled", out var enabledValue) && enabledValue.ValueKind == JsonValueKind.True;
        var defaultEffort = ProviderJson.String(reasoning, "default_effort");
        var supportsMaxTokens = reasoning.TryGetProperty("supports_max_tokens", out var maxValue) && maxValue.ValueKind == JsonValueKind.True;
        var efforts = reasoning.TryGetProperty("supported_efforts", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray()
            : [];
        profile = new ReasoningProfile(mandatory, defaultEnabled, defaultEffort.Length == 0 ? null : defaultEffort, efforts, supportsMaxTokens);
        return true;
    }

    private static IReadOnlyDictionary<string, object?>? BuildReasoningBody(ReasoningProfile? profile, string? requestedEffort)
    {
        if (profile is null) return null;
        var effort = requestedEffort?.Trim().ToLowerInvariant();
        if (effort is null or "" or "auto")
            return profile.Mandatory || profile.DefaultEnabled
                ? new Dictionary<string, object?> { ["reasoning"] = new { exclude = true } }
                : null;
        if (profile.Mandatory && effort == "none") effort = profile.DefaultEffort ?? "low";
        if (profile.SupportedEfforts.Length > 0 && !profile.SupportedEfforts.Contains(effort, StringComparer.OrdinalIgnoreCase))
            effort = profile.DefaultEffort ?? profile.SupportedEfforts.Last();
        return new Dictionary<string, object?> { ["reasoning"] = new { effort, exclude = true } };
    }
}
