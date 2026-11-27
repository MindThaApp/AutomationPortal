using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

public enum DisplayPartKind { Text, Variable, Placeholder, Ask, Color }

/// <summary>A piece of a token's display: literal text, a variable pill, a hint for an empty value …</summary>
public readonly record struct DisplayPart(DisplayPartKind Kind, string Text, VarRef? Var = null);

public interface IValueDisplayContext
{
    VarDisplay Describe(VarRef reference);
    /// <summary>Label for a stored choice id, or null when unknown.</summary>
    string? ChoiceLabel(ParameterDescriptor parameter, string id);
}

/// <summary>Turns a parameter value into what a summary token shows.</summary>
public static class ValueFormat
{
    public static IReadOnlyList<DisplayPart> Parts(ParameterDescriptor p, ParamValue? value, IValueDisplayContext ctx)
    {
        var v = value ?? p.Default;
        var isTextual = p.Type is (ParamType.Text or ParamType.MultilineText or ParamType.Url);
        var hint = isTextual && !string.IsNullOrEmpty(p.Placeholder) ? p.Placeholder! : p.Label.ToLowerInvariant();
        var empty = new[] { new DisplayPart(DisplayPartKind.Placeholder, hint) };
        if (v is null) return empty;
        switch (v)
        {
            case AskValue: return new[] { new DisplayPart(DisplayPartKind.Ask, Strings.Get("ValueFormat_AskEachTime")) };
            case VarValue vv: return new[] { new DisplayPart(DisplayPartKind.Variable, ctx.Describe(vv.Ref).Label, vv.Ref) };
            case TextValue t:
                {
                    if (t.IsBlank) return empty;
                    var list = new List<DisplayPart>();
                    foreach (var part in TokenText.Normalize(t.Parts))
                    {
                        if (part.Var is { } r) list.Add(new DisplayPart(DisplayPartKind.Variable, ctx.Describe(r).Label, r));
                        else list.Add(new DisplayPart(DisplayPartKind.Text, Shorten(part.Literal ?? "", 60)));
                    }
                    if (p.Type == ParamType.Color && list.Count == 1) return new[] { new DisplayPart(DisplayPartKind.Color, list[0].Text) };
                    return list;
                }
            case NumberValue n:
                return One(ContentFormat.Number(n.Value) + (p.Unit is { Length: > 0 } u && p.Type == ParamType.Number ? " " + (n.Value == 1 ? PluralAgreement.Singular(u) : u) : ""));
            case BoolValue b: return One(b.Value ? "On" : "Off");
            case ChoiceValue c: return One(ctx.ChoiceLabel(p, c.Value) ?? c.Value);
            case DateValue d:
                return One(p.Type == ParamType.DateTime || d.Value.TimeOfDay != TimeSpan.Zero
                    ? d.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)
                    : d.Value.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
            case TimeValue tm: return One(tm.Value.ToString("t", CultureInfo.CurrentCulture));
            case DurationValue du: return One(Duration(du.Seconds));
            case HotkeyValue hk: return string.IsNullOrWhiteSpace(hk.Keys) ? empty : One(hk.Keys);
            case FileValue f: return string.IsNullOrWhiteSpace(f.Path) ? empty : One(FileLabel(f.Path));
            case AppValue a: return string.IsNullOrWhiteSpace(a.Name ?? a.Id) ? empty : One(a.DisplayText);
            case ListValue l:
                if (l.Items.Count == 0) return empty;
                if (p.Choices is { Count: > 0 })
                {
                    var labels = l.Items.Select(i => ctx.ChoiceLabel(p, i.ToLiteralText()) ?? i.ToLiteralText()).ToList();
                    var all = string.Join(", ", labels);
                    return One(all.Length <= 40 ? all : $"{labels.Count} selected");
                }
                if (l.Items.All(i => i is TextValue { Parts.Count: 1 } tv && tv.Parts[0].Var is null) && l.Items.Count <= 3)
                {
                    var joined = string.Join(", ", l.Items.Select(i => i.ToLiteralText()));
                    if (joined.Length <= 40) return One(joined);
                }
                return One(l.Items.Count == 1 ? "1 item" : $"{l.Items.Count} items");
            case DictValue dv:
                return dv.Items.Count == 0 ? empty : One(dv.Items.Count == 1 ? "1 item" : $"{dv.Items.Count} items");
            case ConditionsValue cv: return Conditions(cv, p, ctx, empty);
            default: return empty;
        }
    }

    private static IReadOnlyList<DisplayPart> One(string text) => new[] { new DisplayPart(DisplayPartKind.Text, text) };

    private static IReadOnlyList<DisplayPart> Conditions(ConditionsValue cv, ParameterDescriptor p, IValueDisplayContext ctx, IReadOnlyList<DisplayPart> empty)
    {
        if (cv.Items.Count == 0) return new[] { new DisplayPart(DisplayPartKind.Placeholder, "a condition") };
        var row = cv.Items[0];
        var list = new List<DisplayPart>();
        list.AddRange(Operand(row.Left, ctx, "something"));
        list.Add(new DisplayPart(DisplayPartKind.Text, " " + ConditionModel.Label(row.Op) + " "));
        var n = ConditionModel.Operands(row.Op);
        if (n >= 1 && row.Right is not null) list.AddRange(Operand(row.Right, ctx, "a value"));
        if (n >= 2 && row.Right2 is not null) { list.Add(new DisplayPart(DisplayPartKind.Text, " and ")); list.AddRange(Operand(row.Right2, ctx, "a value")); }
        if (list.Count > 0 && list[^1].Kind == DisplayPartKind.Text && list[^1].Text.EndsWith(' ')) list[^1] = list[^1] with { Text = list[^1].Text.TrimEnd() };
        if (cv.Items.Count > 1) list.Add(new DisplayPart(DisplayPartKind.Text, $" {(cv.Match == ConditionMatch.All ? "and" : "or")} {cv.Items.Count - 1} more"));
        return list;
    }

    private static IEnumerable<DisplayPart> Operand(ParamValue v, IValueDisplayContext ctx, string blank)
    {
        switch (v)
        {
            case VarValue vv: yield return new DisplayPart(DisplayPartKind.Variable, ctx.Describe(vv.Ref).Label, vv.Ref); break;
            case TextValue t:
                if (t.IsBlank) { yield return new DisplayPart(DisplayPartKind.Placeholder, blank); break; }
                foreach (var part in TokenText.Normalize(t.Parts))
                    yield return part.Var is { } r ? new DisplayPart(DisplayPartKind.Variable, ctx.Describe(r).Label, r) : new DisplayPart(DisplayPartKind.Text, Shorten(part.Literal ?? "", 30));
                break;
            default: yield return new DisplayPart(DisplayPartKind.Text, Shorten(v.ToLiteralText(), 30)); break;
        }
    }

    public static string Shorten(string text, int max)
    {
        var line = text.Replace("\r\n", "\n");
        var nl = line.IndexOf('\n');
        var multi = nl >= 0;
        if (multi) line = line[..nl];
        if (line.Length > max) return line[..max] + "…";
        return multi ? line + "…" : line;
    }

    /// <summary>"1 minute 30 seconds", "2 hours", "45 seconds".</summary>
    public static string Duration(double seconds)
    {
        var total = (long)Math.Round(seconds);
        if (total < 60) return seconds == 1 ? "1 second" : (seconds % 1 == 0 ? $"{total} seconds" : $"{ContentFormat.Number(seconds)} seconds");
        var h = total / 3600; var m = total % 3600 / 60; var s = total % 60;
        var parts = new List<string>();
        if (h > 0) parts.Add(h == 1 ? "1 hour" : $"{h} hours");
        if (m > 0) parts.Add(m == 1 ? "1 minute" : $"{m} minutes");
        if (s > 0) parts.Add(s == 1 ? "1 second" : $"{s} seconds");
        return string.Join(" ", parts);
    }

    private static string FileLabel(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
