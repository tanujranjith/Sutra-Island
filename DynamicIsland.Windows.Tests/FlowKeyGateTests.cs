using DynamicIsland.Windows.Services.Flow;
using Xunit;
using static DynamicIsland.Windows.Services.Flow.FlowKeyGate;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowKeyGateTests
{
    private const int RCtrl = 0xA3, C = 0x43, Left = 0x25, Shift = 0xA0;

    private static FlowKeyGate Gate(bool dictating = true) => new(() => dictating);

    [Fact]
    public void QuickSecondKeyIsAShortcut()
    {
        var gate = Gate();
        Assert.Equal(Signal.Pressed, gate.OnKey(RCtrl, true, 1000).Signal);
        var c = gate.OnKey(C, true, 1100);
        Assert.Equal(new Decision(Signal.Cancelled, false), c); // Right Ctrl+C reaches the app
        Assert.False(gate.OnKey(C, false, 1150).Swallow);
        Assert.Equal(Signal.None, gate.OnKey(RCtrl, false, 1200).Signal); // no paste after a shortcut
    }

    [Fact]
    public void StrayKeyMidDictationIsSwallowedAndDictationContinues()
    {
        var gate = Gate();
        gate.OnKey(RCtrl, true, 1000);
        Assert.Equal(new Decision(Signal.None, true), gate.OnKey(Left, true, 5000));
        Assert.Equal(new Decision(Signal.None, true), gate.OnKey(Left, true, 5030)); // auto-repeat
        Assert.Equal(new Decision(Signal.None, true), gate.OnKey(Left, false, 5100));
        Assert.Equal(Signal.Released, gate.OnKey(RCtrl, false, 9000).Signal);
    }

    [Fact]
    public void EscCancelsADictation()
    {
        var gate = Gate();
        gate.OnKey(RCtrl, true, 1000);
        Assert.Equal(new Decision(Signal.Cancelled, true), gate.OnKey(VkEscape, true, 3000));
        Assert.True(gate.OnKey(VkEscape, false, 3050).Swallow);
        Assert.Equal(Signal.None, gate.OnKey(RCtrl, false, 3100).Signal);
    }

    [Fact]
    public void NoLockWhenNothingIsRecording()
    {
        var gate = Gate(dictating: false); // e.g. the previous dictation was still processing
        gate.OnKey(RCtrl, true, 1000);
        Assert.Equal(new Decision(Signal.Cancelled, false), gate.OnKey(Left, true, 4000));
    }

    [Fact]
    public void KeysHeldBeforeThePressStillComeUp()
    {
        var gate = Gate();
        Assert.False(gate.OnKey(Shift, true, 900).Swallow);
        gate.OnKey(RCtrl, true, 1000);
        Assert.False(gate.OnKey(Shift, false, 2000).Swallow); // its key-down went through, so its key-up must too
    }

    [Fact]
    public void TickWraparoundStillLocks()
    {
        var gate = Gate();
        gate.OnKey(RCtrl, true, uint.MaxValue - 100);
        Assert.True(gate.OnKey(Left, true, 500).Swallow);
    }

    [Fact]
    public void ResetCancelsAHold()
    {
        var gate = Gate();
        gate.OnKey(RCtrl, true, 1000);
        Assert.True(gate.Reset());
        Assert.False(gate.Reset());
    }

    [Fact]
    public void PushKeyIsConfigurable()
    {
        var gate = Gate();
        gate.PttKey = FlowKeyGate.KeyFor("RightShift");
        Assert.Equal(Signal.None, gate.OnKey(RCtrl, true, 1000).Signal); // Right Ctrl is just a key now
        Assert.Equal(new Decision(Signal.Pressed, false), gate.OnKey(0xA1, true, 1000));
        Assert.Equal(new Decision(Signal.Released, false), gate.OnKey(0xA1, false, 2000));
    }

    [Fact]
    public void RightAltIsSwallowedSoMenusDontOpen()
    {
        var gate = Gate();
        gate.PttKey = FlowKeyGate.KeyFor("RightAlt");
        Assert.Equal(new Decision(Signal.Pressed, true), gate.OnKey(0xA5, true, 1000));
        Assert.Equal(new Decision(Signal.Released, true), gate.OnKey(0xA5, false, 2000));
    }
}
