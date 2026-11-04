namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// The dictation lifecycle, free of Windows types so it can be unit-tested:
/// press → record → release → transcribe → clean → paste → settle.
///
/// Exactly one dictation exists at a time. <see cref="Release"/> marks the session as processing
/// before it returns, so a new press can't start until the previous one has fully finished.
/// Every session carries its own cancellation token; turning Flow off or disposing cancels it,
/// and each stage checks it (and that Flow is still enabled) before doing more work.
/// </summary>
public sealed class FlowPipeline : IDisposable
{
    public sealed record Timing(TimeSpan MinHold, TimeSpan ReleaseBeat, TimeSpan PastedSettle, TimeSpan ErrorSettle)
    {
        public static Timing Default { get; } = new(
            TimeSpan.FromMilliseconds(280), TimeSpan.FromMilliseconds(260),
            TimeSpan.FromMilliseconds(1100), TimeSpan.FromMilliseconds(2000));
    }

    public delegate Task<string> Transcriber(FlowRecording recording, CancellationToken token);
    public delegate Task<string> Cleaner(string raw, CancellationToken token);

    private enum Phase { Idle, Recording, Processing }

    private readonly IFlowAudio _audio;
    private readonly IFlowPaster _paster;
    private readonly Func<bool> _enabled;
    private readonly Transcriber _transcribe;
    private readonly Cleaner _clean;
    private readonly Action<FlowStatus> _publish;
    private readonly Action<int> _onPasted;
    private readonly Action<string> _onUndelivered;
    private readonly Action<string, Exception> _logError;
    private readonly Timing _timing;
    private readonly object _gate = new();

    private Phase _phase;
    private CancellationTokenSource? _session;
    private FlowTarget? _target;
    private DateTime _pressedAt;
    private int _generation;
    private bool _disposed;

    public FlowPipeline(IFlowAudio audio, IFlowPaster paster, Func<bool> enabled, Transcriber transcribe, Cleaner clean,
        Action<FlowStatus> publish, Action<int> onPasted, Action<string> onUndelivered, Action<string, Exception> logError, Timing? timing = null)
    {
        _audio = audio;
        _paster = paster;
        _enabled = enabled;
        _transcribe = transcribe;
        _clean = clean;
        _publish = publish;
        _onPasted = onPasted;
        _onUndelivered = onUndelivered;
        _logError = logError;
        _timing = timing ?? Timing.Default;
    }

    public bool IsRecording { get { lock (_gate) return _phase == Phase.Recording; } }
    public bool IsBusy { get { lock (_gate) return _phase != Phase.Idle; } }
    public float Level => IsRecording ? _audio.Level : 0f;

    /// <summary>The push-to-talk key went down.</summary>
    public void Press()
    {
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _phase != Phase.Idle || !_enabled()) return;
            _target = _paster.CaptureTarget();
            _pressedAt = DateTime.UtcNow;
            try { _audio.Start(); }
            catch (FlowException ex)
            {
                generation = ++_generation;
                Fail(ex.Message, generation);
                return;
            }
            _phase = Phase.Recording;
            _session = new CancellationTokenSource();
            token = _session.Token;
            generation = ++_generation;
        }
        // A quick tap of the key shows nothing; only a real hold morphs the island.
        _ = ShowListeningAfterHold(generation, token);
    }

    private async Task ShowListeningAfterHold(int generation, CancellationToken token)
    {
        try { await Task.Delay(_timing.MinHold, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        lock (_gate) { if (_phase != Phase.Recording || generation != _generation) return; }
        _publish(new FlowStatus(FlowState.Listening));
    }

    /// <summary>Another key was pressed during the hold: it was a shortcut, so drop the recording.</summary>
    public Task Cancel()
    {
        CancellationTokenSource? session;
        lock (_gate)
        {
            if (_phase != Phase.Recording) return Task.CompletedTask;
            _phase = Phase.Processing;
            session = _session;
            _generation++;
        }
        TryCancel(session);
        return DiscardAsync();
    }

    private async Task DiscardAsync()
    {
        try { await _audio.StopAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logError("Sutra Flow: stopping the microphone failed.", ex); }
        finally { Finish(); }
        _publish(new FlowStatus(FlowState.Idle));
    }

    /// <summary>The push-to-talk key came up. The returned task completes when the dictation is fully done.</summary>
    public Task Release()
    {
        CancellationToken token;
        FlowTarget? target;
        TimeSpan held;
        int generation;
        CancellationTokenSource? tap = null;
        lock (_gate)
        {
            if (_phase != Phase.Recording || _session is null) return Task.CompletedTask;
            _phase = Phase.Processing; // before any await: a new press is refused from here on
            token = _session.Token;
            target = _target;
            held = DateTime.UtcNow - _pressedAt;
            generation = ++_generation;
            if (held < _timing.MinHold)
            {
                // A quick tap (or the first tap of a double-tap for command mode): drop it now so a
                // second press a moment later isn't refused while the microphone winds down.
                _phase = Phase.Idle;
                tap = _session;
                _session = null;
                try { _audio.Abort(); } catch (Exception ex) { _logError("Sutra Flow: dropping a tap failed.", ex); }
            }
        }
        if (tap is not null || held < _timing.MinHold)
        {
            TryCancel(tap);
            tap?.Dispose();
            _publish(new FlowStatus(FlowState.Idle));
            return Task.CompletedTask;
        }
        if (target is not null)
        {
            try { _paster.PinTarget(target); }
            catch (Exception ex) { _logError("Sutra Flow: could not record the focused field.", ex); }
        }
        return ProcessAsync(token, target, held, generation);
    }

    /// <summary>Flow was turned off: stop recording and abandon any dictation in progress.</summary>
    public void Disable()
    {
        CancellationTokenSource? session;
        bool recording;
        lock (_gate)
        {
            session = _session;
            recording = _phase == Phase.Recording;
        }
        if (recording) { _ = Cancel(); return; }
        TryCancel(session);
    }

    private async Task ProcessAsync(CancellationToken token, FlowTarget? target, TimeSpan held, int generation)
    {
        TimeSpan? settle = null;
        void Fail(string reason) => settle = Report(new FlowStatus(FlowState.Error, reason), _timing.ErrorSettle);
        try
        {
            var recording = await _audio.StopAsync().ConfigureAwait(false);
            if (held < _timing.MinHold) { _publish(new FlowStatus(FlowState.Idle)); return; }
            if (recording.Seconds < 0.35 || recording.Peak < 0.015) { Fail("Didn't hear anything"); return; }

            _publish(new FlowStatus(FlowState.Released));
            await Task.Delay(_timing.ReleaseBeat, token).ConfigureAwait(false);
            CheckStillWanted(token);
            _publish(new FlowStatus(FlowState.Polishing));

            var raw = (await _transcribe(recording, token).ConfigureAwait(false) ?? "").Trim();
            CheckStillWanted(token);
            if (raw.Length == 0 || FlowTextCleaner.IsLikelyHallucination(raw, recording.VoicedSeconds))
                throw new FlowException("Didn't catch that");

            var text = (await _clean(raw, token).ConfigureAwait(false) ?? "").Trim();
            CheckStillWanted(token);
            if (text.Length == 0) throw new FlowException("Didn't catch that");

            var result = await _paster.PasteAsync(text, target, token).ConfigureAwait(false);
            var outcome = result.Outcome;
            // Text that wasn't delivered is handed back (not put on the clipboard) so the user can
            // choose to copy it. After a partial typing, only the part that didn't make it is held,
            // so copying it never duplicates what is already in the field.
            if (outcome == FlowPasteOutcome.PartlyTyped) { if (!string.IsNullOrEmpty(result.Remaining)) _onUndelivered(result.Remaining); }
            else if (outcome != FlowPasteOutcome.Pasted) _onUndelivered(text);
            switch (outcome)
            {
                case FlowPasteOutcome.Pasted:
                    var words = FlowTextCleaner.WordCount(text);
                    _onPasted(words);
                    settle = Report(new FlowStatus(FlowState.Pasted, $"{words} words", words), _timing.PastedSettle);
                    break;
                case FlowPasteOutcome.TargetChanged:
                    Fail("Focus moved, so nothing was pasted"); break;
                case FlowPasteOutcome.Elevated:
                    Fail("Can't paste into an admin window"); break;
                case FlowPasteOutcome.Blocked:
                    Fail("Windows blocked the paste"); break;
                case FlowPasteOutcome.PartlyTyped:
                    Fail("Windows stopped the typing partway; only the rest is held"); break;
                case FlowPasteOutcome.MultilineNeedsClipboard:
                    Fail("Your clipboard couldn't be saved, so multi-line text wasn't typed"); break;
                default:
                    Fail("Couldn't paste; the clipboard is busy"); break;
            }
        }
        catch (OperationCanceledException)
        {
            _publish(new FlowStatus(FlowState.Idle));
        }
        catch (FlowException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logError("Sutra Flow: dictation failed.", ex);
            Fail("Something went wrong");
        }
        finally
        {
            Finish();
            // Settle only once the session is idle, so the check can't be skipped by a still-busy flag.
            if (settle is { } delay) _ = SettleAsync(delay, generation);
        }
    }

    private void CheckStillWanted(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_enabled()) throw new OperationCanceledException(token);
    }

    private void Finish()
    {
        CancellationTokenSource? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
            _phase = Phase.Idle;
        }
        session?.Dispose();
    }

    private TimeSpan Report(FlowStatus status, TimeSpan settle)
    {
        _publish(status);
        return settle;
    }

    private void Fail(string reason, int generation)
    {
        _publish(new FlowStatus(FlowState.Error, reason));
        _ = SettleAsync(_timing.ErrorSettle, generation);
    }

    /// <summary>Returns the island to normal after a result, unless a newer dictation has started.</summary>
    private async Task SettleAsync(TimeSpan delay, int generation)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        lock (_gate) { if (_disposed || _phase != Phase.Idle || generation != _generation) return; }
        _publish(new FlowStatus(FlowState.Idle));
    }

    public void Dispose()
    {
        CancellationTokenSource? session;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            session = _session;
        }
        TryCancel(session);
    }

    private static void TryCancel(CancellationTokenSource? session)
    {
        try { session?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
