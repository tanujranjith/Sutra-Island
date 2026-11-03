using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DataObject = System.Windows.DataObject;
using Clipboard = System.Windows.Clipboard;
using TextDataFormat = System.Windows.TextDataFormat;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Puts dictated text into the app the user was in when they pressed the key:
/// the text goes on the clipboard, Ctrl+V is sent, and the user's previous clipboard is put back.
///
/// Safeguards:
/// - Pastes only if focus is where it was: the same top-level window, the same focused child
///   window, and, where UI Automation can tell, the same focused element (a browser tab's field). Otherwise the text is
///   left on the clipboard (out of clipboard history) and the user is told, so private dictation
///   never lands in a window they switched to.
/// - Won't send keystrokes into an elevated (admin) window from a non-elevated Island, which
///   Windows would silently drop.
/// - Checks that Windows accepted all four keystrokes.
/// - Restores the previous clipboard only if nothing else has written to it since. If any format on
///   the clipboard can't be saved and restored exactly, it leaves the clipboard alone and types the
///   text as Unicode keystrokes instead.
/// </summary>
public sealed class FlowTextInjector(LoggingService log, Dispatcher ui) : IFlowPaster
{
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56, VK_RETURN = 0x0D;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(450);

    public FlowTarget? CaptureTarget()
    {
        var (window, focus) = FocusNow();
        return window == 0 ? null : new FlowTarget { Window = window, Focus = focus, ElementAtPress = FocusedElementIdAsync() };
    }

    public void PinTarget(FlowTarget target) => target.ElementAtRelease = FocusedElementIdAsync();

    public Task<FlowPasteOutcome> PasteAsync(string text, FlowTarget? target, CancellationToken token) =>
        ui.InvokeAsync(() => PasteOnUiAsync(text, target, token)).Task.Unwrap();

    private async Task<FlowPasteOutcome> PasteOnUiAsync(string text, FlowTarget? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target is null || !await StillFocusedAsync(target))
            return CopyOnly(text) ? FlowPasteOutcome.TargetChanged : FlowPasteOutcome.ClipboardBusy;
        if (IsElevatedBeyondUs(target.Window))
            return CopyOnly(text) ? FlowPasteOutcome.Elevated : FlowPasteOutcome.ClipboardBusy;
        token.ThrowIfCancellationRequested();

        // If any part of the current clipboard can't be saved and put back exactly, don't touch
        // the clipboard at all: type the text as Unicode keystrokes instead.
        var previous = ClipboardSnapshot.Capture(log);
        if (!previous.Complete)
        {
            log.Debug("Sutra Flow: clipboard has formats that can't be restored; typing instead of pasting.");
            if (!WindowsStillFocused(target)) return CopyOnly(text) ? FlowPasteOutcome.TargetChanged : FlowPasteOutcome.ClipboardBusy;
            if (TypeText(text)) return FlowPasteOutcome.Pasted;
            return CopyOnly(text) ? FlowPasteOutcome.Blocked : FlowPasteOutcome.ClipboardBusy;
        }

        if (!TrySetClipboard(Dictation(text))) return FlowPasteOutcome.ClipboardBusy;
        var ours = GetClipboardSequenceNumber();

        try { await Task.Delay(30, token); }
        catch (OperationCanceledException) { RestoreIfUnchanged(previous, ours); throw; }

        // Focus may have moved in the last moment; never type into a window the user switched to.
        if (!WindowsStillFocused(target)) return FlowPasteOutcome.TargetChanged;

        var sent = SendKeys((VK_CONTROL, false), (VK_V, false), (VK_V, true), (VK_CONTROL, true));
        if (sent != 4)
        {
            log.Info($"Sutra Flow: SendInput accepted {sent} of 4 keystrokes (error {Marshal.GetLastWin32Error()}).");
            if (sent > 0) SendKeys((VK_V, true), (VK_CONTROL, true)); // don't leave Ctrl stuck down
            return FlowPasteOutcome.Blocked;
        }

        // Give the target app time to read the clipboard before restoring it.
        await Task.Delay(RestoreDelay);
        RestoreIfUnchanged(previous, ours);
        return FlowPasteOutcome.Pasted;
    }

    /// <summary>Same top-level window, same focused child window, and (when known) the same focused element.</summary>
    private async Task<bool> StillFocusedAsync(FlowTarget target)
    {
        if (!WindowsStillFocused(target)) return false;
        // Browser tabs and fields usually share one window handle, so compare UI Automation
        // elements too. The key-up reading is preferred: by then accessibility is awake in apps
        // like Chrome that only expose fields once a UI Automation client has asked.
        var then = await WithTimeout(target.ElementAtRelease ?? target.ElementAtPress);
        if (then is null) return true;
        var now = await WithTimeout(FocusedElementIdAsync());
        if (now is null || now == then) return true;
        log.Info("Sutra Flow: focused element changed since key-up; not pasting.");
        return false;
    }

    private static bool WindowsStillFocused(FlowTarget target)
    {
        var (window, focus) = FocusNow();
        return window == target.Window && (target.Focus == 0 || focus == 0 || focus == target.Focus);
    }

    private static (nint Window, nint Focus) FocusNow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0) return (0, 0);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        var thread = GetWindowThreadProcessId(foreground, out _);
        var focus = GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
        return (RootOf(foreground), focus);
    }

    private static Task<string?> FocusedElementIdAsync() => Task.Run(() =>
    {
        try
        {
            var element = System.Windows.Automation.AutomationElement.FocusedElement;
            var id = element?.GetRuntimeId();
            return id is { Length: > 0 } ? string.Join(".", id) : null;
        }
        catch { return (string?)null; }
    });

    private static async Task<string?> WithTimeout(Task<string?>? task)
    {
        if (task is null) return null;
        return await Task.WhenAny(task, Task.Delay(400)).ConfigureAwait(true) == task ? task.Result : null;
    }

    /// <summary>Types text as Unicode keystrokes. True if Windows accepted every keystroke.</summary>
    private bool TypeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                inputs.Add(Key(VK_RETURN, false));
                inputs.Add(Key(VK_RETURN, true));
                continue;
            }
            inputs.Add(new Input { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KeybdInput { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } });
            inputs.Add(new Input { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KeybdInput { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });
        }
        var size = Marshal.SizeOf<Input>();
        for (var start = 0; start < inputs.Count; start += 200)
        {
            var chunk = inputs.Skip(start).Take(200).ToArray();
            var sent = SendInput((uint)chunk.Length, chunk, size);
            if (sent != chunk.Length)
            {
                log.Info($"Sutra Flow: typing stopped after {start + sent} of {inputs.Count} keystrokes (error {Marshal.GetLastWin32Error()}).");
                return false;
            }
        }
        return true;
    }

    private static Input Key(ushort vk, bool up) =>
        new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KeybdInput { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };

    private void RestoreIfUnchanged(ClipboardSnapshot previous, uint ours)
    {
        if (GetClipboardSequenceNumber() != ours)
        {
            log.Debug("Sutra Flow: clipboard changed after paste; not restoring.");
            return;
        }
        if (previous.IsEmpty)
        {
            try { Clipboard.Clear(); } catch (COMException) { }
            return;
        }
        TrySetClipboard(previous.ToDataObject());
    }

    private bool CopyOnly(string text) => TrySetClipboard(Dictation(text));

    private static DataObject Dictation(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        MarkPrivate(data);
        return data;
    }

    /// <summary>Asks clipboard history (Win+V), cloud clipboard, and clipboard managers to skip this item.</summary>
    internal static void MarkPrivate(DataObject data)
    {
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
    }

    private bool TrySetClipboard(DataObject data)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetDataObject(data, copy: true); return true; }
            catch (COMException) { Thread.Sleep(40); }
            catch (Exception ex) { log.Error("Sutra Flow: clipboard write failed.", ex); return false; }
        }
        log.Info("Sutra Flow: clipboard stayed locked by another app.");
        return false;
    }

    private static nint RootOf(nint hwnd) => hwnd == 0 ? 0 : GetAncestor(hwnd, GA_ROOT);

    /// <summary>
    /// True when the target window's process runs elevated and Island does not. Windows (UIPI)
    /// drops synthetic input into such windows without reporting an error.
    /// </summary>
    private bool IsElevatedBeyondUs(nint hwnd)
    {
        try
        {
            if (IsProcessElevated(Process.GetCurrentProcess().Handle) == true) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == 0) return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;
            try { return IsProcessElevated(process) == true; }
            finally { CloseHandle(process); }
        }
        catch (Exception ex)
        {
            log.Debug($"Sutra Flow: elevation check skipped ({ex.Message}).");
            return false;
        }
    }

    private static bool? IsProcessElevated(nint process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) ? elevated != 0 : null;
        }
        finally { CloseHandle(token); }
    }

    private static uint SendKeys(params (ushort Key, bool Up)[] keys)
    {
        var inputs = keys.Select(k => new Input
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KeybdInput { wVk = k.Key, dwFlags = k.Up ? KEYEVENTF_KEYUP : 0 } }
        }).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    /// <summary>A detached copy of every clipboard format that could be read.</summary>
    private sealed class ClipboardSnapshot
    {
        private const long MaxStreamBytes = 64L * 1024 * 1024;
        private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        {
            "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard",
        };
        private readonly List<(string Format, object Value)> _items = [];

        public bool IsEmpty => _items.Count == 0;
        /// <summary>False when some format could not be read or copied, so a restore would lose it.</summary>
        public bool Complete { get; private set; } = true;

        public static ClipboardSnapshot Capture(LoggingService log)
        {
            var snapshot = new ClipboardSnapshot();
            try
            {
                var data = Clipboard.GetDataObject();
                if (data is null) return snapshot;
                foreach (var format in data.GetFormats(autoConvert: false))
                {
                    if (Skip.Contains(format)) continue;
                    try
                    {
                        var value = Detach(data.GetData(format, autoConvert: false));
                        if (value is not null) snapshot._items.Add((format, value));
                        else { snapshot.Complete = false; log.Debug($"Sutra Flow: clipboard format {format} can't be copied."); }
                    }
                    catch (Exception ex)
                    {
                        snapshot.Complete = false;
                        log.Debug($"Sutra Flow: clipboard format {format} not saved ({ex.Message}).");
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.Complete = false;
                log.Debug($"Sutra Flow: clipboard snapshot failed ({ex.Message}).");
            }
            return snapshot;
        }

        private static object? Detach(object? value) => value switch
        {
            string s => s,
            string[] files => files.ToArray(),
            BitmapSource bitmap => Frozen(bitmap),
            MemoryStream ms when ms.Length <= MaxStreamBytes => new MemoryStream(ms.ToArray()),
            Stream stream when stream.CanRead && (!stream.CanSeek || stream.Length <= MaxStreamBytes) => Copy(stream),
            _ => null,
        };

        private static BitmapSource Frozen(BitmapSource bitmap)
        {
            var copy = bitmap.IsFrozen ? bitmap : (BitmapSource)bitmap.Clone();
            if (!copy.IsFrozen && copy.CanFreeze) copy.Freeze();
            return copy;
        }

        private static MemoryStream Copy(Stream stream)
        {
            var copy = new MemoryStream();
            if (stream.CanSeek) stream.Position = 0;
            stream.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }

        public DataObject ToDataObject()
        {
            var data = new DataObject();
            foreach (var (format, value) in _items)
                data.SetData(format, value is MemoryStream ms ? new MemoryStream(ms.ToArray()) : value);
            // A restore is not a new copy: keep it out of clipboard history so Win+V doesn't fill with duplicates.
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            return data;
        }
    }

    private const uint GA_ROOT = 2, TOKEN_QUERY = 0x0008, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int TokenElevation = 20, ERROR_ACCESS_DENIED = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Rect rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);
}
