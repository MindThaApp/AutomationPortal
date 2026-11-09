using System.Text.RegularExpressions;

namespace Flowmate.Actions.Windows.Input;

/// <summary>One key press with modifiers held, e.g. Ctrl+Shift+T. Codes are Windows virtual-key codes.</summary>
public sealed record KeyChord(IReadOnlyList<int> Modifiers, int Key)
{
    public override string ToString() => string.Join("+", Modifiers.Select(KeyCombo.NameOf).Append(KeyCombo.NameOf(Key)));
}

/// <summary>Parses key shortcuts written like "Ctrl+Shift+T" (several chords can be separated by spaces: "Ctrl+K Ctrl+C").</summary>
public static class KeyCombo
{
    public const int Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Win = 0x5B;

    private static readonly Dictionary<string, int> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Ctrl, ["control"] = Ctrl, ["ctl"] = Ctrl,
        ["shift"] = Shift,
        ["alt"] = Alt, ["option"] = Alt, ["opt"] = Alt,
        ["win"] = Win, ["windows"] = Win, ["cmd"] = Win, ["command"] = Win, ["super"] = Win, ["meta"] = Win,
    };

    private static readonly Dictionary<string, int> Keys = BuildKeys();

    private static Dictionary<string, int> BuildKeys()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20, ["spacebar"] = 0x20,
            ["backspace"] = 0x08, ["back"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["ins"] = 0x2D,
            ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22, ["pgdown"] = 0x22,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["printscr"] = 0x2C, ["pause"] = 0x13, ["break"] = 0x13,
            ["capslock"] = 0x14, ["caps"] = 0x14, ["numlock"] = 0x90, ["scrolllock"] = 0x91,
            ["menu"] = 0x5D, ["apps"] = 0x5D, ["contextmenu"] = 0x5D,
            ["plus"] = 0xBB, ["equals"] = 0xBB, ["minus"] = 0xBD, ["dash"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE, ["dot"] = 0xBE,
            ["semicolon"] = 0xBA, ["slash"] = 0xBF, ["backslash"] = 0xDC, ["backtick"] = 0xC0, ["tilde"] = 0xC0,
            ["quote"] = 0xDE, ["apostrophe"] = 0xDE, ["openbracket"] = 0xDB, ["closebracket"] = 0xDD,
            ["add"] = 0x6B, ["subtract"] = 0x6D, ["multiply"] = 0x6A, ["divide"] = 0x6F, ["decimal"] = 0x6E,
            ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["volumemute"] = 0xAD, ["mute"] = 0xAD,
            ["playpause"] = 0xB3, ["nexttrack"] = 0xB0, ["prevtrack"] = 0xB1, ["previoustrack"] = 0xB1, ["stopmedia"] = 0xB2,
            ["browserback"] = 0xA6, ["browserforward"] = 0xA7, ["browserrefresh"] = 0xA8, ["browserhome"] = 0xAC,
            [","] = 0xBC, ["."] = 0xBE, [";"] = 0xBA, ["="] = 0xBB, ["-"] = 0xBD, ["/"] = 0xBF, ["`"] = 0xC0, ["["] = 0xDB, ["\\"] = 0xDC,
            ["]"] = 0xDD, ["'"] = 0xDE, ["+"] = 0xBB,
        };
        for (var i = 1; i <= 24; i++) d["f" + i] = 0x6F + i;
        for (var i = 0; i <= 9; i++) { d["numpad" + i] = 0x60 + i; d["num" + i] = 0x60 + i; }
        return d;
    }

    /// <summary>Parses "Ctrl+Shift+T" (or "Ctrl+K Ctrl+C"). Throws <see cref="FormatException"/> with a plain-English message.</summary>
    public static IReadOnlyList<KeyChord> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Type the keys to press, for example Ctrl+C.");
        var normalized = Regex.Replace(text.Trim(), @"\s*\+\s*", "+");
        var chords = new List<KeyChord>();
        foreach (var raw in normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var chord = raw == "+" ? "plus" : raw.Replace("++", "+plus");
            var mods = new List<int>();
            var key = 0;
            foreach (var token in chord.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Modifiers.TryGetValue(token, out var m)) { if (!mods.Contains(m)) mods.Add(m); continue; }
                if (key != 0) throw new FormatException($"'{raw}' has more than one main key. Use + between modifiers and a single key, like Ctrl+Shift+T.");
                key = KeyFor(token) ?? throw new FormatException($"I don't know the key '{token}'. Use names like Enter, Tab, Esc, F5, Left, or a single letter or digit.");
            }
            if (key == 0)
            {
                if (mods.Count == 0) throw new FormatException($"Couldn't read '{raw}' as keys.");
                key = mods[^1];
                mods.RemoveAt(mods.Count - 1);
            }
            chords.Add(new KeyChord(mods, key));
        }
        return chords;
    }

    public static bool TryParse(string text, out IReadOnlyList<KeyChord> chords)
    {
        try { chords = Parse(text); return true; }
        catch (FormatException) { chords = Array.Empty<KeyChord>(); return false; }
    }

    private static int? KeyFor(string token)
    {
        if (Keys.TryGetValue(token, out var vk)) return vk;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        return null;
    }

    public static bool IsExtended(int vk) =>
        vk is >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90;

    public static string NameOf(int vk)
    {
        if (vk == Ctrl) return "Ctrl";
        if (vk == Shift) return "Shift";
        if (vk == Alt) return "Alt";
        if (vk == Win) return "Win";
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return Keys.Where(k => k.Value == vk && k.Key.Length > 1).OrderBy(k => k.Key.Length).Select(k => char.ToUpperInvariant(k.Key[0]) + k.Key[1..]).FirstOrDefault()
               ?? $"0x{vk:X2}";
    }
}
