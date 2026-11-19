using System.Net;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Services;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData("drop tray", PaletteCommand.DropTray)]
    [InlineData("downloads", PaletteCommand.Transfers)]
    [InlineData("copy files", PaletteCommand.Transfers)]
    [InlineData("pin widget", PaletteCommand.PinWidget)]
    [InlineData("quick capture", PaletteCommand.QuickCapture)]
    public void PaletteFindsWorkspaceFeatures(string text, PaletteCommand command) =>
        Assert.Equal(command, CommandCatalog.Match(text)?.Command.Id);

    [Fact]
    public async Task CopyKeepsOriginalAndDoesNotOverwriteAnExistingFile()
    {
        using var directory = new ScratchDirectory();
        var source = Path.Combine(directory.Root, "source.txt");
        var target = Path.Combine(directory.Root, "copy.txt");
        await File.WriteAllTextAsync(source, "original");
        await WorkspaceTransferService.CopyAsync(source, target, new TransferProgress(), default);
        await File.WriteAllTextAsync(target, "keep this");
        await Assert.ThrowsAsync<IOException>(() =>
            WorkspaceTransferService.CopyAsync(source, target, new TransferProgress(), default));
        Assert.Equal("original", await File.ReadAllTextAsync(source));
        Assert.Equal("keep this", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Fact]
    public async Task CanceledCopyAndShortDownloadDoNotLeavePartialFiles()
    {
        using var directory = new ScratchDirectory();
        using var canceled = new CancellationTokenSource();
        var source = Path.Combine(directory.Root, "source.txt");
        var target = Path.Combine(directory.Root, "target.txt");
        await File.WriteAllTextAsync(source, "keep");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkspaceTransferService.CopyAsync(source, target, new TransferProgress(_ => canceled.Cancel()), canceled.Token));
        using var content = new StringContent("short");
        content.Headers.ContentLength = 99;
        using var client = new HttpClient(new ResponseHandler(content));
        await Assert.ThrowsAsync<IOException>(() =>
            WorkspaceTransferService.DownloadAsync(client, new Uri("https://example.com/file"), target, new TransferProgress(), default));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Fact]
    public async Task DownloadStreamsToFinalFile()
    {
        using var directory = new ScratchDirectory();
        var target = Path.Combine(directory.Root, "download.txt");
        using var client = new HttpClient(new ResponseHandler(new StringContent("payload")));
        var updates = new List<int?>();
        Assert.True(WorkspaceTransferService.TryDownloadUri("https://example.com/file.txt", out var url));
        Assert.False(WorkspaceTransferService.TryDownloadUri("file:///C:/secret.txt", out _));
        Assert.False(WorkspaceTransferService.TryDownloadUri("https://user:password@example.com", out _));
        await WorkspaceTransferService.DownloadAsync(client, url, target, new TransferProgress(updates.Add), default);
        Assert.Equal("payload", await File.ReadAllTextAsync(target));
        Assert.Equal(0, updates[0]);
    }

    private sealed class TransferProgress(Action<int?>? update = null) : IProgress<int?>
    {
        public void Report(int? value) => update?.Invoke(value);
    }

    private sealed class ResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("sutra-workspace-test-").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
