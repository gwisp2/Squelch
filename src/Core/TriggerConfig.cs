using System.Text.Json.Serialization;
using Squelch.Interop;

namespace Squelch.Core;

public enum TriggerKind
{
    Keyboard,
    Mouse,
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>
/// The key or mouse button (plus optional modifiers) that drives push-to-talk.
/// </summary>
public sealed class TriggerConfig
{
    public TriggerKind Kind { get; set; } = TriggerKind.Keyboard;

    /// <summary>Virtual-key code for <see cref="TriggerKind.Keyboard"/>, or a <see cref="MouseButtonKind"/> for mouse.</summary>
    public int Code { get; set; } = 0x20; // VK_SPACE

    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.None;

    /// <summary>
    /// Left mouse is deliberately not usable as a trigger: the trigger is swallowed, so choosing it
    /// would leave the user unable to click. Invalid configurations fall back to the default.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable => Kind != TriggerKind.Mouse || (MouseButtonKind)Code != MouseButtonKind.Left;

    public TriggerConfig Clone() => new()
    {
        Kind = Kind,
        Code = Code,
        Modifiers = Modifiers,
    };

    public string Describe()
    {
        var modifiers = DescribeModifiers(Modifiers);
        var key = Kind == TriggerKind.Mouse ? MouseButtonName(Code) : KeyName.Describe(Code);
        return modifiers.Length == 0 ? key : modifiers + " + " + key;
    }

    public static string DescribeModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public override string ToString() => Describe();

    public static string MouseButtonName(int code) => code switch
    {
        (int)MouseButtonKind.Left => "Mouse Left",
        (int)MouseButtonKind.Right => "Mouse Right",
        (int)MouseButtonKind.Middle => "Mouse Middle",
        (int)MouseButtonKind.X1 => "Mouse 4",
        (int)MouseButtonKind.X2 => "Mouse 5",
        _ => $"Mouse {code}",
    };
}

public static class KeyName
{
    public static string Describe(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39) return ((char)('0' + (virtualKey - 0x30))).ToString();
        if (virtualKey is >= 0x41 and <= 0x5A) return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x60 and <= 0x69) return "Num " + (virtualKey - 0x60);
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x6F);

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left Arrow",
            0x26 => "Up Arrow",
            0x27 => "Right Arrow",
            0x28 => "Down Arrow",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5D => "Menu",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"0x{virtualKey:X2}",
        };
    }
}
