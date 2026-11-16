using System.Text.Json;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;

if (!OperatingSystem.IsWindows()) return;
var reportPath = Path.GetFullPath(args[0]);
var log = new LoggingService();
var secrets = new DpapiSecretStore(log);
var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland.Windows", "settings.json");
using var settings = File.Exists(settingsPath) ? JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)) : JsonDocument.Parse("{}");
var providers = new IQProvider[] { new OpenAiQProvider(), new AnthropicQProvider(), new GeminiQProvider(), new GroqQProvider(), new XaiQProvider(), new DeepSeekQProvider(), new OpenRouterQProvider(), new OllamaQProvider() };
var reports = new List<object>();
foreach (var provider in providers)
{
    var key = secrets.Get(provider.Info.Id);
    if (string.IsNullOrWhiteSpace(key)) { reports.Add(new { provider = provider.Info.Id, result = "No saved key; protocol fixtures only" }); continue; }
    var model = provider.Info.Id switch { "groq" => "openai/gpt-oss-120b", "deepseek" => "deepseek-flash", _ => provider.Info.DefaultModel };
    if (provider.Info.Id == "openai" && settings.RootElement.TryGetProperty("QSelectedProvider", out var selected) && selected.GetString() == "openai" && settings.RootElement.TryGetProperty("QSelectedModel", out var selectedModel) && QWebSearch.Supports("openai", selectedModel.GetString())) model = selectedModel.GetString()!;
    Console.WriteLine($"Checking {provider.Info.Id} ({model})");
    var sources = new HashSet<string>(); var searched = false; var completed = false; var text = new System.Text.StringBuilder(); string? error = null;
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var request = new QRequest(QMode.Ask, $"Perform a web search for the official {provider.Info.DisplayName} API documentation. Reply with one short sentence and cite the official documentation URL.", null, [], model, false, 8192, WebSearch: true);
        await foreach (var item in provider.StreamAsync(request, key, null, timeout.Token))
            switch (item)
            {
                case QStreamEvent.Search { Confirmed: true }: searched = true; break;
                case QStreamEvent.Source source: sources.Add(source.Value.Url); break;
                case QStreamEvent.Text chunk: text.Append(chunk.Value); break;
                case QStreamEvent.Completed: completed = true; break;
                case QStreamEvent.Failed: error = "Provider stream reported an error"; break;
            }
    }
    catch (ProviderApiException ex) { error = $"HTTP {(int?)ex.StatusCode}"; }
    catch (OperationCanceledException) { error = "Timed out"; }
    catch (Exception ex) { error = ex.GetType().Name; }
    reports.Add(new { provider = provider.Info.Id, model, completed, reportedSearch = searched, sources = sources.Count, answerCharacters = text.Length, error });
    Console.WriteLine($"{provider.Info.Id}: completed={completed}, search={searched}, sources={sources.Count}, error={error ?? "none"}");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
}
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
