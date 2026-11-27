using System.Runtime.InteropServices;
using AutomationPortal.Actions.Content.Utilities;

namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>What the "Wait until" action looks at. Replaced by fakes in tests.</summary>
internal sealed class WaitProbes
{
    public required Func<DateTime> Now { get; init; }
    public required Func<string, bool> PathExists { get; init; }
    public required Func<TimeSpan> IdleFor { get; init; }
    public required Func<CancellationToken, Task<bool>> AppRunning { get; init; }
    public required Func<CancellationToken, Task<bool>> ClipboardHasText { get; init; }
    public required Func<CancellationToken, Task<bool>> SiteUp { get; init; }
    /// <summary>Waits between two checks; tests make this instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (t, ct) => Task.Delay(t, ct);
}

/// <summary>Read-only: this only looks at the PC, it never changes anything.</summary>
internal static class WaitUntil
{
    public static bool IsKnown(string condition) =>
        condition is "time" or "fileExists" or "fileGone" or "appRunning" or "appNotRunning" or "idle" or "clipboardText" or "siteUp";

    /// <summary>The next moment the time of day is reached: today if it hasn't passed yet, otherwise tomorrow.</summary>
    public static DateTime NextOccurrence(DateTime now, TimeOnly time)
    {
        var target = now.Date + time.ToTimeSpan();
        return target > now ? target : target.AddDays(1);
    }

    /// <summary>Builds the check for a condition. The time of day is fixed to its next occurrence when this is called.</summary>
    public static Func<CancellationToken, Task<bool>> Build(string condition, WaitProbes p, TimeOnly time, string path, TimeSpan idle)
    {
        switch (condition)
        {
            case "time":
                var target = NextOccurrence(p.Now(), time);
                return _ => Task.FromResult(p.Now() >= target);
            case "fileExists": return _ => Task.FromResult(p.PathExists(path));
            case "fileGone": return _ => Task.FromResult(!p.PathExists(path));
            case "appRunning": return ct => p.AppRunning(ct);
            case "appNotRunning": return async ct => !await p.AppRunning(ct);
            case "idle": return _ => Task.FromResult(p.IdleFor() >= idle);
            case "clipboardText": return ct => p.ClipboardHasText(ct);
            case "siteUp": return ct => p.SiteUp(ct);
            default: throw new ActionException(Strings.Get("WaitUntil_Error_Choose_What_To_Wait_For"));
        }
    }

    /// <summary>True when the condition came true, false when the time limit ran out (a limit of zero means no limit).</summary>
    public static async Task<bool> RunAsync(Func<CancellationToken, Task<bool>> check, TimeSpan every, TimeSpan timeout, WaitProbes p, CancellationToken ct)
    {
        var started = p.Now();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await check(ct).ConfigureAwait(false)) return true;
            var left = timeout > TimeSpan.Zero ? timeout - (p.Now() - started) : TimeSpan.MaxValue;
            if (left <= TimeSpan.Zero) return false;
            await p.Delay(left < every ? left : every, ct).ConfigureAwait(false);
        }
    }
}

public sealed class WaitUntilAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "flow.waitUntil", Title = Strings.Get("WaitUntilAction_Title"), Category = ActionCategories.Scripting, Icon = "HourglassHalf24",
        Keywords = new[] { "wait", "until", "pause", "delay", "condition", "idle", "file exists", "time of day", "website up", "clipboard", "app running", "poll" },
        Summary = Strings.Get("WaitUntilAction_Summary"),
        Description = Strings.Get("WaitUntilAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("condition", Strings.Get("WaitUntilAction_Param_Condition_Label"), "time",
                new Choice("time", Strings.Get("WaitUntilAction_Param_Condition_Choice_Time")),
                new Choice("fileExists", Strings.Get("WaitUntilAction_Param_Condition_Choice_FileExists")),
                new Choice("fileGone", Strings.Get("WaitUntilAction_Param_Condition_Choice_FileGone")),
                new Choice("appRunning", Strings.Get("WaitUntilAction_Param_Condition_Choice_AppRunning")),
                new Choice("appNotRunning", Strings.Get("WaitUntilAction_Param_Condition_Choice_AppNotRunning")),
                new Choice("idle", Strings.Get("WaitUntilAction_Param_Condition_Choice_Idle")),
                new Choice("clipboardText", Strings.Get("WaitUntilAction_Param_Condition_Choice_ClipboardText")),
                new Choice("siteUp", Strings.Get("WaitUntilAction_Param_Condition_Choice_SiteUp"))),
            new ParameterDescriptor { Key = "time", Label = Strings.Get("WaitUntilAction_Param_Condition_Label_2"), Type = ParamType.Time, Default = ParamValue.Time("18:00"), VisibleWhen = new VisibleWhen("condition", "time") },
            new ParameterDescriptor { Key = "path", Label = Strings.Get("WaitUntilAction_Param_Condition_Label_3"), Type = ParamType.File, VisibleWhen = new VisibleWhen("condition", "fileExists", "fileGone") },
            P.App() with { VisibleWhen = new VisibleWhen("condition", "appRunning", "appNotRunning") },
            ParameterDescriptor.Number("idleMinutes", Strings.Get("WaitUntilAction_Param_IdleMinutes_Label"), 5, 1, 1440, "minutes") with { VisibleWhen = new VisibleWhen("condition", "idle") },
            new ParameterDescriptor { Key = "url", Label = Strings.Get("WaitUntilAction_Param_IdleMinutes_Label_2"), Type = ParamType.Url, VisibleWhen = new VisibleWhen("condition", "siteUp"), Placeholder = Strings.Get("WaitUntilAction_Param_IdleMinutes_Placeholder") },
            ParameterDescriptor.Number("every", Strings.Get("WaitUntilAction_Param_Every_Label"), 5, 1, 3600, "seconds"),
            ParameterDescriptor.Duration("timeout", Strings.Get("WaitUntilAction_Param_Timeout_Label"), 600),
            ParameterDescriptor.ChoiceOf("onTimeout", Strings.Get("WaitUntilAction_Param_OnTimeout_Label"), "continue", new Choice("continue", Strings.Get("WaitUntilAction_Param_OnTimeout_Choice_Continue")), new Choice("stop", Strings.Get("WaitUntilAction_Param_OnTimeout_Choice_Stop"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var condition = await ctx.GetChoiceAsync("condition", "time");
        if (!WaitUntil.IsKnown(condition)) throw new ActionException(Strings.Get("WaitUntilAction_Error_Choose_What_To_Wait_For"));
        var every = TimeSpan.FromSeconds(Math.Max(1, await ctx.GetNumberAsync("every", 5)));
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromMinutes(10));
        var stop = await ctx.GetChoiceAsync("onTimeout", "continue") == "stop";

        var time = TimeOnly.MinValue;
        var path = "";
        AppItem? app = null;
        Uri? url = null;
        switch (condition)
        {
            case "time":
                time = await ctx.GetTimeAsync("time") ?? throw new ActionException(Strings.Get("WaitUntilAction_Error_Choose_A_Time_Of_Day"));
                break;
            case "fileExists" or "fileGone":
                path = await ctx.GetPathAsync("path") ?? throw new ActionException(Strings.Get("WaitUntilAction_Error_Choose_The_File_Or_Folder"));
                break;
            case "appRunning" or "appNotRunning":
                app = await ctx.RequireAppAsync();
                break;
            case "siteUp":
                var text = (await ctx.GetTextAsync("url")).Trim();
                if (!text.Contains("://")) text = "https://" + text;
                if (!Uri.TryCreate(text, UriKind.Absolute, out url) || url.Scheme is not ("http" or "https"))
                    throw new ActionException(Strings.Get("WaitUntilAction_Error_Enter_The_Website_S_Address_Like"));
                break;
        }
        var idle = TimeSpan.FromMinutes(Math.Max(1, await ctx.GetNumberAsync("idleMinutes", 5)));

        var probes = new WaitProbes
        {
            Now = () => DateTime.Now,
            PathExists = p => File.Exists(p) || Directory.Exists(p),
            IdleFor = IdleTime.Current,
            AppRunning = ct => Task.Run(() => RunningApps.IsRunning(app!), ct),
            ClipboardHasText = async ct => !string.IsNullOrWhiteSpace((await ctx.Services.Clipboard.GetAsync()).AsText()),
            SiteUp = async ct => (await SiteCheck.CheckAsync(ctx.Services.Http, url!, TimeSpan.FromSeconds(10), ct)).IsUp,
        };
        var check = WaitUntil.Build(condition, probes, time, path, idle);
        ctx.ReportProgress(Strings.Get("WaitUntilAction_Progress"));
        var met = await WaitUntil.RunAsync(check, every, timeout, probes, ctx.CancellationToken).ConfigureAwait(false);
        if (met) return ctx.Input;
        var message = Strings.Format("WaitUntilAction_Error_Timeout", DescribeTimeout(timeout));
        if (stop) throw new ActionException(message);
        ctx.Log(message + " Carrying on.");
        return ctx.Input;
    }

    private static string DescribeTimeout(TimeSpan t) =>
        t.TotalMinutes >= 1 && t.TotalSeconds % 60 == 0 ? (t.TotalMinutes == 1 ? Strings.Get("WaitUntilAction_Duration_OneMinute") : Strings.Format("WaitUntilAction_Duration_Minutes", t.TotalMinutes)) : Strings.Format("WaitUntilAction_Duration_Seconds", t.TotalSeconds);
}

internal static class IdleTime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public static TimeSpan Current()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}
