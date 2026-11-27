using System.Windows;
using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

/// <summary>"Undo file changes" for one run, shared by Run History and the editor: confirm, undo, then report.</summary>
public static class RunUndo
{
    public static bool CanUndo(AutomationPortalPaths paths, string runId)
    {
        try { return FileUndo.CanUndo(new FileJournal(paths).Read(runId)); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>True when the undo ran (whatever it could do), false when there was nothing to undo or the user cancelled.</summary>
    public static async Task<bool> RunAsync(Window? owner, AutomationPortalPaths paths, string runId)
    {
        var store = new FileJournal(paths);
        JournaledRun? run;
        try { run = store.Read(runId); }
        catch (ArgumentException) { run = null; }
        if (!FileUndo.CanUndo(run))
        {
            await Prompts.MessageAsync(owner, Strings.Get("RunUndo_NothingToUndo"), run?.UndoneAt is not null
                ? Strings.Get("RunUndo_ThisRunsFileChanges")
                : Strings.Get("RunUndo_ThisRunDidntChange"));
            return false;
        }
        if (store.IsUndoing(runId))
        {
            await Prompts.MessageAsync(owner, Strings.Get("RunHistoryPage_UndoFileChanges"), Strings.Get("RunUndo_ThisRunsFileChanges2"));
            return false;
        }
        var plan = FileUndo.Plan(run!);
        if (!await Prompts.ConfirmAsync(owner, Strings.Get("RunHistoryPage_UndoFileChanges"), RunHistoryActions.UndoConfirmText(plan), Strings.Get("RunUndo_Undo"))) return false;
        IReadOnlyList<UndoOutcome> results;
        try { results = await Task.Run(() => FileUndo.Execute(run!, store)); }
        catch (ActionException ex)
        {
            await Prompts.MessageAsync(owner, Strings.Get("RunHistoryPage_UndoFileChanges"), ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write("Undo file changes failed: " + ex.Message);
            await Prompts.MessageAsync(owner, Strings.Get("RunUndo_CouldnTUndo"), Strings.Get("RunUndo_SomethingWentWrongWhile") + ex.Message);
            return true;
        }
        await Prompts.MessageAsync(owner, Strings.Get("RunHistoryPage_UndoFileChanges"), RunHistoryActions.UndoReportText(results));
        return true;
    }

    /// <summary>How long after a run the toast's Undo still acts at once; after that it shows what it would undo and asks.</summary>
    public static readonly TimeSpan AskAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// True when a one-click Undo should ask first: the run's last recorded change is more than <see cref="AskAfter"/> ago (or its
    /// time is unknown), so the user may no longer remember what it changed.
    /// </summary>
    public static bool AskFirst(JournaledRun? run, DateTimeOffset now) =>
        run is null || run.Changes.Count == 0 || now - run.Changes.Max(c => c.At) > AskAfter;

    /// <summary>Reads a run's record; null when there is none.</summary>
    public static JournaledRun? Read(AutomationPortalPaths paths, string runId)
    {
        try { return new FileJournal(paths).Read(runId); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// The one-click Undo on the result bar and the toast: undoes exactly this run's recorded changes, with the same safety rules as
    /// above (nothing is overwritten, anything changed since the run is left as it is), and says what was and wasn't put back.
    /// </summary>
    public static async Task<(string Title, string Message)> UndoWithoutAskingAsync(AutomationPortalPaths paths, string runId)
    {
        var store = new FileJournal(paths);
        JournaledRun? run;
        try { run = store.Read(runId); }
        catch (ArgumentException) { run = null; }
        if (!FileUndo.CanUndo(run))
            return (Strings.Get("RunUndo_NothingToUndo"), run?.UndoneAt is not null
                ? Strings.Get("RunUndo_ThisRunsFileChanges")
                : Strings.Get("RunUndo_ThisRunDidntChange"));
        if (store.IsUndoing(runId)) return (Strings.Get("RunHistoryPage_UndoFileChanges"), Strings.Get("RunUndo_ThisRunsFileChanges2"));
        try { return RunResultText.UndoReport(await Task.Run(() => FileUndo.Execute(run!, store))); }
        catch (ActionException ex) { return ("Couldn't undo", ex.Message); }
        catch (Exception ex)
        {
            AppLog.Write("Undo file changes failed: " + ex.Message);
            return ("Couldn't undo", Strings.Get("RunUndo_SomethingWentWrongWhile") + ex.Message + Strings.Get("RunUndo_SeeDetailsForWhat"));
        }
    }
}
