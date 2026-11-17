using System.Diagnostics;

namespace AutomationPortal.App.Infrastructure;

/// <summary>Debug-only log of how long the moments with a speed budget take. Nothing is shown to the user.</summary>
public static class SpeedBudget
{
    public const int WindowShownMs = 100;
    public const int PaletteReadyMs = 80;
    public const int ColdStartToTrayMs = 1500;
    public const int ColdStartToLibraryMs = 2500;

    private static bool _coldStartLogged;

    /// <summary>Logs, once, how long it took from the process starting to the first main window being shown.</summary>
    [Conditional("DEBUG")]
    public static void LogColdStart()
    {
        if (_coldStartLogged) return;
        _coldStartLogged = true;
        try
        {
            var elapsed = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            Debug.WriteLine(Describe("cold start to the library drawn", elapsed, ColdStartToLibraryMs));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    public static string Describe(string what, double elapsedMs, int budgetMs) =>
        $"[speed] {what}: {elapsedMs:0} ms (budget {budgetMs} ms){(elapsedMs > budgetMs ? " OVER" : "")}";

    [Conditional("DEBUG")]
    public static void Log(string what, Stopwatch watch, int budgetMs) =>
        Debug.WriteLine(Describe(what, watch.Elapsed.TotalMilliseconds, budgetMs));
}
