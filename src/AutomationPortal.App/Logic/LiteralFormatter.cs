using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>Plain-English text for literal parameter values ("7:30 AM", "5 minutes", "Mon, Wed").</summary>
public static class LiteralFormatter
{
    public static bool IsBlank(ParamValue? v) => v switch
    {
        null => true,
        TextValue t => t.IsBlank,
        ChoiceValue c => string.IsNullOrWhiteSpace(c.Value),
        ListValue l => l.Items.Count == 0,
        FileValue f => string.IsNullOrWhiteSpace(f.Path),
        AppValue a => string.IsNullOrWhiteSpace(a.Id) && string.IsNullOrWhiteSpace(a.Name),
        HotkeyValue h => string.IsNullOrWhiteSpace(h.Keys),
        _ => false,
    };

    /// <summary>The value the user will actually get: the stored one, else the descriptor default.</summary>
    public static ParamValue? Effective(ParameterDescriptor p, IReadOnlyDictionary<string, ParamValue> values) =>
        values.TryGetValue(p.Key, out var v) ? v : p.Default;

    public static string Format(ParameterDescriptor p, ParamValue? value)
    {
        if (IsBlank(value) && p.Type is not (ParamType.Boolean or ParamType.Number or ParamType.Duration)) return "";
        switch (value)
        {
            case null: return "";
            case ChoiceValue c: return ChoiceLabel(p, c.Value);
            case BoolValue b: return b.Value ? "yes" : "no";
            case NumberValue n: return n.Value.ToString("0.##", CultureInfo.CurrentCulture) + (string.IsNullOrEmpty(p.Unit) ? "" : " " + p.Unit);
            case TimeValue t: return DateTime.Today.Add(t.Value.ToTimeSpan()).ToString("t", CultureInfo.CurrentCulture);
            case DateValue d:
                var local = d.Value.ToLocalTime();
                return p.Type == ParamType.Date || local.TimeOfDay == TimeSpan.Zero
                    ? local.ToString("d", CultureInfo.CurrentCulture)
                    : local.ToString("g", CultureInfo.CurrentCulture);
            case DurationValue du: return Duration(du.Seconds);
            case FileValue f: return p.Type == ParamType.Folder ? FolderName(f.Path) : Path.GetFileName(f.Path.TrimEnd('\\', '/')) is { Length: > 0 } n2 ? n2 : f.Path;
            case AppValue a: return a.Name ?? a.Id;
            case HotkeyValue h: return h.Keys;
            case ListValue l: return string.Join(", ", l.Items.Select(i => ChoiceLabel(p, i.ToLiteralText(), shortDays: true)));
            case TextValue tv when p.Type is ParamType.Folder: return FolderName(tv.ToLiteralText());
            default: return value.ToLiteralText();
        }
    }

    private static string FolderName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string ChoiceLabel(ParameterDescriptor p, string id, bool shortDays = false)
    {
        var label = p.Choices?.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Label ?? id;
        if (shortDays && label.Length > 3 && DateTimeFormatInfo.CurrentInfo.DayNames.Contains(label, StringComparer.CurrentCultureIgnoreCase))
            return label[..3];
        return label;
    }

    public static string Duration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalSeconds < 1) return "0 seconds";
        var parts = new List<string>();
        if ((int)ts.TotalDays > 0) parts.Add(Plural((int)ts.TotalDays, "day"));
        if (ts.Hours > 0) parts.Add(Plural(ts.Hours, "hour"));
        if (ts.Minutes > 0) parts.Add(Plural(ts.Minutes, "minute"));
        if (ts.Seconds > 0) parts.Add(Plural(ts.Seconds, "second"));
        return string.Join(" ", parts);
    }

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

    /// <summary>"Spotify", or the placeholder "…" when the user hasn't chosen yet.</summary>
    public static string TokenText(ParameterDescriptor p, IReadOnlyDictionary<string, ParamValue> values, out bool isSet)
    {
        var text = Format(p, Effective(p, values));
        isSet = text.Length > 0;
        return isSet ? text : "[" + p.Label.ToLowerInvariant() + "]";
    }

    /// <summary>Labels of required parameters that are visible but empty.</summary>
    public static IReadOnlyList<string> MissingRequired(IReadOnlyList<ParameterDescriptor> parameters, IReadOnlyDictionary<string, ParamValue> values)
    {
        var missing = new List<string>();
        foreach (var p in parameters)
        {
            if (!p.Required) continue;
            if (!p.IsVisible(k => values.TryGetValue(k, out var v) ? v.ToLiteralText() : parameters.FirstOrDefault(x => x.Key == k)?.Default?.ToLiteralText())) continue;
            if (IsBlank(Effective(p, values))) missing.Add(p.Label);
        }
        return missing;
    }
}
