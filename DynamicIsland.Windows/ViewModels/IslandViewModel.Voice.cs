using DynamicIsland.Q.Core;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class IslandViewModel
{
    private AssistantVoiceController? _voice;
    private IAssistantControls? _assistantControls;
    public void AttachAssistantControls(IAssistantControls controls) { _assistantControls = controls; _qSession.AttachControls(controls); }
    private AssistantVoiceSnapshot _voiceSnapshot = new(AssistantVoiceState.Off, "Voice ready");
    private bool _qScreenSession;
    private bool _qUseScreenNext;
    public bool QUseScreenNext { get => _qUseScreenNext; set { _qUseScreenNext = value; RaiseMany(nameof(QUseScreenNext), nameof(QSourceText)); } }
    public string VoiceStatus => _voiceSnapshot.Error is null ? _voiceSnapshot.Status : _voiceSnapshot.Status + ": " + _voiceSnapshot.Error;
    public bool VoiceBusy => _voiceSnapshot.State is AssistantVoiceState.Listening or AssistantVoiceState.Thinking or AssistantVoiceState.Speaking;
    public bool AssistantSpeaking => _voiceSnapshot.State == AssistantVoiceState.Speaking;
    public QRunState AssistantMascotState => _voiceSnapshot.State == AssistantVoiceState.Listening ? QRunState.Listening : QState;
    public void AttachVoice(AssistantVoiceController voice)
    { _voice = voice; voice.Changed += OnVoiceChanged; }
    private void OnVoiceChanged(AssistantVoiceSnapshot snapshot) => OnUi(() =>
    {
        _voiceSnapshot = snapshot;
        if (snapshot.State == AssistantVoiceState.Listening)
        { CancelQ(); _ = StartChatAsync(useScreen: QUseScreenNext); }
        RaiseMany(nameof(VoiceStatus), nameof(VoiceBusy), nameof(AssistantSpeaking), nameof(AssistantMascotState), nameof(QCanStop), nameof(QShowResponseActions), nameof(PinExpanded));
        UpdateQAutoCloseTimer();
    });
    public Task ConfigureVoiceAsync() => _voice?.ConfigureAsync(Settings.QEnabled && Settings.WakeListeningEnabled, Settings.VoiceReadAloud, Settings.VoiceName) ?? Task.CompletedTask;
    public async Task SetWakeEnabledAsync(bool enabled)
    { Settings.WakeListeningEnabled = enabled; await PersistSettingsAsync(); await ConfigureVoiceAsync(); }
    public async Task StartVoiceAsync()
    {
        if (!Settings.QEnabled) return;
        await StartChatAsync(useScreen: QUseScreenNext);
        if (!Settings.QDisclosureAccepted)
        { _voiceSnapshot = new(AssistantVoiceState.Error, "Allow the assistant provider disclosure before using voice"); RaisePropertyChanged(nameof(VoiceStatus)); return; }
        if (_voice is not null) await _voice.ListenAsync();
    }
    public Task ReadReplyAsync() => _voice?.ReadAsync(CleanQResponseText(QResponse)) ?? Task.CompletedTask;
    public void StopAssistant() { _voice?.Stop(); CancelQ(); }
    public async Task StartChatAsync(string? prompt = null, bool useScreen = false, bool newConversation = false)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        { await dispatcher.InvokeAsync(() => StartChatAsync(prompt, useScreen, newConversation)).Task.Unwrap(); return; }
        if (!Settings.QEnabled) return;
        CancelQActivation(); _qAutoCloseTimer.Stop();
        if (!string.IsNullOrWhiteSpace(prompt) && QCanStop) StopAssistant();
        if (newConversation) { _voice?.Stop(); _qSession.Clear(); _qComparison.Clear(); }
        _qScreenSession = false;
        _qTargetWindow = nint.Zero;
        QUseScreenNext = useScreen;
        QCompareEnabled = false;
        if (!IsQActive || _qSnapshot.Context is not null || newConversation)
            await _qSession.BeginAsync(QMode.Ask, Settings.QSelectedProvider, Settings.QSelectedModel, null);
        SetQMode(QMode.Ask);
        if (Settings.QAutoExpandIsland) IsExpanded = true;
        RaiseQProperties();
        if (!string.IsNullOrWhiteSpace(prompt)) await SubmitQAsync(prompt);
    }
    public async Task<string> SubmitVoiceTurnAsync(string prompt, CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return await dispatcher.InvokeAsync(() => SubmitVoiceTurnAsync(prompt, cancellationToken)).Task.Unwrap();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Settings.QDisclosureAccepted) throw new InvalidOperationException("Allow the assistant provider disclosure before using voice.");
        await StartChatAsync(useScreen: QUseScreenNext);
        using var registration = cancellationToken.Register(() => OnUi(CancelQ));
        await SubmitQAsync(prompt);
        cancellationToken.ThrowIfCancellationRequested();
        var result = _qSession.Snapshot;
        if (result.State == QRunState.Error) throw new InvalidOperationException(result.Error ?? "The assistant could not respond.");
        return result.State == QRunState.Complete ? result.Response : "";
    }
    public void DismissVisibleBanner()
    {
        if (IsLockBannerActive) { _lockFeedbackUntil = default; _lockFeedbackTimer.Stop(); RefreshLockFeedback(); PumpNotifications(); }
        else if (ShowNotification) DismissCurrentNotificationCommand.Execute(null);
        else { _volumeWarningActive = false; _airPodsBannerActive = false; RaiseMany(nameof(ShowBanner), nameof(IsAirPodsBannerActive)); }
    }
}
