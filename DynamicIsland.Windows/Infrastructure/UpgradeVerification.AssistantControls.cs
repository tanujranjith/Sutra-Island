using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.ViewModels;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyAssistantIslandControlsAsync(IslandViewModel island,CommandRunner runner,PaletteAudioFixture audio,PaletteOutputFixture outputs,SutraPaletteHost host,SettingsService persistence,Action<bool,string> check)
    {
        var controls=new AssistantIslandControls(runner,async()=>{await persistence.SaveAsync(island.Settings);island.ApplySettings();}); island.AttachAssistantControls(controls);
        audio.Emit(audio.Current with {Availability=AudioAvailability.Available,MasterVolumePercent=42,SystemMuted=false});
        await island.StartChatAsync("can u increase volume to 10",newConversation:true);
        check(audio.Current.MasterVolumePercent==10 && island.QResponse.Contains("10%"),"Typed assistant request sets absolute volume through the real control adapter and returns actual state");
        check(island.QActionFeedback is {Phase:AssistantActionPhase.Succeeded,Level:10} && !island.QShowInlineThinking,"Confirmed device feedback reaches the assistant view model and replaces generic thinking dots");
        var spoken=await island.SubmitVoiceTurnAsync("decrease volume by five",CancellationToken.None);
        check(audio.Current.MasterVolumePercent==5 && spoken.Contains("5%"),"Voice and typed requests share the same volume action path");
        await controls.ExecuteAsync(new("Mute"),CancellationToken.None);check(audio.Current.SystemMuted,"Assistant mute action changes the existing audio service");
        await controls.ExecuteAsync(new("Unmute"),CancellationToken.None);check(!audio.Current.SystemMuted,"Assistant unmute action changes the existing audio service");
        var before=audio.Current.MasterVolumePercent;var invalid=await controls.ExecuteAsync(new("Volume","150"),CancellationToken.None);
        check(!invalid.Success && audio.Current.MasterVolumePercent==before,"Out-of-range assistant volume does not change audio");
        audio.Emit(audio.Current with {Availability=AudioAvailability.Unsupported});var unavailable=await controls.ExecuteAsync(new("Volume","10"),CancellationToken.None);
        check(!unavailable.Success && audio.Current.MasterVolumePercent==before,"Unavailable audio returns a failure without claiming a change");audio.Emit(audio.Current with {Availability=AudioAvailability.Available});
        var timerCount=runner.Timers.State.Timers.Count;
        await controls.TryExecuteDirectAsync("set a timer for 5 minutes called tea",CancellationToken.None);
        var timer=runner.Timers.State.Timers.Last();check(runner.Timers.State.Timers.Count==timerCount+1 && timer.TotalSeconds==300 && timer.Label=="tea","Assistant creates a labelled timer using existing duration parsing");
        await controls.ExecuteAsync(new("PauseTimer","tea"),CancellationToken.None);check(timer.Phase==TimerPhase.Paused,"Assistant pauses a named timer");
        await controls.ExecuteAsync(new("ResumeTimer","tea"),CancellationToken.None);check(timer.Phase==TimerPhase.Running,"Assistant resumes a named timer");
        await controls.ExecuteAsync(new("CancelTimer","tea"),CancellationToken.None);check(runner.Timers.State.Timers.Count==timerCount,"Assistant cancels a named timer");
        var alarmCount=runner.Timers.State.Alarms.Count;await controls.ExecuteAsync(new("Alarm","7:30 AM"),CancellationToken.None);
        check(runner.Timers.State.Alarms.Count==alarmCount+1,"Assistant sets an alarm through the existing alarm parser");
        await controls.ExecuteAsync(new("CancelAlarm","7:30 AM"),CancellationToken.None);check(runner.Timers.State.Alarms.Count==alarmCount,"Assistant cancels the requested alarm");
        var noAlarm=await controls.ExecuteAsync(new("DismissAlarm"),CancellationToken.None);check(!noAlarm.Success,"Assistant does not claim to dismiss an alarm when none is ringing");
        await controls.ExecuteAsync(new("StartStopwatch"),CancellationToken.None);await Task.Delay(30);await controls.ExecuteAsync(new("LapStopwatch"),CancellationToken.None);
        check(runner.Stopwatch.IsStopwatchRunning && runner.Stopwatch.Laps.Count==1,"Assistant starts the shared stopwatch and records a lap");
        await controls.ExecuteAsync(new("PauseStopwatch"),CancellationToken.None);check(!runner.Stopwatch.IsStopwatchRunning,"Assistant pauses the shared stopwatch");
        await controls.ExecuteAsync(new("ResetStopwatch"),CancellationToken.None);check(runner.Stopwatch.Laps.Count==0,"Assistant resets the shared stopwatch");
        island.SetFocusMode(true);await controls.ExecuteAsync(new("FocusSession","25 minutes"),CancellationToken.None);check(island.FocusSessionActive,"Assistant starts a timed focus session");
        await controls.ExecuteAsync(new("EndFocus"),CancellationToken.None);check(!island.FocusSessionActive && island.Settings.FocusModeEnabled,"Ending assistant focus restores the previous focus mode");island.SetFocusMode(false);
        var date=await controls.ExecuteAsync(new("SetSetting","compact date","true"),CancellationToken.None);
        check(date.Success && island.Settings.ShowCompactDate,"Assistant changes a named display setting and applies it");await controls.ExecuteAsync(new("SetSetting","ShowCompactDate","false"),CancellationToken.None);
        var planner=new IslandControlPlannerFixture();using(var plannedSession=new QSessionController(new QProviderRegistry([planner])))
        {
            plannedSession.AttachControls(controls);
            await plannedSession.SubmitAsync("Make the short date visible beneath the clock",QMode.Ask,"control-planner","fixture",null,null,false,contextPolicy:QContextPolicy.None);
            check(island.Settings.ShowCompactDate && plannedSession.Snapshot.Response.Contains("True") && planner.Request?.ControlInstructions?.Contains("SetSetting")==true,"Provider-planned natural language executes the same real settings adapter and returns its actual receipt");
        }
        await controls.ExecuteAsync(new("SetSetting","ShowCompactDate","false"),CancellationToken.None);
        var width=await controls.ExecuteAsync(new("SetSetting","IslandWidth","1000"),CancellationToken.None);check(width.Success && island.Settings.IslandWidth==360 && width.Message.Contains("360"),"Setting changes report the normalized applied value");
        var selectedProvider=island.Settings.QSelectedProvider;var secret=await controls.ExecuteAsync(new("SetSetting","QSelectedProvider","other"),CancellationToken.None);
        check(!secret.Success && island.Settings.QSelectedProvider==selectedProvider,"Assistant controls cannot rewrite provider credentials or provider selection");
        var malformed=await controls.ExecuteAsync(new("SetSetting","Theme","not-a-theme"),CancellationToken.None);check(!malformed.Success,"Invalid theme settings are rejected before applying");
        await controls.ExecuteAsync(new("NotificationsOff"),CancellationToken.None);check(!island.Settings.ShowNotifications,"Assistant disables notification mirroring");await controls.ExecuteAsync(new("NotificationsOn"),CancellationToken.None);check(island.Settings.ShowNotifications,"Assistant enables notification mirroring");
        await controls.ExecuteAsync(new("AudioOutput"),CancellationToken.None);check(host.LastPanel=="audio output","Assistant opens the existing audio output selector");
        var switched=await controls.ExecuteAsync(new("SetOutput","Speakers"),CancellationToken.None);check(switched.Success && outputs.Current=="speakers","Assistant resolves an audio device name and confirms output selection");
        var badOutput=await controls.ExecuteAsync(new("SetOutput","missing device"),CancellationToken.None);check(!badOutput.Success && outputs.Current=="speakers","Missing output devices do not produce a false success");
        await controls.ExecuteAsync(new("Clipboard"),CancellationToken.None);check(host.LastPanel=="clipboard","Assistant opens existing image-aware clipboard history without reading it into chat");
        await controls.ExecuteAsync(new("OpenSetting","ShowClock"),CancellationToken.None);check(host.LastSetting=="ShowClock","Assistant opens and highlights a requested setting");
        var blocked=await controls.ExecuteAsync(new("RunShell","powershell"),CancellationToken.None);check(!blocked.Success,"Commands outside the island control allowlist cannot execute");
        await island.StartChatAsync("can u go back 30 seconds in song",newConversation:true);
        check(island.QActionFeedback is {Command:"Seek",Phase:AssistantActionPhase.Failed} && island.QResponse.Contains("seek request"),"Reported rewind wording reaches the local seek adapter without a provider or direction-grammar error; isolated profile has no media session");
        var providerSeek=await controls.ExecuteAsync(new("Seek","30 seconds back"),CancellationToken.None);
        check(!providerSeek.Success && providerSeek.Message.Contains("seek request"),"Duration-first provider seek argument passes validation and reaches media availability checking");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();var cancelled=false;
        try{await controls.ExecuteAsync(new("Volume","80"),cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled && audio.Current.MasterVolumePercent==before,"Cancelled assistant actions do not mutate controls");
        island.ClearQ();
    }
    private sealed class IslandControlPlannerFixture : IQProvider
    {
        public QRequest? Request;
        public QProviderInfo Info=>new("control-planner","Planner fixture",QProviderCapabilities.Text|QProviderCapabilities.Streaming,"fixture");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,CancellationToken token,string? baseUrl=null)=>Task.FromResult<IReadOnlyList<QModelInfo>>([]);
        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request,string? credential,string? baseUrl,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token)
        {
            Request=request;yield return new QStreamEvent.Text("<sutra-action>");await Task.Yield();token.ThrowIfCancellationRequested();
            yield return new QStreamEvent.Text("{\"command\":\"SetSetting\",\"argument\":\"ShowCompactDate\",\"value\":true}");yield return new QStreamEvent.Text("</sutra-action>");yield return new QStreamEvent.Completed();
        }
    }
}
