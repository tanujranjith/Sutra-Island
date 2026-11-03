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
    private static readonly TimeSpan MinHold = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(8);

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _ui;
    private readonly LoggingService _log;
    private readonly FlowHotkeyHook _hook;
    private readonly FlowRecorder _recorder;
    private readonly FlowTextInjector _injector;
    private readonly FlowAiCleaner _ai;
    private readonly object _gate = new();
    private DateTime _pressedAt;
    private bool _recording;
    private int _generation;
    private DispatcherTimer? _settleTimer;

    public WhisperEngine Engine { get; }
    public FlowStatus Status { get; private set; } = new(FlowState.Idle);
    /// <summary>Raised on the UI thread whenever <see cref="Status"/> changes.</summary>
    public event EventHandler<FlowStatus>? StatusChanged;
    /// <summary>Current microphone loudness, 0..1 (any thread).</summary>
    public float Level => _recording ? _recorder.Level : 0f;

    public FlowController(AppSettings settings, SettingsService settingsService, Dispatcher ui, LoggingService log,
        DynamicIsland.Q.Core.IQProviderRegistry providers, Q.IQSecretStore secrets)
    {
        _settings = settings;
        _settingsService = settingsService;
        _ui = ui;
        _log = log;
        Engine = new WhisperEngine(log);
        _hook = new FlowHotkeyHook(log);
        _recorder = new FlowRecorder(log);
        _injector = new FlowTextInjector(log);
        _ai = new FlowAiCleaner(settings, providers, secrets, log);
        _hook.Pressed += OnPressed;
        _hook.Released += OnReleased;
        _hook.Cancelled += OnCancelled;
    }

    public string? AiUnavailableReason => _ai.Unavailable();

    public void Start()
    {
        ApplySettings();
        _hook.Start();
    }

    /// <summary>Call after Flow settings change.</summary>
    public void ApplySettings()
    {
        _hook.Configure(_settings.FlowEnabled);
        if (_settings.FlowEnabled) _ = Engine.EnsureModelAsync();
    }

    private void OnPressed()
    {
        if (!_settings.FlowEnabled) return;
        lock (_gate)
        {
            if (_recording || Status.State is FlowState.Polishing) return;
            _pressedAt = DateTime.UtcNow;
            try
            {
                _recorder.Start();
                _recording = true;
            }
            catch (FlowException ex)
            {
                Publish(new FlowStatus(FlowState.Error, ex.Message));
                SettleLater(1800);
                return;
            }
        }
        // Don't show anything for a quick tap of Ctrl; only a real hold morphs the island.
        var generation = ++_generation;
        _ = Task.Delay(MinHold).ContinueWith(_ =>
        {
            lock (_gate) { if (!_recording || generation != _generation) return; }
            Publish(new FlowStatus(FlowState.Listening));
        }, TaskScheduler.Default);
    }

    private void OnCancelled()
    {
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
            _generation++;
        }
        _ = _recorder.StopAsync();
        Publish(new FlowStatus(FlowState.Idle));
    }

    private async void OnReleased()
    {
        FlowRecording recording;
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
            _generation++;
        }
        var held = DateTime.UtcNow - _pressedAt;
        recording = await _recorder.StopAsync().ConfigureAwait(false);
        if (held < MinHold) { Publish(new FlowStatus(FlowState.Idle)); return; }

        if (recording.Seconds < 0.35 || recording.Peak < 0.015)
        {
            Publish(new FlowStatus(FlowState.Error, "Didn't hear anything"));
            SettleLater(1400);
            return;
        }

        Publish(new FlowStatus(FlowState.Released));
        await Task.Delay(260).ConfigureAwait(false);
        Publish(new FlowStatus(FlowState.Polishing));
        try
        {
            if (!Engine.IsModelInstalled)
            {
                _ = Engine.EnsureModelAsync();
                throw new FlowException(Engine.IsDownloading
                    ? $"Speech model downloading {Math.Max(0, Engine.DownloadProgress) * 100:0}%"
                    : "Speech model missing; see Settings");
            }
            var vocabulary = _settings.FlowVocabulary.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var raw = await Task.Run(() => Engine.TranscribeAsync(recording.Samples, vocabulary, cts.Token), cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw)) throw new FlowException("Didn't catch that");

            var text = _settings.FlowCleanup == FlowCleanupMode.Off ? raw.Trim() : FlowTextCleaner.Basic(raw);
            if (_settings.FlowCleanup == FlowCleanupMode.Ai)
            {
                using var aiCts = new CancellationTokenSource(AiTimeout);
                text = await _ai.CleanAsync(text, vocabulary, aiCts.Token).ConfigureAwait(false) ?? text;
            }
            if (string.IsNullOrWhiteSpace(text)) throw new FlowException("Didn't catch that");

            var pasted = await _ui.InvokeAsync(() => _injector.PasteAsync(text)).Task.Unwrap().ConfigureAwait(false);
            if (!pasted) throw new FlowException("Couldn't paste; text is on the clipboard");
            var words = FlowTextCleaner.WordCount(text);
            _settings.FlowWordsDictated += words;
            _ = SaveSettingsQuietly();
            Publish(new FlowStatus(FlowState.Pasted, $"{words} words", words));
            SettleLater(1100);
        }
        catch (FlowException ex)
        {
            Publish(new FlowStatus(FlowState.Error, ex.Message));
            SettleLater(2000);
        }
        catch (Exception ex)
        {
            _log.Error("Sutra Flow: dictation failed.", ex);
            Publish(new FlowStatus(FlowState.Error, "Something went wrong"));
            SettleLater(2000);
        }
    }

    private async Task SaveSettingsQuietly()
    {
        try { await _settingsService.SaveAsync(_settings).ConfigureAwait(false); }
        catch (Exception ex) { _log.Debug($"Sutra Flow: settings save skipped ({ex.Message})."); }
    }

    private void SettleLater(int ms)
    {
        _ui.BeginInvoke(() =>
        {
            _settleTimer?.Stop();
            _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            _settleTimer.Tick += (_, _) =>
            {
                _settleTimer?.Stop();
                if (!_recording && Status.State is FlowState.Pasted or FlowState.Error) Publish(new FlowStatus(FlowState.Idle));
            };
            _settleTimer.Start();
        });
    }

    private void Publish(FlowStatus status)
    {
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => Publish(status)); return; }
        if (status.State is FlowState.Listening) _settleTimer?.Stop();
        if (Status == status) return;
        Status = status;
        _log.Debug($"Sutra Flow: {status.State} {status.Detail}");
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _hook.Dispose();
        _recorder.Dispose();
        Engine.Dispose();
    }
}
