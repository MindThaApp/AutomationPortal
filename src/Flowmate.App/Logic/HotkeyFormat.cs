using System.Windows.Input;

namespace Flowmate.App.Logic;

/// <summary>Converts between WPF key presses and the stored "Ctrl+Alt+K" text.</summary>
public static class HotkeyFormat
{
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.Space] = "Space", [Key.Enter] = "Enter", [Key.Tab] = "Tab", [Key.Back] = "Backspace", [Key.Delete] = "Delete",
        [Key.Insert] = "Insert", [Key.Home] = "Home", [Key.End] = "End", [Key.PageUp] = "PageUp", [Key.PageDown] = "PageDown",
        [Key.Up] = "Up", [Key.Down] = "Down", [Key.Left] = "Left", [Key.Right] = "Right", [Key.Escape] = "Esc",
        [Key.PrintScreen] = "PrintScreen", [Key.OemTilde] = "`", [Key.OemMinus] = "-", [Key.OemPlus] = "=",
        [Key.OemComma] = ",", [Key.OemPeriod] = ".", [Key.Oem2] = "/", [Key.Oem1] = ";", [Key.Oem4] = "[",
        [Key.Oem6] = "]", [Key.Oem5] = "\\", [Key.Oem7] = "'",
    };

    private static readonly Dictionary<string, Key> Reverse = BuildReverse();

    private static Dictionary<string, Key> BuildReverse()
    {
        var d = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Names) d[v] = k;
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = Enum.Parse<Key>(c.ToString());
        for (var i = 0; i <= 9; i++) { d[i.ToString()] = Enum.Parse<Key>("D" + i); d["Num" + i] = Enum.Parse<Key>("NumPad" + i); }
        for (var i = 1; i <= 24; i++) d["F" + i] = Enum.Parse<Key>("F" + i);
        d["Escape"] = Key.Escape; d["Return"] = Key.Enter; d["Del"] = Key.Delete; d["Ins"] = Key.Insert;
        return d;
    }

    public static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.DeadCharProcessed or Key.ImeProcessed or Key.None;

    public static string? KeyName(Key key)
    {
        if (IsModifierKey(key)) return null;
        if (Names.TryGetValue(key, out var n)) return n;
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + ((int)key - (int)Key.NumPad0);
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return null;
    }

    /// <summary>"Ctrl+Alt+K" or null when the key alone can't be used (a bare modifier).</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        var name = KeyName(key);
        if (name is null) return null;
        return string.Concat(ModifierText(modifiers), name);
    }

    public static string ModifierText(ModifierKeys modifiers)
    {
        var s = "";
        if (modifiers.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (modifiers.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (modifiers.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (modifiers.HasFlag(ModifierKeys.Windows)) s += "Win+";
        return s;
    }

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // A literal "+" key is written "Ctrl++" which splits into empty parts; treat a trailing empty part as "+".
        var last = parts[^1];
        if (last.Length == 0 && parts.Length > 1) { last = "="; parts = parts[..^1]; }
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        if (parts.Length == 1) last = parts[0];
        return Reverse.TryGetValue(last, out key);
    }
}
