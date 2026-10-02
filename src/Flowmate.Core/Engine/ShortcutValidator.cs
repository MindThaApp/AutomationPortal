namespace Flowmate.Core;

public enum IssueSeverity { Warning, Error }

public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, string? ActionUid = null, string? ParameterKey = null);

/// <summary>Finds problems the editor should show inline. Codes: unknownAction, duplicateUid, brokenGroup, laterVariable, removedVariable, missingParameter, outsideRepeat, outsideMenu.</summary>
public static class ShortcutValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(Shortcut shortcut, ActionRegistry registry)
    {
        var issues = new List<ValidationIssue>();
        var indexOf = new Dictionary<string, int>();
        var seen = new HashSet<string>();

        for (var i = 0; i < shortcut.Actions.Count; i++)
        {
            var uid = shortcut.Actions[i].Uid;
            if (!seen.Add(uid))
                issues.Add(new(IssueSeverity.Error, "duplicateUid", "Two steps share the same internal id. Duplicate one of them again.", uid));
            indexOf[uid] = i;
        }

        try { FlowTree.Parse(shortcut.Actions); }
        catch (FlowStructureException ex) { issues.Add(new(IssueSeverity.Error, "brokenGroup", ex.Message, ex.Uid)); }

        var repeatDepth = 0;
        var menuDepth = 0;
        var menuStack = new Stack<bool>();
        for (var i = 0; i < shortcut.Actions.Count; i++)
        {
            var a = shortcut.Actions[i];
            var d = registry.Describe(a.ActionId);
            if (d is null)
            {
                issues.Add(new(IssueSeverity.Error, "unknownAction", $"Flowmate doesn't know the action '{a.ActionId}'. Delete this step or update Flowmate.", a.Uid));
                continue;
            }

            if (a.Flow is { } f)
            {
                var kind = FlowIds.KindOf(a.ActionId);
                if (kind is FlowKind.Repeat or FlowKind.RepeatEach)
                {
                    if (f.Role == FlowRole.Start) repeatDepth++; else if (f.Role == FlowRole.End) repeatDepth = Math.Max(0, repeatDepth - 1);
                }
                else if (kind == FlowKind.Menu)
                {
                    if (f.Role == FlowRole.Middle) menuDepth = Math.Max(menuDepth, 1);
                    if (f.Role == FlowRole.Start) { menuStack.Push(false); }
                    if (f.Role == FlowRole.End && menuStack.Count > 0) menuStack.Pop();
                }
            }
            var insideMenu = menuStack.Count > 0;

            if (!a.Disabled && (a.Flow is null || a.Flow.Role == FlowRole.Start))
            {
                foreach (var p in d.Parameters)
                {
                    if (!p.Required) continue;
                    var has = a.Parameters.TryGetValue(p.Key, out var pv) && !IsBlank(pv);
                    if (has || p.Default is not null) continue;
                    if (p.IsInput && (i > 0 || shortcut.AcceptsInput.Count > 0)) continue;
                    issues.Add(new(IssueSeverity.Error, "missingParameter", $"'{d.Title}' needs \"{p.Label}\" to be filled in.", a.Uid, p.Key));
                }
            }

            foreach (var (key, value) in a.Parameters)
            {
                foreach (var r in value.VarRefs())
                {
                    switch (r.Kind)
                    {
                        case VarKind.Output:
                            if (r.Uid is null || !indexOf.TryGetValue(r.Uid, out var target))
                                issues.Add(new(IssueSeverity.Error, "removedVariable", "This uses the result of a step that was deleted. Pick another variable.", a.Uid, key));
                            else if (target >= i)
                                issues.Add(new(IssueSeverity.Error, "laterVariable", "This uses the result of a step that runs later. Move that step above, or pick another variable.", a.Uid, key));
                            break;
                        case VarKind.RepeatItem:
                        case VarKind.RepeatIndex:
                            if (repeatDepth == 0)
                                issues.Add(new(IssueSeverity.Warning, "outsideRepeat", "Repeat Item and Repeat Index only have a value inside a Repeat block.", a.Uid, key));
                            break;
                        case VarKind.MenuResult:
                            if (!insideMenu)
                                issues.Add(new(IssueSeverity.Warning, "outsideMenu", "Menu Result only has a value inside a Choose from Menu block.", a.Uid, key));
                            break;
                    }
                }
            }
        }
        return issues;
    }

    private static bool IsBlank(ParamValue? v) => v is null or TextValue { IsBlank: true } or ListValue { Items.Count: 0 };
}
