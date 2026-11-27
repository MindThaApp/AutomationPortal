namespace AutomationPortal.App.Shell;

/// <summary>Placeholder hints for the Quick Run box; one is picked each time the box opens.</summary>
internal static class QuickRunHints
{
    public static readonly string[] All =
    [
        Strings.Get("QuickRunHints_TypeAShortcutName"),
        Strings.Get("QuickRunHints_Try127Or"),
        Strings.Get("QuickRunHints_TryAShortcutS"),
        Strings.Get("QuickRunHints_TypeAFewLetters"),
    ];

    private static int _next;

    public static string At(int index) => All[((index % All.Length) + All.Length) % All.Length];

    public static string Next() => At(Interlocked.Increment(ref _next) - 1);
}
