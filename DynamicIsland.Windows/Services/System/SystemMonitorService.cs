using System.Net.NetworkInformation;
using DynamicIsland.Windows.Interop;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Lightweight system stats. CPU is computed as Task Manager does: raw busy% (from GetSystemTimes,
/// time-averaged so it can't spike) scaled by the real current/base frequency ratio (CallNtPowerInformation).
/// On a downclocked-idle CPU that scaling is what makes "busy 23% of the time" read as ~6% utility.
/// RAM = GlobalMemoryStatusEx; network = sum of physical adapter throughput.
/// </summary>
public sealed class SystemMonitorService : IDisposable
{
    private readonly object _lifecycleLock = new();
    private readonly int _cores = Math.Max(1, Environment.ProcessorCount);
    private readonly NativeMethods.ProcessorPowerInformation[] _powerInfo;
    private CancellationTokenSource? _shutdown;
    private Thread? _thread;
    private int _pollIntervalMilliseconds = 700;
    private long _lastIdle, _lastKernel, _lastUser;
    private long _lastBytes;
    private long _lastTicks;
    private long _networkInterfacesRefreshedAt;
    private NetworkInterface[] _networkInterfaces = [];
    private double _cpuEma = -1;

    public SystemMonitorService()
    {
        _powerInfo = new NativeMethods.ProcessorPowerInformation[_cores];
    }

    public event EventHandler<SystemStats>? Changed;
    public SystemStats Current { get; private set; } = SystemStats.Empty;

    public void ConfigurePolling(int intervalMilliseconds) =>
        Interlocked.Exchange(ref _pollIntervalMilliseconds, Math.Clamp(intervalMilliseconds, 200, 5000));

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_thread is not null) return;
            var shutdown = new CancellationTokenSource();
            _shutdown = shutdown;
            _thread = new Thread(() => Loop(shutdown.Token))
            {
                IsBackground = true,
                Name = "DynamicIsland.SysMon"
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        CancellationTokenSource? shutdown;
        lock (_lifecycleLock)
        {
            thread = _thread;
            shutdown = _shutdown;
            if (thread is null || shutdown is null) return;
            _thread = null;
            _shutdown = null;
            shutdown.Cancel();
        }
        if (thread.Join(TimeSpan.FromSeconds(2))) shutdown.Dispose();
    }

    private void Loop(CancellationToken token)
    {
        Prime();
        var sincePublish = 0;
        while (!token.IsCancellationRequested)
        {
            token.WaitHandle.WaitOne(Volatile.Read(ref _pollIntervalMilliseconds));
            if (token.IsCancellationRequested) break;
            var next = Read();
            if (++sincePublish >= 2)
            {
                sincePublish = 0;
                if (next != Current) { Current = next; Changed?.Invoke(this, next); }
            }
        }
    }

    private void Prime()
    {
        NativeMethods.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
        _lastBytes = TotalBytes();
        _lastTicks = Environment.TickCount64;
    }

    private SystemStats Read()
    {
        int cpu = (int)Math.Round(Math.Max(0, _cpuEma));
        int ram = 0;
        try
        {
            if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var idleDelta = idle - _lastIdle;
                var totalDelta = (kernel - _lastKernel) + (user - _lastUser); // kernel already includes idle
                _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
                if (totalDelta > 0)
                {
                    var busy = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
                    var raw = Math.Clamp(busy * FrequencyRatio(), 0, 100); // -> Task Manager's "utility"
                    _cpuEma = _cpuEma < 0 ? raw : _cpuEma * 0.8 + raw * 0.2; // steady like Task Manager
                    cpu = (int)Math.Round(_cpuEma);
                }
            }

            var mem = new NativeMethods.MemoryStatusEx
            { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref mem)) ram = (int)mem.MemoryLoad;
        }
        catch { }

        var net = "—";
        double netPerSec = 0;
        try
        {
            var now = Environment.TickCount64;
            var bytes = TotalBytes();
            var seconds = Math.Max(0.001, (now - _lastTicks) / 1000.0);
            var perSec = Math.Max(0, (bytes - _lastBytes) / seconds);
            _lastBytes = bytes; _lastTicks = now;
            netPerSec = perSec;
            net = FormatRate(perSec);
        }
        catch { }

        return new SystemStats(cpu, ram, net, netPerSec);
    }

    // Average current/base frequency ratio across cores. >1 under turbo, <1 when downclocked (idle).
    private double FrequencyRatio()
    {
        try
        {
            var size = (uint)(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>() * _cores);
            if (NativeMethods.CallNtPowerInformation(11, nint.Zero, 0, _powerInfo, size) != 0) return 1.0;
            double cur = 0, max = 0;
            foreach (var p in _powerInfo) { cur += p.CurrentMhz; max += p.MaxMhz; }
            if (max <= 0) return 1.0;
            return Math.Clamp(cur / max, 0.05, 3.0);
        }
        catch { return 1.0; }
    }

    private static readonly string[] VirtualMarkers =
        ["virtual", "hyper-v", "vmware", "vethernet", "pseudo", "loopback", "tap", "tunnel", "bluetooth", "wan miniport"];

    private long TotalBytes()
    {
        var now = Environment.TickCount64;
        if (_networkInterfaces.Length == 0 || now - _networkInterfacesRefreshedAt >= 60_000)
        {
            _networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsPhysicalDataInterface)
                .ToArray();
            _networkInterfacesRefreshedAt = now;
        }

        long total = 0;
        foreach (var nic in _networkInterfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var s = nic.GetIPv4Statistics();
            total += s.BytesReceived + s.BytesSent;
        }
        return total;
    }

    private static bool IsPhysicalDataInterface(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet)) return false;
        return !VirtualMarkers.Any(marker =>
            nic.Description.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
            nic.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):0.0} MB/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec:0} B/s";
    }

    public void Dispose()
    {
        Stop();
    }
}
