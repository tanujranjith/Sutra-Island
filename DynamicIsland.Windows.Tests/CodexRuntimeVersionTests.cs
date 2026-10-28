using DynamicIsland.Windows.Services.Q;
using Xunit;
namespace DynamicIsland.Windows.Tests;
public sealed class CodexRuntimeVersionTests
{
    [Theory]
    [InlineData("codex-cli 0.162.0-alpha.17.2", "0.162.0-alpha.17.2", true)]
    [InlineData("codex-cli 0.162.0", "0.162.0", false)]
    [InlineData("0.162.0-beta.1+windows.123", "0.162.0-beta.1+windows.123", true)]
    [InlineData("v0.162.0", "0.162.0", false)]
    [InlineData("warning: optional config unavailable\r\ncodex-cli 0.162.0-alpha.17.2\r\n", "0.162.0-alpha.17.2", true)]
    public void RuntimeOutputPreservesSemanticVersion(string output,string expected,bool prerelease)
    {
        Assert.True(CodexRuntimeVersion.TryParseOutput(output,out var version));
        Assert.Equal(expected,version.Value); Assert.Equal(prerelease,version.IsPrerelease);
    }
    [Theory]
    [InlineData("0.151.0",true)]
    [InlineData("0.162.0-alpha.17.2",true)]
    [InlineData("0.151.0-alpha.1",false)]
    [InlineData("0.150.99",false)]
    [InlineData("0.144.6",false)]
    public void PrereleaseSupportRetainsMinimumVersionPolicy(string text,bool supported)
    { Assert.True(CodexRuntimeVersion.TryParse(text,out var version)); Assert.Equal(supported,version.IsAtLeast(CodexRuntimeResolver.MinimumSupportedVersion)); }
    [Theory]
    [InlineData("")]
    [InlineData("Codex failed with error 0.162.0")]
    [InlineData("codex-cli unknown")]
    [InlineData("codex-cli 0.162.0-!")]
    [InlineData("codex-cli 0.162.0-alpha.")]
    [InlineData("codex-cli 0.162.0\ncodex-cli 0.163.0")]
    public void InvalidOrAmbiguousOutputIsRejected(string text)=>Assert.False(CodexRuntimeVersion.TryParseOutput(text,out _));
}
