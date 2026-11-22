using System.Windows;
using AutomationPortal.App.Services;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The first-run welcome: opens <see cref="WelcomeWindow"/>, then adds the starter shortcuts that were ticked.
/// Skip or Esc closes it without adding anything; the crash-report choice is saved either way.
/// </summary>
public static class WelcomeDialog
{
    public static async Task ShowAsync(AppHost host, Window? owner)
    {
        var s = host.Settings.Current;
        var window = new WelcomeWindow(host);

        // Remembered as soon as it opens, so a crash or a forced close never brings it back.
        if (!s.WelcomeShown) host.Settings.Update(x => x.WelcomeShown = true);

        var accepted = await window.ShowAsync(owner);
        if (!accepted) return;
        // Starters are new shortcuts too: as many as the plan has room for are added, then the upgrade window explains the rest.
        var room = host.Plan.ShortcutRoom(host.Library.All.Count);
        var skipped = 0;
        foreach (var item in window.TickedStarters)
        {
            if (room <= 0) { skipped++; continue; }
            room--;
            try
            {
                var result = host.Library.Import(item.Shortcut.Clone());   // a name that's taken gets a new one; nothing is replaced
                await host.FinishImportAsync(result, owner, warningsShown: false);
            }
            catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't add it", ex.Message); }
        }
        if (skipped > 0) host.AllowNewShortcut(owner, skipped);
    }
}
