using Squelch.Interop;

namespace Squelch.Core;

/// <summary>
/// Turns the raw key/button stream into <see cref="Activated"/> / <see cref="Deactivated"/> edges.
///
/// <para>
/// Rules:
/// </para>
/// <list type="bullet">
///   <item>The primary key is latched only when the required modifiers are already held at the
///   moment it goes down. Pressing the primary first and adding Ctrl afterwards does not fire -
///   that matches how Discord-style push-to-talk behaves.</item>
///   <item>While latched, releasing either the primary <i>or</i> any required modifier deactivates.
///   Adding an <i>extra</i> modifier does not.</item>
/// </list>
///
/// <para>All of this runs on the hook thread, so it must stay allocation-light. It does.</para>
/// </summary>
public sealed class TriggerDetector
{
    private readonly HashSet<int> _downKeys = [];

    private TriggerConfig _trigger = new();
    private bool _latched;
    private bool _active;

    public TriggerConfig Trigger
    {
        get => _trigger;
        set
        {
            _trigger = value;
            Reset();
        }
    }

    public bool IsActive => _active;

    /// <summary>True when the modifier portion of the trigger is currently held.</summary>
    public bool AreModifiersSatisfied => ModifiersSatisfied();

    public event Action? Activated;
    public event Action? Deactivated;

    /// <summary>Forgets all held-key state. Used when the trigger changes or capture mode ends.</summary>
    public void Reset()
    {
        _downKeys.Clear();
        _latched = false;
        SetActive(false);
    }

    public void HandleKey(KeyInput input)
    {
        RecordKey(input.VirtualKey, input.IsDown);

        if (_trigger.Kind == TriggerKind.Keyboard && input.VirtualKey == _trigger.Code)
            _latched = input.IsDown && ModifiersSatisfied();

        Evaluate();
    }

    public void HandleMouse(MouseInput input)
    {
        if (_trigger.Kind == TriggerKind.Mouse && (int)input.Button == _trigger.Code)
            _latched = input.IsDown && ModifiersSatisfied();

        Evaluate();
    }

    private void Evaluate()
    {
        // Latches only survive while the modifiers are still held, so re-pressing a modifier while
        // the primary is down does not bring the mic back up.
        if (!ModifiersSatisfied())
            _latched = false;

        SetActive(_latched);
    }

    private void SetActive(bool value)
    {
        if (_active == value)
            return;

        _active = value;
        if (value)
            Activated?.Invoke();
        else
            Deactivated?.Invoke();
    }

    private void RecordKey(int virtualKey, bool isDown)
    {
        if (isDown)
            _downKeys.Add(virtualKey);
        else
            _downKeys.Remove(virtualKey);
    }

    private bool ModifiersSatisfied()
    {
        var required = _trigger.Modifiers;
        return required == HotkeyModifiers.None || (CurrentModifiers() & required) == required;
    }

    private HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        foreach (var virtualKey in _downKeys)
        {
            modifiers |= virtualKey switch
            {
                0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
                0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
                0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
                0x5B or 0x5C => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
        }

        return modifiers;
    }
}
