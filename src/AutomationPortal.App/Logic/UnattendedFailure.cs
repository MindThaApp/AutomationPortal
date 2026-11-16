using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The notification for a run that failed with nobody watching (a schedule, a trigger or a reminder).</summary>
public static class UnattendedFailure
{
    public static bool IsUnattended(RunSource source) => source is RunSource.Automation or RunSource.Reminder;

    /// <summary>A shortcut that backs something up: named so, or copying into a new folder like the Back Up a Folder template.</summary>
    public static bool LooksLikeBackup(Shortcut? shortcut)
    {
        if (shortcut is null) return false;
        var name = shortcut.Name ?? "";
        if (name.Contains("backup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("back up", StringComparison.OrdinalIgnoreCase)
            || name.Contains("back-up", StringComparison.OrdinalIgnoreCase)) return true;
        var ids = shortcut.Actions.Select(a => a.ActionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Contains("file.copy") && ids.Contains("file.newFolder");
    }

    /// <summary>Title and text: "Backup didn't run: reason" for an unattended backup, otherwise the usual "didn't finish".</summary>
    public static (string Title, string Body) Describe(string shortcutName, Shortcut? shortcut, RunSource source, string? error)
    {
        var reason = string.IsNullOrWhiteSpace(error) ? Strings.Get("UnattendedFailure_SomethingWentWrong") : error.Trim();
        if (IsUnattended(source) && LooksLikeBackup(shortcut))
            return (Strings.Get("UnattendedFailure_BackupDidntRun"), $"Backup didn't run: {reason} (\"{shortcutName}\")");
        return ($"\"{shortcutName}\" didn't finish", reason);
    }
}
