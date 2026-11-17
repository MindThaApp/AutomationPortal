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
