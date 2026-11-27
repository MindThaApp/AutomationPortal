using System.Text;
using AutomationPortal.Core;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor.Logic;

/// <summary>The text a screen reader announces for an action row: title and sentence, then anything notable.</summary>
internal static class RowAutomationName
{
    public static string Build(ActionRowViewModel row)
    {
        var a = row.Action;
        var d = row.Descriptor;
        if (d is null) return Strings.Format("RowAutomationName_UnavailableAction0", a.ActionId);
        if (row.IsBar)
        {
            if (row.Role == FlowRole.Middle) return row.FlowKind == FlowKind.Menu ? Strings.Format("RowAutomationName_MenuOption0", a.Flow?.Label) : "Otherwise";
            return d.FlowKind switch { FlowKind.If => Strings.Get("RowAutomationName_EndIf"), FlowKind.Menu => Strings.Get("RowAutomationName_EndMenu"), FlowKind.Repeat or FlowKind.RepeatEach => Strings.Get("RowAutomationName_EndRepeat"), _ => "End" };
        }

        string? Current(string key) => (a.Parameters.TryGetValue(key, out var v) ? v : d.FindParameter(key)?.Default)?.ToLiteralText();
        var sentence = new StringBuilder();
        foreach (var seg in SummaryLayout.Visible(d.SummarySegments, k => d.FindParameter(k) is not { } vp || vp.IsVisible(Current)))
        {
            if (!seg.IsToken) { sentence.Append(seg.Text); continue; }
            var p = d.FindParameter(seg.ParamKey!);
            if (p is null) { sentence.Append(seg.Text); continue; }
            a.Parameters.TryGetValue(p.Key, out var value);
            sentence.Append(string.Concat(ValueFormat.Parts(p, value, row.Owner.Env).Select(x => x.Text)));
        }
        var text = sentence.ToString().Trim();
        var name = text.Length == 0 ? d.Title
            : text.StartsWith(d.Title, StringComparison.OrdinalIgnoreCase) ? text : $"{d.Title}: {text}";

        var notes = new List<string>();
        if (a.Disabled) notes.Add(Strings.Get("RowAutomationName_TurnedOff"));
        if (d.IsDestructiveFor(a)) notes.Add(Strings.Get("RowAutomationName_CanChangeOrDelete"));
        foreach (var issue in row.Issues.Take(2)) notes.Add((issue.Severity == IssueSeverity.Error ? "problem: " : "note: ") + issue.Message);
        switch (row.State)
        {
            case StepState.Running: notes.Add("running"); break;
            case StepState.Paused: notes.Add(Strings.Get("RowAutomationName_WaitingForYou")); break;
            case StepState.Succeeded: notes.Add("finished"); break;
            case StepState.Failed: notes.Add("failed" + (row.Error is { Length: > 0 } e ? ": " + e : "")); break;
        }
        if (row.HasBreakpoint) notes.Add(Strings.Get("RowAutomationName_PausesHereWhenYou"));
        return notes.Count == 0 ? name : $"{name} ({string.Join("; ", notes)})";
    }
}
