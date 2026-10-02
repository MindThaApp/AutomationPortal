using System.Runtime.InteropServices;

namespace Flowmate.Actions.Windows.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint length, flags, showCmd;
    public POINT ptMinPosition, ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor, rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEVMODE
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
    public uint dmFields;
    public int dmPositionX, dmPositionY;
    public uint dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public ushort dmLogPixels;
    public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent,
        dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;

    public static DEVMODE Create() => new() { dmDeviceName = "", dmFormName = "", dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID { public uint Low; public int High; }

[StructLayout(LayoutKind.Sequential)]
internal struct DC_PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }

[StructLayout(LayoutKind.Sequential)]
internal struct DC_PATH_TARGET
{
    public LUID adapterId;
    public uint id, modeInfoIdx, outputTechnology, rotation, scaling, refreshNum, refreshDen, scanLineOrdering;
    public int targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DC_PATH_INFO { public DC_PATH_SOURCE source; public DC_PATH_TARGET target; public uint flags; }

[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct DC_MODE_INFO { public uint infoType, id; public LUID adapterId; }

[StructLayout(LayoutKind.Sequential)]
internal struct DC_HEADER { public uint type, size; public LUID adapterId; public uint id; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DC_SOURCE_NAME
{
    public DC_HEADER header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DC_TARGET_NAME
{
    public DC_HEADER header;
    public uint flags, outputTechnology;
    public ushort edidManufactureId, edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DC_ADVANCED_COLOR_INFO { public DC_HEADER header; public uint value, colorEncoding, bitsPerColorChannel; }

[StructLayout(LayoutKind.Sequential)]
internal struct DC_SET_ADVANCED_COLOR { public DC_HEADER header; public uint value; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct PHYSICAL_MONITOR
{
    public IntPtr hPhysicalMonitor;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
    public uint BatteryLifeTime, BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SHQUERYRBINFO { public uint cbSize; public long i64Size, i64NumItems; }

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT { public uint type; public InputUnion u; }

[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p1, p2; }

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY { public Guid fmtid; public uint pid; }

internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

internal static class Win32
{
    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_SHOWNOACTIVATE = 4,
        SW_SHOW = 5, SW_MINIMIZE = 6, SW_SHOWMINNOACTIVE = 7, SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2), HWND_TOP = IntPtr.Zero, HWND_BROADCAST = new(0xFFFF);
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    public const long WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000L, WS_EX_APPWINDOW = 0x40000;
    public const uint GW_OWNER = 4, GW_HWNDNEXT = 2;
    public const uint WM_CLOSE = 0x10, WM_SYSCOMMAND = 0x112, WM_SETTINGCHANGE = 0x1A;
    public const int SC_MONITORPOWER = 0xF170;
    public const uint SMTO_ABORTIFHUNG = 0x2;
    public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public static readonly IntPtr DPI_PER_MONITOR_V2 = new(-4);
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, KEYEVENTF_SCANCODE = 0x8;
    public const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8,
        MOUSEEVENTF_RIGHTUP = 0x10, MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_WHEEL = 0x800,
        MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF;

    // ---- user32: windows
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] text, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, [Out] char[] text, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, string? lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint action, uint param, string? pv, uint flags);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr hdc, int x, int y);

    // ---- user32: monitors & display settings
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettingsExW(string? device, uint mode, ref DEVMODE dm, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsExW(string? device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DC_PATH_INFO[] paths, ref uint modeCount, [Out] DC_MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfo(ref DC_SOURCE_NAME info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfo(ref DC_TARGET_NAME info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfo(ref DC_ADVANCED_COLOR_INFO info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] public static extern int DisplayConfigSetDeviceInfo(ref DC_SET_ADVANCED_COLOR info);
    [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);

    // ---- user32: input
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern short VkKeyScanW(char ch);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);

    // ---- dwmapi
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    // ---- kernel32
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint state);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    // ---- powrprof
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(Guid scheme);
    [DllImport("powrprof.dll")] public static extern uint PowerGetActualOverlayScheme(out Guid scheme);

    // ---- shell32 / ole
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object store);
    [DllImport("shell32.dll")]
    public static extern int SHCreateShellItemArrayFromShellItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object array);
    [DllImport("propsys.dll")] public static extern int PropVariantToStringAlloc(ref PROPVARIANT pv, out IntPtr str);
    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);

    // ---- dxva2 (DDC/CI)
    [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);
    [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);

    public static string GetWindowText(IntPtr hWnd)
    {
        var len = GetWindowTextLengthW(hWnd);
        if (len <= 0) return "";
        var buf = new char[len + 1];
        var n = GetWindowTextW(hWnd, buf, buf.Length);
        return new string(buf, 0, n);
    }

    public static string GetClassName(IntPtr hWnd)
    {
        var buf = new char[256];
        var n = GetClassNameW(hWnd, buf, buf.Length);
        return new string(buf, 0, n);
    }

    public static string? GetProcessPath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[1024];
            var size = (uint)buf.Length;
            return QueryFullProcessImageNameW(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
        }
        finally { CloseHandle(h); }
    }

    public static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { CoTaskMemFree(p); }
    }
}

/// <summary>Makes the current thread PerMonitorV2 DPI aware so coordinates are physical pixels, and restores the old mode on dispose.</summary>
internal readonly struct DpiScope : IDisposable
{
    private readonly IntPtr _previous;
    private DpiScope(IntPtr previous) => _previous = previous;
    public static DpiScope PerMonitorV2() => new(Win32.SetThreadDpiAwarenessContext(Win32.DPI_PER_MONITOR_V2));
    public void Dispose() { if (_previous != IntPtr.Zero) Win32.SetThreadDpiAwarenessContext(_previous); }
}
