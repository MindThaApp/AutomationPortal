using System.Diagnostics;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Devices;

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
        ParameterDescriptor.Bool("askFirst", "Ask before doing it", defaultOn) with { AllowVariables = false, AllowAskEachTime = false };
}

public sealed class KeepPcAwakeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.keepAwake", Title = "Keep PC Awake", Category = ActionCategories.DeviceAndSettings, Icon = "WeatherMoonOff24",
        Keywords = new[] { "caffeine", "stay awake", "no sleep", "presentation", "prevent sleep", "screen on" },
        Summary = "Turn Keep Awake {state} for {duration}",
        Description = "Stops the PC from going to sleep, and optionally the screen from turning off, for a set time. This only lasts while Flowmate is running: if you close Flowmate, the PC may sleep again.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("state", "Keep awake", "on", new Choice("on", "on"), new Choice("off", "off")),
            ParameterDescriptor.Duration("duration", "For (0 = until turned off)", 3600) with { VisibleWhen = new VisibleWhen("state", "on") },
            ParameterDescriptor.Bool("keepScreenOn", "Keep the screen on too", true) with { VisibleWhen = new VisibleWhen("state", "on") },
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
        Id = "dev.battery", Title = "Get Battery Level", Category = ActionCategories.DeviceAndSettings, Icon = "Battery824",
        Keywords = new[] { "charge", "power", "percent", "charging", "plugged in", "laptop" },
        Summary = "Get the battery {info}",
        Description = "Gets the battery percentage, whether the PC is charging, the minutes left, or all battery details at once. PCs without a battery report an error for the percentage.",
        OutputKind = ContentKind.Number, OutputName = "Battery",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "level",
                new Choice("level", "level (percent)"), new Choice("charging", "plugged in (true/false)"),
                new Choice("minutes", "minutes left"), new Choice("all", "details (all of it)")),
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
                if (!b.HasBattery) throw new ActionException("This PC doesn't have a battery.");
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
                if (!b.HasBattery) throw new ActionException("This PC doesn't have a battery.");
                return ContentValue.Number(b.Percent);
        }
    }
}

public sealed class SetPowerModeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerMode", Title = "Set Power Mode", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "performance", "efficiency", "battery", "balanced", "power plan", "gaming" },
        Summary = "Set power mode to {mode}",
        Description = "Switches between Windows power modes: best power efficiency, balanced, better performance and best performance. Some modes aren't offered on every PC.",
        OutputKind = ContentKind.Text, OutputName = "Power Mode",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "Power mode", "balanced", PowerService.Modes.Select(m => new Choice(m.Id, m.Label)).ToArray()),
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
        Id = "dev.lock", Title = "Lock PC", Category = ActionCategories.DeviceAndSettings, Icon = "LockClosed24",
        Keywords = new[] { "lock screen", "secure", "away", "win+l" },
        Summary = "Lock the PC",
        Description = "Locks the PC so a password, PIN or fingerprint is needed to get back in.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx)
    {
        if (!Win32.LockWorkStation()) throw new ActionException("Windows wouldn't lock the PC. Try pressing Win+L instead.");
        return Task.FromResult(ContentValue.Empty);
    }
}

public sealed class SleepAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.sleep", Title = "Sleep", Category = ActionCategories.DeviceAndSettings, Icon = "WeatherMoon24",
        Keywords = new[] { "suspend", "standby", "power" },
        Summary = "Put the PC to sleep",
        Description = "Puts the PC to sleep. The shortcut carries on after the PC wakes up.",
        HasOutput = false,
        Parameters = new[] { Confirm.AskFirst(false) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await Confirm.AskFirstAsync(ctx, "Put the PC to sleep?", "Flowmate wants to put this PC to sleep.");
        var task = Task.Run(() => Win32.SetSuspendState(false, false, false));
        var first = await Task.WhenAny(task, Task.Delay(1500, ctx.CancellationToken)).ConfigureAwait(false);
        if (first == task && !task.Result)
            throw new ActionException("Windows couldn't put the PC to sleep. Check that sleep is allowed in Settings > System > Power & battery.");
        await task.ConfigureAwait(false); // returns once the PC has woken up again
        return ContentValue.Empty;
    }
}

public sealed class PowerAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.power", Title = "Shut Down / Restart / Sign Out / Hibernate", Category = ActionCategories.DeviceAndSettings, Icon = "Power24",
        Keywords = new[] { "shutdown", "turn off", "reboot", "restart", "log off", "logout", "hibernate", "power" },
        Summary = "{action} the PC",
        Description = "Shuts down, restarts, signs out or hibernates the PC. Unsaved work in open apps may be lost, so Flowmate asks first unless you turn that off.",
        IsDestructive = true, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("action", "Action", "shutdown",
                new Choice("shutdown", "Shut down"), new Choice("restart", "Restart"), new Choice("signout", "Sign out of"), new Choice("hibernate", "Hibernate")),
            Confirm.AskFirst(true),
            ParameterDescriptor.Bool("force", "Force apps that don't respond to close", false) with { VisibleWhen = new VisibleWhen("action", "shutdown", "restart", "signout") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var action = await ctx.GetChoiceAsync("action", "shutdown");
        var force = await ctx.GetBoolAsync("force", false);
        var (title, args) = action switch
        {
            "shutdown" => ("Shut down the PC?", "/s /t 0"),
            "restart" => ("Restart the PC?", "/r /t 0"),
            "signout" => ("Sign out of Windows?", "/l"),
            "hibernate" => ("Hibernate the PC?", "/h"),
            _ => throw new ActionException($"'{action}' isn't a power action Flowmate knows. Choose shut down, restart, sign out or hibernate."),
        };
        await Confirm.AskFirstAsync(ctx, title, $"{title} Make sure your work is saved first.");
        if (force && action != "hibernate") args += " /f";
        var result = await ProcessRunner.RunAsync("shutdown.exe", args, TimeSpan.FromSeconds(15), ctx.CancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var hint = action == "hibernate" ? " Hibernate may be turned off on this PC." : "";
            throw new ActionException($"Windows wouldn't do that.{hint} {result.Error}".Trim());
        }
        return ContentValue.Empty;
    }
}

public sealed class TurnOffScreenAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.screenOff", Title = "Turn Off Screen", Category = ActionCategories.DeviceAndSettings, Icon = "ScreenOff24",
        Keywords = new[] { "monitor off", "display off", "blank screen", "save power" },
        Summary = "Turn off the screen",
        Description = "Turns the screen off without locking or sleeping the PC. Moving the mouse or pressing a key turns it back on.",
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
        Id = "dev.settings", Title = "Open Settings Page", Category = ActionCategories.DeviceAndSettings, Icon = "Settings24",
        Keywords = new[] { "windows settings", "control panel", "preferences", "options" },
        Summary = "Open {page} settings",
        Description = "Opens a page of the Windows Settings app, such as Display, Sound, Wi-Fi or Night light.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("page", "Settings page", "display", SettingsPages.All.Select(p => new Choice(p.Id, p.Label)).ToArray()),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var page = await ctx.GetChoiceAsync("page", "display");
        var uri = SettingsPages.ResolveUri(page) ?? throw new ActionException($"I don't know the settings page '{page}'. Choose one from the list.");
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException("Windows couldn't open that settings page on this PC.", ex);
        }
        return ContentValue.Empty;
    }
}

public sealed class EmptyRecycleBinAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.recycleBin", Title = "Empty Recycle Bin", Category = ActionCategories.DeviceAndSettings, Icon = "Delete24",
        Keywords = new[] { "trash", "bin", "clean", "free space", "delete" },
        Summary = "Empty the Recycle Bin",
        Description = "Permanently deletes everything in the Recycle Bin. This can't be undone. Gives back how many items were removed.",
        IsDestructive = true, OutputKind = ContentKind.Number, OutputName = "Items Removed",
        Parameters = new[] { Confirm.AskFirst(true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await Confirm.AskFirstAsync(ctx, "Empty the Recycle Bin?", "Everything in the Recycle Bin will be deleted for good.");
        var removed = await Task.Run(() =>
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHQUERYRBINFO>() };
            var count = Win32.SHQueryRecycleBinW(null, ref info) == 0 ? info.i64NumItems : -1;
            if (count == 0) return 0L;
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            var hr = Win32.SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            if (hr != 0 && hr != 1 && unchecked((uint)hr) != 0x8000FFFF)
                throw new ActionException("Windows couldn't empty the Recycle Bin. Close any window that is using it and try again.");
            return Math.Max(count, 0);
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(removed);
    }
}
