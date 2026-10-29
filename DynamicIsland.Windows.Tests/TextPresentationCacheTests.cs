using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class TextPresentationCacheTests
{
    [Fact]
    public void RepeatedBindingsReuseTheSameFormattedReplyUntilItsTextChanges()
    {
        var calls = 0;
        var cache = new TextPresentationCache(text => { calls++; return text.ToUpperInvariant(); });
        var first = cache.Format("reply");
        Assert.Same(first, cache.Format(new string("reply".ToCharArray())));
        Assert.Equal("REPLY!", cache.Format("reply!"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ThreeResponseLanesAreReusedAndStreamingHistoryIsBounded()
    {
        var calls = 0;
        var cache = new TextPresentationCache(text => { calls++; return "formatted " + text; });
        foreach (var text in new[] { "single", "left", "right" }) cache.Format(text);
        foreach (var text in new[] { "single", "left", "right" }) cache.Format(text);
        Assert.Equal(3, calls);
        cache.Format("next chunk");
        cache.Format("single");
        Assert.Equal(5, calls);
    }

    [Fact]
    public void ClearingAConversationInvalidatesSavedPresentation()
    {
        var calls = 0;
        var cache = new TextPresentationCache(text => { calls++; return text; });
        cache.Format("private response");
        cache.Clear();
        cache.Format("private response");
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("long input", "ok")]
    [InlineData("ok", "long output")]
    public void LargeInputOrOutputIsNotRetained(string input, string output)
    {
        var calls = 0;
        var cache = new TextPresentationCache(_ => { calls++; return output; }, maximumLength: 4);
        Assert.Equal(output, cache.Format(input));
        Assert.Equal(output, cache.Format(input));
        Assert.Equal(2, calls);
    }
}
