using System.Windows;
using System.Windows.Threading;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Pages;
using AutomationPortal.App.Services;
using AutomationPortal.Core;

namespace AutomationPortal.App;

// Free, Plus and Pro. Every way of making a new shortcut or a new automation asks here first.
public sealed partial class AppHost
{
    private EntitlementService? _plan;

    /// <summary>The person's plan. Builds without package identity are Pro and sell nothing.</summary>
    public EntitlementService Plan => _plan ??= SimulatedPlan.TryCreate(ArgValue(_args, SimulatedPlan.Switch), _customDataRoot, Paths.LocalRoot)
        ?? (PackageIdentity.IsPackaged
            ? EntitlementService.ForStore(new StoreLicensing(), new UiSettingsEntitlementCache(UiSettings), trial: NewTrial())
            : EntitlementService.Unpackaged());

    /// <summary>
    /// The trial record lives in trial.json in the local data folder plus a copy under HKCU. A custom data root (tests, demos) gets the
    /// file inside that root only and never touches the registry, so a harness can't read or change the owner's real trial.
    /// </summary>
    private ProTrial NewTrial() => TrialFactory.Create(Paths.LocalRoot, _customDataRoot);

    private DispatcherTimer? _trialTimer;
    private (TrialPhase Phase, int Days, bool Known) _trialSeen;

    /// <summary>Reads the Store once at start, off the UI thread. A failure keeps the cached plan.</summary>
    private void StartPlan()
    {
        if (!Plan.SellsAddOns) return;
        // The timer and first check start at once, so the last-seen time is kept even if the Store never answers. Nothing is shown
        // until ownership is known (TrialTick waits for it), and the read below checks again as soon as it is.
        _trialTimer = new DispatcherTimer(TimeSpan.FromMinutes(30), DispatcherPriority.Background, (_, _) => TrialTick(), Dispatcher);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, TrialTick);
        _ = Task.Run(async () =>
        {
            try { await Plan.RefreshAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Write("Plan refresh: " + ex.Message); }
            finally { Dispatcher.BeginInvoke(TrialTick); }
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

    /// <summary>
    /// Keeps the trial honest while the app runs: remembers the time seen, tells the window when the days left change, shows the one
    /// "ends tomorrow" notification and the one summary after the end. Never touches a shortcut or an automation. UI thread.
    /// </summary>
    public void TrialTick()
    {
        try
        {
            if (Plan.Trial is not { } trial) return;
            var action = TrialMoments.Next(trial, Plan.OwnershipKnown, Plan.Owned.Tier == Tier.Pro);
            var status = trial.Status;
            if ((status.Phase, status.DaysLeft, Plan.OwnershipKnown) != _trialSeen)
            {
                _trialSeen = (status.Phase, status.DaysLeft, Plan.OwnershipKnown);
                Plan.NotifyTrialChanged();
            }
            if (action == TrialAction.Toast)
            {
                Notifications.Show(Strings.Get("Trial_EndsSoonTitle"), Strings.Get("Trial_EndsSoonBody"), null, () => ShowMainWindow(new NavTarget(AppPage.Settings)));
            }
            else if (action == TrialAction.Summary)
            {
                _ = TrialEndedDialog.ShowAsync(this, MainWindowOwner()).ContinueWith(
                    t => AppLog.Write("Trial summary: " + t.Exception?.GetBaseException().Message), TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex) { AppLog.Write("Trial: " + ex.Message); }
    }
}

