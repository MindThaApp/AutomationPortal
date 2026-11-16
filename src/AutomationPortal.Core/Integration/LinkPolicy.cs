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
    public static LinkDecision Decide(AppSettings settings, Guid shortcutId, bool carriesPaths) => settings.LinksMode switch
    {
        LinkMode.Never => LinkDecision.Block,
        LinkMode.PerShortcut when !carriesPaths && IsTrusted(settings, shortcutId) => LinkDecision.Run,
        _ => LinkDecision.Ask,
    };

    /// <summary>Whether the link prompt offers "Don't ask again for this shortcut".</summary>
    public static bool CanRemember(AppSettings settings, bool carriesPaths) => settings.LinksMode == LinkMode.PerShortcut && !carriesPaths;

    public static bool IsTrusted(AppSettings settings, Guid shortcutId) => settings.LinkTrustedShortcutIds?.Contains(shortcutId) == true;

    /// <summary>Turns "Links can run this without asking" on or off for one shortcut.</summary>
    public static void SetTrusted(ISettingsStore settings, Guid shortcutId, bool on) => settings.Update(s =>
    {
        s.LinkTrustedShortcutIds ??= new();
        s.LinkTrustedShortcutIds.RemoveAll(x => x == shortcutId);
        if (on) s.LinkTrustedShortcutIds.Add(shortcutId);
    });
}
