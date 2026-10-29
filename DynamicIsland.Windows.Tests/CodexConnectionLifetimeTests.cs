using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexConnectionLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredReaderFailureCannotFailReplacementRequests(bool pipeFailure)
    {
        await using var fixture = new ClientFixture();
        var retired = fixture.Connect();
        var replacement = fixture.Connect();
        var request = fixture.Request();
        var id = fixture.LastRequestId;

        await fixture.ReadAsync(retired, string.Empty, pipeFailure);

        Assert.False(request.IsCompleted);
        Assert.True(fixture.Initialized);
        await fixture.ReadAsync(replacement, $"{{\"id\":{id},\"result\":{{\"source\":\"replacement\"}}}}\n");
        Assert.Equal("replacement", (await request.WaitAsync(TimeSpan.FromSeconds(2))).GetProperty("source").GetString());
    }

    [Fact]
    public async Task DelayedRetiredExitDoesNotResetReplacementInitialization()
    {
        await using var fixture = new ClientFixture();
        var retired = fixture.Connect();
        var replacement = fixture.Connect();
        var request = fixture.Request();
        var id = fixture.LastRequestId;

        fixture.Exit(retired);

        Assert.True(fixture.Initialized);
        Assert.False(request.IsCompleted);
        await fixture.ReadAsync(replacement, $"{{\"id\":{id},\"result\":{{\"ok\":true}}}}\n");
        Assert.True((await request.WaitAsync(TimeSpan.FromSeconds(2))).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task RetiredMessagesCannotResolveRequestsOrWriteToReplacement()
    {
        await using var fixture = new ClientFixture();
        var retired = fixture.Connect();
        var replacement = fixture.Connect();
        var request = fixture.Request();
        var id = fixture.LastRequestId;
        var bytesBefore = fixture.WrittenBytes;

        await fixture.ReadAsync(retired, $"{{\"id\":{id},\"result\":{{\"source\":\"retired\"}}}}\n");
        await fixture.ReadAsync(retired, "{\"id\":900,\"method\":\"item/commandExecution/requestApproval\"}\n");

        Assert.False(request.IsCompleted);
        Assert.Equal(bytesBefore, fixture.WrittenBytes);
        await fixture.ReadAsync(replacement, $"{{\"id\":{id},\"result\":{{\"source\":\"replacement\"}}}}\n");
        Assert.Equal("replacement", (await request.WaitAsync(TimeSpan.FromSeconds(2))).GetProperty("source").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveConnectionFailureFailsItsPendingRequests(bool exitEvent)
    {
        await using var fixture = new ClientFixture();
        var owner = fixture.Connect();
        var request = fixture.Request();
        if (exitEvent) fixture.Exit(owner);
        else await fixture.ReadAsync(owner, string.Empty);

        var error = await Assert.ThrowsAsync<CodexAppServerException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(CodexFailureKind.ServerExited, error.Kind);
        Assert.False(fixture.Initialized);
    }

    [Fact]
    public async Task QueuedWriteCannotCrossIntoAReplacementConnection()
    {
        await using var fixture = new ClientFixture();
        fixture.Connect();
        await fixture.WriteGate.WaitAsync();
        var request = fixture.Request();
        fixture.Connect();
        fixture.WriteGate.Release();

        var error = await Assert.ThrowsAsync<CodexAppServerException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(CodexFailureKind.ServerExited, error.Kind);
        Assert.Equal(0, fixture.WrittenBytes);
    }

    [Fact]
    public async Task ActiveServerApprovalIsStillDeclinedOnItsOwnConnection()
    {
        await using var fixture = new ClientFixture();
        var owner = fixture.Connect();
        await fixture.ReadAsync(owner, "{\"id\":77,\"method\":\"item/commandExecution/requestApproval\"}\n");
        using var response = JsonDocument.Parse(fixture.WrittenText.Trim());
        Assert.Equal(77, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("decline", response.RootElement.GetProperty("result").GetProperty("decision").GetString());
    }

    [Fact]
    public async Task QueuedWriteCanUnwindAfterDisposalWithoutReleasingADisposedGate()
    {
        await using var fixture = new ClientFixture();
        fixture.Connect();
        await fixture.WriteGate.WaitAsync();
        var request = fixture.Request();
        await fixture.CloseClientAsync();
        fixture.WriteGate.Release();
        var error = await Assert.ThrowsAsync<CodexAppServerException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(CodexFailureKind.ServerExited, error.Kind);
    }

    // Controlled in-memory pipes exercise real request/reader code without starting
    // Codex, changing preview isolation, reading credentials, or contacting a provider.
    private sealed class ClientFixture : IAsyncDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly CodexAppServerClient _client = new();
        private readonly List<Process> _owners = [];
        private readonly List<StreamWriter> _writers = [];
        private readonly List<MemoryStream> _streams = [];
        private MemoryStream _output = null!;

        public bool Initialized => (bool)typeof(CodexAppServerClient).GetField("_initialized", Flags)!.GetValue(_client)!;
        public SemaphoreSlim WriteGate => (SemaphoreSlim)typeof(CodexAppServerClient).GetField("_writeGate", Flags)!.GetValue(_client)!;
        public long WrittenBytes => _output.Length;
        public string WrittenText => Encoding.UTF8.GetString(_output.ToArray());
        public int LastRequestId
        {
            get { using var message = JsonDocument.Parse(WrittenText.Trim().Split('\n')[^1]); return message.RootElement.GetProperty("id").GetInt32(); }
        }

        public Process Connect()
        {
            var owner = new Process(); _owners.Add(owner);
            _output = new MemoryStream(); _streams.Add(_output);
            var writer = new StreamWriter(_output, new UTF8Encoding(false), leaveOpen: true); _writers.Add(writer);
            Set("_process", owner); Set("_writer", writer); Set("_initialized", true);
            return owner;
        }
        public Task<JsonElement> Request() => (Task<JsonElement>)Invoke("SendRequestCoreAsync", "model/list", new { }, CancellationToken.None)!;
        public void Exit(Process owner) => Invoke("OnProcessExited", owner);
        public ValueTask CloseClientAsync() => _client.DisposeAsync();
        public async Task ReadAsync(Process owner, string messages, bool fail = false)
        {
            using StreamReader output = fail ? new FailingReader() : new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(messages)));
            using var error = new StreamReader(new MemoryStream());
            await ((Task)Invoke("ReadLoopAsync", owner, output, error)!).WaitAsync(TimeSpan.FromSeconds(2));
        }
        private object? Invoke(string method, params object[] arguments) => typeof(CodexAppServerClient).GetMethod(method, Flags)!.Invoke(_client, arguments);
        private void Set(string field, object value) => typeof(CodexAppServerClient).GetField(field, Flags)!.SetValue(_client, value);
        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            foreach (var writer in _writers) writer.Dispose();
            foreach (var stream in _streams) stream.Dispose();
            foreach (var owner in _owners) owner.Dispose();
        }
    }

    private sealed class FailingReader() : StreamReader(new MemoryStream())
    {
        public override Task<string?> ReadLineAsync() => Task.FromException<string?>(new IOException("Fixture pipe failure"));
    }
}
