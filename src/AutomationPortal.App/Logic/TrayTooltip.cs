namespace AutomationPortal.App.Logic;

/// <summary>The text on the tray icon: what is running now, or what went wrong last, or what comes next. Short enough for the Windows tooltip.</summary>
public static class TrayTooltip
{
    /// <summary>The tooltip holds 127 characters.</summary>
    public const int MaxLength = 127;

    /// <param name="pauseStatus">Set while automations are paused; it wins over everything else.</param>
    /// <param name="runningNames">The shortcuts running right now.</param>
    /// <param name="failedName">The shortcut whose last run did not finish, if it has not been seen yet.</param>
    /// <param name="next">The next automation to run and when, if there is one.</param>
    public static string Build(string? pauseStatus, IReadOnlyList<string> runningNames, string? failedName, (string Name, DateTime At)? next, DateTime now)
    {
        var app = Strings.Get("App_AutomationPortal");
        string? status;
        if (pauseStatus is not null) status = pauseStatus;
        else if (runningNames.Count == 1) status = Strings.Format("TrayTooltip_Running", Short(runningNames[0]));
        else if (runningNames.Count > 1) status = Strings.Format("TrayTooltip_RunningMany", runningNames.Count);
        else if (failedName is not null) status = Strings.Format("TrayTooltip_Failed", Short(failedName));
        else if (next is { } n) status = Strings.Format("TrayTooltip_Next", Short(n.Name), NextRunText.Describe(now, n.At));
        else status = null;

        var text = status is null ? app : app + "\n" + status;
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }

    private static string Short(string name) => name.Length <= 40 ? name : name[..39] + "…";
}
