using DynamicIsland.Windows.Services.Flow;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class FlowPipelineTests
{
    private static readonly FlowPipeline.Timing Fast = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5));

    private static FlowRecording Speech(double seconds = 1.0)
    {
        var samples = new float[(int)(FlowRecording.SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(0.2 * Math.Sin(i * 0.05));
        return new FlowRecording(samples, 0.2f);
    }

    private static FlowRecording Hiss(double seconds = 1.0)
    {
        var samples = new float[(int)(FlowRecording.SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++) samples[i] = i % 2 == 0 ? 0.016f : -0.016f;
        return new FlowRecording(samples, 0.016f);
    }

    private sealed class FakeAudio : IFlowAudio
    {
        public int Starts, Stops, Open;
        public FlowRecording Next = Speech();
        public TaskCompletionSource? HoldStop;
        public float Level => 0.5f;
        public void Start() { Starts++; Open++; }
        public async Task<FlowRecording> StopAsync()
        {
            if (HoldStop is not null) await HoldStop.Task;
            Stops++; Open--;
            return Next;
        }
    }

    private sealed class FakePaster : IFlowPaster
    {
        public nint Window = 42;
        public int Pins;
        public FlowPasteResult Outcome = FlowPasteOutcome.Pasted;
        public List<(string Text, nint Target)> Pastes = [];
        public FlowTarget? CaptureTarget() => new() { AtPress = new FlowFocus(Window, 0, 0, 0, 0) };
        public void PinTarget(FlowTarget target) => Pins++;
        public Task<FlowPasteResult> PasteAsync(string text, FlowTarget? target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Pastes.Add((text, target?.AtPress.Window ?? 0));
            return Task.FromResult(Outcome);
        }
    }

    private sealed class Rig
    {
        public readonly FakeAudio Audio = new();
        public readonly FakePaster Paster = new();
        public readonly List<FlowStatus> Statuses = [];
        public bool Enabled = true;
        public int Words, Cleans;
        public List<string> Held = [];
        public Func<FlowRecording, CancellationToken, Task<string>> Transcribe = (_, _) => Task.FromResult("hello world");
        public readonly FlowPipeline Pipeline;

        public Rig(FlowPipeline.Timing? timing = null)
        {
            Pipeline = new FlowPipeline(Audio, Paster, () => Enabled,
                (r, t) => Transcribe(r, t),
                (raw, _) => { Cleans++; return Task.FromResult(raw); },
                s => { lock (Statuses) Statuses.Add(s); },
                w => Words += w,
                t => Held.Add(t),
                (_, ex) => throw new Xunit.Sdk.XunitException(ex.ToString()),
                timing ?? Fast);
        }

        public FlowState Last { get { lock (Statuses) return Statuses[^1].State; } }
        public string LastDetail { get { lock (Statuses) return Statuses.Last(s => s.State is FlowState.Error or FlowState.Pasted).Detail; } }
    }

    [Fact]
    public async Task PastesIntoTheWindowFromKeyDown()
    {
        var rig = new Rig();
        rig.Pipeline.Press();
        rig.Paster.Window = 99; // focus moving later must not change the target
        await rig.Pipeline.Release();
        Assert.Equal([("hello world", (nint)42)], rig.Paster.Pastes);
        Assert.Equal(1, rig.Paster.Pins); // the focused field is recorded again at key-up
        Assert.Empty(rig.Held);
        Assert.Equal(2, rig.Words);
        Assert.Contains(rig.Statuses, s => s.State == FlowState.Pasted);
        Assert.False(rig.Pipeline.IsBusy);
    }

    [Fact]
    public async Task RecordingLimitStopsMicrophoneAndDoesNotPaste()
    {
        var timing = Fast with { MaxRecording = TimeSpan.FromMilliseconds(30) };
        var rig = new Rig(timing);
        rig.Pipeline.Press();
        for (var i = 0; i < 100; i++)
        {
            bool reported;
            lock (rig.Statuses) reported = rig.Statuses.Any(s => s.State == FlowState.Error);
            if (reported) break;
            await Task.Delay(10);
        }
        Assert.Equal(1, rig.Audio.Stops);
        Assert.Equal(0, rig.Audio.Open);
        Assert.Empty(rig.Paster.Pastes);
        Assert.Empty(rig.Held);
        Assert.False(rig.Pipeline.IsBusy);
        Assert.Contains(rig.Statuses, s => s.State == FlowState.Error && s.Detail.Contains("three-minute limit"));
        await rig.Pipeline.Release();
        Assert.Equal(1, rig.Audio.Stops);
    }

    [Fact]
    public async Task PressDuringProcessingIsIgnored()
    {
        var rig = new Rig();
        var transcribing = new TaskCompletionSource<string>();
        rig.Transcribe = (_, _) => transcribing.Task;
        rig.Pipeline.Press();
        var first = rig.Pipeline.Release();
        Assert.True(rig.Pipeline.IsBusy);

        rig.Pipeline.Press(); // rapid second press while the first is still transcribing
        await rig.Pipeline.Release();
        Assert.Equal(1, rig.Audio.Starts);

        transcribing.SetResult("first take");
        await first;
        Assert.Equal(0, rig.Audio.Open);
        Assert.Single(rig.Paster.Pastes);

        rig.Transcribe = (_, _) => Task.FromResult("second take");
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        Assert.Equal(2, rig.Audio.Starts);
        Assert.Equal("second take", rig.Paster.Pastes[^1].Text);
    }

    [Fact]
    public async Task PressWhileReleaseIsStillStoppingIsIgnored()
    {
        var rig = new Rig();
        rig.Audio.HoldStop = new TaskCompletionSource();
        rig.Pipeline.Press();
        var release = rig.Pipeline.Release();
        rig.Pipeline.Press();
        rig.Audio.HoldStop.SetResult();
        await release;
        Assert.Equal(1, rig.Audio.Starts);
        Assert.Equal(0, rig.Audio.Open);
    }

    [Fact]
    public async Task TurningFlowOffAbandonsPendingDictation()
    {
        var rig = new Rig();
        var transcribing = new TaskCompletionSource<string>();
        rig.Transcribe = (_, _) => transcribing.Task;
        rig.Pipeline.Press();
        var run = rig.Pipeline.Release();

        rig.Enabled = false;
        rig.Pipeline.Disable();
        transcribing.SetResult("should never paste");
        await run;

        Assert.Empty(rig.Paster.Pastes);
        Assert.Equal(0, rig.Cleans);
        Assert.Equal(0, rig.Words);
        Assert.Equal(FlowState.Idle, rig.Last);
    }

    [Fact]
    public async Task TurningFlowOffWhileRecordingClosesTheMicrophone()
    {
        var rig = new Rig();
        rig.Pipeline.Press();
        rig.Enabled = false;
        rig.Pipeline.Disable();
        for (var i = 0; i < 100 && rig.Pipeline.IsBusy; i++) await Task.Delay(10);
        Assert.Equal(0, rig.Audio.Open);
        Assert.False(rig.Pipeline.IsBusy);
        rig.Pipeline.Press();
        Assert.Equal(1, rig.Audio.Starts);
    }

    [Theory]
    [InlineData(FlowPasteOutcome.TargetChanged, "Focus changed")]
    [InlineData(FlowPasteOutcome.Elevated, "admin window")]
    [InlineData(FlowPasteOutcome.Blocked, "blocked")]
    [InlineData(FlowPasteOutcome.ClipboardBusy, "clipboard is busy")]
    [InlineData(FlowPasteOutcome.MultilineNeedsClipboard, "multi-line")]
    public async Task FailedPasteNeverShowsSuccess(FlowPasteOutcome outcome, string reason)
    {
        var rig = new Rig();
        rig.Paster.Outcome = outcome;
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        Assert.DoesNotContain(rig.Statuses, s => s.State == FlowState.Pasted);
        Assert.Contains(reason, rig.LastDetail);
        Assert.Equal(0, rig.Words);
        Assert.Equal(["hello world"], rig.Held); // handed back for the user to copy, not put on the clipboard
    }

    [Fact]
    public async Task PartialTypingHoldsOnlyTheRest()
    {
        var rig = new Rig();
        rig.Paster.Outcome = new FlowPasteResult(FlowPasteOutcome.PartlyTyped, " world");
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        Assert.DoesNotContain(rig.Statuses, s => s.State == FlowState.Pasted);
        Assert.Contains("partway", rig.LastDetail);
        Assert.Equal([" world"], rig.Held); // copying it can't duplicate "hello", already in the field
    }

    [Theory]
    [InlineData(0, "hello")]
    [InlineData(1, "ello")]   // key-down of 'h' accepted: 'h' was inserted
    [InlineData(2, "ello")]
    [InlineData(9, "")]
    [InlineData(10, "")]
    public void RemainingAfterPartialTyping(int accepted, string expected) =>
        Assert.Equal(expected, FlowTyping.Remaining("hello", accepted));

    [Fact]
    public void TypableDropsEnterAndTab() =>
        Assert.Equal("ab c", FlowTyping.Typable("a\tb\r\n c"));

    [Fact]
    public async Task ShortcutCancelDiscardsTheRecording()
    {
        var rig = new Rig();
        rig.Pipeline.Press();
        await rig.Pipeline.Cancel();
        Assert.Equal(0, rig.Audio.Open);
        Assert.Empty(rig.Paster.Pastes);
        Assert.Equal(FlowState.Idle, rig.Last);
    }

    [Fact]
    public async Task SpokenThankYouIsKept()
    {
        var rig = new Rig { Transcribe = (_, _) => Task.FromResult("Thank you.") };
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        Assert.Equal("Thank you.", rig.Paster.Pastes.Single().Text);
    }

    [Fact]
    public async Task ThankYouOnSilenceIsDropped()
    {
        var rig = new Rig { Transcribe = (_, _) => Task.FromResult("Thank you.") };
        rig.Audio.Next = Hiss();
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        Assert.Empty(rig.Paster.Pastes);
        Assert.Equal("Didn't catch that", rig.LastDetail);
    }

    [Fact]
    public async Task SettlesBackToIdle()
    {
        var rig = new Rig();
        rig.Pipeline.Press();
        await rig.Pipeline.Release();
        for (var i = 0; i < 100 && rig.Last != FlowState.Idle; i++) await Task.Delay(10);
        Assert.Equal(FlowState.Idle, rig.Last);
    }

    private static readonly FlowFocus Field = new(10, 11, 11, 40, 8);

    [Fact]
    public void FocusUnchangedWhenEverythingMatches() =>
        Assert.True(FlowTarget.Unchanged(Field, Field, Field, "a", "a", "a"));

    [Fact]
    public void FocusMovedBetweenPressAndRelease() =>
        Assert.False(FlowTarget.Unchanged(Field, Field, Field, "field-1", "field-2", "field-2"));

    [Fact]
    public void FocusMovedToAnotherTabInTheSameWindow() =>
        Assert.False(FlowTarget.Unchanged(Field, Field, Field, "tab-1", "tab-1", "tab-2"));

    [Fact]
    public void CaretMovedToAnotherFieldWithoutAccessibility() =>
        Assert.False(FlowTarget.Unchanged(Field, Field, Field with { CaretTop = 120 }, null, null, null));

    [Fact]
    public void FocusedChildWindowChanged() =>
        Assert.False(FlowTarget.Unchanged(Field, Field with { Focus = 12 }, Field with { Focus = 12 }, null, null, null));

    [Theory]
    [InlineData(null, "a", "a")]
    [InlineData("a", null, "a")]
    [InlineData("a", "a", null)]
    [InlineData("", "", "")]
    public void MissingAccessibilityBlocksUnverifiedPaste(string? press, string? release, string? now) =>
        Assert.False(FlowTarget.Unchanged(Field, Field, Field, press, release, now));

    [Fact]
    public void MissingReleaseFocusBlocksUnverifiedPaste() =>
        Assert.False(FlowTarget.Unchanged(Field, null, Field, "a", "a", "a"));
    [Fact]
    public void CorrectionReading_RequiresOriginalWindowAndFieldAndRejectsPasswords()
    {
        Assert.True(FlowTarget.CanInspectCorrection((nint)1, (nint)1, "field", "field", false));
        Assert.False(FlowTarget.CanInspectCorrection((nint)1, (nint)2, "field", "field", false));
        Assert.False(FlowTarget.CanInspectCorrection((nint)1, (nint)1, "field", "other", false));
        Assert.False(FlowTarget.CanInspectCorrection((nint)1, (nint)1, "field", "field", true));
        Assert.False(FlowTarget.CanInspectCorrection((nint)1, (nint)1, null, null, false));
    }
}
