namespace AutomationPortal.App.Logic;

/// <summary>Limits the template set-up sheet keeps on its blanks, so a template can't be set up to do more than it says.</summary>
public static class TemplateRules
{
    /// <summary>
    /// The smallest number a blank accepts. "Only files older than (days)" needs at least 1: with 0 the filter would keep every
    /// file, including ones still downloading or just saved.
    /// </summary>
    public static double MinimumFor(string? actionId, string parameterKey, double? declaredMin) =>
        string.Equals(actionId, "file.filter", StringComparison.OrdinalIgnoreCase) && parameterKey == "olderThanDays"
            ? Math.Max(1, declaredMin ?? 0)
            : declaredMin ?? 0;

    /// <summary>The value the blank keeps for what was typed, or null when it isn't a number it can take.</summary>
    public static double? Accept(double typed, double minimum) => double.IsFinite(typed) && typed >= minimum ? typed : null;
}
