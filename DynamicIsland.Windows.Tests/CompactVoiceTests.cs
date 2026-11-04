using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CompactVoiceTests
{
    private static CompactVoicePresenter Wake(bool compact = true)
    {
        var presenter = new CompactVoicePresenter();
        presenter.OnVoice(AssistantVoiceState.WakeListening, "Say “Hey Sutra”", null, compact);
        presenter.OnVoice(AssistantVoiceState.Listening, "Listening for your question…", null, compact);
        return presenter;
    }

    private static CompactVoicePresenter Thinking(string prompt = "remind me to fill out the UIUC form")
    {
        var presenter = Wake();
        presenter.Expire();
        presenter.OnPrompt(prompt);
        presenter.OnVoice(AssistantVoiceState.Thinking, "Sutra is responding…", null, true);
        return presenter;
    }

    [Fact]
    public void WakeSettlesIntoListeningThenThinkingWithoutGrowing()
    {
        var presenter = Wake();
        Assert.Equal(CompactVoiceState.Wake, presenter.State);
        Assert.True(presenter.IsWide);
        Assert.False(presenter.IsTall);
        Assert.Equal(CompactVoiceTimings.Wake, presenter.Deadline);

        presenter.Expire();
        Assert.Equal(CompactVoiceState.Listening, presenter.State);
        Assert.Null(presenter.Deadline);

        presenter.OnPartialTranscript("remind me to");
        Assert.Equal("remind me to", presenter.Transcript);
        presenter.OnPrompt("remind me to fill out the UIUC form");
        presenter.OnVoice(AssistantVoiceState.Thinking, "Sutra is responding…", null, true);
        Assert.Equal(CompactVoiceState.Thinking, presenter.State);
        Assert.Equal("remind me to fill out the UIUC form", presenter.Transcript);
        Assert.False(presenter.IsTall);
    }

    [Fact]
    public void SpokenAnswerGrowsTallerAndHoldsThreeSecondsAfterSpeech()
    {
        var presenter = Thinking();
        presenter.OnQ(QRunState.Complete, "Done. I'll remind you tonight.", null);
        Assert.Equal(CompactVoiceState.Answer, presenter.State);
        Assert.True(presenter.IsTall);

        presenter.OnVoice(AssistantVoiceState.Speaking, "Speaking…", null, true);
        Assert.Equal(CompactVoiceState.Answer, presenter.State);
        Assert.Null(presenter.Deadline); // held while speaking

        presenter.OnVoice(AssistantVoiceState.WakeListening, "Say “Hey Sutra”", null, true);
        Assert.Equal(CompactVoiceTimings.AnswerHold, presenter.Deadline);
        presenter.Expire();
        Assert.Equal(CompactVoiceState.Exit, presenter.State);
        Assert.False(presenter.IsWide);
        presenter.Expire();
        Assert.Equal(CompactVoiceState.Idle, presenter.State);
        Assert.Equal("", presenter.Answer);
    }

    [Fact]
    public void SilentAnswerHoldsForTypingPlusThreeSeconds()
    {
        var presenter = Thinking();
        const string answer = "Done. I'll remind you tonight.";
        presenter.OnQ(QRunState.Complete, answer, null);
        presenter.OnVoice(AssistantVoiceState.Off, "Voice ready", null, true); // read-aloud off
        Assert.Equal(CompactVoiceState.Answer, presenter.State);
        Assert.Equal(CompactVoiceTimings.AnswerHold + CompactVoiceTimings.Typing(answer, false), presenter.Deadline);
    }

    [Fact]
    public void SystemCommandShowsLiveControlWithoutGrowingThenExits()
    {
        var presenter = Thinking("turn the volume down to 5%");
        presenter.OnQ(QRunState.Thinking, "", new AssistantActionFeedback(7, "Volume", "5", AssistantActionPhase.Working), currentLevel: 40);
        Assert.Equal(CompactVoiceState.Thinking, presenter.State);
        presenter.OnQ(QRunState.Thinking, "", new AssistantActionFeedback(7, "Volume", "5", AssistantActionPhase.Succeeded, "Volume is now 5%.", 5), currentLevel: 5);
        Assert.Equal(CompactVoiceState.Action, presenter.State);
        Assert.False(presenter.IsTall);
        Assert.Equal(40, presenter.ActionFromLevel); // the slider starts where the level was
        Assert.Equal(5, presenter.Action!.Level);
        Assert.Equal(CompactVoiceTimings.ActionSettle + CompactVoiceTimings.ActionHold, presenter.Deadline);

        presenter.OnQ(QRunState.Complete, "Volume is now 5%.", presenter.Action);
        presenter.OnVoice(AssistantVoiceState.Speaking, "Speaking…", null, true);
        Assert.Equal(CompactVoiceState.Action, presenter.State);
        presenter.Expire();
        Assert.Equal(CompactVoiceState.Exit, presenter.State);
    }

    [Fact]
    public void FeedbackFromAnEarlierRequestIsNotReplayed()
    {
        var presenter = new CompactVoicePresenter();
        presenter.OnVoice(AssistantVoiceState.Listening, "", null, true);
        presenter.OnQ(QRunState.Complete, "Muted.", new AssistantActionFeedback(3, "Mute", "", AssistantActionPhase.Succeeded, "Muted"));
        presenter.Expire();
        presenter.OnVoice(AssistantVoiceState.Thinking, "", null, true);
        presenter.OnQ(QRunState.Thinking, "", new AssistantActionFeedback(3, "Mute", "", AssistantActionPhase.Succeeded, "Muted"));
        Assert.Equal(CompactVoiceState.Thinking, presenter.State);
        Assert.Null(presenter.Action);
    }

    [Fact]
    public void ErrorsShowForFourSecondsIncludingFailuresRightAfterAWake()
    {
        var presenter = Thinking();
        presenter.OnVoice(AssistantVoiceState.Error, "Voice unavailable — typed chat still works", "Check Windows microphone access and installed speech language.", true);
        Assert.Equal(CompactVoiceState.Error, presenter.State);
        Assert.Equal("Mic is blocked", presenter.ErrorMessage);
        Assert.Equal(CompactVoiceTimings.ErrorHold, presenter.Deadline);
        Assert.False(presenter.IsTall);

        // The controller can fail before it publishes Listening (no recognizer installed) right after a wake.
        var early = new CompactVoicePresenter();
        early.OnVoice(AssistantVoiceState.WakeListening, "", null, true);
        early.OnVoice(AssistantVoiceState.Error, "Voice unavailable — typed chat still works", "Install an English Windows speech recognizer and allow microphone access.", true);
        Assert.Equal(CompactVoiceState.Error, early.State);

        // Wake listening failing at startup is not a voice turn.
        var startup = new CompactVoicePresenter();
        startup.OnVoice(AssistantVoiceState.Error, "Wake listening unavailable", "busy", true);
        Assert.Equal(CompactVoiceState.Idle, startup.State);
    }

    [Fact]
    public void NewWakeDuringAnAnswerGoesStraightBackToListening()
    {
        var presenter = Thinking();
        presenter.OnQ(QRunState.Complete, "Done.", null);
        presenter.OnVoice(AssistantVoiceState.WakeListening, "", null, true);
        presenter.OnVoice(AssistantVoiceState.Listening, "", null, true);
        Assert.Equal(CompactVoiceState.Listening, presenter.State);
        Assert.Equal("", presenter.Answer);
        Assert.Equal("", presenter.Transcript);
    }

    [Fact]
    public void NothingHeardOrCancelledRunsTheExit()
    {
        var presenter = Wake();
        presenter.Expire();
        presenter.OnVoice(AssistantVoiceState.WakeListening, "", null, true);
        Assert.Equal(CompactVoiceState.Exit, presenter.State);

        var cancelled = Thinking();
        cancelled.Dismiss();
        Assert.Equal(CompactVoiceState.Exit, cancelled.State);
        Assert.Equal(CompactVoiceTimings.Exit, cancelled.Deadline);
    }

    [Fact]
    public void HoverPinsTheOrbOpen()
    {
        var presenter = Thinking();
        presenter.OnQ(QRunState.Complete, "Done.", null);
        presenter.SetHovered(true);
        Assert.Null(presenter.Deadline);
        presenter.Expire();
        Assert.Equal(CompactVoiceState.Answer, presenter.State);
        presenter.SetHovered(false);
        Assert.Equal(CompactVoiceTimings.AnswerHold, presenter.Deadline);
    }

    [Fact]
    public void SettingOffKeepsTheOldPanelBehavior()
    {
        var presenter = Wake(compact: false);
        Assert.Equal(CompactVoiceState.Idle, presenter.State);
        presenter.OnVoice(AssistantVoiceState.Thinking, "", null, false);
        presenter.OnQ(QRunState.Complete, "answer", null);
        presenter.OnVoice(AssistantVoiceState.Speaking, "", null, false);
        Assert.Equal(CompactVoiceState.Idle, presenter.State);
        Assert.False(presenter.IsActive);

        Assert.True(CompactVoicePolicy.ExpandPanelForVoice(compactTurn: false, autoExpandSetting: true));
        Assert.False(CompactVoicePolicy.ExpandPanelForVoice(compactTurn: true, autoExpandSetting: true));
        Assert.False(CompactVoicePolicy.ExpandPanelForVoice(compactTurn: false, autoExpandSetting: false));
    }

    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, false, false, true, false)] // setting off: the chat panel, as before
    [InlineData(true, true, false, true, false)]   // already expanded: the panel is on screen
    [InlineData(true, false, true, true, false)]   // always expanded
    [InlineData(true, false, false, false, false)] // the disclosure needs the panel
    public void CompactModeAppliesOnlyToTheCompactIsland(bool setting, bool expanded, bool alwaysExpanded, bool disclosure, bool expected)
        => Assert.Equal(expected, CompactVoicePolicy.UseCompact(setting, expanded, alwaysExpanded, disclosure));

    [Fact]
    public void CompactVoiceModeIsOnByDefault() => Assert.True(CompactVoicePolicy.DefaultEnabled);

    [Theory]
    [InlineData(CompactVoiceState.Wake)]
    [InlineData(CompactVoiceState.Listening)]
    [InlineData(CompactVoiceState.Thinking)]
    [InlineData(CompactVoiceState.Answer)]
    [InlineData(CompactVoiceState.Action)]
    [InlineData(CompactVoiceState.Error)]
    [InlineData(CompactVoiceState.Exit)]
    public void ReducedMotionKeepsCrossfadesOnly(CompactVoiceState state)
    {
        var motion = CompactVoiceMotion.For(state, reducedMotion: true);
        Assert.False(motion.AnyMotion);
        Assert.True(motion.Crossfade);
        Assert.Equal(TimeSpan.Zero, CompactVoiceTimings.Typing("Done. I'll remind you tonight.", reducedMotion: true));
    }

    [Fact]
    public void MotionRunsOnlyInItsOwnState()
    {
        Assert.True(CompactVoiceMotion.For(CompactVoiceState.Wake, false).SlideAndScale);
        Assert.True(CompactVoiceMotion.For(CompactVoiceState.Listening, false).Ripples);
        var thinking = CompactVoiceMotion.For(CompactVoiceState.Thinking, false);
        Assert.True(thinking.Orbit && thinking.Bob && thinking.Shimmer && !thinking.Ripples);
        Assert.True(CompactVoiceMotion.For(CompactVoiceState.Answer, false).Breathe);
        Assert.False(CompactVoiceMotion.For(CompactVoiceState.Action, false).Orbit);
        Assert.False(CompactVoiceMotion.For(CompactVoiceState.Idle, false).AnyMotion); // nothing runs while idle
    }

    [Fact]
    public void OrbSizeFollowsTheMock()
    {
        Assert.Equal((317d, 62d), CompactVoicePolicy.Size(230, 62, tall: false)); // ~38% wider, same height
        Assert.Equal((317d, 84d), CompactVoicePolicy.Size(230, 62, tall: true));  // 110 → 150 ratio
        Assert.Equal((300d, 56d), CompactVoicePolicy.Size(120, 38, tall: false)); // floors for tiny islands
    }

    [Theory]
    [InlineData(80, 3)]
    [InlineData(46, 3)]
    [InlineData(45, 2)]
    [InlineData(21, 2)]
    [InlineData(20, 1)]
    [InlineData(5, 1)]
    public void SpeakerWavesFollowTheLevel(double level, int waves) => Assert.Equal(waves, CompactVoicePolicy.VolumeWaves(level));

    [Fact]
    public void ActionsAndErrorsAreWordedForTheIsland()
    {
        Assert.True(CompactVoicePolicy.ShowsLevel(new AssistantActionFeedback(1, "Volume", "5", AssistantActionPhase.Succeeded, "Volume is now 5%.", 5)));
        Assert.False(CompactVoicePolicy.ShowsLevel(new AssistantActionFeedback(1, "Mute", "", AssistantActionPhase.Succeeded, "Muted")));
        Assert.Equal("Muted", CompactVoicePolicy.ActionConfirmation(new AssistantActionFeedback(1, "Mute", "", AssistantActionPhase.Succeeded, "Audio muted.")));
        Assert.Equal("ms-settings:privacy-microphone", CompactVoicePolicy.FixTarget(CompactVoicePolicy.ErrorMessage("Voice unavailable", "Check Windows microphone access.")));
        Assert.Null(CompactVoicePolicy.FixTarget(CompactVoicePolicy.ErrorMessage("Voice unavailable", "The provider returned 401")));
    }
}
