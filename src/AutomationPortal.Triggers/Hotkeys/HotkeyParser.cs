using System.Globalization;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A parsed keyboard shortcut: modifier keys plus one virtual-key code.</summary>
public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, int VirtualKey)
{
    public override string ToString() => HotkeyParser.Format(this);
}

/// <summary>Parses and formats keyboard shortcuts such as "Ctrl+Alt+Shift+Win+K" (modifiers always in that order).</summary>
public static class HotkeyParser
{
    private static readonly (string Name, int Vk)[] Named =
    {
        ("Space", 0x20), ("Enter", 0x0D), ("Esc", 0x1B), ("Tab", 0x09), ("Backspace", 0x08), ("Delete", 0x2E), ("Insert", 0x2D),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22), ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
        ("PrintScreen", 0x2C), ("Pause", 0x13), ("CapsLock", 0x14), ("NumLock", 0x90), ("ScrollLock", 0x91), ("Menu", 0x5D),
        ("NumpadMultiply", 0x6A), ("NumpadAdd", 0x6B), ("NumpadSubtract", 0x6D), ("NumpadDecimal", 0x6E), ("NumpadDivide", 0x6F),
        (";", 0xBA), ("=", 0xBB), (",", 0xBC), ("-", 0xBD), (".", 0xBE), ("/", 0xBF), ("`", 0xC0), ("[", 0xDB), ("\\", 0xDC), ("]", 0xDD), ("'", 0xDE),
        ("VolumeMute", 0xAD), ("VolumeDown", 0xAE), ("VolumeUp", 0xAF), ("MediaNext", 0xB0), ("MediaPrevious", 0xB1), ("MediaStop", 0xB2), ("MediaPlayPause", 0xB3),
    };

    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = 0x0D, ["Escape"] = 0x1B, ["Back"] = 0x08, ["Del"] = 0x2E, ["Ins"] = 0x2D, ["PgUp"] = 0x21, ["PgDn"] = 0x22,
        ["Prior"] = 0x21, ["Next"] = 0x22, ["PrtSc"] = 0x2C, ["Plus"] = 0xBB, ["+"] = 0xBB, ["Minus"] = 0xBD, ["Comma"] = 0xBC,
        ["Period"] = 0xBE, ["Slash"] = 0xBF, ["Semicolon"] = 0xBA, ["Backtick"] = 0xC0, ["Tilde"] = 0xC0,
    };

    private static readonly Dictionary<string, int> ByName = BuildByName();
    private static readonly Dictionary<int, string> ByVk = Named.ToDictionary(n => n.Vk, n => n.Name);

    private static Dictionary<string, int> BuildByName()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, vk) in Named) d[n] = vk;
        foreach (var (n, vk) in Aliases) d[n] = vk;
        return d;
    }

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        string keyPart;
        string modPart;
        if (s == "+") { modPart = ""; keyPart = "+"; }
        else if (s.EndsWith("++", StringComparison.Ordinal)) { modPart = s[..^2]; keyPart = "+"; }
        else
        {
            var i = s.LastIndexOf('+');
            modPart = i < 0 ? "" : s[..i];
            keyPart = i < 0 ? s : s[(i + 1)..];
        }

        var mods = HotkeyModifiers.None;
        foreach (var raw in modPart.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": case "ctl": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": case "option": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "windows": case "super": case "meta": case "cmd": mods |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }
        if (!TryParseKey(keyPart.Trim(), out var vk)) return false;
        chord = new HotkeyChord(mods, vk);
        return true;
    }

    public static HotkeyChord Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException(Strings.Format("Hotkey_Error_NotValid", text));

    public static bool TryParseKey(string name, out int vk)
    {
        vk = 0;
        if (name.Length == 0) return false;
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) { vk = char.ToUpperInvariant(name[0]); return true; }
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) { vk = name[0]; return true; }
        if ((name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
        { vk = 0x6F + f; return true; }
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("numpad") || lower.StartsWith("num"))
        {
            var rest = lower.StartsWith("numpad") ? lower[6..] : lower[3..];
            if (rest.Length == 1 && char.IsAsciiDigit(rest[0])) { vk = 0x60 + (rest[0] - '0'); return true; }
        }
        return ByName.TryGetValue(name, out vk);
    }

    public static string KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Numpad" + (vk - 0x60);
        return ByVk.TryGetValue(vk, out var n) ? n : "Key" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }

    public static string Format(HotkeyChord chord) => Format(chord.Modifiers, chord.VirtualKey);

    public static string Format(HotkeyModifiers mods, int vk)
    {
        var parts = new List<string>(5);
        if (mods.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join('+', parts);
    }

    /// <summary>Normalises a typed shortcut ("control + alt + m" gives "Ctrl+Alt+M"); null when it can't be parsed.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var c) ? Format(c) : null;

    /// <summary>True for modifier-only virtual keys (Shift, Ctrl, Alt, Win, left/right variants).</summary>
    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    /// <summary>A global shortcut must not hijack plain typing: it needs Ctrl, Alt or Win (function keys and media keys may stand alone).</summary>
    public static bool IsSafeGlobal(HotkeyChord c) =>
        !IsReservedEditChord(c) &&
        ((c.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0
        || c.VirtualKey is >= 0x70 and <= 0x87 or >= 0xAD and <= 0xB3);

    /// <summary>Ctrl+C / V / X / Z / Y / A / S / P (and Ctrl+Shift+Z): every app uses them, so binding them globally would break copy and paste.</summary>
    public static bool IsReservedEditChord(HotkeyChord c) =>
        c.Modifiers == HotkeyModifiers.Ctrl && c.VirtualKey is (int)'C' or (int)'V' or (int)'X' or (int)'Z' or (int)'Y' or (int)'A' or (int)'S' or (int)'P'
        || c.Modifiers == (HotkeyModifiers.Ctrl | HotkeyModifiers.Shift) && c.VirtualKey == (int)'Z';

    internal static uint ToNativeModifiers(HotkeyModifiers m) =>
        (m.HasFlag(HotkeyModifiers.Alt) ? Win32.MOD_ALT : 0) | (m.HasFlag(HotkeyModifiers.Ctrl) ? Win32.MOD_CONTROL : 0)
        | (m.HasFlag(HotkeyModifiers.Shift) ? Win32.MOD_SHIFT : 0) | (m.HasFlag(HotkeyModifiers.Win) ? Win32.MOD_WIN : 0);
}
