using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

/// <summary>
/// Settings › General: "Automation Portal Plus and Pro". The current plan, buy buttons with Store prices, and Restore purchases.
/// Not shown at all in builds without package identity (they are Pro and sell nothing).
/// </summary>
public sealed class PlanSettingsPanel : Border
{
    private readonly AppHost _host;
    private readonly TextBlock _youHave;
    private readonly TextBlock _overCap;
    private readonly StackPanel _offers = new();
    private readonly Wpf.Ui.Controls.Button _restore;
    private readonly TextBlock _status;
    private bool _busy;

    public PlanSettingsPanel(AppHost host)
    {
        _host = host;
        Padding = new Thickness(16, 12, 16, 12);
        Margin = new Thickness(0, 0, 0, 6);
        CornerRadius = AutomationPortal.Editor.Radius.Control;
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "DsCardBrush");
        SetResourceReference(BorderBrushProperty, "DsStrokeBrush");
        AutomationProperties.SetName(this, Strings.Get("Plan_CardTitle"));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new SymbolIcon(SymbolRegular.Premium24) { FontSize = 20, Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(icon);

        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        stack.Children.Add(Ui.Text(Strings.Get("Plan_CardTitle"), size: 14));
        _youHave = Ui.Text("", secondary: true, size: 12);
        stack.Children.Add(_youHave);
        _overCap = Ui.Text("", secondary: true, size: 12);
        stack.Children.Add(_overCap);
        _offers.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_offers);

        _restore = new Wpf.Ui.Controls.Button
        {
            Content = Strings.Get("Plan_RestorePurchases"), Appearance = ControlAppearance.Transparent, Icon = new SymbolIcon(SymbolRegular.ArrowClockwise24),
            ToolTip = Strings.Get("Plan_RestoreTip"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 2, 0, 0),
        };
        AutomationProperties.SetName(_restore, Strings.Get("Plan_RestorePurchases"));
        _restore.Click += async (_, _) => await RestoreAsync();
        stack.Children.Add(_restore);

        _status = Ui.Text("", secondary: true, size: 12);
        _status.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        stack.Children.Add(_status);

        Child = grid;

        EventHandler onPlan = (_, _) => Dispatcher.BeginInvoke(Show);
        EventHandler<AutomationPortal.Core.LibraryChangedEventArgs> onLibrary = (_, _) => Dispatcher.BeginInvoke(Show);
        Loaded += (_, _) =>
        {
            _host.Plan.Changed += onPlan;
            _host.Library.Changed += onLibrary;
            Show();
            _ = _host.Plan.LoadPricesAsync();
        };
        Unloaded += (_, _) => { _host.Plan.Changed -= onPlan; _host.Library.Changed -= onLibrary; };
        Show();
    }

    private void Show()
    {
        var plan = _host.Plan;
        Visibility = plan.SellsAddOns ? Visibility.Visible : Visibility.Collapsed;
        if (!plan.SellsAddOns) return;

        _youHave.Text = PlanText.YouHave(plan.Tier);
        var count = _host.Library.All.Count;
        var over = plan.ShortcutCap is { } cap && count > cap;
        _overCap.Text = over ? Strings.Format("Plan_OverCap", count) : "";
        _overCap.Visibility = over ? Visibility.Visible : Visibility.Collapsed;

        _offers.Children.Clear();
        foreach (var offer in plan.Offers)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(Ui.Text(PlanText.Blurb(offer), secondary: true, size: 12));
            var label = PlanText.BuyLabel(offer, plan.PriceOf(offer));
            var buy = new Wpf.Ui.Controls.Button
            {
                Content = label, Appearance = offer == StoreOffer.Plus && plan.Offers.Count > 1 ? ControlAppearance.Secondary : ControlAppearance.Primary,
                Margin = new Thickness(0, 4, 0, 0), MinWidth = 160, IsEnabled = !_busy,
            };
            AutomationProperties.SetName(buy, label);
            buy.Click += async (_, _) => await BuyAsync(offer);
            row.Children.Add(buy);
            _offers.Children.Add(row);
        }
        _offers.Visibility = _offers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _restore.IsEnabled = !_busy;
    }

    private void Say(string? text)
    {
        _status.Text = text ?? "";
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task BuyAsync(StoreOffer offer)
    {
        if (_busy) return;
        _busy = true;
        Show();
        Say(Strings.Get("Plan_OpeningStore"));
        try
        {
            var owner = Window.GetWindow(this);
            var handle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            var reply = await _host.Plan.BuyAsync(offer, handle);
            Say(PlanText.PurchaseLine(reply));
        }
        finally { _busy = false; Show(); }
    }

    private async Task RestoreAsync()
    {
        if (_busy) return;
        _busy = true;
        Show();
        Say(Strings.Get("Plan_Checking"));
        try
        {
            var outcome = await _host.Plan.RefreshAsync();
            Say(PlanText.RestoreLine(outcome, _host.Plan.Tier));
        }
        finally { _busy = false; Show(); }
    }
}
