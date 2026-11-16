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
            await Prompts.MessageAsync(owner, "Nothing to undo", run?.UndoneAt is not null
                ? "This run's file changes were already undone."
                : "This run didn't change any files that Automation Portal can undo, or its record is older than a week.");
            return false;
        }
        if (store.IsUndoing(runId))
        {
            await Prompts.MessageAsync(owner, "Undo file changes", "This run's file changes are already being undone.");
            return false;
        }
        var plan = FileUndo.Plan(run!);
        if (!await Prompts.ConfirmAsync(owner, "Undo file changes", RunHistoryActions.UndoConfirmText(plan), "Undo")) return false;
        IReadOnlyList<UndoOutcome> results;
        try { results = await Task.Run(() => FileUndo.Execute(run!, store)); }
        catch (ActionException ex)
        {
            await Prompts.MessageAsync(owner, "Undo file changes", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write("Undo file changes failed: " + ex.Message);
            await Prompts.MessageAsync(owner, "Couldn't undo", "Something went wrong while undoing: " + ex.Message);
            return true;
        }
        await Prompts.MessageAsync(owner, "Undo file changes", RunHistoryActions.UndoReportText(results));
        return true;
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
            return ("Nothing to undo", run?.UndoneAt is not null
                ? "This run's file changes were already undone."
                : "This run didn't change any files that Automation Portal can undo, or its record is older than a week.");
        if (store.IsUndoing(runId)) return ("Undo file changes", "This run's file changes are already being undone.");
        try { return RunResultText.UndoReport(await Task.Run(() => FileUndo.Execute(run!, store))); }
        catch (ActionException ex) { return ("Couldn't undo", ex.Message); }
        catch (Exception ex)
        {
            AppLog.Write("Undo file changes failed: " + ex.Message);
            return ("Couldn't undo", "Something went wrong while undoing: " + ex.Message + " See details for what was put back.");
        }
    }
}
