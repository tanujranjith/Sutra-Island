using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Sutra Flow: hold Right Ctrl, speak, let go, and clean text lands where the cursor is.
/// Owns the whole loop (key → microphone → Whisper → cleanup → paste) and publishes
/// one <see cref="FlowStatus"/> the island's dot display draws from.
/// </summary>
public sealed class FlowController : IDisposable
{
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(8);

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _ui;
    private readonly LoggingService _log;
    private readonly FlowHotkeyHook _hook;
    private readonly FlowRecorder _recorder;
    private readonly FlowAiCleaner _ai;
    private readonly FlowPipeline _pipeline;
    private readonly FlowTextInjector _injector;
    private readonly FlowAudioDucker _ducker;
    private readonly FlowSounds _sounds;
    /// <summary>The app that was in front when the push-to-talk key went down (process name).</summary>
    private volatile string? _targetApp;
    private bool _ducking;
    private string? _heldText;
    private DispatcherTimer? _heldTimer;
    /// <summary>How long undelivered dictation stays available to copy (memory only, never saved).</summary>
    private static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(2);

    public WhisperEngine Engine { get; }
    public FlowStatus Status { get; private set; } = new(FlowState.Idle);
    /// <summary>Raised on the UI thread whenever <see cref="Status"/> changes.</summary>
    public event EventHandler<FlowStatus>? StatusChanged;
    /// <summary>Current microphone loudness, 0..1 (any thread).</summary>
    public float Level => _pipeline.Level;

    public FlowController(AppSettings settings, SettingsService settingsService, Dispatcher ui, LoggingService log,
        DynamicIsland.Q.Core.IQProviderRegistry providers, Q.IQSecretStore secrets)
    {
        _settings = settings;
        _settingsService = settingsService;
        _ui = ui;
        _log = log;
        Engine = new WhisperEngine(log, () => _settings.FlowLanguage);
        _hook = new FlowHotkeyHook(log, () => _pipeline?.IsRecording == true);
        _recorder = new FlowRecorder(log) { PreferredDevice = () => _settings.FlowMicrophone };
        _sounds = new FlowSounds(log);
        _ai = new FlowAiCleaner(settings, providers, secrets, log);
        _injector = new FlowTextInjector(log, ui);
        _ducker = new FlowAudioDucker(log);
        _pipeline = new FlowPipeline(_recorder, _injector, () => _settings.FlowEnabled,
            TranscribeAsync, CleanAsync, Publish, OnPasted, Hold, (message, ex) => _log.Error(message, ex));
        // The hook's worker calls these one at a time; each returns at once (Release hands the
        // slow work to a task after marking the session busy), so key events never queue up behind it.
        _hook.Pressed += CaptureTargetApp;
        _hook.Pressed += DuckOnPress;
        _hook.Pressed += _pipeline.Press;
        _hook.Released += () => _ = _pipeline.Release();
        _hook.Cancelled += () => _ = _pipeline.Cancel();
    }

    public string? AiUnavailableReason => _ai.Unavailable();
    /// <summary>The AI cleanup side, for the settings page (provider list, effective model, key check).</summary>
    public FlowAiCleaner Ai => _ai;

    public void Start()
    {
        _ducker.RecoverFromCrash();
        ApplySettings();
        _hook.Start();
    }

    /// <summary>Call after Flow settings change. Turning Flow off abandons a dictation in progress.</summary>
    public void ApplySettings()
    {
        _hook.Configure(_settings.FlowEnabled, FlowKeyGate.KeyFor(_settings.FlowPushKey));
        if (_settings.FlowEnabled) _ = Engine.EnsureModelAsync();
        else _pipeline.Disable();
    }

    private IReadOnlyList<string> Vocabulary() =>
        _settings.FlowVocabulary.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();

    private async Task<string> TranscribeAsync(FlowRecording recording, CancellationToken token)
    {
        if (!Engine.IsModelInstalled)
        {
            _ = Engine.EnsureModelAsync();
            throw new FlowException(Engine.IsDownloading
                ? $"Speech model downloading {Math.Max(0, Engine.DownloadProgress) * 100:0}%"
                : "Speech model missing; see Settings");
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        return await Task.Run(() => Engine.TranscribeAsync(recording.Samples, Vocabulary(), limit.Token, _settings.FlowExpressive), limit.Token).ConfigureAwait(false);
    }

    private string? _lastPasted;

    /// <summary>Raised (UI thread) when words you corrected were added to the dictionary.</summary>
    public event EventHandler<IReadOnlyList<string>>? VocabularyLearned;

    private async Task<string> CleanAsync(string raw, CancellationToken token)
    {
        _lastPasted = null;
        var text = await DictationTextAsync(raw, token).ConfigureAwait(false);
        _lastPasted = text;
        return text;
    }

    private async Task<string> DictationTextAsync(string raw, CancellationToken token)
    {
        var app = _targetApp;
        var inEditor = _settings.FlowCodeVariables && FlowCodeText.IsCodeEditor(app);
        var text = await CleanTextAsync(raw, inEditor ? app : null, token).ConfigureAwait(false);
        if (inEditor) text = FlowCodeText.ApplySpokenCasing(text);
        if (_settings.FlowFileTagging && FlowCodeText.TakesFileTags(app)) text = FlowCodeText.TagFiles(text);
        return text;
    }

    private async Task<string> CleanTextAsync(string raw, string? codeApp, CancellationToken token)
    {
        if (_settings.FlowCleanup == FlowCleanupMode.Off) return raw;
        var text = FlowTextCleaner.Basic(raw, _settings.FlowExpressive, _settings.FlowSmartFormatting);
        if (_settings.FlowCleanup != FlowCleanupMode.Ai) return text;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(AiTimeout);
        try { return await _ai.CleanAsync(text, Vocabulary(), limit.Token, codeApp).ConfigureAwait(false) ?? text; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return text; } // AI too slow: keep Basic
    }

    /// <summary>Runs on the hook's event worker, not inside the hook callback.</summary>
    private void CaptureTargetApp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = pid == 0 ? null : System.Diagnostics.Process.GetProcessById((int)pid);
            _targetApp = process?.ProcessName;
        }
        catch { _targetApp = null; }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    /// <summary>Show "Dictating with Sutra Flow" on the island while listening.</summary>
    public bool CreatorMode => _settings.FlowCreatorMode;

    /// <summary>True while a dictation that couldn't be pasted is waiting for the user to copy it.</summary>
    public bool HasHeldText => _heldText is not null;
    /// <summary>Raised on the UI thread when held text appears or goes away.</summary>
    public event EventHandler? HeldTextChanged;

    private void Hold(string text) => _ui.BeginInvoke(() =>
    {
        _heldText = text;
        _heldTimer?.Stop();
        _heldTimer = new DispatcherTimer { Interval = HoldFor };
        _heldTimer.Tick += (_, _) => ClearHeld();
        _heldTimer.Start();
        HeldTextChanged?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Copies the held dictation because the user asked to (UI thread). Clears it afterwards.</summary>
    public bool CopyHeldText()
    {
        if (_heldText is not { } text) return false;
        var copied = _injector.CopyForUser(text);
        if (copied) ClearHeld();
        return copied;
    }

    private void ClearHeld()
    {
        _heldTimer?.Stop();
        if (_heldText is null) return;
        _heldText = null;
        HeldTextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPasted(int words)
    {
        _settings.FlowWordsDictated += words;
        _ = SaveSettingsQuietly();
        if (_settings.FlowAutoDictionary && _lastPasted is { } pasted && _injector.LastPastedElementId is { } id)
            _ = WatchForCorrectionsAsync(pasted, id, _injector.LastPastedWindow);
    }

    /// <summary>
    /// Auto-add to dictionary: reads the field right after the paste and again a little later. Words
    /// you retyped that closely resemble a pasted word (Cirk → Circ) are added to the vocabulary.
    /// Only fields that expose their text to UI Automation can be checked.
    /// </summary>
    private async Task WatchForCorrectionsAsync(string pasted, string expectedId, nint expectedWindow)
    {
        try
        {
            await Task.Delay(500).ConfigureAwait(false);
            var (id, before) = await Task.Run(() => FlowTextInjector.ReadFocusedText(expectedId, expectedWindow)).ConfigureAwait(false);
            if (id is null || string.IsNullOrEmpty(before) || before.Length > 20000) return;
            var first = FlowCorrections.Words(pasted).FirstOrDefault();
            if (first is null || !before.Contains(first, StringComparison.Ordinal)) return; // can't see our text in this field
            foreach (var wait in new[] { 10, 15 })
            {
                await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);
                if (!_settings.FlowAutoDictionary) return;
                var (nowId, after) = await Task.Run(() => FlowTextInjector.ReadFocusedText(expectedId, expectedWindow)).ConfigureAwait(false);
                if (nowId != id || after is null) return; // you moved on
                var learned = FlowCorrections.Find(pasted, before, after, _settings.FlowVocabulary);
                if (learned.Count == 0) continue;
                await _ui.InvokeAsync(() =>
                {
                    foreach (var w in learned) if (_settings.FlowVocabulary.Count < 300 && !_settings.FlowVocabulary.Contains(w, StringComparer.OrdinalIgnoreCase)) _settings.FlowVocabulary.Add(w);
                    VocabularyLearned?.Invoke(this, learned);
                });
                _log.Info($"Sutra Flow: added {learned.Count} corrected word(s) to the dictionary.");
                await SaveSettingsQuietly().ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex) { _log.Debug($"Sutra Flow: correction check skipped ({ex.Message})."); }
    }

    private async Task SaveSettingsQuietly()
    {
        try { await _settingsService.SaveAsync(_settings).ConfigureAwait(false); }
        catch (Exception ex) { _log.Debug($"Sutra Flow: settings save skipped ({ex.Message})."); }
    }

    private void Publish(FlowStatus status)
    {
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => Publish(status)); return; }
        if (Status == status) return;
        var previous = Status.State;
        Status = status;
        UpdateDucking(status.State);
        PlayCue(previous, status.State);
        _log.Debug($"Sutra Flow: {status.State} {status.Detail}");
        StatusChanged?.Invoke(this, status);
    }

    private void PlayCue(FlowState from, FlowState to)
    {
        if (!_settings.FlowSounds || from == to) return;
        if (to == FlowState.Listening) _sounds.PlayStart();
        else if (to == FlowState.Polishing && from is FlowState.Listening or FlowState.Released) _sounds.PlayStop(); // after the mic's tail, so it isn't recorded
        else if (to == FlowState.Error) _sounds.PlayError();
    }

    /// <summary>Music goes down when listening starts and comes back once the recording's tail is captured.</summary>
    /// <summary>
    /// Lowers other audio the moment the key goes down. Waiting for the island's Listening state
    /// (about 0.3 s later) let the microphone record full-volume music at the start of every take,
    /// which Whisper turned into stray words.
    /// </summary>
    private void DuckOnPress()
    {
        if (!_settings.FlowEnabled || !_settings.FlowDuckAudio || _pipeline.IsBusy) return;
        lock (_duckGate)
        {
            _flowWantsDuck = true;
            ApplyDucking();
        }
    }

    private void UpdateDucking(FlowState state)
    {
        lock (_duckGate)
        {
            _flowWantsDuck = (state is FlowState.Listening or FlowState.Released) && _flowWantsDuck
                || state == FlowState.Listening && _pipeline.IsRecording;
            ApplyDucking();
        }
    }

    /// <summary>
    /// Sutra Assistant's voice chat uses the same setting: other audio stays lowered while it
    /// listens, thinks, and speaks its reply, and comes back when the voice turn ends.
    /// </summary>
    public void SetAssistantVoiceActive(bool active)
    {
        lock (_duckGate)
        {
            _assistantWantsDuck = active;
            ApplyDucking();
        }
    }

    private readonly object _duckGate = new();
    private bool _flowWantsDuck;
    private bool _assistantWantsDuck;

    private void ApplyDucking()
    {
        var want = (_flowWantsDuck || _assistantWantsDuck) && (_ducking || _settings.FlowDuckAudio);
        if (want == _ducking) return;
        _ducking = want;
        if (want) _ducker.Duck(FlowDucking.GainFor(_settings.FlowDuckPercent));
        else _ducker.Restore();
    }

    /// <summary>Settings' Test button: lowers other audio for a moment, then puts it back. Returns the apps it lowered.</summary>
    public async Task<IReadOnlyList<string>?> TestDuckingAsync()
    {
        if (_ducking || Status.State != FlowState.Idle) return null; // a real dictation owns the ducker
        var lowered = await _ducker.DuckAsync(FlowDucking.GainFor(_settings.FlowDuckPercent)).ConfigureAwait(false);
        await Task.Delay(2500).ConfigureAwait(false);
        if (!_ducking) _ducker.Restore();
        return lowered;
    }

    public void Dispose()
    {
        _heldText = null;
        _ducker.Dispose();
        _pipeline.Dispose();
        _hook.Dispose();
        _recorder.Dispose();
        Engine.Dispose();
    }
}
