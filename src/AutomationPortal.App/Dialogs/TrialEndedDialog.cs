using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The one summary after the Pro trial ends: what stopped, what stays. Nothing the person made is changed; only making new things
/// goes back to the plan's limits.
/// </summary>
public static class TrialEndedDialog
{
    public static async Task ShowAsync(AppHost host, Window? owner)
    {
        var body = new StackPanel();
        var cap = TierRules.ShortcutCap(host.Plan.Tier) ?? TierRules.FreeShortcuts;
        body.Children.Add(Ui.Text(Strings.Format("Trial_EndedStopped", cap), size: 14));
        var stays = Ui.Text(Strings.Get("Trial_EndedStays"), size: 14);
        stays.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(stays);

        var canBuy = host.Plan.SellsAddOns && host.Plan.Offers.Count > 0;
        var dlg = new FlowDialog(Strings.Get("Trial_EndedTitle"), body, canBuy ? Strings.Get("Plan_GetPro") : null, Strings.Get("Trial_Ok"), width: 500);
        var getPro = await dlg.ShowTopmostAsync(default, owner);
        if (getPro && canBuy) await UpgradeDialog.ShowAsync(host, owner, UpgradeReason.Shortcuts);
    }
}
