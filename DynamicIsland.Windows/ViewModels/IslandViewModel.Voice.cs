using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure;

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
    public AssistantVoiceState VoiceState => _voiceSnapshot.State;
    public bool AssistantSpeaking => _voiceSnapshot.State == AssistantVoiceState.Speaking;
    public QRunState AssistantMascotState => _voiceSnapshot.State == AssistantVoiceState.Listening ? QRunState.Listening : QState;
    public void AttachVoice(AssistantVoiceController voice)
    {
        _voice = voice; voice.Changed += OnVoiceChanged;
        if (_compactVoiceAttached) return; // the orb's presenter and timer are wired once, whatever controller attaches
        _compactVoiceAttached = true;
        _compactVoice.StateChanged += OnCompactVoiceChanged;
        _compactVoiceTimer.Tick += (_, _) => { _compactVoiceTimer.Stop(); _compactVoice.Expire(); };
    }
    private bool _compactVoiceAttached;
    private void OnVoiceChanged(AssistantVoiceSnapshot snapshot) => OnUi(() =>
    {
        _voiceSnapshot = snapshot;
        // Compact voice mode: the island itself becomes the voice UI instead of opening the chat panel.
        _compactVoice.ReducedMotion = IsReducedMotion;
        _compactVoice.OnVoice(snapshot.State, snapshot.Status, snapshot.Error,
            CompactVoicePolicy.UseCompact(Settings.CompactVoiceMode, IsExpanded, Settings.AlwaysExpanded, Settings.QDisclosureAccepted));
        if (snapshot.State == AssistantVoiceState.Listening)
        {
            CancelQ();
            // Feedback already on the session belongs to an earlier request; the orb must not replay it.
            _compactVoice.OnQ(_qSnapshot.State, _qSnapshot.Response, _qSnapshot.Action);
            _ = StartChatAsync(useScreen: QUseScreenNext, expand: CompactVoicePolicy.ExpandPanelForVoice(CompactVoiceActive, Settings.QAutoExpandIsland));
        }
        RaiseMany(nameof(VoiceStatus), nameof(VoiceState), nameof(VoiceBusy), nameof(AssistantSpeaking), nameof(AssistantMascotState), nameof(QCanStop), nameof(QShowResponseActions), nameof(PinExpanded));
        UpdateQAutoCloseTimer();
    });

    // ===== Compact voice orb =====
    private readonly CompactVoicePresenter _compactVoice = new();
    private readonly System.Windows.Threading.DispatcherTimer _compactVoiceTimer = new();
    private int _compactVoiceDeadlineVersion = -1;
    private CompactVoiceState _compactVoiceLastState = CompactVoiceState.Idle;
    public CompactVoiceState CompactVoiceState => _compactVoice.State;
    /// <summary>A voice turn is presented by the compact orb (from wake until its exit finishes).</summary>
    public bool CompactVoiceActive => _compactVoice.IsActive;
    public bool CompactVoiceVisible => _compactVoice.IsActive && !IsExpanded;
    /// <summary>The island is widened for the orb.</summary>
    public bool CompactVoiceWide => _compactVoice.IsWide && !IsExpanded;
    /// <summary>The island is taller for an answer.</summary>
    public bool CompactVoiceTall => _compactVoice.IsTall && !IsExpanded;
    public string CompactVoiceTranscript => _compactVoice.Transcript;
    public string CompactVoiceAnswer => CleanQResponseText(_compactVoice.Answer);
    public string CompactVoiceError => _compactVoice.ErrorMessage;
    public AssistantActionFeedback? CompactVoiceAction => _compactVoice.Action;
    public double? CompactVoiceActionFrom => _compactVoice.ActionFromLevel;

    /// <summary>Interim words from the recognizer, so the transcript appears as it is spoken.</summary>
    public void OnVoicePartialTranscript(string text) => OnUi(() => _compactVoice.OnPartialTranscript(text));
    private void OnCompactVoiceQChanged() => _compactVoice.OnQ(_qSnapshot.State, _qSnapshot.Response, _qSnapshot.Action, Audio.MasterVolumePercent);

    private void OnCompactVoiceChanged(CompactVoiceState state)
    {
        var previous = _compactVoiceLastState;
        _compactVoiceLastState = state;
        if (_compactVoice.DeadlineVersion != _compactVoiceDeadlineVersion)
        {
            _compactVoiceDeadlineVersion = _compactVoice.DeadlineVersion;
            _compactVoiceTimer.Stop();
            if (_compactVoice.Deadline is { } hold) { _compactVoiceTimer.Interval = hold; _compactVoiceTimer.Start(); }
        }
        // The view reads the content first, then the state, so each state change finds its text in place.
        RaiseMany(nameof(CompactVoiceTranscript), nameof(CompactVoiceAnswer), nameof(CompactVoiceError), nameof(CompactVoiceAction), nameof(CompactVoiceActionFrom));
        if (state == previous) return;
        UpdateQAutoCloseTimer();
        RaiseMany(nameof(CompactVoiceState), nameof(CompactVoiceActive), nameof(CompactVoiceVisible), nameof(CompactVoiceWide), nameof(CompactVoiceTall), nameof(PinExpanded), nameof(IslandBaseSurfaceBrush));
        if (state == CompactVoiceState.Idle || previous == CompactVoiceState.Idle)
            RaiseMany(nameof(ShowCompactMediaContent), nameof(ShowCompactQContent), nameof(QShowCompactSingle), nameof(ShowCompactHolePunchQOutput), nameof(ShowCompactArtSurface), nameof(ShowCompactEqualizer));
        // Hand the island back to the music: once the exit has begun (and the shell is shrinking),
        // release the finished voice session so the compact content shows media again.
        if (state == CompactVoiceState.Exit) ReleaseCompactVoiceSession();
    }

    private void ReleaseCompactVoiceSession()
    {
        if (IsExpanded || _voiceSnapshot.State is AssistantVoiceState.Listening or AssistantVoiceState.Thinking or AssistantVoiceState.Speaking) return;
        if (QState is QRunState.Capturing or QRunState.Thinking or QRunState.Streaming) CancelQ();
        _qScreenSession = false; QUseScreenNext = false; CancelQActivation(); _qAutoCloseTimer.Stop();
        _qComparison.Clear(); _qSession.Clear(); _responsePresentation.Clear();
    }

    /// <summary>Hovering pins the orb open.</summary>
    public void SetCompactVoiceHovered(bool hovered) => _compactVoice.SetHovered(hovered);

    /// <summary>Esc: stop listening, cancel the response and speech, and run the exit.</summary>
    public void DismissCompactVoice()
    {
        if (!_compactVoice.IsActive || _compactVoice.State == CompactVoiceState.Exit) return;
        _compactVoice.Dismiss();
        _voice?.Stop(); CancelQ();
        ReleaseCompactVoiceSession();
    }

    /// <summary>Clicking the mascot while it speaks stops the speech; the answer stays up.</summary>
    public void StopCompactVoiceSpeech()
    {
        if (CompactVoicePolicy.CanStopSpeech(_compactVoice.State, AssistantSpeaking)) _voice?.Stop();
    }

    /// <summary>"Open in chat": the existing chat panel takes over with this conversation.</summary>
    public void OpenCompactVoiceInChat()
    {
        if (!_compactVoice.IsActive) return;
        _compactVoiceTimer.Stop();
        IsExpanded = true; // resets the orb (OnExpandedDuringCompactVoice) after the panel starts opening
        _compactVoice.Reset();
    }

    /// <summary>"Fix": the Windows settings page for the problem, when there is one.</summary>
    public string? CompactVoiceFixTarget => CompactVoicePolicy.FixTarget(_compactVoice.ErrorMessage);

    private void OnExpandedDuringCompactVoice()
    {
        // Something expanded the island (a click, a pinned setting): the panel owns the turn from here.
        if (!_compactVoice.IsActive) return;
        _compactVoiceTimer.Stop();
        _compactVoice.Reset();
    }
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
    public async Task StartChatAsync(string? prompt = null, bool useScreen = false, bool newConversation = false, bool expand = true)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        { await dispatcher.InvokeAsync(() => StartChatAsync(prompt, useScreen, newConversation, expand)).Task.Unwrap(); return; }
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
        if (expand && Settings.QAutoExpandIsland) IsExpanded = true;
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
        _compactVoice.OnPrompt(prompt);
        await StartChatAsync(useScreen: QUseScreenNext, expand: CompactVoicePolicy.ExpandPanelForVoice(CompactVoiceActive, Settings.QAutoExpandIsland));
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
