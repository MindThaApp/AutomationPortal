using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

public enum UpgradeReason { Shortcuts, Automations }

/// <summary>
/// Shown when a new shortcut or automation needs a bigger plan. Says what's safe (everything the person has keeps working),
/// offers what the plan rules allow with Store prices, and buys through the Store. Never changes a shortcut or an automation.
/// </summary>
public static class UpgradeDialog
{
    private static FlowDialog? _open;

    public static async Task ShowAsync(AppHost host, Window? owner, UpgradeReason reason)
    {
        var plan = host.Plan;
        if (!plan.SellsAddOns || plan.Offers.Count == 0) return;
        if (_open is { IsVisible: true } already) { already.Activate(); return; }

        var body = new StackPanel();
        body.Children.Add(Ui.Text(Message(host, reason), size: 14));

        var offers = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(offers);
        var status = Ui.Text("", secondary: true, size: 13);
        status.Margin = new Thickness(0, 10, 0, 0);
        status.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        body.Children.Add(status);

        var title = reason == UpgradeReason.Automations ? Strings.Get("Plan_AutomationsTitle") : Strings.Get("Plan_ShortcutsTitle");
        var dlg = new FlowDialog(title, body, primaryText: null, secondaryText: Strings.Get("Plan_NotNow"), width: 500);
        _open = dlg;

        void Say(string? text)
        {
            status.Text = text ?? "";
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void Build()
        {
            offers.Children.Clear();
            if (plan.CanStartTrial)
            {
                var trialRow = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
                var start = new Wpf.Ui.Controls.Button
                {
                    Content = Strings.Get("Trial_StartButton"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 0, 4), MinWidth = 160,
                };
                AutomationProperties.SetName(start, Strings.Get("Trial_StartButton").Replace("_", ""));
                start.Click += (_, _) =>
                {
                    Say(Strings.Get(plan.StartTrial() ? "Trial_Started" : "Trial_StartFailed"));
                    dlg.SetSecondaryText(Strings.Get("Plan_Close"));
                    Build();
                };
                trialRow.Children.Add(start);
                trialRow.Children.Add(Ui.Text(Strings.Get("Trial_StartNote"), secondary: true, size: 13));
                offers.Children.Add(trialRow);
            }
            var list = reason == UpgradeReason.Automations
                ? plan.Offers.Where(o => o != StoreOffer.Plus || (!plan.Owned.Plus && plan.Owned.ProUpgrade)).ToList()   // only offers that bring automations
                : plan.Offers.ToList();
            foreach (var offer in list)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(Ui.Text(PlanText.Blurb(offer), secondary: true, size: 13));
                var button = new Wpf.Ui.Controls.Button
                {
                    Content = PlanText.BuyLabel(offer, plan.PriceOf(offer)),
                    Appearance = offer == StoreOffer.Plus && list.Count > 1 ? ControlAppearance.Secondary : ControlAppearance.Primary,
                    Margin = new Thickness(0, 4, 0, 0), MinWidth = 160,
                };
                AutomationProperties.SetName(button, PlanText.BuyLabel(offer, plan.PriceOf(offer)));
                button.Click += async (_, _) =>
                {
                    SetEnabled(offers, false);
                    Say(Strings.Get("Plan_OpeningStore"));
                    var reply = await plan.BuyAsync(offer, new WindowInteropHelper(dlg).Handle);
                    Say(PlanText.PurchaseLine(reply));
                    if (reply.Status is PurchaseStatus.Succeeded or PurchaseStatus.AlreadyPurchased)
                    {
                        dlg.SetSecondaryText(Strings.Get("Plan_Close"));
                        Build();
                    }
                    else SetEnabled(offers, true);
                };
                row.Children.Add(button);
                offers.Children.Add(row);
            }
        }

        Build();
        EventHandler onChanged = (_, _) => dlg.Dispatcher.BeginInvoke(() => { if (dlg.IsVisible && offers.IsEnabled) Build(); });
        plan.Changed += onChanged;
        dlg.Closed += (_, _) => { plan.Changed -= onChanged; if (ReferenceEquals(_open, dlg)) _open = null; };
        _ = plan.LoadPricesAsync();
        await dlg.ShowTopmostAsync(default, owner);
    }

    private static void SetEnabled(Panel panel, bool on) => panel.IsEnabled = on;

    private static string Message(AppHost host, UpgradeReason reason)
    {
        if (reason == UpgradeReason.Automations) return Strings.Get("Plan_AutomationsBody");
        var count = host.Library.All.Count;
        return host.Plan.Tier == Tier.Plus
            ? Strings.Format("Plan_ShortcutsPlus", TierRules.PlusShortcuts, count)
            : Strings.Format("Plan_ShortcutsFree", TierRules.FreeShortcuts, count, TierRules.PlusShortcuts);
    }
}

/// <summary>Words for plans and offers, shared by Settings and the upgrade window.</summary>
public static class PlanText
{
    public static string TierName(Tier tier) => tier switch
    {
        Tier.Pro => Strings.Get("Plan_TierPro"),
        Tier.Plus => Strings.Get("Plan_TierPlus"),
        _ => Strings.Get("Plan_TierFree"),
    };

    /// <summary>"Pro trial: 5 days left".</summary>
    public static string TrialLine(int daysLeft) => daysLeft == 1 ? Strings.Get("Trial_ChipOne") : Strings.Format("Trial_Chip", daysLeft);

    public static string YouHave(Tier tier) => tier switch
    {
        Tier.Pro => Strings.Get("Plan_YouHavePro"),
        Tier.Plus => Strings.Format("Plan_YouHavePlus", TierRules.PlusShortcuts),
        _ => Strings.Format("Plan_YouHaveFree", TierRules.FreeShortcuts),
    };

    public static string Blurb(StoreOffer offer) => offer switch
    {
        StoreOffer.Plus => Strings.Format("Plan_PlusBlurb", TierRules.PlusShortcuts),
        StoreOffer.Pro => Strings.Get("Plan_ProBlurb"),
        _ => Strings.Get("Plan_UpgradeBlurb"),
    };

    /// <summary>"Get Plus for $6.99"; without a known price just "Get Plus".</summary>
    public static string BuyLabel(StoreOffer offer, string? price)
    {
        var verb = offer switch
        {
            StoreOffer.Plus => Strings.Get("Plan_GetPlus"),
            StoreOffer.Pro => Strings.Get("Plan_GetPro"),
            _ => Strings.Get("Plan_UpgradeToPro"),
        };
        return string.IsNullOrWhiteSpace(price) ? verb : Strings.Format("Plan_WithPrice", verb, price);
    }

    /// <summary>One line after a purchase: thanks, nothing (the person closed the Store window), or one plain sentence.</summary>
    public static string? PurchaseLine(PurchaseReply reply) => reply.Status switch
    {
        PurchaseStatus.Succeeded or PurchaseStatus.AlreadyPurchased => Strings.Format("Plan_Thanks", TierName(reply.Tier)),
        PurchaseStatus.NotPurchased => null,
        _ => Strings.Get("Plan_PurchaseProblem"),
    };

    public static string RestoreLine(RefreshOutcome outcome, Tier tier) => outcome switch
    {
        RefreshOutcome.StoreUnavailable => Strings.Get("Plan_RestoreProblem"),
        RefreshOutcome.Changed => Strings.Format("Plan_RestoreChanged", TierName(tier)),
        _ => Strings.Format("Plan_RestoreUpToDate", TierName(tier)),
    };
}
