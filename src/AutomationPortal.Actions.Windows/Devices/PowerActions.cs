using System.Diagnostics;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Devices;

internal static class Confirm
{
    /// <summary>Asks "Are you sure?" unless the user turned "Ask first" off. Cancelling ends the shortcut quietly.</summary>
    public static async Task AskFirstAsync(ActionContext ctx, string title, string message)
    {
        if (!await ctx.GetBoolAsync("askFirst", true).ConfigureAwait(false)) return;
        var ok = await ctx.Services.UserInteraction.ShowAlertAsync(title, message, true, ctx.CancellationToken).ConfigureAwait(false);
        if (!ok) throw new UserCancelledException();
    }

    public static ParameterDescriptor AskFirst(bool defaultOn) =>
        ParameterDescriptor.Bool("askFirst", Strings.Get("Confirm_Param_AskFirst_Label"), defaultOn) with { AllowVariables = false, AllowAskEachTime = false };
}

public sealed class KeepPcAwakeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.keepAwake", Title = Strings.Get("KeepPcAwakeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "WeatherMoonOff24",
        Keywords = new[] { "caffeine", "stay awake", "no sleep", "presentation", "prevent sleep", "screen on" },
        Summary = Strings.Get("KeepPcAwakeAction_Summary"),
        Description = Strings.Get("KeepPcAwakeAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("state", Strings.Get("KeepPcAwakeAction_Param_State_Label"), "on", new Choice("on", Strings.Get("KeepPcAwakeAction_Param_State_Choice_On")), new Choice("off", Strings.Get("KeepPcAwakeAction_Param_State_Choice_Off"))),
            ParameterDescriptor.Duration("duration", Strings.Get("KeepPcAwakeAction_Param_Duration_Label"), 3600) with { VisibleWhen = new VisibleWhen("state", "on") },
            ParameterDescriptor.Bool("keepScreenOn", Strings.Get("KeepPcAwakeAction_Param_KeepScreenOn_Label"), true) with { VisibleWhen = new VisibleWhen("state", "on") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        if (await ctx.GetChoiceAsync("state", "on") == "off")
        {
            KeepAwakeService.Stop();
            return ContentValue.Empty;
        }
        var duration = await ctx.GetDurationAsync("duration", TimeSpan.FromHours(1));
        KeepAwakeService.Start(duration, await ctx.GetBoolAsync("keepScreenOn", true));
        return ContentValue.Empty;
    }
}

public sealed class GetBatteryAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.battery", Title = Strings.Get("GetBatteryAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Battery824",
        Keywords = new[] { "charge", "power", "percent", "charging", "plugged in", "laptop" },
        Summary = Strings.Get("GetBatteryAction_Summary"),
        Description = Strings.Get("GetBatteryAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("GetBatteryAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetBatteryAction_Param_Info_Label"), "level",
                new Choice("level", Strings.Get("GetBatteryAction_Param_Info_Choice_Level")), new Choice("charging", Strings.Get("GetBatteryAction_Param_Info_Choice_Charging")),
                new Choice("minutes", Strings.Get("GetBatteryAction_Param_Info_Choice_Minutes")), new Choice("all", Strings.Get("GetBatteryAction_Param_Info_Choice_All"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "level");
        var b = PowerService.GetBattery();
        switch (info)
        {
            case "charging": return ContentValue.Bool(b.PluggedIn);
            case "minutes":
                if (!b.HasBattery) throw new ActionException(Strings.Get("GetBatteryAction_Error_This_PC_Doesn_T_Have_A"));
                return b.MinutesLeft is { } m ? ContentValue.Number(m) : ContentValue.Empty;
            case "all":
                return ContentValue.Dict(
                    ("Has Battery", ContentValue.Bool(b.HasBattery)),
                    ("Level", b.HasBattery ? ContentValue.Number(b.Percent) : ContentValue.Empty),
                    ("Plugged In", ContentValue.Bool(b.PluggedIn)),
                    ("Charging", ContentValue.Bool(b.Charging)),
                    ("Minutes Left", b.MinutesLeft is { } mm ? ContentValue.Number(mm) : ContentValue.Empty),
                    ("Battery Saver", ContentValue.Bool(b.BatterySaverOn)));
            default:
                if (!b.HasBattery) throw new ActionException(Strings.Get("GetBatteryAction_Error_This_PC_Doesn_T_Have_A"));
                return ContentValue.Number(b.Percent);
        }
    }
}

public sealed class SetPowerModeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerMode", Title = Strings.Get("SetPowerModeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "performance", "efficiency", "battery", "balanced", "power plan", "gaming" },
        Summary = Strings.Get("SetPowerModeAction_Summary"),
        Description = Strings.Get("SetPowerModeAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("SetPowerModeAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("SetPowerModeAction_Param_Mode_Label"), "balanced", PowerService.Modes.Select(m => new Choice(m.Id, m.Label)).ToArray()),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var id = await ctx.GetChoiceAsync("mode", "balanced");
        await PowerService.SetModeAsync(id, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Text(PowerService.Modes.First(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Label);
    }
}

public sealed class LockPcAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.lock", Title = Strings.Get("LockPcAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "LockClosed24",
        Keywords = new[] { "lock screen", "secure", "away", "win+l" },
        Summary = Strings.Get("LockPcAction_Summary"),
        Description = Strings.Get("LockPcAction_Description"),
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx)
    {
        if (!Win32.LockWorkStation()) throw new ActionException(Strings.Get("LockPcAction_Error_Windows_Wouldn_T_Lock_The_PC"));
        return Task.FromResult(ContentValue.Empty);
    }
}

public sealed class SleepAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.sleep", Title = Strings.Get("SleepAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "WeatherMoon24",
        Keywords = new[] { "suspend", "standby", "power" },
        Summary = Strings.Get("SleepAction_Summary"),
        Description = Strings.Get("SleepAction_Description"),
        HasOutput = false,
        Parameters = new[] { Confirm.AskFirst(false) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await Confirm.AskFirstAsync(ctx, Strings.Get("SleepAction_Confirm_Title"), Strings.Get("SleepAction_Confirm_Body"));
        var task = Task.Run(() => Win32.SetSuspendState(false, false, false));
        var first = await Task.WhenAny(task, Task.Delay(1500, ctx.CancellationToken)).ConfigureAwait(false);
        if (first == task && !task.Result)
            throw new ActionException(Strings.Get("SleepAction_Error_Windows_Couldn_T_Put_The_PC"));
        await task.ConfigureAwait(false); // returns once the PC has woken up again
        return ContentValue.Empty;
    }
}

public sealed class PowerAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.power", Title = Strings.Get("PowerAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Power24",
        Keywords = new[] { "shutdown", "turn off", "reboot", "restart", "log off", "logout", "hibernate", "power" },
        Summary = Strings.Get("PowerAction_Summary"),
        Description = Strings.Get("PowerAction_Description"),
        IsDestructive = true, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("action", Strings.Get("PowerAction_Param_Action_Label"), "shutdown",
                new Choice("shutdown", Strings.Get("PowerAction_Param_Action_Choice_Shutdown")), new Choice("restart", Strings.Get("PowerAction_Param_Action_Choice_Restart")), new Choice("signout", Strings.Get("PowerAction_Param_Action_Choice_Signout")), new Choice("hibernate", Strings.Get("PowerAction_Param_Action_Choice_Hibernate"))),
            Confirm.AskFirst(true),
            ParameterDescriptor.Bool("force", Strings.Get("PowerAction_Param_Force_Label"), false) with { VisibleWhen = new VisibleWhen("action", "shutdown", "restart", "signout") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var action = await ctx.GetChoiceAsync("action", "shutdown");
        var force = await ctx.GetBoolAsync("force", false);
        var (title, args) = action switch
        {
            "shutdown" => (Strings.Get("PowerAction_Confirm_Shutdown"), "/s /t 0"),
            "restart" => (Strings.Get("PowerAction_Confirm_Restart"), "/r /t 0"),
            "signout" => (Strings.Get("PowerAction_Confirm_Signout"), "/l"),
            "hibernate" => (Strings.Get("PowerAction_Confirm_Hibernate"), "/h"),
            _ => throw new ActionException(Strings.Format("PowerAction_Error_0_Isn_T_A_Power_Action", action)),
        };
        await Confirm.AskFirstAsync(ctx, title, Strings.Format("PowerAction_Confirm_Body", title));
        if (force && action != "hibernate") args += " /f";
        var result = await ProcessRunner.RunAsync("shutdown.exe", args, TimeSpan.FromSeconds(15), ctx.CancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var hint = action == "hibernate" ? Strings.Get("PowerAction_Hint_HibernateOff") : "";
            throw new ActionException(Strings.Format("PowerAction_Error_Windows_Wouldn_T_Do_That_0_1", hint, result.Error).Trim());
        }
        return ContentValue.Empty;
    }
}

public sealed class TurnOffScreenAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.screenOff", Title = Strings.Get("TurnOffScreenAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "ProjectionScreenDismiss24",
        Keywords = new[] { "monitor off", "display off", "blank screen", "save power" },
        Summary = Strings.Get("TurnOffScreenAction_Summary"),
        Description = Strings.Get("TurnOffScreenAction_Description"),
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        // Give the user a moment to let go of the keys that started this shortcut, or the screen wakes straight away.
        await Task.Delay(700, ctx.CancellationToken).ConfigureAwait(false);
        Win32.SendMessageTimeoutW(Win32.HWND_BROADCAST, Win32.WM_SYSCOMMAND, new IntPtr(Win32.SC_MONITORPOWER), new IntPtr(2), Win32.SMTO_ABORTIFHUNG, 2000, out _);
        return ContentValue.Empty;
    }
}

public sealed class OpenSettingsPageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.settings", Title = Strings.Get("OpenSettingsPageAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Settings24",
        Keywords = new[] { "windows settings", "control panel", "preferences", "options" },
        Summary = Strings.Get("OpenSettingsPageAction_Summary"),
        Description = Strings.Get("OpenSettingsPageAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("page", Strings.Get("OpenSettingsPageAction_Param_Page_Label"), "display", SettingsPages.All.Select(p => new Choice(p.Id, p.Label)).ToArray()),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var page = await ctx.GetChoiceAsync("page", "display");
        var uri = SettingsPages.ResolveUri(page) ?? throw new ActionException(Strings.Format("OpenSettingsPageAction_Error_I_Don_T_Know_The_Settings", page));
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException(Strings.Get("OpenSettingsPageAction_Error_Windows_Couldn_T_Open_That_Settings"), ex);
        }
        return ContentValue.Empty;
    }
}

public sealed class EmptyRecycleBinAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.recycleBin", Title = Strings.Get("EmptyRecycleBinAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Delete24",
        Keywords = new[] { "trash", "bin", "clean", "free space", "delete" },
        Summary = Strings.Get("EmptyRecycleBinAction_Summary"),
        Description = Strings.Get("EmptyRecycleBinAction_Description"),
        IsDestructive = true, OutputKind = ContentKind.Number, OutputName = Strings.Get("EmptyRecycleBinAction_OutputName"),
        Parameters = new[] { Confirm.AskFirst(true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await Confirm.AskFirstAsync(ctx, Strings.Get("EmptyRecycleBinAction_Confirm_Title"), Strings.Get("EmptyRecycleBinAction_Confirm_Body"));
        var removed = await Task.Run(() =>
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHQUERYRBINFO>() };
            var count = Win32.SHQueryRecycleBinW(null, ref info) == 0 ? info.i64NumItems : -1;
            if (count == 0) return 0L;
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            var hr = Win32.SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            if (hr != 0 && hr != 1 && unchecked((uint)hr) != 0x8000FFFF)
                throw new ActionException(Strings.Get("EmptyRecycleBinAction_Error_Windows_Couldn_T_Empty_The_Recycle"));
            return Math.Max(count, 0);
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(removed);
    }
}
