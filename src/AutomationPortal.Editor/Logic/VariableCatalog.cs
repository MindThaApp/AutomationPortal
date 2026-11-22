using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

public enum VariableGroup { Actions, Special, Named }

/// <summary>One entry of the variable picker.</summary>
public sealed record VariableOption(VarRef Ref, string Name, string Icon, string ColorKey, ContentKind Kind, VariableGroup Group, string? Detail = null);

/// <summary>How a variable shows up as a pill.</summary>
public sealed record VarDisplay(string Label, string Icon, string ColorKey, ContentKind Kind, bool Missing = false);

public static class VariableCatalog
{
    /// <summary>
    /// Everything a parameter of the action at position <paramref name="index"/> can use: outputs of the actions above it
    /// (nearest first), then Shortcut Input, Clipboard, Current Date, Repeat / Menu values when inside those blocks, and named variables.
    /// </summary>
    public static IReadOnlyList<VariableOption> Available(Shortcut shortcut, ActionRegistry registry, int index)
    {
        var actions = shortcut.Actions;
        index = Math.Clamp(index, 0, actions.Count);
        var outputs = new List<(int Pos, VariableOption Option)>();
        var open = new List<(int Pos, ActionInstance Start)>();
        var namedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var named = new List<VariableOption>();

        for (var i = 0; i < index; i++)
        {
            var a = actions[i];
            var d = registry.Describe(a.ActionId);
            if (a.Flow is { } f)
            {
                if (f.Role == FlowRole.Start) { open.Add((i, a)); continue; }
                if (f.Role == FlowRole.End && open.Count > 0)
                {
                    var (pos, start) = open[^1];
                    open.RemoveAt(open.Count - 1);
                    var sd = registry.Describe(start.ActionId);
                    if (sd is { HasOutput: true } && !start.Disabled && sd.FlowKind != FlowKind.Menu)
                        outputs.Add((pos, OptionFor(start, sd)));
                }
                continue;
            }
            if (d is null) continue;
            if (d.Id is "var.set" or "var.add" && a.Parameters.TryGetValue("name", out var nv) && nv is TextValue { IsBlank: false } nt
                && namedSeen.Add(nt.ToLiteralText().Trim()))
                named.Add(new VariableOption(VarRef.Named(nt.ToLiteralText().Trim()), nt.ToLiteralText().Trim(), "BracesVariable24", "orange", ContentKind.Any, VariableGroup.Named));
            if (!d.HasOutput || a.Disabled) continue;
            outputs.Add((i, OptionFor(a, d)));
        }

        var result = new List<VariableOption>();
        result.AddRange(outputs.OrderByDescending(o => o.Pos).Select(o => o.Option));
        result.Add(Special(VarRef.ShortcutInput, Strings.Get("VariableCatalog_ShortcutInput"), "ArrowEnterLeft24", "blue", ContentKind.Any));
        result.Add(Special(VarRef.Clipboard, "Clipboard", "Clipboard24", "teal", ContentKind.Any));
        result.Add(Special(VarRef.CurrentDate, Strings.Get("VariableCatalog_CurrentDate"), "Calendar24", "red", ContentKind.Date));
        var kinds = open.Select(o => FlowIds.KindOf(o.Start.ActionId)).ToList();
        if (kinds.Contains(FlowKind.RepeatEach)) result.Add(Special(VarRef.RepeatItem, "Repeat Item", "ArrowSync24", "orange", ContentKind.Any));
        if (kinds.Contains(FlowKind.Repeat) || kinds.Contains(FlowKind.RepeatEach)) result.Add(Special(VarRef.RepeatIndex, "Repeat Index", "NumberSymbol24", "orange", ContentKind.Number));
        if (kinds.Contains(FlowKind.Menu)) result.Add(Special(VarRef.MenuResult, "Menu Result", "AppsListDetail24", "blue", ContentKind.Text));
        result.Add(Special(VarRef.Device, Strings.Get("VariableCatalog_DeviceDetails"), "Desktop24", "gray", ContentKind.Dictionary));
        result.AddRange(named);
        return result;
    }

    private static VariableOption Special(VarRef r, string name, string icon, string color, ContentKind kind) =>
        new(r, name, icon, color, kind, VariableGroup.Special);

    private static VariableOption OptionFor(ActionInstance a, ActionDescriptor d) =>
        new(VarRef.Output(a.Uid), a.OutputName is { Length: > 0 } n ? n : d.EffectiveOutputName, d.Icon, d.Color, d.OutputKindOf(a), VariableGroup.Actions, d.Title);

    /// <summary>The label, icon and color for a pill. Unknown outputs read "Missing variable".</summary>
    public static VarDisplay Describe(VarRef r, Shortcut shortcut, ActionRegistry registry)
    {
        string label; string icon; string color; ContentKind kind = ContentKind.Any; var missing = false;
        switch (r.Kind)
        {
            case VarKind.Output:
                var a = r.Uid is null ? null : shortcut.FindAction(r.Uid);
                var d = a is null ? null : registry.Describe(a.ActionId);
                if (a is null || d is null) { label = Strings.Get("VariableCatalog_MissingVariable"); icon = "Warning24"; color = "red"; missing = true; }
                else { label = a.OutputName is { Length: > 0 } n ? n : d.EffectiveOutputName; icon = d.Icon; color = d.Color; kind = d.OutputKindOf(a); }
                break;
            case VarKind.Named: label = r.Name ?? "Variable"; icon = "BracesVariable24"; color = "orange"; break;
            case VarKind.Input: label = "Shortcut Input"; icon = "ArrowEnterLeft24"; color = "blue"; break;
            case VarKind.Clipboard: label = "Clipboard"; icon = "Clipboard24"; color = "teal"; break;
            case VarKind.CurrentDate: label = "Current Date"; icon = "Calendar24"; color = "red"; kind = ContentKind.Date; break;
            case VarKind.RepeatItem: label = "Repeat Item"; icon = "ArrowSync24"; color = "orange"; break;
            case VarKind.RepeatIndex: label = "Repeat Index"; icon = "NumberSymbol24"; color = "orange"; kind = ContentKind.Number; break;
            case VarKind.MenuResult: label = "Menu Result"; icon = "AppsListDetail24"; color = "blue"; kind = ContentKind.Text; break;
            default: label = Strings.Get("VariableCatalog_DeviceDetails"); icon = "Desktop24"; color = "gray"; kind = ContentKind.Dictionary; break;
        }
        if (r.Property is { Length: > 0 } p) label += " › " + (p.StartsWith("key:", StringComparison.OrdinalIgnoreCase) ? p[4..] : p);
        if (r.Pick != VarPick.All) label = r.Pick switch { VarPick.First => "First ", VarPick.Last => "Last ", _ => "Random " } + label[..1].ToLowerInvariant() + label[1..];
        if (r.CoerceTo is { } c) label += " as " + c;
        return new VarDisplay(label, icon, color, r.CoerceTo ?? kind, missing);
    }

    /// <summary>Property names offered for a variable of this kind (from <see cref="ContentItem.PropertyNames"/>).</summary>
    public static IReadOnlyList<string> PropertiesOf(ContentKind kind)
    {
        ContentItem? sample = kind switch
        {
            ContentKind.Text => new TextItem(""),
            ContentKind.Date => new DateItem(DateTimeOffset.Now),
            ContentKind.Url => new UrlItem(new Uri("https://example.org")),
            ContentKind.File => new FileItem("x"),
            ContentKind.Folder => new FolderItem("x"),
            ContentKind.Image => new ImageItem("x"),
            ContentKind.Dictionary => DictionaryItem.Empty,
            ContentKind.Location => new LocationItem(0, 0),
            ContentKind.App => new AppItem("x", "x"),
            ContentKind.Window => new WindowItem(0, "x", "x"),
            _ => null,
        };
        return sample?.PropertyNames ?? Array.Empty<string>();
    }

    /// <summary>Names of variables created by Set Variable / Add to Variable above <paramref name="index"/> (for the variable name editor).</summary>
    public static IReadOnlyList<string> NamedVariables(Shortcut shortcut, int index = int.MaxValue) =>
        shortcut.Actions.Take(Math.Min(index, shortcut.Actions.Count))
            .Where(a => a.ActionId is "var.set" or "var.add" && a.Parameters.TryGetValue("name", out var v) && v is TextValue { IsBlank: false })
            .Select(a => ((TextValue)a.Parameters["name"]).ToLiteralText().Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
