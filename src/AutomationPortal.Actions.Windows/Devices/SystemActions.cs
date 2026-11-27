using System.Management;
using AutomationPortal.Actions.Windows.Native;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace AutomationPortal.Actions.Windows.Devices;

public sealed class SetThemeAction : ShortcutAction
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.theme", Title = Strings.Get("SetThemeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "WeatherSunny24",
        Keywords = new[] { "dark mode", "light mode", "theme", "night", "appearance", "colour" },
        Summary = Strings.Get("SetThemeAction_Summary"),
        Description = Strings.Get("SetThemeAction_Description")
            + (PackageCapabilities.UnvirtualizedWritesAllowed ? "" : Strings.Get("SetThemeAction_Description_StoreNote")),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("SetThemeAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("SetThemeAction_Param_Mode_Label"), "dark", new Choice("light", Strings.Get("SetThemeAction_Param_Mode_Choice_Light")), new Choice("dark", Strings.Get("SetThemeAction_Param_Mode_Choice_Dark")), new Choice("toggle", Strings.Get("SetThemeAction_Param_Mode_Choice_Toggle"))),
            ParameterDescriptor.ChoiceOf("apply", Strings.Get("SetThemeAction_Param_Apply_Label"), "both", new Choice("both", Strings.Get("SetThemeAction_Param_Apply_Choice_Both")), new Choice("apps", Strings.Get("SetThemeAction_Param_Apply_Choice_Apps")), new Choice("windows", Strings.Get("SetThemeAction_Param_Apply_Choice_Windows"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        // The Microsoft Store package can't change the real Windows colour settings (its registry writes stay private), so don't pretend.
        if (!PackageCapabilities.UnvirtualizedWritesAllowed)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:colors") { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            throw new ActionException(Strings.Get("SetThemeAction_Error_Switching_Windows_Dark_Mode_Isn_T"));
        }
        var mode = await ctx.GetChoiceAsync("mode", "dark");
        var apply = await ctx.GetChoiceAsync("apply", "both");
        var light = await Task.Run(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: true)
                            ?? throw new ActionException(Strings.Get("SetThemeAction_Error_Couldn_T_Open_The_Windows_Colour"));
            var appsLight = Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) != 0;
            var wantLight = mode switch { "light" => true, "dark" => false, _ => !appsLight };
            var value = wantLight ? 1 : 0;
            if (apply is "both" or "apps") key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
            if (apply is "both" or "windows") key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
            Win32.SendMessageTimeoutW(Win32.HWND_BROADCAST, Win32.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", Win32.SMTO_ABORTIFHUNG, 5000, out _);
            return wantLight;
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Text(light ? "Light" : "Dark");
    }
}

public sealed class SetWallpaperAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.wallpaper", Title = Strings.Get("SetWallpaperAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Image24",
        Keywords = new[] { "background", "desktop", "picture", "image" },
        Summary = Strings.Get("SetWallpaperAction_Summary"),
        Description = Strings.Get("SetWallpaperAction_Description"),
        InputKinds = new[] { ContentKind.Image, ContentKind.File }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", Strings.Get("SetWallpaperAction_Param_Image_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("style", Strings.Get("SetWallpaperAction_Param_Style_Label"), "fill",
                new Choice("fill", Strings.Get("SetWallpaperAction_Param_Style_Choice_Fill")), new Choice("fit", Strings.Get("SetWallpaperAction_Param_Style_Choice_Fit")), new Choice("stretch", Strings.Get("SetWallpaperAction_Param_Style_Choice_Stretch")),
                new Choice("tile", Strings.Get("SetWallpaperAction_Param_Style_Choice_Tile")), new Choice("center", Strings.Get("SetWallpaperAction_Param_Style_Choice_Center")), new Choice("span", Strings.Get("SetWallpaperAction_Param_Style_Choice_Span"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    private static int Position(string style) => style switch { "center" => 0, "tile" => 1, "stretch" => 2, "fit" => 3, "span" => 5, _ => 4 };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        TestRootGuard.BlockSystemChange("changing the wallpaper");
        var files = await ctx.GetFilesAsync("image");
        var file = files.FirstOrDefault() ?? throw new ActionException(Strings.Get("SetWallpaperAction_Error_Choose_A_Picture_To_Use"));
        if (!file.Exists) throw new ActionException(Strings.Format("SetWallpaperAction_Error_Couldn_T_Find_The_Picture_0", file.FullName));
        var path = file.Path;
        var paths = ctx.Services.Paths;
        if (path.StartsWith(paths.TempRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Run temp folders are deleted after the run, so keep a copy that stays.
            var keep = Path.Combine(paths.RoamingRoot, "Wallpaper");
            Directory.CreateDirectory(keep);
            path = Path.Combine(keep, "wallpaper" + Path.GetExtension(path));
            File.Copy(file.Path, path, overwrite: true);
        }
        var position = Position(await ctx.GetChoiceAsync("style", "fill"));
        await Sta.Run(() =>
        {
            try
            {
                var wallpaper = ComGuids.Create<IDesktopWallpaper>(ComGuids.DesktopWallpaper);
                try
                {
                    wallpaper.SetPosition(position);
                    wallpaper.SetWallpaper(null, path);
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(wallpaper); }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or InvalidCastException)
            {
                // SPI_SETDESKWALLPAPER, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
                if (!Win32.SystemParametersInfoW(0x14, 0, path, 0x3))
                    throw new ActionException(Strings.Get("SetWallpaperAction_Error_Windows_Couldn_T_Set_That_Picture"), ex);
            }
        }).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class WifiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.wifi", Title = Strings.Get("WifiAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Wifi124",
        Keywords = new[] { "wireless", "network", "internet", "airplane" },
        Summary = Strings.Get("WifiAction_Summary"),
        Description = Strings.Get("WifiAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("WifiAction_OutputName"),
        Parameters = new[] { P.OnOffToggle("state", Strings.Get("WifiAction_Param_State_Label"), "on", "off") },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Bool(await NetworkService.SetRadioAsync(RadioKind.WiFi, await ctx.GetChoiceAsync("state", "toggle")).ConfigureAwait(false));
}

public sealed class BluetoothAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.bluetooth", Title = Strings.Get("BluetoothAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Bluetooth24",
        Keywords = new[] { "wireless", "headphones", "devices" },
        Summary = Strings.Get("BluetoothAction_Summary"),
        Description = Strings.Get("BluetoothAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("BluetoothAction_OutputName"),
        Parameters = new[] { P.OnOffToggle("state", Strings.Get("BluetoothAction_Param_State_Label"), "on", "off") },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Bool(await NetworkService.SetRadioAsync(RadioKind.Bluetooth, await ctx.GetChoiceAsync("state", "toggle")).ConfigureAwait(false));
}

public sealed class JoinWifiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.joinWifi", Title = Strings.Get("JoinWifiAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Wifi124",
        Keywords = new[] { "join", "network", "wireless", "switch" },
        Summary = Strings.Get("JoinWifiAction_Summary"),
        Description = Strings.Get("JoinWifiAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("JoinWifiAction_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "network", Label = Strings.Get("JoinWifiAction_Param_Style_Label"), Type = ParamType.Choice, Required = true,
                ChoicesProvider = async (_, ct) => (await NetworkService.GetSavedWifiNamesAsync(ct).ConfigureAwait(false)).Select(n => new Choice(n, n)).ToList(),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetChoiceAsync("network")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("JoinWifiAction_Error_Choose_Which_Wi_Fi_Network_To"));
        return ContentValue.Text(await NetworkService.JoinWifiAsync(name, ctx.CancellationToken).ConfigureAwait(false));
    }
}

public sealed class GetNetworkDetailsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.network", Title = Strings.Get("GetNetworkDetailsAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "GlobeDesktop24",
        Keywords = new[] { "wifi name", "ssid", "ip address", "internet", "online", "connection", "signal" },
        Summary = Strings.Get("GetNetworkDetailsAction_Summary"),
        Description = Strings.Get("GetNetworkDetailsAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("GetNetworkDetailsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetNetworkDetailsAction_Param_Info_Label"), "all",
                new Choice("all", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_All")), new Choice("name", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_Name")), new Choice("online", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_Online")),
                new Choice("type", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_Type")), new Choice("signal", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_Signal")), new Choice("ip", Strings.Get("GetNetworkDetailsAction_Param_Info_Choice_Ip"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "all");
        var n = await Task.Run(NetworkService.GetDetails, ctx.CancellationToken).ConfigureAwait(false);
        static ContentValue T(string? s) => s is null ? ContentValue.Empty : ContentValue.Text(s);
        switch (info)
        {
            case "name":
                if (n.Name.Length == 0 && n.Note is not null) throw new ActionException(n.Note);
                return ContentValue.Text(n.Name);
            case "online": return ContentValue.Bool(n.Internet);
            case "type": return ContentValue.Text(n.Type);
            case "signal": return n.SignalPercent is { } s ? ContentValue.Number(s) : ContentValue.Empty;
            case "ip": return T(n.IpAddress);
            default:
                return ContentValue.Dict(
                    ("Connected", ContentValue.Bool(n.Connected)),
                    ("Online", ContentValue.Bool(n.Internet)),
                    ("Type", ContentValue.Text(n.Type)),
                    ("Name", ContentValue.Text(n.Name)),
                    ("Signal Strength", n.SignalPercent is { } sp ? ContentValue.Number(sp) : ContentValue.Empty),
                    ("IP Address", T(n.IpAddress)),
                    ("Gateway", T(n.Gateway)),
                    ("DNS Servers", T(n.Dns)),
                    ("Link Speed (Mbps)", n.SpeedMbps is { } sm ? ContentValue.Number(sm) : ContentValue.Empty),
                    ("MAC Address", T(n.Mac)),
                    ("Adapter", T(n.Adapter)),
                    ("Note", T(n.Note)));
        }
    }
}

public sealed class GetDeviceDetailsAction : ShortcutAction
{
    private static readonly string[] Infos = { "all", "name", "manufacturer", "model", "windows", "processor", "memory", "graphics", "uptime", "user", "screens" };

    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.details", Title = Strings.Get("GetDeviceDetailsAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Laptop24",
        Keywords = new[] { "pc name", "computer name", "model", "windows version", "ram", "cpu", "specs", "uptime" },
        Summary = Strings.Get("GetDeviceDetailsAction_Summary"),
        Description = Strings.Get("GetDeviceDetailsAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("GetDeviceDetailsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetDeviceDetailsAction_Param_Info_Label"), "all",
                new Choice("all", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_All")), new Choice("name", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Name")), new Choice("manufacturer", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Manufacturer")), new Choice("model", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Model")),
                new Choice("windows", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Windows")), new Choice("processor", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Processor")), new Choice("memory", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Memory")),
                new Choice("graphics", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Graphics")), new Choice("uptime", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Uptime")), new Choice("user", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_User")), new Choice("screens", Strings.Get("GetDeviceDetailsAction_Param_Info_Choice_Screens"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "all");
        if (!Infos.Contains(info)) info = "all";
        var d = await Task.Run(Collect, ctx.CancellationToken).ConfigureAwait(false);
        string[] map = info switch
        {
            "name" => new[] { "Device Name" },
            "manufacturer" => new[] { "Manufacturer" },
            "model" => new[] { "Model" },
            "windows" => new[] { "Windows" },
            "processor" => new[] { "Processor" },
            "memory" => new[] { "Memory (GB)" },
            "graphics" => new[] { "Graphics" },
            "uptime" => new[] { "Hours Since Startup" },
            "user" => new[] { "User Name" },
            "screens" => new[] { "Screens" },
            _ => Array.Empty<string>(),
        };
        if (map.Length == 1) return d.First(e => e.Key == map[0]).Value;
        return ContentValue.Dict(d);
    }

    private static List<KeyValuePair<string, ContentValue>> Collect()
    {
        string manufacturer = "", model = "", processor = "";
        double memoryGb = 0;
        var graphics = new List<string>();
        try
        {
            using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem"))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        manufacturer = o["Manufacturer"]?.ToString()?.Trim() ?? "";
                        model = o["Model"]?.ToString()?.Trim() ?? "";
                        memoryGb = Math.Round(Convert.ToDouble(o["TotalPhysicalMemory"] ?? 0) / (1024.0 * 1024 * 1024), 1);
                    }
            using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                foreach (ManagementObject o in s.Get()) using (o) { processor = o["Name"]?.ToString()?.Trim() ?? ""; break; }
            using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                foreach (ManagementObject o in s.Get()) using (o) { if (o["Name"]?.ToString() is { Length: > 0 } n) graphics.Add(n.Trim()); }
        }
        catch (ManagementException) { }
        catch (System.Runtime.InteropServices.COMException) { }

        var screens = DisplayService.GetMonitors().Select((m, i) =>
        {
            var mode = DisplayService.CurrentMode(m.DeviceName);
            return $"{m.FriendlyName}{(mode is { } md ? $" ({md.Width} x {md.Height})" : "")}";
        });

        return new List<KeyValuePair<string, ContentValue>>
        {
            new("Device Name", Environment.MachineName),
            new("Manufacturer", manufacturer),
            new("Model", model),
            new("Windows", WindowsVersionText()),
            new("Processor", processor),
            new("Memory (GB)", memoryGb),
            new("Graphics", string.Join(", ", graphics)),
            new("Hours Since Startup", Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, 1)),
            new("User Name", Environment.UserName),
            new("Screens", string.Join(", ", screens)),
        };
    }

    internal static string WindowsVersionText()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName")?.ToString() ?? "Windows";
        var display = key?.GetValue("DisplayVersion")?.ToString() ?? key?.GetValue("ReleaseId")?.ToString();
        var build = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
        if (int.TryParse(build, out var b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return $"{product}{(display is null ? "" : " " + display)} (build {build})";
    }
}
