using System.Windows.Input;
using DynamicIsland.Windows.Interop;

namespace DynamicIsland.Windows.Infrastructure;

/// <summary>Parses and formats global-hotkey strings like "Ctrl+Alt+K" for RegisterHotKey.
/// Canonical form: Ctrl/Alt/Shift (in that order) plus a letter, digit, F-key, or Space.</summary>
public static class HotkeyParser
{
    public static bool TryParse(string? text, out uint modifiers, out uint key,
        out string canonical, out string? error)
    {
        modifiers = 0;
        key = 0;
        canonical = "";
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Shortcut is empty.";
            return false;
        }
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "Use modifiers plus a key, e.g. Ctrl+Alt+K.";
            return false;
        }
        var modifierNames = new List<string>(3);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    if ((modifiers & NativeMethods.HotkeyModifierControl) == 0)
                    { modifiers |= NativeMethods.HotkeyModifierControl; modifierNames.Add("Ctrl"); }
                    break;
                case "alt":
                    if ((modifiers & NativeMethods.HotkeyModifierAlt) == 0)
                    { modifiers |= NativeMethods.HotkeyModifierAlt; modifierNames.Add("Alt"); }
                    break;
                case "shift":
                    if ((modifiers & NativeMethods.HotkeyModifierShift) == 0)
                    { modifiers |= NativeMethods.HotkeyModifierShift; modifierNames.Add("Shift"); }
                    break;
                default:
                    error = $"Unknown modifier '{parts[i]}'. Use Ctrl, Alt, or Shift.";
                    return false;
            }
        }
        if (modifiers == 0)
        {
            error = "Add at least Ctrl, Alt, or Shift.";
            return false;
        }
        if (!TryParseKey(parts[^1], out key, out var keyName))
        {
            error = $"Unknown key '{parts[^1]}'. Use a letter, digit, F1–F12, or Space.";
            return false;
        }
        canonical = string.Join('+', modifierNames) + "+" + keyName;
        return true;
    }

    /// <summary>Builds a canonical combo from a captured WPF key press, or null when the
    /// combination is unsupported (modifier-only, no modifiers, or an unmapped key).</summary>
    public static string? Format(Key key, ModifierKeys modifiers)
    {
        if (modifiers is ModifierKeys.None or ModifierKeys.Windows) return null;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return null;
        if (!TryGetVirtualKey(key, out var vk)) return null;
        var names = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0) names.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) names.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) names.Add("Shift");
        names.Add(KeyDisplayName(key));
        return string.Join('+', names);
    }

    public static string Display(string canonical) =>
        string.Join(" + ", canonical.Split('+'));

    private static bool TryParseKey(string text, out uint virtualKey, out string canonical)
    {
        virtualKey = 0;
        canonical = "";
        if (text.Length == 1)
        {
            var c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                canonical = c.ToString();
                return true;
            }
            return false;
        }
        if (text.Length is >= 2 and <= 3 &&
            (text[0] is 'F' or 'f') && int.TryParse(text[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            canonical = "F" + functionKey;
            return true;
        }
        if (text.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 0x20;
            canonical = "Space";
            return true;
        }
        return false;
    }

    private static bool TryGetVirtualKey(Key key, out uint virtualKey)
    {
        virtualKey = key switch
        {
            >= Key.A and <= Key.Z => (uint)('A' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
            >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
            Key.Space => 0x20,
            _ => 0,
        };
        return virtualKey != 0;
    }

    private static string KeyDisplayName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),
        Key.Space => "Space",
        _ => key.ToString(),
    };
}
