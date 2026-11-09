using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

/// <summary>Editing rules for the "If" conditions builder.</summary>
public static class ConditionModel
{
    public static string GroupFor(ContentKind? kind) => kind switch
    {
        ContentKind.Number => "Number",
        ContentKind.Date => "Date",
        ContentKind.Boolean => "Boolean",
        _ => "Text",
    };

    /// <summary>Every operator, with the group that suits <paramref name="leftKind"/> first.</summary>
    public static IReadOnlyList<OperatorInfo> OperatorsFor(ContentKind? leftKind)
    {
        var first = GroupFor(leftKind);
        return ConditionOperators.All.OrderBy(o => o.Group == first ? 0 : 1).ToList();
    }

    public static string DefaultOperator(ContentKind? kind) => GroupFor(kind) switch
    {
        "Number" => ConditionOperators.Equals_,
        "Date" => ConditionOperators.IsToday,
        "Boolean" => ConditionOperators.IsTrue,
        _ => ConditionOperators.Is,
    };

    /// <summary>A fresh row, with the previous action's output (when there is one) as the left side.</summary>
    public static ConditionRow NewRow(VariableOption? left = null)
    {
        var op = DefaultOperator(left?.Kind);
        return new ConditionRow(left is null ? ParamValue.Text() : ParamValue.Var(left.Ref), op, NeedsRight(op) ? ParamValue.Text() : null, null);
    }

    public static int Operands(string op) => ConditionOperators.Find(op)?.Operands ?? 1;
    public static bool NeedsRight(string op) => Operands(op) >= 1;

    /// <summary>Changes the operator and drops or adds right-hand values so the row matches how many it needs.</summary>
    public static ConditionRow WithOperator(ConditionRow row, string op)
    {
        var n = Operands(op);
        var right = n >= 1 ? row.Right ?? ParamValue.Text() : null;
        var right2 = n >= 2 ? row.Right2 ?? ParamValue.Text() : null;
        return row with { Op = op, Right = right, Right2 = right2 };
    }

    public static ConditionsValue AddRow(ConditionsValue c, ConditionRow row) =>
        new(c.Match, c.Items.Append(row).ToList());

    public static ConditionsValue RemoveRow(ConditionsValue c, int index) =>
        new(c.Match, c.Items.Where((_, i) => i != index).ToList());

    public static ConditionsValue ReplaceRow(ConditionsValue c, int index, ConditionRow row) =>
        new(c.Match, c.Items.Select((r, i) => i == index ? row : r).ToList());

    public static ConditionsValue WithMatch(ConditionsValue c, ConditionMatch match) => new(match, c.Items);

    public static string Label(string op) => ConditionOperators.Find(op)?.Label ?? op;
}
