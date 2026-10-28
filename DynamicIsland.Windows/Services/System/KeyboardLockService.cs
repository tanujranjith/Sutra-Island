using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland.Windows.Services;

public interface IKeyboardToggleState { bool IsOn(int virtualKey); }
public sealed class WindowsKeyboardToggleState : IKeyboardToggleState
{
    public bool IsOn(int virtualKey) => (GetKeyState(virtualKey) & 1) != 0;
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
}

/// <summary>Observes only lock keys; the hook always passes every input event through.</summary>
public sealed class KeyboardLockService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IKeyboardToggleState _state;
    private readonly LoggingService _log;
    private readonly HashSet<int> _down = [];
    private readonly Dictionary<int, bool> _known = [];
    private HookProc? _callback;
    private nint _hook;
    public event Action<string, bool>? Changed;
    public KeyboardLockService(Dispatcher dispatcher, LoggingService log, IKeyboardToggleState? state = null)
    { _dispatcher = dispatcher; _log = log; _state = state ?? new WindowsKeyboardToggleState(); }
    public void Start()
    {
        if (_hook != 0) return;
        Reconcile();
        _callback = Callback;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == 0) _log.Info("Lock-key feedback unavailable: keyboard hook could not be registered.");
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => _dispatcher.BeginInvoke(Reconcile);
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => _dispatcher.BeginInvoke(Reconcile);
    public void Reconcile()
    { _down.Clear(); _known[0x14] = _state.IsOn(0x14); _known[0x90] = _state.IsOn(0x90); }
    private nint Callback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            // Read only the virtual key; no typed content is decoded or retained.
            var key = Marshal.ReadInt32(data);
            if (key is 0x14 or 0x90)
            {
                if (message is 0x101 or 0x105) _down.Remove(key);
                else if (message is 0x100 or 0x104 && _down.Add(key))
                    _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (_hook == 0) return;
                        var on = _state.IsOn(key);
                        if (_known.TryGetValue(key, out var old) && old == on) return;
                        _known[key] = on;
                        Changed?.Invoke(key == 0x14 ? "Caps Lock" : "Num Lock", on);
                    }));
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        _down.Clear(); _callback = null;
    }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
