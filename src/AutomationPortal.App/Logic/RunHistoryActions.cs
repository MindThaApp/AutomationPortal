using System.Text;
using System.Text.RegularExpressions;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public enum SourceGroup { All, Manual, Automations, LinksAndApps }

public sealed record RunAgainState(bool Enabled, string Tooltip);

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
                ? "This shortcut was deleted. Restore it from Recently deleted in your library to run it again."
                : "This shortcut no longer exists, so it can't be run again.");
        if (shortcut.AcceptsInput.Count > 0)
            return new RunAgainState(false, "This shortcut needs something to work on, and Run History doesn't keep that. Run it from your library or Quick Run instead.");
        return new RunAgainState(true, "Run this shortcut again.");
    }

    public static StepLog? FailedStep(RunRecord r) =>
        r.Steps.FirstOrDefault(s => !string.IsNullOrEmpty(r.FailedStepUid) && s.Uid == r.FailedStepUid)
        ?? r.Steps.FirstOrDefault(s => !string.IsNullOrEmpty(s.Error));

    public static string Summary(RunRecord r, string sourceText, string statusText)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Shortcut: {r.ShortcutName}");
        sb.AppendLine($"Started: {r.Started.ToLocalTime():f}");
        sb.AppendLine($"Duration: {FormatDuration(r.DurationMs)}");
        sb.AppendLine($"Result: {statusText}");
        sb.AppendLine($"Source: {sourceText}");
        if (!string.IsNullOrWhiteSpace(r.ErrorMessage)) sb.AppendLine($"Problem: {Redact(r.ErrorMessage!)}");
        if (FailedStep(r) is { } step)
        {
            var n = r.Steps.IndexOf(step) + 1;
            var title = string.IsNullOrWhiteSpace(step.Title) ? step.ActionId : step.Title;
            sb.AppendLine($"Step that didn't finish: {n}. {Redact(title)}");
            if (!string.IsNullOrWhiteSpace(step.Error) && step.Error != r.ErrorMessage) sb.AppendLine($"Step problem: {Redact(step.Error!)}");
        }
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
