using System.Management;
using Flowmate.Actions.Windows.Native;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace Flowmate.Actions.Windows.Devices;

public sealed class SetThemeAction : ShortcutAction
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.theme", Title = "Set Light/Dark Mode", Category = ActionCategories.DeviceAndSettings, Icon = "WeatherSunny24",
        Keywords = new[] { "dark mode", "light mode", "theme", "night", "appearance", "colour" },
        Summary = "Switch to {mode} mode",
        Description = "Switches Windows and apps between light and dark mode.",
        OutputKind = ContentKind.Text, OutputName = "Mode",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "Mode", "dark", new Choice("light", "light"), new Choice("dark", "dark"), new Choice("toggle", "the other")),
            ParameterDescriptor.ChoiceOf("apply", "Change", "both", new Choice("both", "Apps and Windows (taskbar, Start)"), new Choice("apps", "Apps only"), new Choice("windows", "Windows only")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var mode = await ctx.GetChoiceAsync("mode", "dark");
        var apply = await ctx.GetChoiceAsync("apply", "both");
        var light = await Task.Run(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: true)
                            ?? throw new ActionException("Couldn't open the Windows colour settings.");
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
        Id = "dev.wallpaper", Title = "Set Wallpaper", Category = ActionCategories.DeviceAndSettings, Icon = "Image24",
        Keywords = new[] { "background", "desktop", "picture", "image" },
        Summary = "Set the wallpaper to {image}",
        Description = "Uses a picture as the desktop wallpaper on all screens.",
        InputKinds = new[] { ContentKind.Image, ContentKind.File }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", "Picture", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("style", "Fit", "fill",
                new Choice("fill", "Fill"), new Choice("fit", "Fit"), new Choice("stretch", "Stretch"),
                new Choice("tile", "Tile"), new Choice("center", "Center"), new Choice("span", "Span across screens")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    private static int Position(string style) => style switch { "center" => 0, "tile" => 1, "stretch" => 2, "fit" => 3, "span" => 5, _ => 4 };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await ctx.GetFilesAsync("image");
        var file = files.FirstOrDefault() ?? throw new ActionException("Choose a picture to use as the wallpaper.");
        if (!file.Exists) throw new ActionException($"Couldn't find the picture '{file.FullName}'. Check the file still exists.");
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
                    throw new ActionException("Windows couldn't set that picture as the wallpaper. Check it is a normal picture file (JPG, PNG or BMP).", ex);
            }
        }).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class WifiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.wifi", Title = "Turn Wi-Fi On/Off", Category = ActionCategories.DeviceAndSettings, Icon = "Wifi124",
        Keywords = new[] { "wireless", "network", "internet", "airplane" },
        Summary = "Turn Wi-Fi {state}",
        Description = "Switches the PC's Wi-Fi on or off. Gives back true when Wi-Fi is on afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "Wi-Fi On",
        Parameters = new[] { P.OnOffToggle("state", "Wi-Fi", "on", "off") },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Bool(await NetworkService.SetRadioAsync(RadioKind.WiFi, await ctx.GetChoiceAsync("state", "toggle")).ConfigureAwait(false));
}

public sealed class BluetoothAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.bluetooth", Title = "Turn Bluetooth On/Off", Category = ActionCategories.DeviceAndSettings, Icon = "Bluetooth24",
        Keywords = new[] { "wireless", "headphones", "devices" },
        Summary = "Turn Bluetooth {state}",
        Description = "Switches the PC's Bluetooth on or off. Gives back true when Bluetooth is on afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "Bluetooth On",
        Parameters = new[] { P.OnOffToggle("state", "Bluetooth", "on", "off") },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Bool(await NetworkService.SetRadioAsync(RadioKind.Bluetooth, await ctx.GetChoiceAsync("state", "toggle")).ConfigureAwait(false));
}

public sealed class JoinWifiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.joinWifi", Title = "Connect to Wi-Fi", Category = ActionCategories.DeviceAndSettings, Icon = "Wifi124",
        Keywords = new[] { "join", "network", "wireless", "switch" },
        Summary = "Connect to {network}",
        Description = "Connects to a Wi-Fi network this PC already knows (one you have connected to before). Wi-Fi has to be turned on.",
        OutputKind = ContentKind.Text, OutputName = "Wi-Fi Network",
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "network", Label = "Network", Type = ParamType.Choice, Required = true,
                ChoicesProvider = async (_, ct) => (await NetworkService.GetSavedWifiNamesAsync(ct).ConfigureAwait(false)).Select(n => new Choice(n, n)).ToList(),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetChoiceAsync("network")).Trim();
        if (name.Length == 0) throw new ActionException("Choose which Wi-Fi network to connect to.");
        return ContentValue.Text(await NetworkService.JoinWifiAsync(name, ctx.CancellationToken).ConfigureAwait(false));
    }
}

public sealed class GetNetworkDetailsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.network", Title = "Get Network Details", Category = ActionCategories.DeviceAndSettings, Icon = "GlobeDesktop24",
        Keywords = new[] { "wifi name", "ssid", "ip address", "internet", "online", "connection", "signal" },
        Summary = "Get network {info}",
        Description = "Gets details about the current connection: whether you're online, the network name, the connection type, signal strength and IP address. On Windows 11 the Wi-Fi name only shows if Flow Mate is allowed to use your location.",
        OutputKind = ContentKind.Dictionary, OutputName = "Network Details",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "all",
                new Choice("all", "details (all of it)"), new Choice("name", "name"), new Choice("online", "online (true/false)"),
                new Choice("type", "type (Wi-Fi, Ethernet…)"), new Choice("signal", "signal strength (percent)"), new Choice("ip", "IP address")),
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
        Id = "dev.details", Title = "Get Device Details", Category = ActionCategories.DeviceAndSettings, Icon = "Laptop24",
        Keywords = new[] { "pc name", "computer name", "model", "windows version", "ram", "cpu", "specs", "uptime" },
        Summary = "Get device {info}",
        Description = "Gets facts about this PC: its name, make and model, Windows version, processor, memory, graphics, how long it has been on, and its screens.",
        OutputKind = ContentKind.Dictionary, OutputName = "Device Details",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "all",
                new Choice("all", "details (all of it)"), new Choice("name", "name"), new Choice("manufacturer", "maker"), new Choice("model", "model"),
                new Choice("windows", "Windows version"), new Choice("processor", "processor"), new Choice("memory", "memory (GB)"),
                new Choice("graphics", "graphics card"), new Choice("uptime", "hours since startup"), new Choice("user", "user name"), new Choice("screens", "screens")),
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
