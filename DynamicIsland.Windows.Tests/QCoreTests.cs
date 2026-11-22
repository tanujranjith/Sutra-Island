using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class QCoreTests
{
    [Fact]
    public void AskPromptUsesVisibleContext()
    {
        var context = new QScreenContext("Browser", "chrome", 100, 100, "Why do you want this job?", null, DateTimeOffset.Now);
        var request = new QRequest(QMode.Ask, "What should I answer?", context, [], "test", false);

        var messages = QPromptComposer.BuildMessages(request);

        Assert.Contains("active window", messages[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Why do you want this job?", messages[1].Content);
        Assert.Contains("What should I answer?", messages[^1].Content);
    }

    [Fact]
    public void SayPromptUsesFirstPersonGuidance()
    {
        Assert.Contains("first-person", QPromptComposer.SystemPrompt(QMode.Say), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomSystemPromptIsAppendedWithoutRemovingModeGuidance()
    {
        var request = new QRequest(QMode.Ask, "Summarize this", null, [], "test", false, 8192, "Use concise bullet points.");

        var systemPrompt = QPromptComposer.BuildMessages(request)[0].Content;

        Assert.Contains("Ask mode", systemPrompt);
        Assert.Contains("Additional instructions from the user", systemPrompt);
        Assert.Contains("Use concise bullet points.", systemPrompt);
    }

    [Fact]
    public void DictationCleanupDropsAssistantPersonaAndWrapsTranscript()
    {
        var request = new QRequest(QMode.Say, "write a function that parses csv", null, [], "test", false, 256, "You are a dictation cleanup engine.", DictationCleanup: true);

        var messages = QPromptComposer.BuildMessages(request);

        Assert.Equal("You are a dictation cleanup engine.", messages[0].Content);
        Assert.DoesNotContain("Sutra Assistant", messages[0].Content);
        Assert.Equal("<transcript>\nwrite a function that parses csv\n</transcript>", messages[^1].Content);
        Assert.DoesNotContain("User request", messages[^1].Content);
    }

    [Fact]
    public async Task SessionStreamsAndKeepsShortHistory()
    {
        var provider = new FakeProvider();
        using var session = new QSessionController(new QProviderRegistry([provider]));
        await session.BeginAsync(QMode.Ask, "fake", "demo", null);
        await session.SubmitAsync("hello", QMode.Ask, "fake", "demo", "key", null, false);
        await session.SubmitAsync("follow up", QMode.Ask, "fake", "demo", "key", null, false);

        Assert.Equal(QRunState.Complete, session.Snapshot.State);
        Assert.Equal("hello world", session.Snapshot.Response);
        Assert.Equal(2, provider.LastRequest!.History.Count);
    }

    [Fact]
    public async Task SessionReportsProviderErrorsWithoutThrowing()
    {
        var provider = new FakeProvider { ShouldFail = true };
        using var session = new QSessionController(new QProviderRegistry([provider]));
        await session.SubmitAsync("hello", QMode.Ask, "fake", "demo", "key", null, false);

        Assert.Equal(QRunState.Error, session.Snapshot.State);
        Assert.Contains("fake provider failure", session.Snapshot.Error);
    }

    [Fact]
    public async Task SessionTreatsAnEmptyProviderStreamAsAnError()
    {
        var provider = new FakeProvider { ReturnEmpty = true };
        using var session = new QSessionController(new QProviderRegistry([provider]));

        await session.SubmitAsync("hello", QMode.Ask, "fake", "demo", "key", null, false);

        Assert.Equal(QRunState.Error, session.Snapshot.State);
        Assert.Contains("empty response", session.Snapshot.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeProvider : IQProvider
    {
        public bool ShouldFail { get; set; }
        public bool ReturnEmpty { get; set; }
        public QRequest? LastRequest { get; private set; }
        public QProviderInfo Info { get; } = new("fake", "Fake", QProviderCapabilities.Text | QProviderCapabilities.Streaming, "demo");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken, string? baseUrl = null) =>
            Task.FromResult<IReadOnlyList<QModelInfo>>([new QModelInfo("demo", "Demo", Info.Capabilities, true)]);

        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ShouldFail) throw new InvalidOperationException("fake provider failure");
            yield return new QStreamEvent.Started();
            if (ReturnEmpty)
            {
                yield return new QStreamEvent.Completed();
                yield break;
            }
            yield return new QStreamEvent.Text("hello ");
            await Task.Yield();
            yield return new QStreamEvent.Text("world");
            yield return new QStreamEvent.Completed();
        }
    }
}
