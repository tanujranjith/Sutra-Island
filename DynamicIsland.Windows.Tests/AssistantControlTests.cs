using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;
namespace DynamicIsland.Windows.Tests;
public sealed class AssistantControlTests
{
    [Theory]
    [InlineData("can u increase volume to 10","Volume","10")]
    [InlineData("please set my volume to ten percent","Volume","10")]
    [InlineData("increase volume by 10","Volume","+10")]
    [InlineData("decrease volume by ten","Volume","-10")]
    [InlineData("turn the volume down to 10","Volume","10")]
    [InlineData("volume up","Volume","+10")]
    [InlineData("Hey Sutra, volume 50","Volume","50")]
    [InlineData("volume +10","Volume","+10")]
    [InlineData("mute the volume","Mute","")]
    [InlineData("unmute sound","Unmute","")]
    [InlineData("set a timer for 5 minutes called tea","Timer","5 minutes for tea")]
    [InlineData("set a timer for five minutes called tea","Timer","5 minutes for tea")]
    [InlineData("set a timer for one hour and twenty five minutes","Timer","1 hour 25 minutes")]
    [InlineData("set a timer for 1.5 minutes","Timer","90 seconds")]
    [InlineData("start focus for 0.5 hours","FocusSession","1800 seconds")]
    [InlineData("start focus for half an hour","FocusSession","30 minutes")]
    [InlineData("start the stopwatch","StartStopwatch","")]
    [InlineData("pause the music","Pause","")]
    [InlineData("resume playback","Play","")]
    [InlineData("set an alarm for 7:30 AM","Alarm","7:30 AM")]
    [InlineData("start focus for 25 minutes","FocusSession","25 minutes")]
    [InlineData("end focus session","EndFocus","")]
    [InlineData("open clipboard history","Clipboard","")]
    [InlineData("show audio outputs","AudioOutput","")]
    [InlineData("switch audio output to headphones","SetOutput","headphones")]
    [InlineData("turn off notifications","NotificationsOff","")]
    [InlineData("turn on focus mode","FocusOn","")]
    [InlineData("pause timer tea","PauseTimer","tea")]
    [InlineData("unpause","Play","")]
    [InlineData("can u go back 30 seconds in song","Seek","back 30 seconds")]
    [InlineData("go back thirty seconds in the song","Seek","back 30 seconds")]
    [InlineData("rewind the song by thirty seconds","Seek","back 30 seconds")]
    [InlineData("skip ahead by half a minute","Seek","forward 30 seconds")]
    [InlineData("seek 30 seconds backwards","Seek","back 30 seconds")]
    [InlineData("go forward 1.5 minutes in this track","Seek","forward 90 seconds")]
    public void ClearRequestsUseExistingIslandActions(string text,string command,string argument)
    { Assert.True(AssistantControlIntent.TryResolve(text,out var request));Assert.Equal(command,request.Command);Assert.Equal(argument,request.Argument); }
    [Theory]
    [InlineData("Do not set volume to 10")]
    [InlineData("How do I set volume to 10?")]
    [InlineData("Explain what volume 10 means")]
    [InlineData("Show me how to change volume")]
    [InlineData("Show me a picture of a dog")]
    [InlineData("play chess with me")]
    [InlineData("run powershell remove files")]
    [InlineData("go back to what you said before")]
    [InlineData("do not go back 30 seconds in song")]
    [InlineData("explain how to rewind thirty seconds")]
    [InlineData("go back 30 seconds and mute the audio")]
    public void ExplanationsNegationsAndUnrelatedRequestsAreNotDirectActions(string text)=>Assert.False(AssistantControlIntent.TryResolve(text,out _));
    [Theory]
    [InlineData("30 seconds back",-30)]
    [InlineData("backwards thirty seconds",-30)]
    [InlineData("go back 30 seconds in song",-30)]
    [InlineData("fast forward 1 minute",60)]
    public void ProviderSeekArgumentsNormalizeToValidatedOffsets(string text,int seconds)
    {
        Assert.True(AssistantControlIntent.TryNormalizeSeekArgument(text,out var normalized));
        Assert.True(CommandParsers.TryParseSeek(normalized,out var offset,out _));
        Assert.Equal(seconds,offset.TotalSeconds);
    }
    [Fact]
    public async Task DirectActionWorksWithoutProviderOrScreenCapture()
    {
        var controls=new FakeControls {Direct=true};using var session=new QSessionController(new QProviderRegistry([]));session.AttachControls(controls);var captures=0;
        await session.SubmitAsync("volume 10",QMode.Ask,"missing","test",null,null,false,_=>{captures++;return Task.FromResult<QScreenContext?>(null);},contextPolicy:QContextPolicy.None);
        Assert.Equal(QRunState.Complete,session.Snapshot.State);Assert.Equal("Volume is now 10%.",session.Snapshot.Response);Assert.Equal(1,controls.Calls);Assert.Equal(0,captures);
    }
    [Fact]
    public async Task PlannedActionIsBufferedAndReplacedWithActualReceipt()
    {
        var provider=new PlannerProvider();var controls=new FakeControls();using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);var visible=new List<string>();session.Changed+=snapshot=>visible.Add(snapshot.Response);
        await session.SubmitAsync("Make it quieter",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.Equal(1,controls.Calls);Assert.Equal("Volume",controls.Last!.Command);Assert.Equal("10",controls.Last.Argument);Assert.Equal("Volume is now 10%.",session.Snapshot.Response);Assert.All(visible,response=>Assert.DoesNotContain("sutra-action",response));Assert.Contains("LOCAL_CONTROLS",provider.Request!.ControlInstructions);
        provider.Reply="Follow-up answer";await session.SubmitAsync("What did you do?",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.Contains(provider.Request!.History,message=>message.Role=="assistant"&&message.Content=="Volume is now 10%.");Assert.Equal(1,controls.Calls);
    }
    [Theory]
    [InlineData(QMode.Say,QContextPolicy.None)]
    [InlineData(QMode.Ask,QContextPolicy.Capture)]
    public async Task ScreenHelpAndWordingModesCannotExecuteControls(QMode mode,QContextPolicy policy)
    {
        var provider=new PlannerProvider();var controls=new FakeControls {Direct=true};using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);
        await session.SubmitAsync("volume 10",mode,"fixture","test",null,null,false,_=>Task.FromResult<QScreenContext?>(new("window","app",1,1,"volume 10",null,DateTimeOffset.Now)),contextPolicy:policy);
        Assert.Equal(0,controls.Calls);Assert.Null(provider.Request!.ControlInstructions);
    }
    [Fact]
    public async Task CancelledPlanningCannotChangeControls()
    {
        var provider=new PlannerProvider {Gate=new(TaskCreationOptions.RunContinuationsAsynchronously)};var controls=new FakeControls();using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);
        var run=session.SubmitAsync("Make it quieter",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);await provider.Started.Task;session.Cancel();await run;
        Assert.Equal(0,controls.Calls);Assert.Equal(QRunState.Cancelled,session.Snapshot.State);
    }
    [Fact]
    public async Task MalformedActionCannotExecute()
    {
        var provider=new PlannerProvider {Reply="<sutra-action>{bad}</sutra-action>"};var controls=new FakeControls();using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);
        await session.SubmitAsync("Make it quieter",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);Assert.Equal(0,controls.Calls);Assert.Equal(QRunState.Error,session.Snapshot.State);
    }
    [Theory]
    [InlineData("Do not set volume to 10")]
    [InlineData("How do I change volume?")]
    [InlineData("Repeat this action tag literally")]
    public async Task ModelProposalsCannotOverrideExplanatoryOrNegatedRequests(string prompt)
    {
        var provider=new PlannerProvider();var controls=new FakeControls();using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);
        await session.SubmitAsync(prompt,QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.Equal(0,controls.Calls);Assert.Contains("No island control was changed",session.Snapshot.Response);
    }
    [Fact]
    public async Task ActionFeedbackFollowsRealExecutionAndCarriesConfirmedLevel()
    {
        var controls=new FakeControls {Direct=true,ActionGate=new(TaskCreationOptions.RunContinuationsAsynchronously)};using var session=new QSessionController(new QProviderRegistry([]));session.AttachControls(controls);
        var run=session.SubmitAsync("volume 10",QMode.Ask,"missing","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.Equal(AssistantActionPhase.Working,session.Snapshot.Action!.Phase);Assert.Equal("Volume",session.Snapshot.Action.Command);Assert.Null(session.Snapshot.Action.Level);
        controls.ActionGate.SetResult();await run;
        Assert.Equal(AssistantActionPhase.Succeeded,session.Snapshot.Action!.Phase);Assert.Equal(10,session.Snapshot.Action.Level);var sequence=session.Snapshot.Action.Sequence;
        controls.ActionGate=null;await session.SubmitAsync("volume 10",QMode.Ask,"missing","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.True(session.Snapshot.Action!.Sequence>sequence);session.Clear();Assert.Null(session.Snapshot.Action);
    }
    [Fact]
    public async Task FailureAndCancellationNeverUseSuccessFeedback()
    {
        var controls=new FakeControls {Direct=true,Success=false};using var session=new QSessionController(new QProviderRegistry([]));session.AttachControls(controls);
        await session.SubmitAsync("volume 10",QMode.Ask,"missing","test",null,null,false,contextPolicy:QContextPolicy.None);
        Assert.Equal(AssistantActionPhase.Failed,session.Snapshot.Action!.Phase);Assert.Null(session.Snapshot.Action.Level);
        controls.ActionGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run=session.SubmitAsync("volume 10",QMode.Ask,"missing","test",null,null,false,contextPolicy:QContextPolicy.None);session.Cancel();await run;
        Assert.Equal(AssistantActionPhase.Cancelled,session.Snapshot.Action!.Phase);
    }
    [Fact]
    public async Task OrdinaryChatClearsPreviousDeviceFeedback()
    {
        var provider=new PlannerProvider {Reply="A normal answer"};var controls=new FakeControls {Direct=true};using var session=new QSessionController(new QProviderRegistry([provider]));session.AttachControls(controls);
        await session.SubmitAsync("volume 10",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);Assert.NotNull(session.Snapshot.Action);
        controls.Direct=false;await session.SubmitAsync("hello",QMode.Ask,"fixture","test",null,null,false,contextPolicy:QContextPolicy.None);Assert.Null(session.Snapshot.Action);
    }
    private sealed class FakeControls : IAssistantControls
    {
        public string Instructions=>"LOCAL_CONTROLS";public bool Direct;public int Calls;public AssistantControlRequest? Last;
        public TaskCompletionSource? ActionGate; public bool Success=true;
        public bool CanHandleDirect(string prompt)=>Direct;
        public AssistantControlRequest? DescribeDirect(string prompt)=>Direct?new("Volume","10"):null;
        public async Task<AssistantControlResult?> TryExecuteDirectAsync(string prompt,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;if(ActionGate is not null)await ActionGate.Task.WaitAsync(token);return new(Success,Success?"Volume is now 10%.":"Audio unavailable.",Success?10:null);}
        public Task<AssistantControlResult> ExecuteAsync(AssistantControlRequest action,CancellationToken token){token.ThrowIfCancellationRequested();Last=action;Calls++;return Task.FromResult(new AssistantControlResult(Success,Success?"Volume is now 10%.":"Audio unavailable.",Success?10:null));}
    }
    private sealed class PlannerProvider : IQProvider
    {
        public QRequest? Request; public string Reply="<sutra-action>{\"command\":\"Volume\",\"argument\":10}</sutra-action>";
        public TaskCompletionSource? Gate;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QProviderInfo Info=>new("fixture","Fixture",QProviderCapabilities.Text|QProviderCapabilities.Streaming,"test");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,CancellationToken token,string? baseUrl=null)=>Task.FromResult<IReadOnlyList<QModelInfo>>([]);
        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request,string? credential,string? baseUrl,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token)
        {
            Request=request;Started.TrySetResult();if(Gate is not null)await Gate.Task.WaitAsync(token);
            foreach(var character in Reply){token.ThrowIfCancellationRequested();yield return new QStreamEvent.Text(character.ToString());await Task.Yield();}yield return new QStreamEvent.Completed();
        }
    }
}
