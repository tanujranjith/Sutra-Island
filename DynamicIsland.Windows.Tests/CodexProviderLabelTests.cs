using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexProviderLabelTests
{
    [Theory]
    [InlineData("chatgpt", "Codex · Subscription")]
    [InlineData("CHATGPT", "Codex · Subscription")]
    [InlineData("apiKey", "Codex · API key")]
    [InlineData("APIKEY", "Codex · API key")]
    [InlineData("unknown", "Codex · Account")]
    public void LabelReflectsAccountAuthentication(string mode, string expected)
    {
        Assert.Equal(expected, CodexProviderLabel.For(new CodexAccount(null, null, mode)));
    }

    [Fact]
    public void MissingAccountPromptsSignIn()
    {
        Assert.Equal("Codex · Sign in", CodexProviderLabel.For(null));
    }
}
