using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexTurnResourcesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sutra-turn-resources-" + Guid.NewGuid().ToString("N"));
    public CodexTurnResourcesTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedImagePreparationDeletesPartialFileAndReleasesThread(bool cancelled)
    {
        var released = 0;
        string? imagePath = null;
        async Task Prepare()
        {
            await using var resources = new CodexTurnResources(() => { released++; return Task.CompletedTask; }, _directory);
            imagePath = resources.CreateImagePath();
            await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
            if (cancelled) throw new OperationCanceledException();
            throw new IOException("Fixture failed during image preparation");
        }
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(Prepare);
        else await Assert.ThrowsAsync<IOException>(Prepare);
        Assert.False(File.Exists(imagePath));
        Assert.Equal(1, released);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task TextOnlyTurnReleasesThreadWithoutCreatingFiles()
    {
        var released = 0;
        var resources = new CodexTurnResources(() => { released++; return Task.CompletedTask; }, _directory);
        await resources.DisposeAsync(); await resources.DisposeAsync();
        Assert.Equal(1, released);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task ImagePathIsStableAndOwnedUntilTurnDisposal()
    {
        var released = 0;
        var resources = new CodexTurnResources(() => { released++; return Task.CompletedTask; }, _directory);
        var path = resources.CreateImagePath();
        Assert.Equal(path, resources.CreateImagePath());
        await File.WriteAllBytesAsync(path, [9, 8, 7]);
        Assert.True(File.Exists(path));
        await resources.DisposeAsync();
        Assert.False(File.Exists(path));
        Assert.Equal(1, released);
        Assert.Throws<ObjectDisposedException>(() => resources.CreateImagePath());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
