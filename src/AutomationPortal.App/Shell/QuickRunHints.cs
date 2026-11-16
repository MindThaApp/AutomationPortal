namespace AutomationPortal.App.Shell;

/// <summary>Placeholder hints for the Quick Run box; one is picked each time the box opens.</summary>
internal static class QuickRunHints
{
    public static readonly string[] All =
    [
        Strings.Get("QuickRunHints_TypeAShortcutName"),
        "Try 12*7, or 5 km in miles",
        "Try: a shortcut's short name, if you gave it one",
        Strings.Get("QuickRunHints_TypeAFewLetters"),
    ];

    private static int _next;

    public static string At(int index) => All[((index % All.Length) + All.Length) % All.Length];

    public static string Next() => At(Interlocked.Increment(ref _next) - 1);
}
