using System.Windows.Threading;

namespace DynamicIsland.Windows.Services;

public sealed class ClockService : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private int _pollIntervalSeconds = 1;
    public event EventHandler<DateTimeOffset>? Tick;

    public void ConfigurePolling(int intervalSeconds)
    {
        var normalized = Math.Clamp(intervalSeconds, 1, 60);
        if (_pollIntervalSeconds == normalized) return;
        _pollIntervalSeconds = normalized;
        ScheduleNext();
    }

    public ClockService()
    {
        _timer.Tick += OnTick;
        ScheduleNext();
    }

    public void Start()
    {
        Tick?.Invoke(this, DateTimeOffset.Now);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Tick?.Invoke(this, DateTimeOffset.Now);
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        var now = DateTimeOffset.Now;
        var secondsToBoundary = _pollIntervalSeconds - now.Second % _pollIntervalSeconds;
        var milliseconds = secondsToBoundary * 1000 - now.Millisecond;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, milliseconds));
    }

    public void Dispose() => _timer.Stop();
}
