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
/// - Pastes only if the window that had focus at key-down still has focus. Otherwise the text is
///   left on the clipboard (out of clipboard history) and the user is told, so private dictation
///   never lands in a window they switched to.
/// - Won't send keystrokes into an elevated (admin) window from a non-elevated Island, which
///   Windows would silently drop.
/// - Checks that Windows accepted all four keystrokes.
/// - Restores the previous clipboard only if nothing else has written to it since, and restores
///   every format it could read (text, rich text, HTML, images, file lists), not just text.
/// </summary>
public sealed class FlowTextInjector(LoggingService log, Dispatcher ui) : IFlowPaster
{
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(450);

    public nint CaptureTarget() => RootOf(GetForegroundWindow());

    public Task<FlowPasteOutcome> PasteAsync(string text, nint target, CancellationToken token) =>
        ui.InvokeAsync(() => PasteOnUiAsync(text, target, token)).Task.Unwrap();

    private async Task<FlowPasteOutcome> PasteOnUiAsync(string text, nint target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target == 0 || RootOf(GetForegroundWindow()) != target)
            return CopyOnly(text) ? FlowPasteOutcome.TargetChanged : FlowPasteOutcome.ClipboardBusy;
        if (IsElevatedBeyondUs(target))
            return CopyOnly(text) ? FlowPasteOutcome.Elevated : FlowPasteOutcome.ClipboardBusy;

        var previous = ClipboardSnapshot.Capture(log);
        if (!TrySetClipboard(Dictation(text))) return FlowPasteOutcome.ClipboardBusy;
        var ours = GetClipboardSequenceNumber();

        try { await Task.Delay(30, token); }
        catch (OperationCanceledException) { RestoreIfUnchanged(previous, ours); throw; }

        // Focus may have moved in the last moment; never type into a window the user switched to.
        if (RootOf(GetForegroundWindow()) != target) return FlowPasteOutcome.TargetChanged;

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
                    }
                    catch (Exception ex) { log.Debug($"Sutra Flow: clipboard format {format} not saved ({ex.Message})."); }
                }
            }
            catch (Exception ex) { log.Debug($"Sutra Flow: clipboard snapshot failed ({ex.Message})."); }
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
