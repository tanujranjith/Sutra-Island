using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class AssistantContextTests
{
    [Fact]
    public async Task OrdinaryChatNeitherCapturesNorReusesExistingScreenContext()
    {
        var provider = new RecordingProvider();
        using var session = new QSessionController(new QProviderRegistry([provider]));
        var context = new QScreenContext("private window", "app", 10, 10, "private OCR", [1], DateTimeOffset.Now);
        await session.BeginAsync(QMode.Ask, "fixture", "test", context);
        var captures = 0;
        await session.SubmitAsync("hello", QMode.Ask, "fixture", "test", null, null, true,
            _ => { captures++; return Task.FromResult<QScreenContext?>(context); }, contextPolicy: QContextPolicy.None);
        Assert.Equal(0, captures); Assert.Null(provider.Request!.ScreenContext); Assert.False(provider.Request.IncludeImage);
        Assert.Null(session.Snapshot.Context);
    }
    [Fact]
    public async Task ExplicitScreenRequestCapturesOnceAndNextChatClearsIt()
    {
        var provider = new RecordingProvider();
        using var session = new QSessionController(new QProviderRegistry([provider]));
        var captures = 0;
        var context = new QScreenContext("window", "app", 10, 10, "OCR", [1], DateTimeOffset.Now);
        await session.SubmitAsync("explain", QMode.Ask, "fixture", "test", null, null, true,
            _ => { captures++; return Task.FromResult<QScreenContext?>(context); }, contextPolicy: QContextPolicy.Capture);
        Assert.Equal(context, provider.Request!.ScreenContext);
        await session.SubmitAsync("another question", QMode.Ask, "fixture", "test", null, null, true, contextPolicy: QContextPolicy.None);
        Assert.Equal(1, captures); Assert.Null(provider.Request!.ScreenContext);
        Assert.Equal(2, provider.Request.History.Count);
    }
    private sealed class RecordingProvider : IQProvider
    {
        public QRequest? Request { get; private set; }
        public QProviderInfo Info => new("fixture", "Fixture", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "test");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken token, string? baseUrl = null) => Task.FromResult<IReadOnlyList<QModelInfo>>([]);
        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { Request = request; await Task.Yield(); yield return new QStreamEvent.Text("answer"); yield return new QStreamEvent.Completed(); }
    }
}
