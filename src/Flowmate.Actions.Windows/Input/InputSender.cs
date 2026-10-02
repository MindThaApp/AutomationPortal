using System.Runtime.InteropServices;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Input;

public enum MouseButton { Left, Right, Middle }

/// <summary>Sends keyboard and mouse input with SendInput. Mouse positions are physical screen pixels.</summary>
internal static class InputSender
{
    private static readonly int Size = Marshal.SizeOf<INPUT>();

    private static INPUT Key(int vk, bool up)
    {
        var flags = (up ? Win32.KEYEVENTF_KEYUP : 0u) | (KeyCombo.IsExtended(vk) ? Win32.KEYEVENTF_EXTENDEDKEY : 0u);
        return new INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)Win32.MapVirtualKeyW((uint)vk, 0), dwFlags = flags } },
        };
    }

    private static INPUT UnicodeKey(char c, bool up) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = Win32.KEYEVENTF_UNICODE | (up ? Win32.KEYEVENTF_KEYUP : 0u) } },
    };

    private static INPUT Mouse(uint flags, uint data = 0) => new()
    {
        type = Win32.INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } },
    };

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = Win32.SendInput((uint)inputs.Length, inputs, Size);
        if (sent != inputs.Length)
            throw new ActionException("Windows blocked the keyboard or mouse input. This usually happens when the app in front is running as administrator. Close it and run it normally, or run Flowmate as administrator.");
    }

    public static void TapKey(int vk) => Send(Key(vk, false), Key(vk, true));

    /// <summary>
    /// Lets go of Shift/Ctrl/Alt/Win that the user may still be holding (for example from the keyboard shortcut that started the shortcut),
    /// so they don't combine with the keys we send.
    /// </summary>
    public static async Task ReleaseHeldModifiersAsync(int waitMs = 400, CancellationToken ct = default)
    {
        int[] mods = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
        var until = Environment.TickCount64 + waitMs;
        while (mods.Any(IsDown) && Environment.TickCount64 < until) await Task.Delay(20, ct).ConfigureAwait(false);
        var stuck = mods.Where(IsDown).ToArray();
        if (stuck.Length > 0) Send(stuck.Select(v => Key(v, true)).ToArray());
    }

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    public static async Task PressAsync(IReadOnlyList<KeyChord> chords, int times, CancellationToken ct)
    {
        for (var n = 0; n < Math.Max(1, times); n++)
        {
            foreach (var chord in chords)
            {
                ct.ThrowIfCancellationRequested();
                var seq = new List<INPUT>();
                foreach (var m in chord.Modifiers) seq.Add(Key(m, false));
                seq.Add(Key(chord.Key, false));
                seq.Add(Key(chord.Key, true));
                for (var i = chord.Modifiers.Count - 1; i >= 0; i--) seq.Add(Key(chord.Modifiers[i], true));
                Send(seq.ToArray());
                if (chords.Count > 1 || times > 1) await Task.Delay(40, ct).ConfigureAwait(false);
            }
        }
    }

    public static async Task TypeAsync(string text, int delayMs, CancellationToken ct)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var batch = new List<INPUT>();
        void Flush() { if (batch.Count > 0) { Send(batch.ToArray()); batch.Clear(); } }

        foreach (var c in text)
        {
            ct.ThrowIfCancellationRequested();
            switch (c)
            {
                case '\n': batch.Add(Key(0x0D, false)); batch.Add(Key(0x0D, true)); break;
                case '\t': batch.Add(Key(0x09, false)); batch.Add(Key(0x09, true)); break;
                default: batch.Add(UnicodeKey(c, false)); batch.Add(UnicodeKey(c, true)); break;
            }
            if (delayMs > 0)
            {
                Flush();
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            else if (batch.Count >= 200) Flush();
        }
        Flush();
    }

    // ---- mouse -------------------------------------------------------------------------------

    public static (int X, int Y) CursorPosition()
    {
        using var _ = DpiScope.PerMonitorV2();
        Win32.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static (int Left, int Top, int Width, int Height) VirtualScreen()
    {
        using var _ = DpiScope.PerMonitorV2();
        return (Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
            Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
    }

    public static void MoveTo(int x, int y)
    {
        var (l, t, w, h) = VirtualScreen();
        if (x < l || y < t || x >= l + w || y >= t + h)
            throw new ActionException($"The point ({x}, {y}) is outside your screens. Pick a spot that is on screen.");
        using var _ = DpiScope.PerMonitorV2();
        Win32.SetCursorPos(x, y);
    }

    public static async Task ClickAsync(MouseButton button, int clicks, CancellationToken ct)
    {
        var (down, up) = button switch
        {
            MouseButton.Right => (Win32.MOUSEEVENTF_RIGHTDOWN, Win32.MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (Win32.MOUSEEVENTF_MIDDLEDOWN, Win32.MOUSEEVENTF_MIDDLEUP),
            _ => (Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP),
        };
        await Task.Delay(30, ct).ConfigureAwait(false);
        for (var i = 0; i < Math.Clamp(clicks, 1, 3); i++)
        {
            Send(Mouse(down), Mouse(up));
            if (i < clicks - 1) await Task.Delay(50, ct).ConfigureAwait(false);
        }
    }

    public static void Scroll(int notches, bool horizontal)
    {
        var data = unchecked((uint)(notches * 120));
        Send(Mouse(horizontal ? Win32.MOUSEEVENTF_HWHEEL : Win32.MOUSEEVENTF_WHEEL, data));
    }
}
