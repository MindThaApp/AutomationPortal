using System.Globalization;
using System.Text.RegularExpressions;

namespace AutomationPortal.Core;

/// <summary>One If/Filter row with all variables resolved.</summary>
public sealed record ResolvedCondition(ContentValue Left, string Op, ContentValue Right, ContentValue Right2);

/// <summary>Resolved conditions ("match all/any of these"). Get one with <c>ctx.GetConditionsAsync(key)</c>.</summary>
public sealed class ConditionSet
{
    public ConditionSet(ConditionMatch match, IReadOnlyList<ResolvedCondition> rows) { Match = match; Rows = rows; }

    public ConditionMatch Match { get; }
    public IReadOnlyList<ResolvedCondition> Rows { get; }

    /// <summary>True when all (or any) rows hold. No rows: true for All, false for Any.</summary>
    public bool Evaluate() => Evaluate(null);

    /// <summary>Like <see cref="Evaluate()"/> but lets the caller replace each row's left side (e.g. the current list item or one of its properties for Filter List).</summary>
    public bool Evaluate(Func<ResolvedCondition, ContentValue>? leftSelector)
    {
        if (Match == ConditionMatch.All)
            return Rows.All(r => ConditionEvaluator.Evaluate(leftSelector?.Invoke(r) ?? r.Left, r.Op, r.Right, r.Right2));
        return Rows.Any(r => ConditionEvaluator.Evaluate(leftSelector?.Invoke(r) ?? r.Left, r.Op, r.Right, r.Right2));
    }
}

/// <summary>Implements every operator in <see cref="ConditionOperators"/>.</summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(ContentValue left, string op, ContentValue right, ContentValue right2)
    {
        var lt = left.AsText();
        var rt = right.AsText();
        switch (op)
        {
            case ConditionOperators.Is: return Same(left, right);
            case ConditionOperators.IsNot: return !Same(left, right);
            case ConditionOperators.Contains: return lt.Contains(rt, StringComparison.OrdinalIgnoreCase);
            case ConditionOperators.DoesNotContain: return !lt.Contains(rt, StringComparison.OrdinalIgnoreCase);
            case ConditionOperators.BeginsWith: return lt.StartsWith(rt, StringComparison.OrdinalIgnoreCase);
            case ConditionOperators.EndsWith: return lt.EndsWith(rt, StringComparison.OrdinalIgnoreCase);
            case ConditionOperators.Matches:
                try { return Regex.IsMatch(lt, rt, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
                catch (ArgumentException) { throw new ActionException($"The pattern '{rt}' isn't valid. Check it and try again."); }
                catch (RegexMatchTimeoutException) { throw new ActionException("The pattern took too long to check. Try a simpler one."); }
            case ConditionOperators.HasValue: return !left.IsEmpty && !string.IsNullOrWhiteSpace(lt);
            case ConditionOperators.HasNoValue: return left.IsEmpty || string.IsNullOrWhiteSpace(lt);

            case ConditionOperators.Equals_: return Num(left, right, (a, b) => a == b, false);
            case ConditionOperators.NotEquals: return Num(left, right, (a, b) => a != b, true);
            case ConditionOperators.GreaterThan: return Num(left, right, (a, b) => a > b, false);
            case ConditionOperators.GreaterOrEqual: return Num(left, right, (a, b) => a >= b, false);
            case ConditionOperators.LessThan: return Num(left, right, (a, b) => a < b, false);
            case ConditionOperators.LessOrEqual: return Num(left, right, (a, b) => a <= b, false);
            case ConditionOperators.Between:
                {
                    if (left.AsNumber() is not { } x || right.AsNumber() is not { } lo || right2.AsNumber() is not { } hi) return false;
                    if (lo > hi) (lo, hi) = (hi, lo);
                    return x >= lo && x <= hi;
                }

            case ConditionOperators.IsToday:
                return left.AsDate() is { } d0 && d0.ToLocalTime().Date == DateTime.Today;
            case ConditionOperators.IsBefore:
                return left.AsDate() is { } d1 && right.AsDate() is { } d2 && d1 < d2;
            case ConditionOperators.IsAfter:
                return left.AsDate() is { } d3 && right.AsDate() is { } d4 && d3 > d4;
            case ConditionOperators.DateBetween:
                {
                    if (left.AsDate() is not { } x || right.AsDate() is not { } lo || right2.AsDate() is not { } hi) return false;
                    if (lo > hi) (lo, hi) = (hi, lo);
                    return x >= lo && x <= hi;
                }

            case ConditionOperators.IsTrue: return left.AsBool();
            case ConditionOperators.IsFalse: return !left.AsBool();
            default: throw new ActionException($"Unknown comparison '{op}'.");
        }
    }

    private static bool Same(ContentValue a, ContentValue b)
    {
        if (string.Equals(a.AsText(), b.AsText(), StringComparison.OrdinalIgnoreCase)) return true;
        return a.Count == 1 && b.Count == 1 && a[0] is NumberItem && b[0] is NumberItem or TextItem
               && a.AsNumber() is { } x && b.AsNumber() is { } y && x == y;
    }

    private static bool Num(ContentValue l, ContentValue r, Func<double, double, bool> cmp, bool whenMissing)
    {
        if (l.AsNumber() is not { } a || r.AsNumber() is not { } b) return whenMissing;
        return cmp(a, b);
    }
}
