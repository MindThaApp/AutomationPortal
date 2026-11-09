using System.Runtime.InteropServices;
using System.Text;
using Flowmate.Triggers.Native;

namespace Flowmate.Triggers;

/// <summary>Sends keystrokes with SendInput. Everything sent here is flagged as injected, which Flowmate's own keyboard hooks ignore.</summary>
internal static class InputSender
{
    private static readonly int Size = Marshal.SizeOf<Win32.INPUT>();

    private static Win32.INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static bool IsExtended(int vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or 0xA3 or 0xA5;

    public static void Send(List<Win32.INPUT> inputs)
    {
        for (var i = 0; i < inputs.Count; i += 64)
        {
            var chunk = inputs.Skip(i).Take(64).ToArray();
            Win32.SendInput((uint)chunk.Length, chunk, Size);
            if (inputs.Count > 64) Thread.Sleep(3);
        }
    }

    public static void AddKeyPress(List<Win32.INPUT> list, int vk)
    {
        var ext = IsExtended(vk) ? Win32.KEYEVENTF_EXTENDEDKEY : 0u;
        list.Add(Key((ushort)vk, 0, ext));
        list.Add(Key((ushort)vk, 0, ext | Win32.KEYEVENTF_KEYUP));
    }

    public static void PressKey(int vk, int times = 1)
    {
        var list = new List<Win32.INPUT>();
        for (var i = 0; i < times; i++) AddKeyPress(list, vk);
        Send(list);
    }

    /// <summary>Presses modifiers + key together, e.g. Ctrl+V.</summary>
    public static void PressChord(HotkeyChord chord)
    {
        var list = new List<Win32.INPUT>();
        var mods = new List<int>();
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods.Add(Win32.VK_CONTROL);
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods.Add(Win32.VK_MENU);
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods.Add(Win32.VK_SHIFT);
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Win)) mods.Add(Win32.VK_LWIN);
        foreach (var m in mods) list.Add(Key((ushort)m, 0, IsExtended(m) ? Win32.KEYEVENTF_EXTENDEDKEY : 0));
        AddKeyPress(list, chord.VirtualKey);
        for (var i = mods.Count - 1; i >= 0; i--)
            list.Add(Key((ushort)mods[i], 0, Win32.KEYEVENTF_KEYUP | (IsExtended(mods[i]) ? Win32.KEYEVENTF_EXTENDEDKEY : 0)));
        Send(list);
    }

    /// <summary>Types text as Unicode keystrokes. New lines become Enter, tabs become Tab.</summary>
    public static void TypeText(string text)
    {
        var list = new List<Win32.INPUT>(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; AddKeyPress(list, Win32.VK_RETURN); }
            else if (c == '\n') AddKeyPress(list, Win32.VK_RETURN);
            else if (c == '\t') AddKeyPress(list, Win32.VK_TAB);
            else
            {
                list.Add(Key(0, c, Win32.KEYEVENTF_UNICODE));
                list.Add(Key(0, c, Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP));
            }
        }
        Send(list);
    }

    /// <summary>Waits until Shift, Ctrl, Alt and Win are all released (or the timeout passes). Returns true when they are up.</summary>
    public static bool WaitForModifiersReleased(int timeoutMs = 600)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var down = IsDown(Win32.VK_SHIFT) || IsDown(Win32.VK_CONTROL) || IsDown(Win32.VK_MENU) || IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN);
            if (!down) return true;
            if (Environment.TickCount64 > until) return false;
            Thread.Sleep(15);
        }
    }

    public static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool WaitForKeyReleased(int vk, int timeoutMs = 400)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while ((Win32.GetAsyncKeyState(vk) & 0x8000) != 0)
        {
            if (Environment.TickCount64 > until) return false;
            Thread.Sleep(10);
        }
        return true;
    }
}

/// <summary>Turns a key event into the text it produces on the current keyboard layout, without disturbing dead-key state.</summary>
internal static class KeyTranslator
{
    /// <summary>Printable text for the key as typed right now. Empty for keys that type nothing (arrows, Ctrl combinations, dead keys …).</summary>
    public static string ToText(uint vk, uint scanCode, IntPtr foreground)
    {
        var ctrl = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
        var alt = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
        var win = InputSender.IsDown(Win32.VK_LWIN) || InputSender.IsDown(Win32.VK_RWIN);
        if (win) return "";
        if (ctrl != alt) return ""; // Ctrl or Alt alone is a command; Ctrl+Alt together is AltGr, which does type characters.

        var state = new byte[256];
        if ((Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0) state[Win32.VK_SHIFT] = 0x80;
        if (ctrl) { state[Win32.VK_CONTROL] = 0x80; state[Win32.VK_MENU] = 0x80; }
        if ((Win32.GetKeyState(Win32.VK_CAPITAL) & 1) != 0) state[Win32.VK_CAPITAL] = 1;
        var layout = Win32.GetKeyboardLayout(foreground == IntPtr.Zero ? 0 : Win32.GetWindowThreadProcessId(foreground, out _));
        var sb = new StringBuilder(8);
        // Flag 4: don't change the keyboard state (so dead keys keep working for the real typing).
        var n = Win32.ToUnicodeEx(vk, scanCode, state, sb, sb.Capacity, 4, layout);
        if (n <= 0) return "";
        var s = sb.ToString(0, Math.Min(n, sb.Length));
        return s.Length > 0 && !char.IsControl(s[0]) ? s : "";
    }
}
