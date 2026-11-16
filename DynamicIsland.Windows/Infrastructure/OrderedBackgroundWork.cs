namespace DynamicIsland.Windows.Infrastructure;

/// <summary>Runs ordered work on the pool even when the previous operation has finished.</summary>
internal sealed class OrderedBackgroundWork
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Func<Task> work)
    {
        lock (_gate)
            return _tail = _tail.ContinueWith(_ => work(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    public Task FlushAsync() { lock (_gate) return _tail; }
}
