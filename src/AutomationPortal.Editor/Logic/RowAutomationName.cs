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
        if (d is null) return $"Unavailable action {a.ActionId}";
        if (row.IsBar)
        {
            if (row.Role == FlowRole.Middle) return row.FlowKind == FlowKind.Menu ? $"Menu option {a.Flow?.Label}" : "Otherwise";
            return d.FlowKind switch { FlowKind.If => "End If", FlowKind.Menu => "End Menu", FlowKind.Repeat or FlowKind.RepeatEach => "End Repeat", _ => "End" };
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
        if (a.Disabled) notes.Add("turned off");
        if (d.IsDestructiveFor(a)) notes.Add("can change or delete things");
        foreach (var issue in row.Issues.Take(2)) notes.Add((issue.Severity == IssueSeverity.Error ? "problem: " : "note: ") + issue.Message);
        switch (row.State)
        {
            case StepState.Running: notes.Add("running"); break;
            case StepState.Succeeded: notes.Add("finished"); break;
            case StepState.Failed: notes.Add("failed" + (row.Error is { Length: > 0 } e ? ": " + e : "")); break;
        }
        return notes.Count == 0 ? name : $"{name} ({string.Join("; ", notes)})";
    }
}
