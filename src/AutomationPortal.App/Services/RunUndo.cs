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
        var plan = FileUndo.Plan(run!);
        if (!await Prompts.ConfirmAsync(owner, "Undo file changes", RunHistoryActions.UndoConfirmText(plan), "Undo")) return false;
        IReadOnlyList<UndoOutcome> results;
        try { results = await Task.Run(() => FileUndo.Execute(run!, store)); }
        catch (Exception ex)
        {
            AppLog.Write("Undo file changes failed: " + ex.Message);
            await Prompts.MessageAsync(owner, "Couldn't undo", "Something went wrong while undoing: " + ex.Message);
            return true;
        }
        await Prompts.MessageAsync(owner, "Undo file changes", RunHistoryActions.UndoReportText(results));
        return true;
    }
}
