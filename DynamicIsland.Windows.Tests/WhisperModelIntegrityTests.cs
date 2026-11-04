using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WhisperModelIntegrityTests
{
    [Fact]
    public void DigestMustMatchAndUnknownModelsAreRejected()
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("abc"));
        Assert.True(WhisperModelIntegrity.Matches(digest, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        Assert.False(WhisperModelIntegrity.Matches(digest, WhisperModelIntegrity.ExpectedSha256("ggml-base.en.bin")));
        Assert.Null(WhisperModelIntegrity.ExpectedSha256("other.bin"));
        Assert.False(WhisperModelIntegrity.Matches(digest, null));
    }
}
