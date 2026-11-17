namespace AutomationPortal.Editor;

/// <summary>Works out which rows slid to a new place, from where they stood before a change and where they stand after it.</summary>
internal static class RowMotionPlan
{
    /// <summary>Smaller shifts than this (in device-independent pixels) are not worth moving.</summary>
    public const double MinShift = 0.5;

    /// <summary>Rows that are in both lists and moved: how far up (negative) or down (positive) they started from their new place. Rows in only one list are left out.</summary>
    public static Dictionary<T, double> Shifts<T>(IReadOnlyDictionary<T, double> before, IReadOnlyDictionary<T, double> after) where T : notnull
    {
        var result = new Dictionary<T, double>();
        foreach (var (key, was) in before)
            if (after.TryGetValue(key, out var now) && Math.Abs(was - now) > MinShift) result[key] = was - now;
        return result;
    }

    /// <summary>Motion is for small changes by hand. A bulk change (a paste of many steps, a version restore) just lands.</summary>
    public static bool IsSmallChange(int added, int removed) => added + removed is > 0 and <= 3;
}
