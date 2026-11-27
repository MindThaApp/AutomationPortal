using System.Text;
using System.Text.RegularExpressions;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public enum SourceGroup { All, Manual, Automations, LinksAndApps }

public sealed record RunAgainState(bool Enabled, string Tooltip, bool AsksForFiles = false);

/// <summary>The pure logic behind the Run History buttons: source groups, "Run again" eligibility, the copied summary and redaction.</summary>
public static class RunHistoryActions
{
    public static bool InGroup(RunSource source, SourceGroup group) => group switch
    {
        SourceGroup.Manual => source is RunSource.Editor or RunSource.Library or RunSource.Hotkey or RunSource.QuickRun or RunSource.SendTo,
        SourceGroup.Automations => source is RunSource.Automation or RunSource.Reminder or RunSource.Nested,
        SourceGroup.LinksAndApps => source is RunSource.Link or RunSource.Cli,
        _ => true,
    };

    /// <summary>
    /// Run History doesn't keep what a run was given, so "Run again" only covers shortcuts that start on their own.
    /// </summary>
    public static RunAgainState RunAgain(Shortcut? shortcut, bool inRecentlyDeleted)
    {
        if (shortcut is null)
            return new RunAgainState(false, inRecentlyDeleted
                ? Strings.Get("RunHistoryActions_ThisShortcutWasDeleted")
                : Strings.Get("RunHistoryActions_ThisShortcutNoLonger"));
        if (shortcut.AcceptsInput.Count > 0)
        {
            var files = shortcut.AcceptsInput.Any(k => k is ContentKind.File or ContentKind.Folder or ContentKind.Image or ContentKind.Any);
            return files
                ? new RunAgainState(true, Strings.Get("RunHistoryActions_ChooseTheFilesTo"), AsksForFiles: true)
                : new RunAgainState(false, Strings.Get("RunHistoryActions_ThisShortcutNeedsSomething"));
        }
        return new RunAgainState(true, Strings.Get("RunHistoryActions_RunThisShortcutAgain"));
    }

    public static StepLog? FailedStep(RunRecord r) =>
        r.Steps.FirstOrDefault(s => !string.IsNullOrEmpty(r.FailedStepUid) && s.Uid == r.FailedStepUid)
        ?? r.Steps.FirstOrDefault(s => !string.IsNullOrEmpty(s.Error));

    public static string Summary(RunRecord r, string sourceText, string statusText)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Strings.Format("RunHistoryActions_Shortcut0", r.ShortcutName));
        sb.AppendLine(Strings.Format("RunHistoryActions_Started0F", r.Started.ToLocalTime()));
        sb.AppendLine(Strings.Format("RunHistoryActions_Duration0", FormatDuration(r.DurationMs)));
        sb.AppendLine(Strings.Format("RunHistoryActions_Result0", statusText));
        sb.AppendLine(Strings.Format("RunHistoryActions_Source0", sourceText));
        if (!string.IsNullOrWhiteSpace(r.ErrorMessage)) sb.AppendLine(Strings.Format("RunHistoryActions_Problem0", Redact(r.ErrorMessage!)));
        if (FailedStep(r) is { } step)
        {
            var n = r.Steps.IndexOf(step) + 1;
            var title = string.IsNullOrWhiteSpace(step.Title) ? step.ActionId : step.Title;
            sb.AppendLine(Strings.Format("RunHistoryActions_StepThatDidnT", n, Redact(title)));
            if (!string.IsNullOrWhiteSpace(step.Error) && step.Error != r.ErrorMessage) sb.AppendLine(Strings.Format("RunHistoryActions_StepProblem0", Redact(step.Error!)));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The confirm text for "Undo file changes": what will be done, then what the user has to do themselves.</summary>
    public static string UndoConfirmText(IReadOnlyList<AutomationPortal.Actions.Documents.UndoStep> plan)
    {
        var sb = new StringBuilder();
        var auto = plan.Where(s => s.Automatic).ToList();
        var manual = plan.Where(s => !s.Automatic).ToList();
        if (auto.Count > 0)
        {
            sb.AppendLine(Strings.Get("RunHistoryActions_AutomationPortalWill"));
            foreach (var s in auto) sb.AppendLine("• " + s.Description);
        }
        if (manual.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine(Strings.Get("RunHistoryActions_NotUndoneAutomatically"));
            foreach (var s in manual) sb.AppendLine("• " + s.Description);
        }
        sb.AppendLine();
        sb.AppendLine(Strings.Get("RunHistoryActions_NothingIsOverwrittenItems"));
        sb.Append(Strings.Get("RunHistoryActions_IfWindowsSaysAn"));
        return sb.ToString();
    }

    public static string UndoReportText(IReadOnlyList<AutomationPortal.Actions.Documents.UndoOutcome> results)
    {
        var done = results.Count(r => r.Done);
        var sb = new StringBuilder();
        sb.AppendLine(results.Count > 0 && done == results.Count ? Strings.Get("RunHistoryActions_EverythingWasUndone") : Strings.Format("RunHistoryActions_0Of1Changes", done, results.Count));
        foreach (var r in results.Where(r => !r.Done && r.Step.Automatic))
            sb.AppendLine(Strings.Format("RunHistoryActions_Skipped01", r.Step.Description, r.Problem));
        foreach (var r in results.Where(r => !r.Step.Automatic))
            sb.AppendLine("• " + r.Step.Description);
        return sb.ToString().TrimEnd();
    }

    private static string FormatDuration(double ms) =>
        ms < 1000 ? $"{Math.Max(1, (int)ms)} ms" : ms < 60_000 ? $"{ms / 1000:0.#} s" : $"{(int)(ms / 60_000)} min {(int)(ms / 1000 % 60)} s";

    private static readonly Regex[] Secrets =
    {
        new(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.Compiled),
        new(@"(?i)\b(api[_-]?key|apikey|token|secret|password|passwd|pwd|authorization|access[_-]?key)\b(\s*[:=]\s*)(""[^""]*""|'[^']*'|\S+)", RegexOptions.Compiled),
        new(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{12,}", RegexOptions.Compiled),
        new(@"\b(?:ghp|gho|ghu|ghs|github_pat|xox[abprs]|AIza|AKIA)[A-Za-z0-9_\-]{12,}", RegexOptions.Compiled),
        new(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]*", RegexOptions.Compiled),
        new(@"(?i)([?&](?:key|token|api_key|apikey|access_token|sig|signature)=)[^&\s]+", RegexOptions.Compiled),
        new(@"\b[A-Fa-f0-9]{32,}\b|\b[A-Za-z0-9_\-]{40,}\b", RegexOptions.Compiled),
    };

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = Secrets[0].Replace(text, "Bearer [hidden]");
        t = Secrets[1].Replace(t, m => m.Groups[1].Value + m.Groups[2].Value + "[hidden]");
        t = Secrets[2].Replace(t, "[hidden]");
        t = Secrets[3].Replace(t, "[hidden]");
        t = Secrets[4].Replace(t, "[hidden]");
        t = Secrets[5].Replace(t, m => m.Groups[1].Value + "[hidden]");
        t = Secrets[6].Replace(t, "[hidden]");
        return t;
    }
}
