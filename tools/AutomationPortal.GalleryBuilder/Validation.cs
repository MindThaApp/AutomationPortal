using AutomationPortal.Core;

namespace AutomationPortal.GalleryBuilder;

/// <summary>Strict checks on top of <see cref="ShortcutValidator"/>: parameter keys, value kinds, choices, variables, import questions, icons, colours.</summary>
internal static class Validation
{
    private static readonly string[] Colors =
        { "red", "orange", "yellow", "green", "mint", "teal", "cyan", "blue", "indigo", "purple", "pink", "brown", "gray", "graphite", "slate" };

    public static List<string> Check(Starter starter, ActionRegistry registry, TriggerRegistry triggers, HashSet<string> icons)
    {
        var errors = new List<string>();
        var s = starter.Shortcut;
        void Err(string m) => errors.Add(m);

        if (string.IsNullOrWhiteSpace(s.Name)) Err("Missing name.");
        if (string.IsNullOrWhiteSpace(s.Description)) Err("Missing description.");
        if (!Colors.Contains(s.Color)) Err($"Unknown colour '{s.Color}'.");
        if (!icons.Contains(s.Icon)) Err($"Icon '{s.Icon}' is not a WPF-UI SymbolRegular name.");
        if (s.ShowInSendTo && s.AcceptsInput.Count == 0) Err("Shown in Send To but accepts no input.");

        foreach (var i in ShortcutValidator.Validate(s, registry))
            Err($"[{i.Severity}] {i.Code}: {i.Message} (step {i.ActionUid}, parameter {i.ParameterKey})");

        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uids = new Dictionary<string, int>();
        for (var i = 0; i < s.Actions.Count; i++) uids[s.Actions[i].Uid] = i;

        for (var i = 0; i < s.Actions.Count; i++)
        {
            var a = s.Actions[i];
            var d = registry.Describe(a.ActionId);
            if (d is null) { Err($"Step '{a.Uid}': unknown action '{a.ActionId}'."); continue; }

            foreach (var (key, value) in a.Parameters)
            {
                var p = d.FindParameter(key);
                if (p is null) { Err($"Step '{a.Uid}' ({a.ActionId}): unknown parameter '{key}'."); continue; }
                if (!KindOk(p, value)) Err($"Step '{a.Uid}' ({a.ActionId}): parameter '{key}' ({p.Type}) got a {value.GetType().Name}.");
                if (value is ChoiceValue cv && p.Choices is { Count: > 0 } choices && !choices.Any(c => c.Id == cv.Value))
                    Err($"Step '{a.Uid}' ({a.ActionId}): '{cv.Value}' is not a choice of '{key}'.");
                if (p.Min is { } min && value is NumberValue nmin && nmin.Value < min) Err($"Step '{a.Uid}': '{key}' below minimum.");
                if (p.Max is { } max && value is NumberValue nmax && nmax.Value > max) Err($"Step '{a.Uid}': '{key}' above maximum.");

                foreach (var r in value.VarRefs())
                {
                    if (r.Kind == VarKind.Output && r.Uid is not null && uids.TryGetValue(r.Uid, out var idx))
                    {
                        var target = registry.Describe(s.Actions[idx].ActionId);
                        if (target is { HasOutput: false }) Err($"Step '{a.Uid}': uses the output of '{r.Uid}' ({target.Title}), which has no output.");
                    }
                    if (r.Kind == VarKind.Named && r.Name is not null && !named.Contains(r.Name)) Err($"Step '{a.Uid}': variable '{r.Name}' is used before it is set.");
                    if (r.Kind == VarKind.Input && s.AcceptsInput.Count == 0) Err($"Step '{a.Uid}': uses Shortcut Input but the shortcut accepts no input.");
                }
            }

            if (a.ActionId is "var.set" or "var.add" && a.Parameters.TryGetValue("name", out var nv)) named.Add(nv.ToLiteralText());
        }

        foreach (var q in s.ImportQuestions)
        {
            var a = s.FindAction(q.ActionUid);
            var d = a is null ? null : registry.Describe(a.ActionId);
            var p = d?.FindParameter(q.ParameterKey);
            if (a is null || d is null || p is null) { Err($"Import question '{q.Question}' points at {q.ActionUid}.{q.ParameterKey}, which doesn't exist."); continue; }
            if (q.DefaultValue is null) Err($"Import question '{q.Question}' has no default.");
            else if (!KindOk(p, q.DefaultValue)) Err($"Import question '{q.Question}': default is a {q.DefaultValue.GetType().Name} but the parameter is {p.Type}.");
            if (q.DefaultValue is ChoiceValue cv && p.Choices is { Count: > 0 } ch && !ch.Any(c => c.Id == cv.Value)) Err($"Import question '{q.Question}': '{cv.Value}' is not a choice.");
            if (!q.Question.EndsWith('?') && !q.Question.EndsWith(')') && !q.Question.EndsWith('.')) Err($"Import question '{q.Question}' should read like a question.");
        }

        if (starter.Suggested is { } t)
        {
            var tp = triggers.Get(t.TriggerId);
            if (tp is null) Err($"Suggested trigger '{t.TriggerId}' doesn't exist.");
            else foreach (var (k, v) in t.Parameters)
            {
                var p = tp.Descriptor.Parameters.FirstOrDefault(x => x.Key == k);
                if (p is null) Err($"Suggested trigger '{t.TriggerId}': unknown parameter '{k}'.");
                else if (v is ChoiceValue cv && p.Choices is { Count: > 0 } ch && !ch.Any(c => c.Id == cv.Value)) Err($"Suggested trigger '{t.TriggerId}': '{cv.Value}' is not a choice of '{k}'.");
            }
        }

        // Round-trip through the real export/import code.
        try
        {
            var back = ShortcutPackage.Parse(ShortcutPackage.Serialize(s));
            if (back.Actions.Count != s.Actions.Count) Err("Export/import round trip changed the number of steps.");
            if (AutomationPortalJson.Serialize(back, false) != AutomationPortalJson.Serialize(s, false)) Err("Export/import round trip changed the shortcut.");
        }
        catch (Exception ex) { Err("Export/import round trip failed: " + ex.Message); }

        return errors;
    }

    private static bool KindOk(ParameterDescriptor p, ParamValue v)
    {
        if (v is AskValue) return p.AllowAskEachTime;
        if (v is VarValue) return p.AllowVariables;
        return p.Type switch
        {
            ParamType.Text or ParamType.MultilineText or ParamType.Url or ParamType.VariableName or ParamType.Color
                => v is TextValue or NumberValue,
            ParamType.Number => v is NumberValue,
            ParamType.Boolean => v is BoolValue,
            ParamType.Choice => v is ChoiceValue,
            ParamType.Date or ParamType.DateTime => v is DateValue,
            ParamType.Time => v is TimeValue,
            ParamType.Duration => v is DurationValue,
            ParamType.File or ParamType.Folder or ParamType.SaveFile => v is FileValue or TextValue,
            ParamType.App => v is AppValue,
            ParamType.Hotkey => v is HotkeyValue,
            ParamType.Content => true,
            ParamType.List => v is ListValue,
            ParamType.Dictionary => v is DictValue,
            ParamType.Conditions => v is ConditionsValue,
            ParamType.Shortcut => v is TextValue,
            _ => true,
        };
    }
}
