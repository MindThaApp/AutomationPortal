using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

/// <summary>Conversion between a token text box's content (text with variable pills) and <see cref="ParamValue"/>.</summary>
public static class TokenText
{
    /// <summary>Merges neighbouring literals and drops empty ones.</summary>
    public static List<TextPart> Normalize(IEnumerable<TextPart> parts)
    {
        var result = new List<TextPart>();
        var sb = new System.Text.StringBuilder();
        void Flush() { if (sb.Length > 0) { result.Add(TextPart.Of(sb.ToString())); sb.Clear(); } }
        foreach (var p in parts)
        {
            if (p.Var is { } v) { Flush(); result.Add(TextPart.Of(v)); }
            else sb.Append(p.Literal);
        }
        Flush();
        return result;
    }

    /// <summary>The parts a text box shows for a value: template parts, a lone variable, or the value's plain text.</summary>
    public static IReadOnlyList<TextPart> PartsOf(ParamValue? value) => value switch
    {
        null => Array.Empty<TextPart>(),
        TextValue t => Normalize(t.Parts),
        VarValue v => new[] { TextPart.Of(v.Ref) },
        _ => Normalize(new[] { TextPart.Of(value.ToLiteralText()) }),
    };

    /// <summary>A lone variable stays a whole-value variable; anything else is a text template.</summary>
    public static ParamValue FromParts(IEnumerable<TextPart> parts)
    {
        var list = Normalize(parts);
        if (list.Count == 1 && list[0].Var is { } v) return new VarValue(v);
        return new TextValue(list);
    }

    public static bool HasVariables(ParamValue? value) => value?.VarRefs().Any() == true;
}
