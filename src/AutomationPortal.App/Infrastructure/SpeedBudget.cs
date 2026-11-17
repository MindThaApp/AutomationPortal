using System.Diagnostics;

namespace AutomationPortal.App.Infrastructure;

/// <summary>Debug-only log of how long the moments with a speed budget take. Nothing is shown to the user.</summary>
public static class SpeedBudget
{
    public const int WindowShownMs = 100;
    public const int PaletteReadyMs = 80;

    public static string Describe(string what, double elapsedMs, int budgetMs) =>
        $"[speed] {what}: {elapsedMs:0} ms (budget {budgetMs} ms){(elapsedMs > budgetMs ? " OVER" : "")}";

    [Conditional("DEBUG")]
    public static void Log(string what, Stopwatch watch, int budgetMs) =>
        Debug.WriteLine(Describe(what, watch.Elapsed.TotalMilliseconds, budgetMs));
}
