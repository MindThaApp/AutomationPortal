using System.Management;
using System.Runtime.InteropServices;
using Flowmate.Actions.Windows.Apps;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Devices;

internal sealed record MonitorInfo(IntPtr Handle, string DeviceName, Box Bounds, Box WorkArea, bool IsPrimary, string FriendlyName, bool IsInternal)
{
    public string Label(int index) => $"Display {index}: {FriendlyName}{(IsPrimary ? " (main)" : "")}";
}

internal sealed record DisplayTarget(string GdiName, string FriendlyName, LUID Adapter, uint TargetId, uint OutputTechnology)
{
    // INTERNAL = 0x80000000, DISPLAYPORT_EMBEDDED = 11, UDI_EMBEDDED = 13
    public bool IsInternal => OutputTechnology is 0x80000000u or 11u or 13u;
}

internal readonly record struct DisplayMode(int Width, int Height, int RefreshRate)
{
    public override string ToString() => $"{Width} x {Height}";
    public string Id => $"{Width}x{Height}";
}

/// <summary>Maps the Set Display Mode choices to Windows display topologies. Pure, unit-tested.</summary>
public static class DisplayModeMap
{
    public const uint SDC_TOPOLOGY_INTERNAL = 0x1, SDC_TOPOLOGY_CLONE = 0x2, SDC_TOPOLOGY_EXTEND = 0x4, SDC_TOPOLOGY_EXTERNAL = 0x8,
        SDC_APPLY = 0x80, SDC_ALLOW_CHANGES = 0x400;

    public static uint TopologyFlag(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "pc" => SDC_TOPOLOGY_INTERNAL,
        "duplicate" => SDC_TOPOLOGY_CLONE,
        "extend" => SDC_TOPOLOGY_EXTEND,
        "second" => SDC_TOPOLOGY_EXTERNAL,
        _ => throw new ActionException($"'{mode}' isn't a display mode Flowmate knows. Choose PC screen only, Duplicate, Extend or Second screen only."),
    };

    public static string Describe(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "pc" => "PC screen only",
        "duplicate" => "Duplicate",
        "extend" => "Extend",
        "second" => "Second screen only",
        _ => mode,
    };

    /// <summary>Parses "1920x1080" / "1920 x 1080" / "1920×1080".</summary>
    public static bool TryParseResolution(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Split(new[] { 'x', 'X', '×', '*' }, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0;
    }
}

internal static class DisplayService
{
    // ---- monitors ----------------------------------------------------------------------------

    public static List<MonitorInfo> GetMonitors()
    {
        using var _ = DpiScope.PerMonitorV2();
        var targets = GetActiveTargets();
        var list = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (Win32.GetMonitorInfoW(h, ref mi))
            {
                var target = targets.FirstOrDefault(t => t.GdiName.Equals(mi.szDevice, StringComparison.OrdinalIgnoreCase));
                var name = string.IsNullOrWhiteSpace(target?.FriendlyName) ? "Generic monitor" : target!.FriendlyName;
                list.Add(new MonitorInfo(h, mi.szDevice, ToBox(mi.rcMonitor), ToBox(mi.rcWork), (mi.dwFlags & 1) != 0, name, target?.IsInternal ?? false));
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Box ToBox(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    public static MonitorInfo? FindMonitor(IReadOnlyList<MonitorInfo> monitors, string? idOrAll)
    {
        if (string.IsNullOrWhiteSpace(idOrAll) || idOrAll == "all" || idOrAll == "primary") return monitors.FirstOrDefault(m => m.IsPrimary);
        return monitors.FirstOrDefault(m => m.DeviceName.Equals(idOrAll, StringComparison.OrdinalIgnoreCase))
               ?? (int.TryParse(idOrAll, out var n) && n >= 1 && n <= monitors.Count ? monitors[n - 1] : null);
    }

    public static async Task<IReadOnlyList<Choice>> MonitorChoicesAsync(bool includeAll, CancellationToken ct)
    {
        var monitors = await Task.Run(GetMonitors, ct).ConfigureAwait(false);
        var list = new List<Choice>();
        if (includeAll) list.Add(new Choice("all", "All displays"));
        for (var i = 0; i < monitors.Count; i++) list.Add(new Choice(monitors[i].DeviceName, monitors[i].Label(i + 1)));
        return list;
    }

    // ---- display configuration (names, HDR) --------------------------------------------------

    private static List<DC_PATH_INFO> QueryActivePaths()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (Win32.GetDisplayConfigBufferSizes(0x2, out var np, out var nm) != 0) return new();
            var paths = new DC_PATH_INFO[np];
            var modes = new DC_MODE_INFO[nm];
            var rc = Win32.QueryDisplayConfig(0x2, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (rc == 122) continue; // ERROR_INSUFFICIENT_BUFFER: configuration changed meanwhile
            if (rc != 0) return new();
            return paths.Take((int)np).ToList();
        }
        return new();
    }

    public static List<DisplayTarget> GetActiveTargets()
    {
        var result = new List<DisplayTarget>();
        foreach (var p in QueryActivePaths())
        {
            var src = new DC_SOURCE_NAME { header = new DC_HEADER { type = 1, size = (uint)Marshal.SizeOf<DC_SOURCE_NAME>(), adapterId = p.source.adapterId, id = p.source.id }, viewGdiDeviceName = "" };
            var tgt = new DC_TARGET_NAME { header = new DC_HEADER { type = 2, size = (uint)Marshal.SizeOf<DC_TARGET_NAME>(), adapterId = p.target.adapterId, id = p.target.id }, monitorFriendlyDeviceName = "", monitorDevicePath = "" };
            if (Win32.DisplayConfigGetDeviceInfo(ref src) != 0) continue;
            var friendly = Win32.DisplayConfigGetDeviceInfo(ref tgt) == 0 ? tgt.monitorFriendlyDeviceName : "";
            result.Add(new DisplayTarget(src.viewGdiDeviceName, friendly, p.target.adapterId, p.target.id, p.target.outputTechnology));
        }
        return result;
    }

    /// <summary>(supported, enabled) for HDR ("advanced colour") on one display; null when it can't be read.</summary>
    public static (bool Supported, bool Enabled)? GetHdr(DisplayTarget t)
    {
        var info = new DC_ADVANCED_COLOR_INFO { header = new DC_HEADER { type = 9, size = (uint)Marshal.SizeOf<DC_ADVANCED_COLOR_INFO>(), adapterId = t.Adapter, id = t.TargetId } };
        if (Win32.DisplayConfigGetDeviceInfo(ref info) != 0) return null;
        return ((info.value & 0x1) != 0, (info.value & 0x2) != 0);
    }

    public static bool SetHdr(DisplayTarget t, bool enable)
    {
        var set = new DC_SET_ADVANCED_COLOR
        {
            header = new DC_HEADER { type = 10, size = (uint)Marshal.SizeOf<DC_SET_ADVANCED_COLOR>(), adapterId = t.Adapter, id = t.TargetId },
            value = enable ? 1u : 0u,
        };
        return Win32.DisplayConfigSetDeviceInfo(ref set) == 0;
    }

    public static int SetTopology(string mode) =>
        Win32.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, DisplayModeMap.SDC_APPLY | DisplayModeMap.SDC_ALLOW_CHANGES | DisplayModeMap.TopologyFlag(mode));

    // ---- resolution / refresh rate -----------------------------------------------------------

    public static DisplayMode? CurrentMode(string deviceName)
    {
        var dm = DEVMODE.Create();
        if (!Win32.EnumDisplaySettingsExW(deviceName, Win32.ENUM_CURRENT_SETTINGS, ref dm, 0)) return null;
        return new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);
    }

    public static List<DisplayMode> SupportedModes(string deviceName)
    {
        var set = new HashSet<DisplayMode>();
        for (uint i = 0; ; i++)
        {
            var dm = DEVMODE.Create();
            if (!Win32.EnumDisplaySettingsExW(deviceName, i, ref dm, 0)) break;
            if (dm.dmBitsPerPel >= 32) set.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency));
        }
        return set.OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.RefreshRate).ToList();
    }

    /// <summary>Changes resolution and (optionally) refresh rate. Returns null on success or a plain-English problem.</summary>
    public static string? SetMode(string deviceName, int width, int height, int refreshRate)
    {
        var modes = SupportedModes(deviceName);
        var matching = modes.Where(m => m.Width == width && m.Height == height).ToList();
        if (matching.Count == 0)
            return $"This display can't show {width} x {height}. Pick one of the resolutions in the list.";
        DisplayMode chosen;
        if (refreshRate > 0)
        {
            var exact = matching.Where(m => m.RefreshRate == refreshRate).ToList();
            if (exact.Count == 0)
                return $"This display can't run {width} x {height} at {refreshRate} Hz. It supports {string.Join(", ", matching.Select(m => m.RefreshRate + " Hz").Distinct())}.";
            chosen = exact[0];
        }
        else
        {
            var cur = CurrentMode(deviceName)?.RefreshRate ?? 0;
            chosen = matching.FirstOrDefault(m => m.RefreshRate == cur);
            if (chosen.Width == 0) chosen = matching[0];
        }

        var dm = DEVMODE.Create();
        if (!Win32.EnumDisplaySettingsExW(deviceName, Win32.ENUM_CURRENT_SETTINGS, ref dm, 0)) return "Couldn't read the display's current settings.";
        dm.dmPelsWidth = (uint)chosen.Width;
        dm.dmPelsHeight = (uint)chosen.Height;
        dm.dmDisplayFrequency = (uint)chosen.RefreshRate;
        dm.dmFields = 0x80000 | 0x100000 | 0x400000; // width, height, frequency
        const uint CDS_UPDATEREGISTRY = 0x1, CDS_TEST = 0x2;
        var test = Win32.ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != 0) return "Windows says this display can't use that setting right now.";
        var rc = Win32.ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return rc switch
        {
            0 => null,
            1 => "The change needs a restart of the PC to apply.",
            _ => "Windows couldn't change the resolution.",
        };
    }

    // ---- DDC/CI (external monitor controls) --------------------------------------------------

    /// <summary>Runs <paramref name="action"/> on each physical monitor behind a display until it returns true. Returns whether any succeeded.</summary>
    public static bool WithPhysicalMonitors(MonitorInfo monitor, Func<IntPtr, bool> action)
    {
        if (!Win32.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out var count) || count == 0) return false;
        var arr = new PHYSICAL_MONITOR[count];
        if (!Win32.GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, arr)) return false;
        try
        {
            var any = false;
            foreach (var pm in arr)
            {
                try { any |= action(pm.hPhysicalMonitor); }
                catch { }
            }
            return any;
        }
        finally { Win32.DestroyPhysicalMonitors(count, arr); }
    }

    public static bool SetVcp(MonitorInfo monitor, byte code, uint value) =>
        WithPhysicalMonitors(monitor, h => Win32.SetVCPFeature(h, code, value));

    public static bool SetDdcBrightness(MonitorInfo monitor, int percent) =>
        WithPhysicalMonitors(monitor, h =>
        {
            uint max = 100;
            if (Win32.GetVCPFeatureAndVCPFeatureReply(h, 0x10, IntPtr.Zero, out _, out var m) && m > 0) max = m;
            return Win32.SetVCPFeature(h, 0x10, (uint)Math.Round(percent / 100.0 * max));
        });

    // ---- laptop brightness (WMI) -------------------------------------------------------------

    public static bool SetWmiBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            var any = false;
            foreach (ManagementObject m in searcher.Get())
            {
                using (m) { m.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Clamp(percent, 0, 100) }); any = true; }
            }
            return any;
        }
        catch (ManagementException) { return false; }
        catch (COMException) { return false; }
    }

    public static int? GetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject m in searcher.Get())
                using (m) return Convert.ToInt32(m["CurrentBrightness"]);
        }
        catch (ManagementException) { }
        catch (COMException) { }
        return null;
    }
}
