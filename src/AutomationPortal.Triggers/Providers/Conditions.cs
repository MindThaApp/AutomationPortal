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
        Id = "cond.timeBetween", Title = Strings.Get("Trigger_CondTimeBetween_Title"), Category = "Time", Icon = "Clock24",
        Summary = Strings.Get("Trigger_CondTimeBetween_Summary"),
        Description = Strings.Get("Trigger_CondTimeBetween_Description"),
        Parameters = new[] { TP.TimeParam("from", Strings.Get("Trigger_CondTimeBetween_Param_From_Label"), "09:00"), TP.TimeParam("to", Strings.Get("Trigger_CondTimeBetween_Param_To_Label"), "17:00") },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(ConditionLogic.IsBetween(TimeOnly.FromDateTime(DateTime.Now), config.GetTime("from") ?? new TimeOnly(9, 0), config.GetTime("to") ?? new TimeOnly(17, 0)));
}

public sealed class DaysCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.days", Title = Strings.Get("Trigger_CondDays_Title"), Category = "Time", Icon = "CalendarLtr24",
        Summary = Strings.Get("Trigger_CondDays_Summary"),
        Description = Strings.Get("Trigger_CondDays_Description"),
        Parameters = new[] { TP.DaysParam("days", Strings.Get("Trigger_CondDays_Param_Days_Label")) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(ConditionLogic.IsDayAllowed(DateTime.Now.DayOfWeek, TP.ParseDays(config.GetStrings("days"))));
}

public sealed class PowerSourceCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.power", Title = Strings.Get("Trigger_CondPower_Title"), Category = "Power", Icon = "PlugConnected24",
        Summary = Strings.Get("Trigger_CondPower_Summary"),
        Description = Strings.Get("Trigger_CondPower_Description"),
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondPower_Param_State_Label"), "battery", new Choice("battery", Strings.Get("Trigger_CondPower_Param_State_Choice_Battery")), new Choice("plugged", Strings.Get("Trigger_CondPower_Param_State_Choice_Plugged"))) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(PowerState.Read().OnAc == (config.GetChoice("state", "battery") == "plugged"));
}

public sealed class PowerPlanCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.powerPlan", Title = Strings.Get("Trigger_CondPowerPlan_Title"), Category = "Power", Icon = "Flash24",
        Summary = Strings.Get("Trigger_CondPowerPlan_Summary"),
        Description = Strings.Get("Trigger_CondPowerPlan_Description"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", Strings.Get("Trigger_CondPowerPlan_Param_Plan_Label"), (_, _) =>
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
        Id = "cond.battery", Title = Strings.Get("Trigger_CondBattery_Title"), Category = "Power", Icon = "Battery224",
        Summary = Strings.Get("Trigger_CondBattery_Summary"),
        Description = Strings.Get("Trigger_CondBattery_Description"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", Strings.Get("Trigger_CondBattery_Param_Direction_Label"), "below", new Choice("below", Strings.Get("Trigger_CondBattery_Param_Direction_Choice_Below")), new Choice("above", Strings.Get("Trigger_CondBattery_Param_Direction_Choice_Above"))),
            ParameterDescriptor.Number("percent", Strings.Get("Trigger_CondBattery_Param_Percent_Label"), 50, 1, 99, Strings.Get("Trigger_CondBattery_Param_Percent_Unit")),
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
        Id = "cond.wifi", Title = Strings.Get("Trigger_CondWifi_Title"), Category = "Network", Icon = "Wifi124",
        Summary = Strings.Get("Trigger_CondWifi_Summary"),
        Description = Strings.Get("Trigger_CondWifi_Description"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondWifi_Param_State_Label"), "connected", new Choice("connected", Strings.Get("Trigger_CondWifi_Param_State_Choice_Connected")), new Choice("notConnected", Strings.Get("Trigger_CondWifi_Param_State_Choice_NotConnected"))),
            ParameterDescriptor.DynamicChoice("network", Strings.Get("Trigger_CondWifi_Param_Network_Label"), async (_, ct) =>
                new[] { new Choice("", Strings.Get("Trigger_CondWifi_Param_Network_Choice_X")) }.Concat((await NetworkMonitor.KnownWifiNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
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
        Id = "cond.appRunning", Title = Strings.Get("Trigger_CondAppRunning_Title"), Category = "Apps", Icon = "Apps24",
        Summary = Strings.Get("Trigger_CondAppRunning_Summary"),
        Description = Strings.Get("Trigger_CondAppRunning_Description"),
        Parameters = new[]
        {
            TP.AppParam(),
            ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondAppRunning_Param_State_Label"), "running", new Choice("running", Strings.Get("Trigger_CondAppRunning_Param_State_Choice_Running")), new Choice("notRunning", Strings.Get("Trigger_CondAppRunning_Param_State_Choice_NotRunning"))),
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
        Id = "cond.appFront", Title = Strings.Get("Trigger_CondAppFront_Title"), Category = "Apps", Icon = "WindowArrowUp24",
        Summary = Strings.Get("Trigger_CondAppRunning_Summary"),
        Description = Strings.Get("Trigger_CondAppFront_Description"),
        Parameters = new[]
        {
            TP.AppParam(),
            ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondAppFront_Param_State_Label"), "front", new Choice("front", Strings.Get("Trigger_CondAppFront_Param_State_Choice_Front")), new Choice("notFront", Strings.Get("Trigger_CondAppFront_Param_State_Choice_NotFront"))),
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
        Id = "cond.notFullscreen", Title = Strings.Get("Trigger_CondNotFullscreen_Title"), Category = "Apps", Icon = "FullScreenMaximize24",
        Summary = Strings.Get("Trigger_CondNotFullscreen_Summary"),
        Description = Strings.Get("Trigger_CondNotFullscreen_Description"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) => Task.FromResult(!FullscreenDetector.IsFullscreen());
}

public sealed class OnlineCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.online", Title = Strings.Get("Trigger_CondOnline_Title"), Category = "Network", Icon = "Globe24",
        Summary = Strings.Get("Trigger_CondOnline_Summary"),
        Description = Strings.Get("Trigger_CondOnline_Description"),
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondOnline_Param_State_Label"), "online", new Choice("online", Strings.Get("Trigger_CondOnline_Param_State_Choice_Online")), new Choice("offline", Strings.Get("Trigger_CondOnline_Param_State_Choice_Offline"))) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(NetState.Read().Online == (config.GetChoice("state", "online") == "online"));
}

public sealed class InCallCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.inCall", Title = Strings.Get("Trigger_CondInCall_Title"), Category = "Calls & Screen", Icon = "Call24",
        Summary = Strings.Get("Trigger_CondInCall_Summary"),
        Description = Strings.Get("Trigger_CondInCall_Description"),
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_CondInCall_Param_State_Label"), "notInCall", new Choice("inCall", Strings.Get("Trigger_CondInCall_Param_State_Choice_InCall")), new Choice("notInCall", Strings.Get("Trigger_CondInCall_Param_State_Choice_NotInCall"))) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(CallMonitor.ReadNow().Any == (config.GetChoice("state", "notInCall") == "inCall"));
}
