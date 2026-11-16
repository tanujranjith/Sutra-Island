using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Windows.Services;

/// <summary>Observes Media Foundation streaming reports without opening or reading the camera.</summary>
internal sealed class WindowsCameraActivityMonitor(Action<IReadOnlyList<string>> changed, Action<string> log) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string[]> _devices = new(StringComparer.OrdinalIgnoreCase);
    private ActivityCallback? _callback;
    private ISensorActivityMonitor? _monitor;
    private bool _mediaStarted;
    private volatile bool _disposed;
    private string[] _apps = [];
    public IReadOnlyList<string> CurrentApps => Volatile.Read(ref _apps);

    public bool Start()
    {
        if (_disposed) return false;
        try
        {
            Marshal.ThrowExceptionForHR(MFStartup(0x20070, 1));
            _mediaStarted = true;
            _callback = new ActivityCallback(this);
            Marshal.ThrowExceptionForHR(MFCreateSensorActivityMonitor(_callback, out _monitor));
            Marshal.ThrowExceptionForHR(_monitor.Start());
            log("Live camera activity monitor started.");
            return true;
        }
        catch (Exception ex)
        {
            log($"Live camera activity monitor unavailable: {ex.GetType().Name} (0x{ex.HResult:X8}); using camera usage history.");
            Dispose();
            return false;
        }
    }

    private void OnReport(ISensorActivitiesReport reports)
    {
        if (_disposed) return;
        try
        {
            Marshal.ThrowExceptionForHR(reports.GetCount(out var count));
            if (count == 0) return; // Empty notifications contain no device update.
            var updates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(reports.GetActivityReport(i, out var report));
                try
                {
                    var link = new StringBuilder(4096);
                    Marshal.ThrowExceptionForHR(report.GetSymbolicLink(link, (uint)link.Capacity, out _));
                    Marshal.ThrowExceptionForHR(report.GetProcessCount(out var processCount));
                    var apps = new List<string>();
                    for (uint j = 0; j < processCount; j++)
                    {
                        Marshal.ThrowExceptionForHR(report.GetProcessActivity(j, out var activity));
                        try
                        {
                            Marshal.ThrowExceptionForHR(activity.GetStreamingState(out var streaming));
                            if (!streaming) continue;
                            Marshal.ThrowExceptionForHR(activity.GetProcessId(out var pid));
                            apps.Add(ProcessName(pid));
                        }
                        finally { Marshal.ReleaseComObject(activity); }
                    }
                    updates[link.ToString()] = apps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                finally { Marshal.ReleaseComObject(report); }
            }
            string[] next;
            lock (_gate)
            {
                if (_disposed) return;
                foreach (var (device, apps) in updates)
                    if (apps.Length == 0) _devices.Remove(device); else _devices[device] = apps;
                next = _devices.Values.SelectMany(apps => apps).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(app => app, StringComparer.OrdinalIgnoreCase).ToArray();
                if (_apps.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return;
                Volatile.Write(ref _apps, next);
            }
            log($"Live camera activity changed: streamingApps={next.Length}.");
            changed(next);
        }
        catch (Exception ex) { log($"Live camera report could not be read: {ex.GetType().Name} (0x{ex.HResult:X8})."); }
    }
    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            if (process.ProcessName.Equals("WindowsCamera", StringComparison.OrdinalIgnoreCase)) return "Camera";
            try
            {
                var info = process.MainModule?.FileVersionInfo;
                if (!string.IsNullOrWhiteSpace(info?.ProductName)) return info.ProductName.Trim();
            }
            catch (Exception) { }
            return process.ProcessName;
        }
        catch (Exception) { return "Unknown app"; }
    }
    public void Dispose()
    {
        _disposed = true;
        var monitor = _monitor; _monitor = null;
        if (monitor is not null)
        {
            try { monitor.Stop(); } catch (Exception) { }
            try { if (monitor is IMediaShutdown shutdown) shutdown.Shutdown(); } catch (Exception) { }
            Marshal.ReleaseComObject(monitor);
        }
        _callback = null;
        if (_mediaStarted) { MFShutdown(); _mediaStarted = false; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivityCallback(WindowsCameraActivityMonitor owner) : ISensorActivitiesReportCallback
    {
        public int OnActivitiesReport(ISensorActivitiesReport reports) { owner.OnReport(reports); return 0; }
    }
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfsensorgroup.dll", ExactSpelling = true)] private static extern int MFCreateSensorActivityMonitor(ISensorActivitiesReportCallback callback, out ISensorActivityMonitor monitor);
    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorActivityMonitor { [PreserveSig] int Start(); [PreserveSig] int Stop(); }
    [ComVisible(true), Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorActivitiesReportCallback { [PreserveSig] int OnActivitiesReport(ISensorActivitiesReport reports); }
    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorActivitiesReport
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetActivityReport(uint index, out ISensorActivityReport report);
        [PreserveSig] int GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string name, out ISensorActivityReport report);
    }
    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorActivityReport
    {
        [PreserveSig] int GetFriendlyName([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint capacity, out uint written);
        [PreserveSig] int GetSymbolicLink([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder link, uint capacity, out uint written);
        [PreserveSig] int GetProcessCount(out uint count);
        [PreserveSig] int GetProcessActivity(uint index, out ISensorProcessActivity activity);
    }
    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorProcessActivity
    {
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
        [PreserveSig] int GetStreamingMode(out int mode);
        [PreserveSig] int GetReportTime(out System.Runtime.InteropServices.ComTypes.FILETIME time);
    }
    [ComImport, Guid("97EC2EA4-0E42-4937-97AC-9D6D328824E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaShutdown { [PreserveSig] int Shutdown(); [PreserveSig] int GetShutdownStatus(out int status); }
}
