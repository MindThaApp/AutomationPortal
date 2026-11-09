using AutomationPortal.Core;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

/// <summary>The decisions behind the "Only if…" conditions, kept pure so they can be tested.</summary>
public static class ConditionLogic
{
    /// <summary>From is included, To is not. From &gt; To means overnight (22:00 to 06:00). From = To means all day.</summary>
    public static bool IsBetween(TimeOnly now, TimeOnly from, TimeOnly to) =>
        from == to || (from < to ? now >= from && now < to : now >= from || now < to);

    public static bool IsDayAllowed(DayOfWeek day, IReadOnlyCollection<DayOfWeek> days) => days.Contains(day);

    public static bool BatteryIs(string direction, int level, int? battery) =>
        battery is { } b && (direction == "above" ? b > level : b < level);

    /// <summary>True when the foreground window covers its whole monitor and isn't the desktop.</summary>
    internal static bool CoversMonitor(Win32.RECT window, Win32.RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    /// <summary>An empty wanted name means any plan.</summary>
    public static bool PlanMatches(string? current, string wanted) =>
        wanted.Length == 0 || string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase);

    public static bool WifiMatches(string? current, string wanted) =>
        current is not null && (wanted.Length == 0 || string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase));
}

internal static class FullscreenDetector
{
    public static bool IsFullscreen()
    {
        if (Win32.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4) return true;

        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Win32.GetDesktopWindow() || hwnd == Win32.GetShellWindow()) return false;
        var cls = Win32.GetWindowClass(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!Win32.GetWindowRect(hwnd, out var rect)) return false;
        var monitor = Win32.MonitorFromWindow(hwnd, 2);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        return monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info) && ConditionLogic.CoversMonitor(rect, info.rcMonitor);
    }
}

public sealed class TimeBetweenCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.timeBetween", Title = "Time of day", Category = "Time", Icon = "Clock24",
        Summary = "it's between {from} and {to}",
        Description = "Only runs during these hours. It also works overnight, like 10 PM to 6 AM.",
        Parameters = new[] { TP.TimeParam("from", "From", "09:00"), TP.TimeParam("to", "Until", "17:00") },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(ConditionLogic.IsBetween(TimeOnly.FromDateTime(DateTime.Now), config.GetTime("from") ?? new TimeOnly(9, 0), config.GetTime("to") ?? new TimeOnly(17, 0)));
}

public sealed class DaysCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.days", Title = "Day of the week", Category = "Time", Icon = "CalendarLtr24",
        Summary = "it's {days}",
        Description = "Only runs on the days you pick.",
        Parameters = new[] { TP.DaysParam("days", "On these days") },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(ConditionLogic.IsDayAllowed(DateTime.Now.DayOfWeek, TP.ParseDays(config.GetStrings("days"))));
}

public sealed class PowerSourceCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.power", Title = "Power source", Category = "Power", Icon = "PlugConnected24",
        Summary = "I'm {state}",
        Description = "Only runs when the PC is on battery, or when it's plugged in.",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", "State", "battery", new Choice("battery", "on battery"), new Choice("plugged", "plugged in")) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(PowerState.Read().OnAc == (config.GetChoice("state", "battery") == "plugged"));
}

public sealed class PowerPlanCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.powerPlan", Title = "Power plan", Category = "Power", Icon = "Flash24",
        Summary = "the power plan is {plan}",
        Description = "Only runs when Windows is using this power plan.",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", "Plan", (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(PowerPlanApis.Current.List().Select(p => new Choice(p.Name, p.Name)).ToList())),
        },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(config.GetChoice("plan", "").Trim() is { Length: > 0 } wanted
            && ConditionLogic.PlanMatches(PowerPlanApis.Current.GetActive()?.Name, wanted));
}

public sealed class BatteryCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.battery", Title = "Battery level", Category = "Power", Icon = "Battery224",
        Summary = "the battery is {direction} {percent}%",
        Description = "Only runs when the battery is below or above a level. PCs without a battery never meet this.",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", "Is", "below", new Choice("below", "below"), new Choice("above", "above")),
            ParameterDescriptor.Number("percent", "Battery level", 50, 1, 99, "%"),
        },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(ConditionLogic.BatteryIs(config.GetChoice("direction", "below"), config.GetInt("percent", 50), PowerState.Read().BatteryPercent));
}

public sealed class WifiCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.wifi", Title = "Wi-Fi network", Category = "Network", Icon = "Wifi124",
        Summary = "I'm {state} {network}",
        Description = "Only runs when the PC is (or isn't) connected to a Wi-Fi network. Windows 11 may hide the Wi-Fi name unless Location is on.",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("state", "State", "connected", new Choice("connected", "connected to"), new Choice("notConnected", "not connected to")),
            ParameterDescriptor.DynamicChoice("network", "Network", async (_, ct) =>
                new[] { new Choice("", "any Wi-Fi network") }.Concat((await NetworkMonitor.KnownWifiNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
        },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct)
    {
        var matches = ConditionLogic.WifiMatches(NetState.Read().WifiName, config.GetChoice("network", "").Trim());
        return Task.FromResult(config.GetChoice("state", "connected") == "connected" ? matches : !matches);
    }
}

public sealed class AppRunningCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.appRunning", Title = "App is running", Category = "Apps", Icon = "Apps24",
        Summary = "{app} is {state}",
        Description = "Only runs when an app is open, or when it isn't.",
        Parameters = new[]
        {
            TP.AppParam(),
            ParameterDescriptor.ChoiceOf("state", "State", "running", new Choice("running", "running"), new Choice("notRunning", "not running")),
        },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct)
    {
        var app = config.GetApp("app");
        if (app is null) return Task.FromResult(false);
        var running = ProcessMonitor.EnumerateAll(withPaths: true).Any(p => AppMatcher.Matches(app.Id, app.Name, p.Name, p.Path));
        return Task.FromResult(config.GetChoice("state", "running") == "running" ? running : !running);
    }
}

public sealed class AppFrontCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.appFront", Title = "App is in front", Category = "Apps", Icon = "WindowArrowUp24",
        Summary = "{app} is {state}",
        Description = "Only runs when an app is the one you're using, or when it isn't.",
        Parameters = new[]
        {
            TP.AppParam(),
            ParameterDescriptor.ChoiceOf("state", "State", "front", new Choice("front", "in front"), new Choice("notFront", "not in front")),
        },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct)
    {
        var app = config.GetApp("app");
        var front = AppInfo.Foreground();
        var isFront = app is not null && front is not null && AppMatcher.Matches(app, front);
        return Task.FromResult(config.GetChoice("state", "front") == "front" ? isFront : !isFront);
    }
}

public sealed class NotFullscreenCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.notFullscreen", Title = "Not in a full-screen app", Category = "Apps", Icon = "FullScreenMaximize24",
        Summary = "I'm not in a full-screen app, game or presentation",
        Description = "Only runs when you're not playing a game, giving a presentation or watching something full screen, so nothing pops up in the middle of it.",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) => Task.FromResult(!FullscreenDetector.IsFullscreen());
}

public sealed class OnlineCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.online", Title = "Internet connection", Category = "Network", Icon = "Globe24",
        Summary = "the internet is {state}",
        Description = "Only runs when the PC is online, or when it's offline.",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", "State", "online", new Choice("online", "working"), new Choice("offline", "not working")) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(NetState.Read().Online == (config.GetChoice("state", "online") == "online"));
}

public sealed class InCallCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.inCall", Title = "In a call", Category = "Calls & Screen", Icon = "Call24",
        Summary = "I'm {state}",
        Description = "Only runs while an app is using your microphone or camera (a call or meeting), or only when none is.",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", "State", "notInCall", new Choice("inCall", "in a call"), new Choice("notInCall", "not in a call")) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(CallMonitor.ReadNow().Any == (config.GetChoice("state", "notInCall") == "inCall"));
}
