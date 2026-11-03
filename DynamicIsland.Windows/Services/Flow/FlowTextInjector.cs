using System.Runtime.InteropServices;
using System.Windows;
using DataObject = System.Windows.DataObject;
using Clipboard = System.Windows.Clipboard;
using TextDataFormat = System.Windows.TextDataFormat;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Types text into whatever app has focus: put it on the clipboard, press Ctrl+V,
/// then put the user's previous clipboard text back. The dictation is marked so it
/// stays out of Windows clipboard history (Win+V) and Island's own clipboard panel.
/// Must be called on the UI (STA) thread.
/// </summary>
public sealed class FlowTextInjector(LoggingService log)
{
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;

    public async Task<bool> PasteAsync(string text)
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }

        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // Ask clipboard history and clipboard managers to skip this item.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        if (!TrySetClipboard(data)) return false;

        await Task.Delay(30);
        SendKeys((VK_CONTROL, false), (VK_V, false), (VK_V, true), (VK_CONTROL, true));

        // Give the target app time to read the clipboard before restoring it.
        await Task.Delay(450);
        if (previous is not null)
        {
            var restore = new DataObject();
            restore.SetText(previous, TextDataFormat.UnicodeText);
            restore.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            TrySetClipboard(restore);
        }
        return true;
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

    private static void SendKeys(params (ushort Key, bool Up)[] keys)
    {
        var inputs = keys.Select(k => new Input
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KeybdInput { wVk = k.Key, dwFlags = k.Up ? KEYEVENTF_KEYUP : 0 } }
        }).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
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
}
