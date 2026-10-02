using Flowmate.Core;

namespace Flowmate.Editor.Logic;

internal static class ParamValueEx
{
    /// <summary>Returns a copy with every variable reference passed through <paramref name="map"/>.</summary>
    public static ParamValue MapVars(this ParamValue value, Func<VarRef, VarRef> map) => value switch
    {
        TextValue t => new TextValue(t.Parts.Select(p => p.Var is { } r ? TextPart.Of(map(r)) : p).ToList()),
        VarValue v => new VarValue(map(v.Ref)),
        ListValue l => new ListValue(l.Items.Select(i => i.MapVars(map)).ToList()),
        DictValue d => new DictValue(d.Items.Select(e => new DictEntry(e.Key.MapVars(map), e.Value.MapVars(map))).ToList()),
        ConditionsValue c => new ConditionsValue(c.Match, c.Items.Select(r =>
            new ConditionRow(r.Left.MapVars(map), r.Op, r.Right?.MapVars(map), r.Right2?.MapVars(map))).ToList()),
        _ => value,
    };

    public static bool IsBlank(ParamValue? v) => v switch
    {
        null => true,
        TextValue t => t.IsBlank,
        ListValue l => l.Items.Count == 0,
        DictValue d => d.Items.Count == 0,
        _ => false,
    };
}
