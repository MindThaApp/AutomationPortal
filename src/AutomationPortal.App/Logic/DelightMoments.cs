using AutomationPortal.App.Services;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The one notification the first time an automation runs by itself.</summary>
public static class FirstAutomationRun
{
    /// <summary>
    /// Takes the moment once: true the first time only, and records that it was taken.
    /// Call inside <see cref="LocalUiSettings.Update"/> so the flag is saved with it.
    /// </summary>
    public static bool TryTake(LocalUiSettingsData data)
    {
        if (data.FirstAutomationRunShown) return false;
        data.FirstAutomationRunShown = true;
        return true;
    }

    public static string Title(string shortcutName) => string.Format(Strings.Get("FirstAutomationRun_JustRanByItself"), shortcutName);

    /// <summary>"It moved 14 files." from what the run changed, or "It ran without problems." when it changed no files.</summary>
    public static string Body(RunResultBarModel? changes) =>
        changes is null ? Strings.Get("FirstAutomationRun_RanWithoutProblems") : "It " + changes.Summary + ".";
}

public enum RunSound { None, Done, Problem }

/// <summary>Which sound, if any, a finished run plays. Only runs the person started themselves ever do.</summary>
public static class RunSounds
{
    public static bool StartedByPerson(RunSource source) =>
        source is RunSource.Library or RunSource.Hotkey or RunSource.QuickRun or RunSource.SendTo or RunSource.Link or RunSource.Editor;

    public static RunSound For(bool enabled, RunSource source, TileRunState state) =>
        !enabled || !StartedByPerson(source) ? RunSound.None
        : state == TileRunState.Succeeded ? RunSound.Done
        : state == TileRunState.Failed ? RunSound.Problem
        : RunSound.None;

    public static void Play(RunSound sound)
    {
        try
        {
            if (sound != RunSound.None && !NotificationState.AllowsSoundNow()) return;
            if (sound == RunSound.Done) System.Media.SystemSounds.Asterisk.Play();
            else if (sound == RunSound.Problem) System.Media.SystemSounds.Exclamation.Play();
        }
        catch (Exception) { /* no sound device: nothing to tell the person */ }
    }
}

/// <summary>Counts that are simply true, from the run records that exist.</summary>
public static class RunCounts
{
    /// <summary>"Since 3 November: 86 runs, 41 by themselves." Null when there are no runs. Only runs that finished are counted.</summary>
    public static string? Text(IReadOnlyList<RunRecord> records, DateTimeOffset now)
    {
        var finished = records.Where(r => r.Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Stopped).ToList();
        if (finished.Count == 0) return null;
        var since = finished.Min(r => r.Started);
        var by = finished.Count(r => r.Source is RunSource.Automation or RunSource.Reminder);
        var period = since.Date == now.Date ? Strings.Get("RunCounts_Today") : string.Format(Strings.Get("RunCounts_Since"), since.ToString("d MMMM", System.Globalization.CultureInfo.CurrentCulture));
        var runs = finished.Count == 1 ? "1 run" : $"{finished.Count} runs";
        return $"{period}: {runs}, {by} by themselves.";
    }
}

/// <summary>Whether Windows lets an app make a sound or show something right now.</summary>
public static class NotificationState
{
    /// <summary>QUNS_ACCEPTS_NOTIFICATIONS: no full-screen app, no presentation, no Focus assist or Do not disturb.</summary>
    public const int AcceptsNotifications = 5;

    public static bool AllowsSound(int state) => state == AcceptsNotifications;

    /// <summary>True only when Windows says so. Any error means silent.</summary>
    public static bool AllowsSound(Func<int> query)
    {
        try { return AllowsSound(query()); }
        catch (Exception) { return false; }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    public static bool AllowsSoundNow() => AllowsSound(() => SHQueryUserNotificationState(out var s) == 0 ? s : -1);
}

/// <summary>The line under an automation: how often it ran by itself, and when last.</summary>
public static class RanTimes
{
    /// <summary>"Ran 12 times. Last: today at 14:05". Null when it never ran.</summary>
    public static string? Text(int count, DateTimeOffset? last, DateTimeOffset now)
    {
        if (count <= 0 || last is null) return null;
        var local = last.Value.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var when = local.Date == today ? Strings.Format("RanTimes_Today", local.ToString("t"))
            : local.Date == today.AddDays(-1) ? Strings.Format("RanTimes_Yesterday", local.ToString("t"))
            : local.ToString("d");
        return Strings.Format(count == 1 ? "RanTimes_Once" : "RanTimes_Many", count, when);
    }

    /// <summary>Finished runs that automations started for this shortcut, from the records that exist.</summary>
    public static (int Count, DateTimeOffset? Last) For(Guid shortcutId, IEnumerable<RunRecord> records)
    {
        var mine = records.Where(r => r.ShortcutId == shortcutId && r.Source == RunSource.Automation
            && r.Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Stopped).ToList();
        return (mine.Count, mine.Count == 0 ? null : mine.Max(r => r.Started));
    }
}
