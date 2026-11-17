using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.Cli;

/// <summary>
/// The plan's shortcut limit for <c>ap import</c> when Automation Portal is closed and ap.exe writes to the library itself.
/// (With the app open the import goes through the app, which does its own check.) The plan comes from the add-ons the app last saw,
/// kept in app-ui.json; a build without package identity is Pro, like the app. Nothing that exists is ever changed.
/// </summary>
internal static class ImportGate
{
    private sealed class CacheShape { public List<string>? OwnedAddOns { get; set; } }

    /// <summary>The add-ons the app last saw as owned, or null when there is no cache yet or it can't be read.</summary>
    internal static IReadOnlyList<string>? ReadOwned(AutomationPortalPaths paths)
    {
        try
        {
            var file = Path.Combine(paths.RoamingRoot, "app-ui.json");
            return File.Exists(file) ? AutomationPortalJson.Deserialize<CacheShape>(File.ReadAllText(file))?.OwnedAddOns : null;
        }
        catch (Exception) { return null; }
    }

    internal static Tier TierFor(bool packaged, IEnumerable<string>? owned) => packaged ? Ownership.FromIds(owned).Tier : Tier.Pro;

    /// <summary>
    /// Imports each text with <paramref name="importOne"/> (which returns an exit code, 0 for added) until the plan is full; the rest are
    /// left alone. Prints one line when some were left out. Returns 0 when at least one was imported, otherwise the first failure's code
    /// (or <paramref name="problemExit"/> when they were all held back).
    /// </summary>
    internal static async Task<int> ImportUpToAsync(IReadOnlyList<string> texts, Tier tier, int existing, Func<string, Task<int>> importOne,
        TextWriter output, int problemExit)
    {
        var room = TierRules.RoomFor(tier, existing);
        int imported = 0, held = 0, firstFailure = 0;
        foreach (var text in texts)
        {
            if (imported >= room) { held++; continue; }
            var code = await importOne(text).ConfigureAwait(false);
            if (code == 0) imported++;
            else if (firstFailure == 0) firstFailure = code;
        }
        if (held > 0) output.WriteLine($"Imported {imported} of {texts.Count}. Open Automation Portal to add more.");
        if (imported > 0) return 0;
        return firstFailure != 0 ? firstFailure : problemExit;
    }
}
