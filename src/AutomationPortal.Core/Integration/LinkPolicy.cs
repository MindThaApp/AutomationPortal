using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>What automationportal:// links may do (Settings › Links).</summary>
public enum LinkMode
{
    /// <summary>Links never run shortcuts.</summary>
    Never,
    /// <summary>Every link asks first.</summary>
    Ask,
    /// <summary>Links ask first, except for shortcuts whose Details say "Links can run this without asking".</summary>
    PerShortcut,
}

public enum LinkDecision { Block, Ask, Run }

/// <summary>Decides whether a link that asks to run a shortcut runs it, asks first, or is refused.</summary>
public static class LinkPolicy
{
    /// <param name="carriesPaths">The link hands over files, or text that looks like a file path: always shown first.</param>
    /// <param name="controlsApps">The shortcut clicks and types in other apps: always shown first, even for a trusted shortcut.</param>
    /// <param name="stepsHash">
    /// <see cref="StepsHash"/> of the shortcut as it is now. Trust given to other steps doesn't apply: a shortcut whose steps changed
    /// since it was trusted asks again.
    /// </param>
    /// <param name="rateLimited">Links ran this shortcut too often lately (see <see cref="LinkRunLimiter"/>): ask.</param>
    public static LinkDecision Decide(AppSettings settings, Guid shortcutId, bool carriesPaths, bool controlsApps = false,
        string? stepsHash = null, bool rateLimited = false) => ModeFor(settings, shortcutId) switch
    {
        LinkMode.Never => LinkDecision.Block,
        LinkMode.PerShortcut when !carriesPaths && !controlsApps && !rateLimited && IsTrustedFor(settings, shortcutId, stepsHash) => LinkDecision.Run,
        _ => LinkDecision.Ask,
    };

    /// <summary>Whether the link prompt offers "Don't ask again for this shortcut".</summary>
    public static bool CanRemember(AppSettings settings, bool carriesPaths, bool controlsApps = false, Guid? shortcutId = null) =>
        (shortcutId is { } id ? ModeFor(settings, id) : settings.LinksMode) == LinkMode.PerShortcut && !carriesPaths && !controlsApps;

    /// <summary>The shortcut's own setting, or the global one for a shortcut that has none (every shortcut from before the setting existed).</summary>
    public static LinkMode ModeFor(AppSettings settings, Guid shortcutId) =>
        settings.ShortcutLinkModes is { } modes && modes.TryGetValue(shortcutId.ToString("D"), out var own) ? own : settings.LinksMode;

    /// <summary>Gives a new shortcut the current global setting as its own. Does nothing if it already has one.</summary>
    public static void ApplyDefaultToNew(ISettingsStore settings, Guid shortcutId) => settings.Update(s =>
    {
        s.ShortcutLinkModes ??= new();
        s.ShortcutLinkModes.TryAdd(shortcutId.ToString("D"), s.LinksMode);
    });

    /// <summary>Sets what links may do for one shortcut.</summary>
    public static void SetMode(ISettingsStore settings, Guid shortcutId, LinkMode mode) => settings.Update(s =>
    {
        s.ShortcutLinkModes ??= new();
        s.ShortcutLinkModes[shortcutId.ToString("D")] = mode;
    });

    /// <summary>The shortcut is on the "Links can run this without asking" list (whatever its steps are now).</summary>
    public static bool IsTrusted(AppSettings settings, Guid shortcutId) => settings.LinkTrustedShortcutIds?.Contains(shortcutId) == true;

    /// <summary>On the list, and trusted with these same steps.</summary>
    public static bool IsTrustedFor(AppSettings settings, Guid shortcutId, string? stepsHash) =>
        IsTrusted(settings, shortcutId) && SameSteps(settings, shortcutId.ToString("D"), stepsHash);

    /// <summary>Turns "Links can run this without asking" on or off for one shortcut, remembering the steps it was trusted with.</summary>
    public static void SetTrusted(ISettingsStore settings, Guid shortcutId, bool on, string? stepsHash = null) => settings.Update(s =>
    {
        s.LinkTrustedShortcutIds ??= new();
        s.LinkTrustedShortcutIds.RemoveAll(x => x == shortcutId);
        s.TrustedStepHashes ??= new();
        s.TrustedStepHashes.Remove(shortcutId.ToString("D"));
        if (!on) return;
        s.LinkTrustedShortcutIds.Add(shortcutId);
        if (stepsHash is not null) s.TrustedStepHashes[shortcutId.ToString("D")] = stepsHash;
    });

    /// <summary>The steps hash kept with a trust entry equals <paramref name="stepsHash"/> (both missing counts as equal).</summary>
    internal static bool SameSteps(AppSettings settings, string key, string? stepsHash)
    {
        string? kept = null;
        settings.TrustedStepHashes?.TryGetValue(key, out kept);
        return string.Equals(kept, stepsHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fingerprint of what a shortcut does: every step's action, settings, error handling and flow, in order. Renaming the
    /// shortcut or collapsing a step in the editor doesn't change it; adding, removing, reordering or editing a step does.
    /// </summary>
    public static string StepsHash(Shortcut shortcut)
    {
        var steps = shortcut.Actions.Select(a => new
        {
            a.Uid,
            a.ActionId,
            a.Disabled,
            a.OutputName,
            a.OnError,
            a.Flow,
            Parameters = new SortedDictionary<string, ParamValue>(a.Parameters, StringComparer.Ordinal),
        }).ToList();
        var json = JsonSerializer.Serialize(new { shortcut.WhenNoInput, shortcut.AcceptsInput, steps }, AutomationPortalJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}

/// <summary>
/// Limits how often links run each shortcut: at most one run per <see cref="MinGap"/> and <see cref="PerHour"/> runs an hour.
/// Past that, the link asks first and says why.
/// </summary>
public sealed class LinkRunLimiter
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(10);
    public const int PerHour = 20;

    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<Guid, List<DateTimeOffset>> _runs = new();
    private readonly object _gate = new();

    public LinkRunLimiter(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Null when a link may run the shortcut now; otherwise the reason it has to ask, in plain words.</summary>
    public string? Check(Guid shortcutId)
    {
        lock (_gate)
        {
            var now = _now();
            if (!_runs.TryGetValue(shortcutId, out var times)) return null;
            times.RemoveAll(t => now - t >= TimeSpan.FromHours(1));
            if (times.Count > 0 && now - times[^1] < MinGap)
                return $"A link already ran this shortcut less than {(int)MinGap.TotalSeconds} seconds ago.";
            if (times.Count >= PerHour)
                return $"Links have run this shortcut {times.Count} times in the last hour.";
            return null;
        }
    }

    /// <summary>Notes that a link ran the shortcut now.</summary>
    public void Ran(Guid shortcutId)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(shortcutId, out var times)) _runs[shortcutId] = times = new();
            times.Add(_now());
        }
    }
}
