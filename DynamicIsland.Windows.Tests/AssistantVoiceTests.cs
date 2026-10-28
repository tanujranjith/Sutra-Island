using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class AssistantVoiceTests
{
    [Fact]
    public async Task ClosingAnIdleChatDoesNotDisarmTheNextWake()
    {
        var platform = new FakeVoice();
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new AssistantVoiceController(platform, platform, platform,
            (_, _) => { submitted.TrySetResult(); return Task.FromResult("answer"); }, () => { });
        await controller.ConfigureAsync(true, true, "");

        controller.Stop(); // ClearQ uses Stop when closing the assistant surface.

        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
        Assert.True(platform.WakeActive);
        platform.TriggerWake();
        await submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ClosingACompletedVoiceChatKeepsWakeEnabledUntilExplicitlyDisabled()
    {
        var platform = new FakeVoice();
        using var controller = new AssistantVoiceController(platform, platform, platform,
            (_, _) => Task.FromResult("answer"), () => { });
        await controller.ConfigureAsync(true, true, "");
        await controller.ListenAsync();

        controller.Stop();

        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
        Assert.True(platform.WakeActive);
        await controller.ConfigureAsync(false, true, "");
        Assert.Equal(AssistantVoiceState.Off, controller.Snapshot.State);
        Assert.False(platform.WakeActive);
    }

    [Fact]
    public async Task DictationTimeoutRearmsWakeWithoutSubmittingAnEmptyQuestion()
    {
        var platform = new FakeVoice { CancelDictation = true };
        var submitted = false;
        using var controller = new AssistantVoiceController(platform, platform, platform,
            (_, _) => { submitted = true; return Task.FromResult("answer"); }, () => { });
        await controller.ConfigureAsync(true, true, "");

        await controller.ListenAsync();

        Assert.False(submitted);
        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
        Assert.True(platform.WakeActive);
    }

    [Fact]
    public async Task WakeCallbackStartsATurnOffTheRecognitionCallback()
    {
        var platform = new FakeVoice { HoldDictation = true };
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult("answer"), () => { });
        await controller.ConfigureAsync(true, true, "");
        platform.TriggerWake();
        await platform.DictationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        controller.Stop();
        for (var i = 0; i < 100 && controller.Snapshot.State != AssistantVoiceState.WakeListening; i++) await Task.Delay(10);
        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
    }
    [Fact]
    public async Task WakeOffAlsoCancelsAManualVoiceTurnWhenWakeWasAlreadyOff()
    {
        var platform = new FakeVoice { HoldDictation = true };
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult("answer"), () => { });
        var turn = controller.ListenAsync(); await platform.DictationStarted.Task;
        await controller.ConfigureAsync(false, true, ""); await turn;
        Assert.False(platform.WakeActive);
        Assert.Equal(AssistantVoiceState.Off, controller.Snapshot.State);
    }
    [Fact]
    public async Task VoiceTurnStopsWakeBeforeDictationAndSpeechThenRearms()
    {
        var platform = new FakeVoice();
        var states = new List<AssistantVoiceState>();
        using var controller = new AssistantVoiceController(platform, platform, platform, (text, _) =>
        { Assert.Equal("question", text); Assert.False(platform.WakeActive); return Task.FromResult("answer"); }, () => { });
        controller.Changed += state => states.Add(state.State);
        await controller.ConfigureAsync(true, true, "voice");
        await controller.ListenAsync();
        Assert.Equal(new[] { "wake", "stop wake", "dictate", "speak:answer", "wake" }, platform.Calls);
        Assert.Contains(AssistantVoiceState.Listening, states);
        Assert.Contains(AssistantVoiceState.Thinking, states);
        Assert.Contains(AssistantVoiceState.Speaking, states);
        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
        Assert.Equal("voice", platform.LastVoice);
    }
    [Fact]
    public async Task EmptyTranscriptNeverSubmitsOrSpeaks()
    {
        var platform = new FakeVoice { Transcript = " " };
        var submissions = 0;
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => { submissions++; return Task.FromResult("answer"); }, () => { });
        await controller.ListenAsync();
        Assert.Equal(0, submissions);
        Assert.DoesNotContain(platform.Calls, x => x.StartsWith("speak:"));
    }
    [Fact]
    public async Task ReadAloudCanBeDisabledWhileDictationStillSubmits()
    {
        var platform = new FakeVoice();
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult("answer"), () => { });
        await controller.ConfigureAsync(true, false, "");
        await controller.ListenAsync();
        Assert.DoesNotContain(platform.Calls, x => x.StartsWith("speak:"));
        Assert.True(platform.WakeActive);
    }
    [Fact]
    public async Task StopCancelsInFlightDictationAndDoesNotSubmit()
    {
        var platform = new FakeVoice { HoldDictation = true };
        var submitted = false;
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => { submitted = true; return Task.FromResult("answer"); }, () => { });
        await controller.ConfigureAsync(true, true, "");
        var turn = controller.ListenAsync();
        await platform.DictationStarted.Task;
        controller.Stop();
        await turn;
        Assert.False(submitted);
        Assert.Equal(AssistantVoiceState.WakeListening, controller.Snapshot.State);
    }
    [Fact]
    public async Task DuplicateWakeOrManualInputCannotStartConcurrentTurns()
    {
        var platform = new FakeVoice { HoldDictation = true };
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult("answer"), () => { });
        var turn = controller.ListenAsync();
        await platform.DictationStarted.Task;
        await controller.ListenAsync();
        Assert.Equal(1, platform.Calls.Count(x => x == "dictate"));
        controller.Stop(); await turn;
    }
    [Fact]
    public async Task DisablingWakeReleasesMicrophoneAndCancelsTurn()
    {
        var platform = new FakeVoice { HoldDictation = true };
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult("answer"), () => { });
        await controller.ConfigureAsync(true, true, "");
        var turn = controller.ListenAsync(); await platform.DictationStarted.Task;
        await controller.ConfigureAsync(false, true, ""); await turn;
        Assert.False(platform.WakeActive);
        Assert.Equal(AssistantVoiceState.Off, controller.Snapshot.State);
    }
    [Fact]
    public async Task ProviderFailureDoesNotReadOrAutomaticallyRetry()
    {
        var platform = new FakeVoice();
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => throw new InvalidOperationException("Provider unavailable"), () => { });
        await controller.ConfigureAsync(true, true, ""); await controller.ListenAsync();
        Assert.Equal(AssistantVoiceState.Error, controller.Snapshot.State);
        Assert.Equal("Provider unavailable", controller.Snapshot.Error);
        Assert.False(platform.WakeActive);
        Assert.DoesNotContain(platform.Calls, x => x.StartsWith("speak:"));
    }
    [Fact]
    public async Task MissingRecognizerProducesActionableErrorWithoutSubmitting()
    {
        var platform = new FakeVoice { IsAvailable = false };
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => throw new Exception("Must not submit"), () => { });
        await controller.ListenAsync();
        Assert.Equal(AssistantVoiceState.Error, controller.Snapshot.State);
        Assert.Contains("English Windows speech recognizer", controller.Snapshot.Error);
    }
    [Fact]
    public async Task ReadReplyPausesWakeAndResumesAfterPlayback()
    {
        var platform = new FakeVoice();
        using var controller = new AssistantVoiceController(platform, platform, platform, (_, _) => Task.FromResult(""), () => { });
        await controller.ConfigureAsync(true, true, ""); await controller.ReadAsync("last reply");
        Assert.Equal(new[] { "wake", "stop wake", "speak:last reply", "wake" }, platform.Calls);
    }
    private sealed class FakeVoice : IWakeWordInput, IQSpeechInputService, ISpeechOutput
    {
        public event Action? WakeDetected;
        public void TriggerWake() => WakeDetected?.Invoke();
        public List<string> Calls { get; } = [];
        public bool IsAvailable { get; set; } = true;
        public bool WakeActive { get; private set; }
        public string Transcript { get; set; } = "question";
        public bool HoldDictation { get; set; }
        public bool CancelDictation { get; set; }
        public string? LastVoice { get; private set; }
        public TaskCompletionSource DictationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartWakeAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Calls.Add("wake"); WakeActive = true; return Task.CompletedTask; }
        public Task StopWakeAsync() { Calls.Add("stop wake"); WakeActive = false; return Task.CompletedTask; }
        public async Task<string?> DictateAsync(CancellationToken token)
        {
            Assert.False(WakeActive); Calls.Add("dictate"); DictationStarted.TrySetResult();
            if (CancelDictation) throw new OperationCanceledException();
            if (HoldDictation) await Task.Delay(Timeout.Infinite, token);
            return Transcript;
        }
        public Task SpeakAsync(string text, string voice, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Assert.False(WakeActive); LastVoice = voice; Calls.Add("speak:" + text); return Task.CompletedTask; }
        public void StopSpeaking() { }
    }
}
