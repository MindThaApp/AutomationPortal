namespace AutomationPortal.Core;

public sealed record OperatorInfo(string Id, string Label, string Group, int Operands);

/// <summary>Operator ids used in <see cref="ConditionRow.Op"/> (text, number, date and boolean groups).</summary>
public static class ConditionOperators
{
    // Text
    public const string Is = "is";
    public const string IsNot = "isNot";
    public const string Contains = "contains";
    public const string DoesNotContain = "doesNotContain";
    public const string BeginsWith = "beginsWith";
    public const string EndsWith = "endsWith";
    public const string Matches = "matches";
    public const string HasValue = "hasValue";
    public const string HasNoValue = "noValue";
    // Number
    public const string Equals_ = "eq";
    public const string NotEquals = "ne";
    public const string GreaterThan = "gt";
    public const string GreaterOrEqual = "ge";
    public const string LessThan = "lt";
    public const string LessOrEqual = "le";
    public const string Between = "between";
    // Date
    public const string IsToday = "isToday";
    public const string IsBefore = "isBefore";
    public const string IsAfter = "isAfter";
    public const string DateBetween = "dateBetween";
    // Boolean
    public const string IsTrue = "isTrue";
    public const string IsFalse = "isFalse";

    /// <summary>Every operator with its plain-English label, group and number of right-hand operands (0, 1 or 2). For the editor.</summary>
    public static IReadOnlyList<OperatorInfo> All { get; } = new OperatorInfo[]
    {
        new(Is, Strings.Get("Operator_Is"), "Text", 1), new(IsNot, Strings.Get("Operator_IsNot"), "Text", 1), new(Contains, Strings.Get("Operator_Contains"), "Text", 1),
        new(DoesNotContain, Strings.Get("Operator_DoesNotContain"), "Text", 1), new(BeginsWith, Strings.Get("Operator_BeginsWith"), "Text", 1),
        new(EndsWith, Strings.Get("Operator_EndsWith"), "Text", 1), new(Matches, Strings.Get("Operator_Matches"), "Text", 1),
        new(HasValue, Strings.Get("Operator_HasValue"), "Text", 0), new(HasNoValue, Strings.Get("Operator_HasNoValue"), "Text", 0),
        new(Equals_, "=", "Number", 1), new(NotEquals, "≠", "Number", 1), new(GreaterThan, ">", "Number", 1),
        new(GreaterOrEqual, "≥", "Number", 1), new(LessThan, "<", "Number", 1), new(LessOrEqual, "≤", "Number", 1),
        new(Between, "is between", "Number", 2),
        new(IsToday, "is today", "Date", 0), new(IsBefore, "is before", "Date", 1), new(IsAfter, "is after", "Date", 1),
        new(DateBetween, "is between (dates)", "Date", 2),
        new(IsTrue, "is true", "Boolean", 0), new(IsFalse, "is false", "Boolean", 0),
    };

    public static OperatorInfo? Find(string id) => All.FirstOrDefault(o => o.Id == id);
}
