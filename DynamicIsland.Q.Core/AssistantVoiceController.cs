namespace DynamicIsland.Q.Core;

public enum AssistantVoiceState { Off, WakeListening, Listening, Thinking, Speaking, Error }
public sealed record AssistantVoiceSnapshot(AssistantVoiceState State, string Status, string? Error = null);
public interface IWakeWordInput
{
    event Action? WakeDetected;
    Task StartWakeAsync(CancellationToken cancellationToken);
    Task StopWakeAsync();
}
public interface ISpeechOutput
{
    Task SpeakAsync(string text, string voice, CancellationToken cancellationToken);
    void StopSpeaking();
}

/// <summary>Owns microphone transitions and voice-turn cancellation independently of the UI.</summary>
public sealed class AssistantVoiceController : IDisposable
{
    private readonly IWakeWordInput _wake;
    private readonly IQSpeechInputService _input;
    private readonly ISpeechOutput _output;
    private readonly Func<string, CancellationToken, Task<string>> _submit;
    private readonly Action _cancelResponse;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _turn;
    private bool _enabled, _disposed;
    private bool _readAloud = true;
    private string _voice = "";
    public AssistantVoiceSnapshot Snapshot { get; private set; } = new(AssistantVoiceState.Off, "Voice off");
    public event Action<AssistantVoiceSnapshot>? Changed;
    public AssistantVoiceController(IWakeWordInput wake, IQSpeechInputService input, ISpeechOutput output,
        Func<string, CancellationToken, Task<string>> submit, Action cancelResponse)
    { _wake = wake; _input = input; _output = output; _submit = submit; _cancelResponse = cancelResponse; _wake.WakeDetected += OnWake; }
    private void OnWake() { if (_enabled && Snapshot.State == AssistantVoiceState.WakeListening) _ = Task.Run(ListenAsync); }
    public async Task ConfigureAsync(bool enabled, bool readAloud, string voice)
    {
        if (_disposed) return;
        _readAloud = readAloud; _voice = voice;
        if (_enabled == enabled && (enabled ? Snapshot.State != AssistantVoiceState.Off : _turn is null)) return;
        _enabled = enabled;
        if (!enabled) { Stop(); await _wake.StopWakeAsync(); Publish(AssistantVoiceState.Off, "Voice off"); }
        else await ResumeWakeAsync();
    }
    public async Task ListenAsync()
    {
        if (_disposed || !await _turnGate.WaitAsync(0)) return;
        var turn = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _turn = turn;
        try
        {
            await _wake.StopWakeAsync();
            _output.StopSpeaking();
            turn.Token.ThrowIfCancellationRequested();
            if (!_input.IsAvailable) throw new InvalidOperationException("Install an English Windows speech recognizer and allow microphone access. Typed chat remains available.");
            Publish(AssistantVoiceState.Listening, "Listening for your question…");
            string? text;
            using (var dictation = CancellationTokenSource.CreateLinkedTokenSource(turn.Token))
            {
                dictation.CancelAfter(TimeSpan.FromSeconds(30));
                text = await _input.DictateAsync(dictation.Token);
            }
            turn.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return;
            Publish(AssistantVoiceState.Thinking, "Sutra is responding…");
            var answer = await _submit(text.Trim(), turn.Token);
            turn.Token.ThrowIfCancellationRequested();
            if (_readAloud && !string.IsNullOrWhiteSpace(answer))
            {
                Publish(AssistantVoiceState.Speaking, "Speaking…");
                await _output.SpeakAsync(answer, _voice, turn.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Publish(AssistantVoiceState.Error, "Voice unavailable — typed chat still works", ex.Message); }
        finally
        {
            if (ReferenceEquals(_turn, turn)) _turn = null;
            turn.Dispose(); _turnGate.Release();
            if (!_disposed && Snapshot.State != AssistantVoiceState.Error) await ResumeWakeAsync();
        }
    }
    public async Task ReadAsync(string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text) || !await _turnGate.WaitAsync(0)) return;
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _turn = turn;
        try
        {
            await _wake.StopWakeAsync();
            turn.Token.ThrowIfCancellationRequested();
            Publish(AssistantVoiceState.Speaking, "Speaking…");
            await _output.SpeakAsync(text, _voice, turn.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Publish(AssistantVoiceState.Error, "Could not read reply", ex.Message); }
        finally { _turn = null; _turnGate.Release(); if (!_disposed && Snapshot.State != AssistantVoiceState.Error) await ResumeWakeAsync(); }
    }
    public void Stop()
    {
        _turn?.Cancel(); _output.StopSpeaking(); _cancelResponse();
        // Closing a chat also calls Stop. An idle wake recognizer still owns the
        // microphone, so retain its state or OnWake would ignore every later hit.
        // Active turns rearm wake listening in their finally block instead.
        if (!_disposed && !(_enabled && _turn is null && Snapshot.State == AssistantVoiceState.WakeListening))
            Publish(AssistantVoiceState.Off, "Stopped");
    }
    private async Task ResumeWakeAsync()
    {
        if (_disposed) return;
        if (!_enabled) { Publish(AssistantVoiceState.Off, "Voice ready"); return; }
        try { await _wake.StartWakeAsync(_lifetime.Token); if (_enabled && !_disposed) Publish(AssistantVoiceState.WakeListening, "Say “Hey Sutra”"); else await _wake.StopWakeAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Publish(AssistantVoiceState.Error, "Wake listening unavailable", ex.Message); }
    }
    private void Publish(AssistantVoiceState state, string status, string? error = null)
    { Snapshot = new(state, status, error); Changed?.Invoke(Snapshot); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _enabled = false; _wake.WakeDetected -= OnWake;
        _lifetime.Cancel(); _turn?.Cancel(); _output.StopSpeaking(); _cancelResponse();
        _ = _wake.StopWakeAsync();
    }
}
