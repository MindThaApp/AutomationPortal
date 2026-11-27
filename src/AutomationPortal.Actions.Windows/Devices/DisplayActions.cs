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
        Id = "dev.displayMode", Title = Strings.Get("SetDisplayModeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "ScreenSearch24",
        Keywords = new[] { "project", "second screen", "duplicate", "extend", "monitor", "projector", "tv" },
        Summary = Strings.Get("SetDisplayModeAction_Summary"),
        Description = Strings.Get("SetDisplayModeAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("SetDisplayModeAction_Param_Mode_Label"), "extend",
                new Choice("pc", Strings.Get("SetDisplayModeAction_Param_Mode_Choice_Pc")), new Choice("duplicate", Strings.Get("SetDisplayModeAction_Param_Mode_Choice_Duplicate")),
                new Choice("extend", Strings.Get("SetDisplayModeAction_Param_Mode_Choice_Extend")), new Choice("second", Strings.Get("SetDisplayModeAction_Param_Mode_Choice_Second"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var mode = await ctx.GetChoiceAsync("mode", "extend");
        var label = DisplayModeMap.Describe(mode);
        var rc = await Task.Run(() => DisplayService.SetTopology(mode), ctx.CancellationToken).ConfigureAwait(false);
        if (rc != 0)
            throw new ActionException(Strings.Format("SetDisplayModeAction_Error_Windows_Couldn_T_Switch_To_0", label));
        return ContentValue.Empty;
    }
}

public sealed class SetResolutionAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.resolution", Title = Strings.Get("SetResolutionAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "DesktopFlow24",
        Keywords = new[] { "resolution", "refresh rate", "hz", "screen size", "display", "1080p", "4k" },
        Summary = Strings.Get("SetResolutionAction_Summary"),
        Description = Strings.Get("SetResolutionAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "monitor", Label = Strings.Get("SetResolutionAction_Param_Mode_Label"), Type = ParamType.Choice, Default = ParamValue.Choice("primary"),
                ChoicesProvider = async (_, ct) =>
                {
                    var list = (await DisplayService.MonitorChoicesAsync(false, ct).ConfigureAwait(false)).ToList();
                    list.Insert(0, new Choice("primary", Strings.Get("SetResolutionAction_Param_Mode_Choice_Primary")));
                    return list;
                },
            },
            new ParameterDescriptor
            {
                Key = "resolution", Label = Strings.Get("SetResolutionAction_Param_Mode_Label_2"), Type = ParamType.Choice, Required = true, Placeholder = Strings.Get("SetResolutionAction_Param_Mode_Placeholder"),
                ChoicesProvider = (_, ct) => Task.Run<IReadOnlyList<Choice>>(() =>
                {
                    var primary = DisplayService.GetMonitors().FirstOrDefault(m => m.IsPrimary);
                    if (primary is null) return Array.Empty<Choice>();
                    return DisplayService.SupportedModes(primary.DeviceName)
                        .Select(m => new Choice(m.Id, m.ToString())).DistinctBy(c => c.Id).ToList();
                }, ct),
            },
            ParameterDescriptor.Number("refresh", Strings.Get("SetResolutionAction_Param_Refresh_Label"), 0, 0, 500, "Hz"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetChoiceAsync("resolution");
        if (!DisplayModeMap.TryParseResolution(text, out var w, out var h))
            throw new ActionException(Strings.Get("SetResolutionAction_Error_Choose_A_Resolution_Like_1920"));
        var hz = (int)Math.Round(await ctx.GetNumberAsync("refresh", 0));
        var monitorId = await ctx.GetChoiceAsync("monitor", "primary");
        var problem = await Task.Run(() =>
        {
            var monitors = DisplayService.GetMonitors();
            var monitor = DisplayService.FindMonitor(monitors, monitorId)
                          ?? throw new ActionException(Strings.Get("SetResolutionAction_Error_Couldn_T_Find_That_Screen_Check"));
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
        Id = "dev.hdr", Title = Strings.Get("SetHdrAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Hdr24",
        Keywords = new[] { "hdr", "high dynamic range", "colour", "color", "display", "game" },
        Summary = Strings.Get("SetHdrAction_Summary"),
        Description = Strings.Get("SetHdrAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("SetHdrAction_OutputName"),
        Parameters = new[]
        {
            P.OnOffToggle("state", Strings.Get("SetHdrAction_Param_State_Label"), "on", "off"),
            new ParameterDescriptor
            {
                Key = "monitor", Label = Strings.Get("SetHdrAction_Param_Refresh_Label"), Type = ParamType.Choice, Default = ParamValue.Choice("all"),
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
                              ?? throw new ActionException(Strings.Get("SetHdrAction_Error_Couldn_T_Find_That_Screen_Check"));
                targets = targets.Where(t => t.GdiName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            bool? result = null;
            foreach (var t in targets)
            {
                if (DisplayService.GetHdr(t) is not { Supported: true } info) continue;
                var want = Toggle.Resolve(state, info.Enabled);
                if (want == info.Enabled) { result ??= want; continue; }
                if (!DisplayService.SetHdr(t, want))
                    throw new ActionException(Strings.Get("SetHdrAction_Error_Windows_Wouldn_T_Change_HDR_Check"));
                result ??= want;
            }
            return result ?? throw new ActionException(Strings.Get("SetHdrAction_Error_None_Of_Your_Screens_Support"));
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Bool(on);
    }
}

public sealed class SwitchMonitorInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.monitorInput", Title = Strings.Get("SwitchMonitorInputAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "PlugConnected24",
        Keywords = new[] { "monitor", "hdmi", "displayport", "input source", "ddc", "kvm", "switch" },
        Summary = Strings.Get("SwitchMonitorInputAction_Summary"),
        Description = Strings.Get("SwitchMonitorInputAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "monitor", Label = Strings.Get("SwitchMonitorInputAction_Param_Refresh_Label"), Type = ParamType.Choice, Default = ParamValue.Choice("primary"),
                ChoicesProvider = async (_, ct) =>
                {
                    var list = (await DisplayService.MonitorChoicesAsync(false, ct).ConfigureAwait(false)).ToList();
                    list.Insert(0, new Choice("primary", Strings.Get("SwitchMonitorInputAction_Param_Refresh_Choice_Primary")));
                    return list;
                },
            },
            ParameterDescriptor.ChoiceOf("input", Strings.Get("SwitchMonitorInputAction_Param_Input_Label"), "hdmi1",
                MonitorInputs.Known.Select(k => new Choice(k.Id, k.Label)).Append(new Choice("custom", Strings.Get("SwitchMonitorInputAction_Param_Input_Choice_Custom"))).ToArray()),
            ParameterDescriptor.Number("code", Strings.Get("SwitchMonitorInputAction_Param_Code_Label"), 17, 0, 255) with { VisibleWhen = new VisibleWhen("input", "custom") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetChoiceAsync("input", "hdmi1");
        uint code;
        if (input == "custom") code = (uint)Math.Clamp(await ctx.GetNumberAsync("code", 17), 0, 255);
        else code = MonitorInputs.CodeFor(input) ?? throw new ActionException(Strings.Format("SwitchMonitorInputAction_Error_I_Don_T_Know_The_Input", input));
        var monitorId = await ctx.GetChoiceAsync("monitor", "primary");
        var ok = await Task.Run(() =>
        {
            var monitor = DisplayService.FindMonitor(DisplayService.GetMonitors(), monitorId)
                          ?? throw new ActionException(Strings.Get("SwitchMonitorInputAction_Error_Couldn_T_Find_That_Monitor_Check"));
            return DisplayService.SetVcp(monitor, 0x60, code);
        }, ctx.CancellationToken).ConfigureAwait(false);
        if (!ok)
            throw new ActionException(Strings.Get("SwitchMonitorInputAction_Error_The_Monitor_Didn_T_Accept_The"));
        return ContentValue.Empty;
    }
}

public sealed class SetBrightnessAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.brightness", Title = Strings.Get("SetBrightnessAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "BrightnessHigh24",
        Keywords = new[] { "dim", "screen", "backlight", "monitor", "night" },
        Summary = Strings.Get("SetBrightnessAction_Summary"),
        Description = Strings.Get("SetBrightnessAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("SetBrightnessAction_OutputName"),
        Parameters = new[]
        {
            P.Percent("level", Strings.Get("SetBrightnessAction_Param_Level_Label"), 70),
            new ParameterDescriptor
            {
                Key = "monitor", Label = Strings.Get("SetBrightnessAction_Param_Code_Label"), Type = ParamType.Choice, Default = ParamValue.Choice("all"),
                ChoicesProvider = (_, ct) => DisplayService.MonitorChoicesAsync(true, ct),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 70));
        if (level is < 0 or > 100) throw new ActionException(Strings.Get("SetBrightnessAction_Error_Brightness_Has_To_Be_Between"));
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
                          ?? throw new ActionException(Strings.Get("SetBrightnessAction_Error_Couldn_T_Find_That_Screen_Check"));
            return monitor.IsInternal ? DisplayService.SetWmiBrightness(level) : DisplayService.SetDdcBrightness(monitor, level);
        }, ctx.CancellationToken).ConfigureAwait(false);
        if (!changed)
            throw new ActionException(Strings.Get("SetBrightnessAction_Error_Couldn_T_Change_The_Brightness_Desktop"));
        return ContentValue.Number(level);
    }
}
