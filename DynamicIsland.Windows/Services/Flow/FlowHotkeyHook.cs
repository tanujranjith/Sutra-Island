using System.Runtime.InteropServices;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Push-to-talk detector. A low-level keyboard hook lives on its own thread with its
/// own message loop, so heavy work elsewhere (Whisper on the CPU, the UI thread) can
/// never stall the callback past Windows' LowLevelHooksTimeout. When that happens
/// Windows silently removes the hook, so the callback here only records a key change
/// and posts it to a worker; it never blocks.
/// Right Ctrl itself always passes through, so it still works as Ctrl. What other keys do
/// while it is held is decided by <see cref="FlowKeyGate"/>: a quick second key is a shortcut
/// (cancels, passes through); once a dictation is under way, stray keys are swallowed and
/// only Esc cancels.
/// </summary>
public sealed class FlowHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly LoggingService _log;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private HookProc? _proc;
    private volatile bool _enabled = true;
    private readonly FlowKeyGate _gate;

    /// <summary>The push-to-talk key went down (on the hook thread; marshal before touching UI).</summary>
    public event Action? Pressed;
    /// <summary>Whether the latest press was a double-tap-and-hold (command mode).</summary>
    public bool IsCommandPress => _gate.CommandPress;
    /// <summary>The push-to-talk key came up after a clean hold.</summary>
    public event Action? Released;
    /// <summary>A shortcut during the hold, or Esc during a dictation: discard it.</summary>
    public event Action? Cancelled;

    /// <param name="isDictating">True while a dictation is recording; read on the hook thread, so it must be quick.</param>
    public FlowHotkeyHook(LoggingService log, Func<bool> isDictating)
    {
        _log = log;
        _gate = new FlowKeyGate(isDictating);
        _pump = Task.Run(PumpAsync);
    }

    public void Configure(bool enabled, int pushKey = FlowKeyGate.VkRightCtrl)
    {
        if (_gate.PttKey != pushKey) { if (_gate.Reset()) Raise(Cancelled); _gate.PttKey = pushKey; }
        _enabled = enabled;
        if (!enabled && _gate.Reset()) Raise(Cancelled);
    }

    public void Start()
    {
        if (_thread is not null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Sutra Flow key hook", Priority = ThreadPriority.Highest };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == 0) _log.Error($"Sutra Flow: keyboard hook failed (error {Marshal.GetLastWin32Error()}).");
        else _log.Info("Sutra Flow: push-to-talk hook installed.");
        ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _enabled)
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            // Our own SendInput (the Ctrl+V paste) must never start or cancel a dictation.
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                var msg = (int)wParam;
                var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (down || msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    var decision = _gate.OnKey((int)data.vkCode, down, data.time);
                    switch (decision.Signal)
                    {
                        case FlowKeyGate.Signal.Pressed: Raise(Pressed); break;
                        case FlowKeyGate.Signal.Released: Raise(Released); break;
                        case FlowKeyGate.Signal.Cancelled: Raise(Cancelled); break;
                    }
                    if (decision.Swallow) return 1; // a stray key during dictation never reaches the app
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // Never run listeners on the hook thread: a slow listener would get the hook removed.
    // One serial worker keeps press/release/cancel in the order they happened.
    private readonly System.Threading.Channels.Channel<Action> _events =
        System.Threading.Channels.Channel.CreateUnbounded<Action>(new() { SingleReader = true });
    private readonly Task _pump;

    private void Raise(Action? handler)
    {
        if (handler is null) return;
        _events.Writer.TryWrite(handler);
    }

    private async Task PumpAsync()
    {
        await foreach (var handler in _events.Reader.ReadAllAsync())
        {
            try { handler(); }
            catch (Exception ex) { _log.Error("Sutra Flow: key handler failed.", ex); }
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread = null;
        _events.Writer.TryComplete();
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int ptX, ptY; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc fn, nint hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hhk, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, int msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
