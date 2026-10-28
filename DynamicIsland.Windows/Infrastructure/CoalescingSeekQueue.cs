using System.Diagnostics;

namespace DynamicIsland.Windows.Infrastructure;

public sealed record SeekContext(object? Source, string Track, TimeSpan Position, TimeSpan Duration, bool CanSeek)
{
    public bool SameTrack(SeekContext other) => ReferenceEquals(Source, other.Source) && Track == other.Track;
}

/// <summary>Combine short bursts into one position write, retaining every relative step.
/// Only one write can be in flight. Queued clicks remain attached to their original track.</summary>
public sealed class CoalescingSeekQueue(
    Func<SeekContext> read,
    Func<SeekContext, TimeSpan, CancellationToken, Task<bool>> write,
    CancellationToken shutdown = default,
    TimeSpan? quietPeriod = null)
{
    private sealed record Request(SeekContext Context, TimeSpan? Offset, double? Fraction,
        CancellationToken Token, TaskCompletionSource<bool> Completion);
    private readonly object _gate = new();
    private readonly List<Request> _requests = [];
    private readonly TimeSpan _quietPeriod = quietPeriod ?? TimeSpan.FromMilliseconds(120);
    private bool _running;
    private long _lastQueued;

    public Task<bool> RelativeAsync(TimeSpan offset, CancellationToken token = default) => Enqueue(offset, null, token);
    public Task<bool> FractionAsync(double fraction, CancellationToken token = default) => Enqueue(null, fraction, token);

    private Task<bool> Enqueue(TimeSpan? offset, double? fraction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); shutdown.ThrowIfCancellationRequested();
        if (fraction is { } value && !double.IsFinite(value)) return Task.FromResult(false);
        var context = read();
        if (context.Source is null || !context.CanSeek || context.Duration <= TimeSpan.Zero) return Task.FromResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _requests.Add(new(context, offset, fraction, token, completion));
            _lastQueued = Stopwatch.GetTimestamp();
            if (!_running) { _running = true; _ = PumpAsync(); }
        }
        return completion.Task.WaitAsync(token);
    }

    private async Task PumpAsync()
    {
        Request[] batch = [];
        try
        {
            while (true)
            {
                // Wait for a brief gap, with a cap so a held button still makes progress.
                var started = Stopwatch.GetTimestamp();
                do
                {
                    await Task.Delay(_quietPeriod, shutdown);
                    lock (_gate)
                        if (Stopwatch.GetElapsedTime(_lastQueued) >= _quietPeriod || Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(300)) break;
                } while (true);
                lock (_gate) { batch = _requests.ToArray(); _requests.Clear(); }
                var context = read();
                var active = new List<Request>();
                var target = context.Position;
                foreach (var request in batch)
                {
                    if (request.Token.IsCancellationRequested) { request.Completion.TrySetCanceled(request.Token); continue; }
                    if (!context.CanSeek || context.Duration <= TimeSpan.Zero || !context.SameTrack(request.Context))
                    { request.Completion.TrySetResult(false); continue; }
                    active.Add(request);
                    target = request.Offset is { } offset ? SeekMath.ClampSeek(target, offset, context.Duration)
                        : TimeSpan.FromTicks((long)(Math.Clamp(request.Fraction!.Value, 0, 1) * context.Duration.Ticks));
                }
                if (active.Count > 0)
                {
                    try
                    {
                        var accepted = await write(context, target, shutdown);
                        foreach (var request in active) request.Completion.TrySetResult(accepted);
                    }
                    catch (Exception ex)
                    {
                        foreach (var request in active) request.Completion.TrySetException(ex);
                    }
                }
                batch = [];
                lock (_gate) { if (_requests.Count == 0) { _running = false; return; } }
            }
        }
        catch (Exception ex)
        {
            Request[] pending;
            lock (_gate) { pending = _requests.ToArray(); _requests.Clear(); _running = false; }
            foreach (var request in batch.Concat(pending))
            {
                if (ex is OperationCanceledException) request.Completion.TrySetCanceled(shutdown);
                else request.Completion.TrySetException(ex);
            }
        }
    }
}
