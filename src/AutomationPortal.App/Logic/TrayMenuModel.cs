namespace AutomationPortal.App.Logic;

public enum PauseChoice { ThirtyMinutes, OneHour, UntilTomorrow, UntilResumed }

public sealed record LastFailure(Guid ShortcutId, string Name, string? RunId);

public sealed record TrayMenuModel(
    bool Paused,
    string PauseStatus,
    int RunningCount,
    string? StopHeader,
    string? FailedHeader,
    IReadOnlyList<Guid> PinnedShown,
    bool ShowMorePinned,
    string Tooltip);

/// <summary>Pure decisions for the tray menu and its pause timer.</summary>
public static class TrayMenuLogic
{
    public const int MaxPinned = 10;

    /// <summary>Null means no end time (until the user resumes).</summary>
    public static DateTimeOffset? PauseUntil(PauseChoice choice, DateTimeOffset now)
    {
        switch (choice)
        {
            case PauseChoice.ThirtyMinutes: return now.AddMinutes(30);
            case PauseChoice.OneHour: return now.AddHours(1);
            case PauseChoice.UntilTomorrow:
                var local = now.LocalDateTime;
                var next = local.Date.AddHours(6);
                if (next <= local) next = next.AddDays(1);
                return new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next));
            default: return null;
        }
    }

    /// <summary>True while a pause is in force. A stored end time that has passed means it is over.</summary>
    public static bool IsPaused(bool paused, DateTimeOffset? until, DateTimeOffset now) =>
        paused && (until is null || until > now);

    public static string DescribeUntil(DateTimeOffset until, DateTimeOffset now)
    {
        var local = until.ToLocalTime();
        var time = local.ToString("HH:mm");
        if (local.Date == now.ToLocalTime().Date) return time;
        return local.Date == now.ToLocalTime().Date.AddDays(1) ? "tomorrow " + time : local.ToString("ddd HH:mm");
    }

    public static string PauseStatus(bool paused, DateTimeOffset? until, DateTimeOffset now)
    {
        if (!paused) return "";
        return until is null ? "Paused until you resume" : "Paused until " + DescribeUntil(until.Value, now);
    }

    public static TrayMenuModel Build(bool paused, DateTimeOffset? until, DateTimeOffset now, int running,
        LastFailure? failure, IReadOnlyList<Guid> pinned)
    {
        var status = PauseStatus(paused, until, now);
        return new TrayMenuModel(
            paused, status, running,
            running > 0 ? $"Stop running shortcuts ({running})" : null,
            failure is null ? null : $"Didn't finish: {failure.Name}",
            pinned.Take(MaxPinned).ToList(),
            pinned.Count > MaxPinned,
            paused ? "Automation Portal - " + status :"Automation Portal");
    }

    /// <summary>Keeps the last failure unless a later run of that shortcut succeeded.</summary>
    public static LastFailure? NextFailure(LastFailure? current, Guid shortcutId, string name, bool failed, bool succeeded, string? runId)
    {
        if (failed) return new LastFailure(shortcutId, name, runId);
        if (succeeded && current?.ShortcutId == shortcutId) return null;
        return current;
    }
}
