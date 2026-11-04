namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Decides what each physical key does while the push-to-talk key is held. Platform-free so it can
/// be unit-tested; <see cref="FlowHotkeyHook"/> feeds it from the keyboard hook.
/// <list type="bullet">
/// <item>For the first <see cref="ShortcutWindowMs"/> after the press, another key means a shortcut
/// (Right Ctrl+C): dictation is cancelled and the key goes through.</item>
/// <item>After that, if a dictation is really recording, it locks: stray keys are swallowed so the
/// cursor doesn't move and dictation keeps going. Esc is the one key that cancels.</item>
/// </list>
/// </summary>
public sealed class FlowKeyGate(Func<bool> isDictating)
{
    public const int ShortcutWindowMs = 300;
    public const int VkEscape = 0x1B;

    public enum Signal { None, Pressed, Released, Cancelled }
    public readonly record struct Decision(Signal Signal, bool Swallow);

    public const int VkRightCtrl = 0xA3, VkRightAlt = 0xA5, VkRightShift = 0xA1;

    private int _pttKey = VkRightCtrl;

    /// <summary>
    /// The push-to-talk key. Right Alt's own press and release are swallowed, because on its own
    /// Alt would open the focused app's menu bar when you let go.
    /// </summary>
    public int PttKey
    {
        get => _pttKey;
        set { if (value == _pttKey) return; _pttKey = value; _held = false; }
    }

    private bool SwallowPtt => _pttKey == VkRightAlt;

    public static int KeyFor(string? name) => name switch
    {
        "RightAlt" => VkRightAlt,
        "RightShift" => VkRightShift,
        _ => VkRightCtrl,
    };
    private readonly HashSet<int> _swallowed = [];
    private bool _held;
    private uint _pressedAt;

    public bool Held => _held;

    /// <summary>Handles one physical (non-injected) key event; <paramref name="time"/> is the event's tick in ms.</summary>
    public Decision OnKey(int vk, bool down, uint time)
    {
        if (vk == _pttKey)
        {
            if (down && !_held)
            {
                _held = true; _pressedAt = time; _swallowed.Clear(); // Clear: never let a missed key-up block a key for good
                return new(Signal.Pressed, SwallowPtt);
            }
            if (!down && _held)
            {
                _held = false;
                return new(Signal.Released, SwallowPtt);
            }
            return new(Signal.None, SwallowPtt); // auto-repeat, or an up after a cancel
        }

        if (!down)
        {
            // Swallow the key-up of a key whose key-down we swallowed, so apps never see half a press.
            return new(Signal.None, _swallowed.Remove(vk));
        }

        if (_swallowed.Contains(vk)) return new(Signal.None, true); // auto-repeat of a swallowed key
        if (!_held) return new(Signal.None, false);

        var locked = unchecked(time - _pressedAt) >= ShortcutWindowMs && isDictating();
        if (!locked)
        {
            _held = false;
            return new(Signal.Cancelled, false); // a shortcut: let it through
        }

        _swallowed.Add(vk);
        if (vk == VkEscape) { _held = false; return new(Signal.Cancelled, true); }
        return new(Signal.None, true);
    }

    /// <summary>Flow was turned off mid-hold.</summary>
    public bool Reset()
    {
        var wasHeld = _held;
        _held = false;
        return wasHeld;
    }
}
