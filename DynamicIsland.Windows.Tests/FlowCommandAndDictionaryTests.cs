using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowCommandAndDictionaryTests
{
    private const int Ptt = FlowKeyGate.VkRightCtrl;

    [Fact]
    public void DoubleTapAndHoldIsCommandMode()
    {
        var gate = new FlowKeyGate(() => true);
        gate.OnKey(Ptt, true, 1000);
        gate.OnKey(Ptt, false, 1100);           // 100 ms tap
        gate.OnKey(Ptt, true, 1300);            // pressed again 200 ms later
        Assert.True(gate.CommandPress);
    }

    [Fact]
    public void NormalHoldIsNotCommandMode()
    {
        var gate = new FlowKeyGate(() => true);
        gate.OnKey(Ptt, true, 1000);
        Assert.False(gate.CommandPress);
        gate.OnKey(Ptt, false, 3000);           // a real dictation
        gate.OnKey(Ptt, true, 3200);            // the next one starts soon after
        Assert.False(gate.CommandPress);
    }

    [Fact]
    public void SlowSecondPressIsNotCommandMode()
    {
        var gate = new FlowKeyGate(() => true);
        gate.OnKey(Ptt, true, 1000);
        gate.OnKey(Ptt, false, 1100);
        gate.OnKey(Ptt, true, 1100 + FlowKeyGate.DoubleTapMs + 50);
        Assert.False(gate.CommandPress);
    }

    [Fact]
    public void OtherKeyBetweenTapsCancelsDoubleTap()
    {
        var gate = new FlowKeyGate(() => true);
        gate.OnKey(Ptt, true, 1000);
        gate.OnKey(Ptt, false, 1100);
        gate.OnKey(0x41, true, 1150);           // typing "a"
        gate.OnKey(0x41, false, 1170);
        gate.OnKey(Ptt, true, 1250);
        Assert.False(gate.CommandPress);
    }

    [Fact]
    public void TripleTapDoesNotChainCommands()
    {
        var gate = new FlowKeyGate(() => true);
        gate.OnKey(Ptt, true, 1000); gate.OnKey(Ptt, false, 1100);
        gate.OnKey(Ptt, true, 1200); Assert.True(gate.CommandPress);
        gate.OnKey(Ptt, false, 1300);
        gate.OnKey(Ptt, true, 1400);
        Assert.False(gate.CommandPress);
    }

    [Fact]
    public void LearnsARetypedName()
    {
        var pasted = "Ask Cirk to review the PR.";
        var before = "Hi team. Ask Cirk to review the PR.";
        var after = "Hi team. Ask Circ to review the PR.";
        Assert.Equal(["Circ"], FlowCorrections.Find(pasted, before, after, []));
    }

    [Fact]
    public void LearnsACapitalizedName()
    {
        Assert.Equal(["Sutra"], FlowCorrections.Find("open sutra island", "open sutra island", "open Sutra island", []));
    }

    [Fact]
    public void IgnoresRewritesAndUnrelatedEdits()
    {
        // A different word, not a spelling fix.
        Assert.Empty(FlowCorrections.Find("meet on Monday", "meet on Monday", "meet on Friday", []));
        // Text added elsewhere, nothing pasted was changed.
        Assert.Empty(FlowCorrections.Find("Thanks Dana", "Thanks Dana", "Thanks Dana, see you tomorrow", []));
        // A full rewrite.
        Assert.Empty(FlowCorrections.Find("one two three four five six", "one two three four five six", "alpha beta gamma delta epsilon zeta", []));
        // Unchanged.
        Assert.Empty(FlowCorrections.Find("hello", "hello", "hello", []));
    }

    [Fact]
    public void SkipsWordsAlreadyKnownOrCommon()
    {
        Assert.Empty(FlowCorrections.Find("Ask Cirk now", "Ask Cirk now", "Ask Circ now", ["circ"]));
        Assert.Empty(FlowCorrections.Find("go to teh store", "go to teh store", "go to the store", []));
    }

    [Theory]
    [InlineData("Cirk", "Circ", true)]
    [InlineData("tanooj", "Tanuj", true)]
    [InlineData("Monday", "Friday", false)]
    [InlineData("cat", "dog", false)]
    public void SimilarMeansASpellingFix(string old, string fix, bool similar) => Assert.Equal(similar, FlowCorrections.Similar(old, fix));
}
