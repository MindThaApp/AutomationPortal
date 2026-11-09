namespace AutomationPortal.Actions.Windows.Devices;

/// <summary>DDC/CI "input source" values (VCP code 0x60) for the Switch Monitor Input action.</summary>
public static class MonitorInputs
{
    public static readonly IReadOnlyList<(string Id, string Label, uint Code)> Known = new[]
    {
        ("hdmi1", "HDMI 1", 0x11u), ("hdmi2", "HDMI 2", 0x12u),
        ("dp1", "DisplayPort 1", 0x0Fu), ("dp2", "DisplayPort 2", 0x10u),
        ("usbc", "USB-C", 0x1Bu),
        ("dvi1", "DVI 1", 0x03u), ("dvi2", "DVI 2", 0x04u),
        ("vga1", "VGA 1", 0x01u), ("vga2", "VGA 2", 0x02u),
    };

    public static uint? CodeFor(string id) =>
        Known.Where(k => k.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase)).Select(k => (uint?)k.Code).FirstOrDefault();
}

public sealed class SetDisplayModeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.displayMode", Title = "Set Display Mode", Category = ActionCategories.DeviceAndSettings, Icon = "ScreenSearch24",
        Keywords = new[] { "project", "second screen", "duplicate", "extend", "monitor", "projector", "tv" },
        Summary = "Show the desktop on {mode}",
        Description = "Switches between using only this PC's screen, duplicating it, extending it onto a second screen, or using only the second screen (like Win+P).",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "Display mode", "extend",
                new Choice("pc", "PC screen only"), new Choice("duplicate", "both screens (duplicate)"),
                new Choice("extend", "both screens (extend)"), new Choice("second", "second screen only")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var mode = await ctx.GetChoiceAsync("mode", "extend");
        var label = DisplayModeMap.Describe(mode);
        var rc = await Task.Run(() => DisplayService.SetTopology(mode), ctx.CancellationToken).ConfigureAwait(false);
        if (rc != 0)
            throw new ActionException($"Windows couldn't switch to '{label}'. Make sure the second screen is connected and turned on, then try again.");
        return ContentValue.Empty;
    }
}

public sealed class SetResolutionAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.resolution", Title = "Set Screen Resolution & Refresh Rate", Category = ActionCategories.DeviceAndSettings, Icon = "DesktopFlow24",
        Keywords = new[] { "resolution", "refresh rate", "hz", "screen size", "display", "1080p", "4k" },
        Summary = "Set {monitor} to {resolution}",
        Description = "Changes how many pixels a screen shows, and optionally its refresh rate. Windows may ask to keep the change if it can't be tested.",
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "monitor", Label = "Screen", Type = ParamType.Choice, Default = ParamValue.Choice("primary"),
                ChoicesProvider = async (_, ct) =>
                {
                    var list = (await DisplayService.MonitorChoicesAsync(false, ct).ConfigureAwait(false)).ToList();
                    list.Insert(0, new Choice("primary", "the main screen"));
                    return list;
                },
            },
            new ParameterDescriptor
            {
                Key = "resolution", Label = "Resolution", Type = ParamType.Choice, Required = true, Placeholder = "1920x1080",
                ChoicesProvider = (_, ct) => Task.Run<IReadOnlyList<Choice>>(() =>
                {
                    var primary = DisplayService.GetMonitors().FirstOrDefault(m => m.IsPrimary);
                    if (primary is null) return Array.Empty<Choice>();
                    return DisplayService.SupportedModes(primary.DeviceName)
                        .Select(m => new Choice(m.Id, m.ToString())).DistinctBy(c => c.Id).ToList();
                }, ct),
            },
            ParameterDescriptor.Number("refresh", "Refresh rate (0 = keep current)", 0, 0, 500, "Hz"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetChoiceAsync("resolution");
        if (!DisplayModeMap.TryParseResolution(text, out var w, out var h))
            throw new ActionException("Choose a resolution like 1920 x 1080.");
        var hz = (int)Math.Round(await ctx.GetNumberAsync("refresh", 0));
        var monitorId = await ctx.GetChoiceAsync("monitor", "primary");
        var problem = await Task.Run(() =>
        {
            var monitors = DisplayService.GetMonitors();
            var monitor = DisplayService.FindMonitor(monitors, monitorId)
                          ?? throw new ActionException("Couldn't find that screen. Check it is connected, then choose it again.");
            return DisplayService.SetMode(monitor.DeviceName, w, h, hz);
        }, ctx.CancellationToken).ConfigureAwait(false);
        if (problem is not null) throw new ActionException(problem);
        return ContentValue.Empty;
    }
}

public sealed class SetHdrAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.hdr", Title = "Turn HDR On/Off", Category = ActionCategories.DeviceAndSettings, Icon = "Hdr24",
        Keywords = new[] { "hdr", "high dynamic range", "colour", "color", "display", "game" },
        Summary = "Turn HDR {state} for {monitor}",
        Description = "Turns HDR (high dynamic range) on or off for screens that support it. Gives back true when HDR is on afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "HDR On",
        Parameters = new[]
        {
            P.OnOffToggle("state", "HDR", "on", "off"),
            new ParameterDescriptor
            {
                Key = "monitor", Label = "Screen", Type = ParamType.Choice, Default = ParamValue.Choice("all"),
                ChoicesProvider = (_, ct) => DisplayService.MonitorChoicesAsync(true, ct),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var state = await ctx.GetChoiceAsync("state", "toggle");
        var monitorId = await ctx.GetChoiceAsync("monitor", "all");
        var on = await Task.Run(() =>
        {
            var targets = DisplayService.GetActiveTargets();
            if (monitorId != "all" && !string.IsNullOrWhiteSpace(monitorId))
            {
                var monitor = DisplayService.FindMonitor(DisplayService.GetMonitors(), monitorId)
                              ?? throw new ActionException("Couldn't find that screen. Check it is connected, then choose it again.");
                targets = targets.Where(t => t.GdiName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            bool? result = null;
            foreach (var t in targets)
            {
                if (DisplayService.GetHdr(t) is not { Supported: true } info) continue;
                var want = Toggle.Resolve(state, info.Enabled);
                if (want == info.Enabled) { result ??= want; continue; }
                if (!DisplayService.SetHdr(t, want))
                    throw new ActionException("Windows wouldn't change HDR. Check that the screen is HDR-ready and try turning it on in Settings > System > Display.");
                result ??= want;
            }
            return result ?? throw new ActionException("None of your screens support HDR, or HDR isn't available right now.");
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Bool(on);
    }
}

public sealed class SwitchMonitorInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.monitorInput", Title = "Switch Monitor Input", Category = ActionCategories.DeviceAndSettings, Icon = "PlugConnected24",
        Keywords = new[] { "monitor", "hdmi", "displayport", "input source", "ddc", "kvm", "switch" },
        Summary = "Switch {monitor} to {input}",
        Description = "Tells an external monitor to show a different input (HDMI, DisplayPort…), like pressing the buttons on the monitor. The monitor must support DDC/CI, which you may need to turn on in its own menu. The screen goes blank if nothing is plugged into that input.",
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "monitor", Label = "Monitor", Type = ParamType.Choice, Default = ParamValue.Choice("primary"),
                ChoicesProvider = async (_, ct) =>
                {
                    var list = (await DisplayService.MonitorChoicesAsync(false, ct).ConfigureAwait(false)).ToList();
                    list.Insert(0, new Choice("primary", "the main monitor"));
                    return list;
                },
            },
            ParameterDescriptor.ChoiceOf("input", "Input", "hdmi1",
                MonitorInputs.Known.Select(k => new Choice(k.Id, k.Label)).Append(new Choice("custom", "Other (enter a code)")).ToArray()),
            ParameterDescriptor.Number("code", "Input code (see your monitor's manual)", 17, 0, 255) with { VisibleWhen = new VisibleWhen("input", "custom") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetChoiceAsync("input", "hdmi1");
        uint code;
        if (input == "custom") code = (uint)Math.Clamp(await ctx.GetNumberAsync("code", 17), 0, 255);
        else code = MonitorInputs.CodeFor(input) ?? throw new ActionException($"I don't know the input '{input}'. Choose one from the list.");
        var monitorId = await ctx.GetChoiceAsync("monitor", "primary");
        var ok = await Task.Run(() =>
        {
            var monitor = DisplayService.FindMonitor(DisplayService.GetMonitors(), monitorId)
                          ?? throw new ActionException("Couldn't find that monitor. Check it is connected, then choose it again.");
            return DisplayService.SetVcp(monitor, 0x60, code);
        }, ctx.CancellationToken).ConfigureAwait(false);
        if (!ok)
            throw new ActionException("The monitor didn't accept the change. Turn on DDC/CI in the monitor's own menu, and check the cable supports it (some docks and adapters block it).");
        return ContentValue.Empty;
    }
}

public sealed class SetBrightnessAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.brightness", Title = "Set Brightness", Category = ActionCategories.DeviceAndSettings, Icon = "BrightnessHigh24",
        Keywords = new[] { "dim", "screen", "backlight", "monitor", "night" },
        Summary = "Set brightness to {level}",
        Description = "Sets screen brightness. Works on laptop screens, and on external monitors that support DDC/CI (turn it on in the monitor's own menu).",
        OutputKind = ContentKind.Number, OutputName = "Brightness",
        Parameters = new[]
        {
            P.Percent("level", "Brightness", 70),
            new ParameterDescriptor
            {
                Key = "monitor", Label = "Screen", Type = ParamType.Choice, Default = ParamValue.Choice("all"),
                ChoicesProvider = (_, ct) => DisplayService.MonitorChoicesAsync(true, ct),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 70));
        if (level is < 0 or > 100) throw new ActionException("Brightness has to be between 0 and 100.");
        var monitorId = await ctx.GetChoiceAsync("monitor", "all");
        var changed = await Task.Run(() =>
        {
            var monitors = DisplayService.GetMonitors();
            if (monitorId == "all" || string.IsNullOrWhiteSpace(monitorId))
            {
                var any = DisplayService.SetWmiBrightness(level);
                foreach (var m in monitors) any |= DisplayService.SetDdcBrightness(m, level);
                return any;
            }
            var monitor = DisplayService.FindMonitor(monitors, monitorId)
                          ?? throw new ActionException("Couldn't find that screen. Check it is connected, then choose it again.");
            return monitor.IsInternal ? DisplayService.SetWmiBrightness(level) : DisplayService.SetDdcBrightness(monitor, level);
        }, ctx.CancellationToken).ConfigureAwait(false);
        if (!changed)
            throw new ActionException("Couldn't change the brightness. Desktop screens only work if the monitor supports DDC/CI; turn it on in the monitor's own menu and try again.");
        return ContentValue.Number(level);
    }
}
