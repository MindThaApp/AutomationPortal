using System.Windows;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Services;
using AutomationPortal.Core;

namespace AutomationPortal.App;

// Free, Plus and Pro. Every way of making a new shortcut or a new automation asks here first.
public sealed partial class AppHost
{
    private EntitlementService? _plan;

    /// <summary>The person's plan. Builds without package identity are Pro and sell nothing.</summary>
    public EntitlementService Plan => _plan ??= PackageIdentity.IsPackaged
        ? EntitlementService.ForStore(new StoreLicensing(), new UiSettingsEntitlementCache(UiSettings))
        : EntitlementService.Unpackaged();

    /// <summary>Reads the Store once at start, off the UI thread. A failure keeps the cached plan.</summary>
    private void StartPlan()
    {
        if (!Plan.SellsAddOns) return;
        _ = Task.Run(async () =>
        {
            try { await Plan.RefreshAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Write("Plan refresh: " + ex.Message); }
        });
    }

    /// <summary>
    /// The one check before making <paramref name="count"/> new shortcuts (new, gallery, template, import, duplicate, record, a copy of
    /// a version). True when they fit the plan. Otherwise shows the upgrade window and returns false: nothing is made, and nothing
    /// the person already has is touched. Call on the UI thread.
    /// </summary>
    public bool AllowNewShortcut(Window? owner, int count = 1)
    {
        var existing = Library.All.Count;
        if (Plan.CanAddShortcuts(existing, count)) return true;
        AppLog.Write($"Plan: a new shortcut waits for an upgrade ({existing} on {Plan.Tier}).");
        _ = UpgradeDialog.ShowAsync(this, owner ?? MainWindowOwner(), UpgradeReason.Shortcuts);
        return false;
    }

    /// <summary>
    /// The one check before making a new automation. Automations that already exist keep running and can still be edited, turned on
    /// or off on every plan. Returns false (after showing the Pro offer) when a new one needs Pro. Call on the UI thread.
    /// </summary>
    public bool AllowNewAutomation(Window? owner)
    {
        if (Plan.CanAddAutomation) return true;
        AppLog.Write($"Plan: a new automation waits for Pro ({Plan.Tier}).");
        _ = UpgradeDialog.ShowAsync(this, owner ?? MainWindowOwner(), UpgradeReason.Automations);
        return false;
    }
}
