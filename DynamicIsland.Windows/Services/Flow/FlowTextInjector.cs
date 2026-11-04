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
/// - Pastes only if focus hasn't moved: the top-level window, focused child window, and caret
///   match at key-down, key-up, and right before the keystrokes, and so do the UI Automation
///   focused elements (a browser tab's field). If identity is unavailable or changes, text is held
///   for the user to copy instead of being pasted into an unverified field.
/// - Won't send keystrokes into an elevated (admin) window from a non-elevated Island, which
///   Windows would silently drop.
/// - Checks that Windows accepted all four keystrokes.
/// - Restores the previous clipboard only if nothing else has written to it since. If any format on
///   the clipboard can't be saved and restored exactly, it leaves the clipboard alone and types
///   single-line text as Unicode keystrokes (never Enter or other control keys).
/// - When delivery fails, the controller holds undelivered text for the user to copy. A partial
///   keystroke sequence or clipboard restore failure may still leave partial changes.
/// </summary>
public sealed class FlowTextInjector(LoggingService log, Dispatcher ui) : IFlowPaster
{
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(450);

    public string? LastPastedElementId { get; private set; }
    public nint LastPastedWindow { get; private set; }

    public FlowTarget? CaptureTarget()
    {
        var focus = FocusNow();
        return focus.Window == 0 ? null : new FlowTarget { AtPress = focus, ElementAtPress = FocusedElementIdAsync() };
    }

    public void PinTarget(FlowTarget target)
    {
        target.AtRelease = FocusNow();
        target.ElementAtRelease = FocusedElementIdAsync();
    }

    public Task<FlowPasteResult> PasteAsync(string text, FlowTarget? target, CancellationToken token) =>
        ui.InvokeAsync(() => PasteOnUiAsync(text, target, token)).Task.Unwrap();

    // Hold undelivered text on failure; restore is best-effort if the clipboard is still ours.
    private async Task<FlowPasteResult> PasteOnUiAsync(string text, FlowTarget? target, CancellationToken token)
    {
        LastPastedElementId = null;
        LastPastedWindow = 0;
        token.ThrowIfCancellationRequested();
        if (target is null || !await StillFocusedAsync(target)) return FlowPasteOutcome.TargetChanged;
        if (IsElevatedBeyondUs(target.AtPress.Window)) return FlowPasteOutcome.Elevated;
        token.ThrowIfCancellationRequested();

        // If any part of the current clipboard can't be saved and put back exactly, don't touch the
        // clipboard: type the text as Unicode keystrokes. Line breaks would have to be typed as Enter,
        // which sends messages and submits forms, so multi-line text is handed back instead.
        var beforeSnapshot = GetClipboardSequenceNumber();
        var previous = ClipboardSnapshot.Capture(log);
        // Do not overwrite a clipboard another app changed while the snapshot was being read.
        if (GetClipboardSequenceNumber() != beforeSnapshot) return FlowPasteOutcome.ClipboardBusy;
        if (!previous.Complete)
        {
            if (text.Contains('\n') || text.Contains('\r')) return FlowPasteOutcome.MultilineNeedsClipboard;
            if (!await StillFocusedAsync(target)) return FlowPasteOutcome.TargetChanged;
            var typed = TypeText(text);
            if (typed.Outcome == FlowPasteOutcome.Pasted)
            {
                LastPastedElementId = await WithTimeout(target.ElementAtPress);
                LastPastedWindow = target.AtPress.Window;
            }
            return typed;
        }

        if (!TrySetClipboard(Dictation(text), beforeSnapshot)) return FlowPasteOutcome.ClipboardBusy;
        var ours = GetClipboardSequenceNumber();
        try
        {
            await Task.Delay(30, token);
            // Full focus check immediately before the keystrokes, so a late switch is caught.
            if (!await StillFocusedAsync(target)) { RestoreIfUnchanged(previous, ours); return FlowPasteOutcome.TargetChanged; }
        }
        catch (OperationCanceledException) { RestoreIfUnchanged(previous, ours); throw; }

        var sent = SendKeys((VK_CONTROL, false), (VK_V, false), (VK_V, true), (VK_CONTROL, true));
        if (sent != 4)
        {
            log.Info($"Sutra Flow: SendInput accepted {sent} of 4 keystrokes (error {Marshal.GetLastWin32Error()}).");
            if (sent > 0) SendKeys((VK_V, true), (VK_CONTROL, true)); // don't leave Ctrl stuck down
            RestoreIfUnchanged(previous, ours);
            return FlowPasteOutcome.Blocked;
        }

        // Give the target app time to read the clipboard before restoring it.
        await Task.Delay(RestoreDelay);
        RestoreIfUnchanged(previous, ours);
        LastPastedElementId = await WithTimeout(target.ElementAtPress);
        LastPastedWindow = target.AtPress.Window;
        return FlowPasteOutcome.Pasted;
    }

    /// <summary>Focus readings and UI Automation elements from key-down, key-up, and now all agree.</summary>
    private async Task<bool> StillFocusedAsync(FlowTarget target)
    {
        var now = FocusNow();
        if (target.AtRelease is null || !target.AtPress.SamePlaceAs(now)
            || !target.AtPress.SamePlaceAs(target.AtRelease) || !target.AtRelease.SamePlaceAs(now))
            return Moved("window or caret");
        var press = await WithTimeout(target.ElementAtPress);
        var release = await WithTimeout(target.ElementAtRelease);
        var current = await WithTimeout(FocusedElementIdAsync());
        // Re-read the window last so the gap between this check and the keystrokes stays tiny.
        if (!FlowTarget.Unchanged(target.AtPress, target.AtRelease, FocusNow(), press, release, current)) return Moved("field");
        return true;
    }

    private bool Moved(string what)
    {
        log.Info($"Sutra Flow: focus moved ({what}); not pasting.");
        return false;
    }

    private static FlowFocus FocusNow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0) return new FlowFocus(0, 0, 0, 0, 0);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        var thread = GetWindowThreadProcessId(foreground, out _);
        return GetGUIThreadInfo(thread, ref info)
            ? new FlowFocus(RootOf(foreground), info.hwndFocus, info.hwndCaret, info.rcCaret.Left, info.rcCaret.Top)
            : new FlowFocus(RootOf(foreground), 0, 0, 0, 0);
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

    /// <summary>
    /// Types single-line text as Unicode keystrokes in one SendInput call. If Windows accepts only
    /// some of them, the result says which part didn't get typed.
    /// </summary>
    private FlowPasteResult TypeText(string text)
    {
        var typable = FlowTyping.Typable(text); // never type Enter, Tab, or other control keys
        if (typable.Length == 0) return FlowPasteOutcome.Pasted;
        var inputs = new Input[typable.Length * 2];
        for (var i = 0; i < typable.Length; i++)
        {
            inputs[2 * i] = new Input { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KeybdInput { wScan = typable[i], dwFlags = KEYEVENTF_UNICODE } } };
            inputs[2 * i + 1] = new Input { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KeybdInput { wScan = typable[i], dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
        }
        var size = Marshal.SizeOf<Input>();
        var sent = (int)SendInput((uint)inputs.Length, inputs, size);
        if (sent == inputs.Length) return FlowPasteOutcome.Pasted;

        log.Info($"Sutra Flow: typing stopped after {sent} of {inputs.Length} keystrokes (error {Marshal.GetLastWin32Error()}).");
        if (sent % 2 == 1) SendInput(1, [inputs[sent]], size); // release the last key that went down
        if (sent == 0) return FlowPasteOutcome.Blocked;
        return new FlowPasteResult(FlowPasteOutcome.PartlyTyped, FlowTyping.Remaining(typable, sent));
    }

    /// <summary>Copies text the user asked for (kept out of clipboard history). UI thread only.</summary>
    public bool CopyForUser(string text) => TrySetClipboard(Dictation(text));

    /// <summary>The focused field's id and full text through UI Automation, or nulls when it doesn't expose any.</summary>
    public static (string? Id, string? Text) ReadFocusedText(string expectedId, nint expectedWindow)
    {
        try
        {
            if (expectedWindow == 0 || FocusNow().Window != expectedWindow) return (null, null);
            var element = System.Windows.Automation.AutomationElement.FocusedElement;
            if (element is null) return (null, null);
            var id = string.Join(".", element.GetRuntimeId() ?? []);
            // Never inspect a different field, or a password field, for automatic vocabulary.
            if (!FlowTarget.CanInspectCorrection(expectedWindow, FocusNow().Window, expectedId, id, element.Current.IsPassword)) return (null, null);
            if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var v) && v is System.Windows.Automation.ValuePattern value)
                return (id, value.Current.Value);
            if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var t) && t is System.Windows.Automation.TextPattern text)
                return (id, text.DocumentRange.GetText(20000));
            return (id, null);
        }
        catch { return (null, null); }
    }

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

    private bool TrySetClipboard(DataObject data, uint? expectedSequence = null)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (expectedSequence is uint expected && GetClipboardSequenceNumber() != expected) return false;
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
            Stream stream when stream.CanRead && stream.CanSeek && stream.Length <= MaxStreamBytes => Copy(stream),
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
            if (!_items.Any(item => string.Equals(item.Format, "ExcludeClipboardContentFromMonitorProcessing", StringComparison.OrdinalIgnoreCase)))
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
