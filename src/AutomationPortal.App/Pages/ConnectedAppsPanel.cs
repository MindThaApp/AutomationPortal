using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

/// <summary>Settings › Connected apps: whether Files Portal, Power Plan Manager and Desk Pal are installed, with a button for the one next step. Checked on demand only.</summary>
public sealed class ConnectedAppsPanel : StackPanel
{
    // Microsoft Store product ids. "Get it" points to the full Power Plan Manager (its free trial covers every action).
    private const string FilesPortalStoreId = "9N0SH8H0TH2D";
    private const string PowerPlanManagerStoreId = "9NC994G4LCQG";
    private const string FilesPortalAppId = "34458MindtheApp.FilesPortal_24e29cj9741rt!App";

    private enum Probe { None, PowerPlanManager, DeskPal }

    private sealed record Row(string Name, string Blurb, string[] Schemes, string? StoreId, bool Released, bool HasOpen, Probe Probe);

    private static readonly Row[] Rows =
    {
        new("Files Portal", "Browse and manage your files.", new[] { "filesportal" }, FilesPortalStoreId, true, true, Probe.None),
        new("Power Plan Manager", "Switch power plans and keep your PC awake.", new[] { "powerplanmanager", "powerplanmanager-lite" }, PowerPlanManagerStoreId, true, true, Probe.PowerPlanManager),
        // Not in the Store yet (no "Get it"); its status comes from its control pipe, see ConnectedApps.MapDeskPal.
        new("Desk Pal", "Break, focus and pause reminders at your desk.", new[] { "deskpal" }, null, false, true, Probe.DeskPal),
    };

    private readonly StackPanel _list = new();
    private readonly Wpf.Ui.Controls.Button _refresh;

    public ConnectedAppsPanel()
    {
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Text("Other apps from the same maker that Automation Portal can work with.", secondary: true, size: 12));
        _refresh = new Wpf.Ui.Controls.Button { Content = "Refresh", Icon = new SymbolIcon(SymbolRegular.ArrowClockwise24) };
        AutomationProperties.SetName(_refresh, "Refresh connected apps");
        _refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refresh, 1);
        head.Children.Add(_refresh);
        Children.Add(head);
        Children.Add(_list);
        Loaded += async (_, _) => { if (_list.Children.Count == 0) await RefreshAsync(); };
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        try
        {
            // Registry reads and the status pipe run off the UI thread; the pipe gets a short deadline.
            var results = await Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var list = new List<(Row Row, SiblingStatus Status)>();
                foreach (var row in Rows)
                {
                    if (row.Probe == Probe.DeskPal)
                    {
                        DeskPalLinkState probe;
                        try { probe = await SiblingApps.ProbeDeskPalAsync(cts.Token); }
                        catch (OperationCanceledException) { probe = DeskPalLinkState.NotRunning; }
                        list.Add((row, ConnectedApps.MapDeskPal(row.Schemes.Any(SiblingApps.IsRegistered), probe)));
                        continue;
                    }
                    var installed = row.Released && row.Schemes.Any(SiblingApps.IsRegistered);
                    PpmLinkState? ppm = null;
                    if (installed && row.Probe == Probe.PowerPlanManager)
                    {
                        try { ppm = await SiblingApps.ProbePowerPlanManagerAsync(cts.Token); }
                        catch (OperationCanceledException) { ppm = PpmLinkState.NotRunning; }
                    }
                    list.Add((row, ConnectedApps.Map(row.Released, installed, ppm)));
                }
                return list;
            });
            _list.Children.Clear();
            foreach (var (row, status) in results) _list.Children.Add(BuildRow(row, status));
        }
        finally { _refresh.IsEnabled = true; }
    }

    private UIElement BuildRow(Row row, SiblingStatus status)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(Ui.Text(row.Name, size: 14));
        var statusLine = ConnectedApps.StatusText(status);
        text.Children.Add(Ui.Text(statusLine == "Control off" ? statusLine + ". " + ConnectedApps.FixText(status, row.Name) : $"{statusLine}. {row.Blurb}", secondary: true, size: 12));
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(buttons, 1);
        var store = ConnectedApps.StoreUri(row.StoreId);
        if (status == SiblingStatus.NotInstalled && store is not null)
        {
            var get = new Wpf.Ui.Controls.Button { Content = Strings.Get("ConnectedAppsPanel_GetIt"), Appearance = ControlAppearance.Primary };
            AutomationProperties.SetName(get, "Get " + row.Name + " from the Microsoft Store");
            get.Click += (_, _) => OpenLink(store);
            buttons.Children.Add(get);
        }
        else if (row.HasOpen && status is SiblingStatus.Installed or SiblingStatus.Running)
        {
            var open = new Wpf.Ui.Controls.Button { Content = "Open" };
            AutomationProperties.SetName(open, "Open " + row.Name);
            if (row.Name == "Files Portal") open.Click += (_, _) => OpenApp(FilesPortalAppId);
            else if (row.Probe == Probe.DeskPal) open.Click += (_, _) => OpenLink("deskpal://open?page=home");
            else open.Click += async (_, _) => await OpenPpmAsync();
            buttons.Children.Add(open);
        }
        grid.Children.Add(buttons);

        var card = new Border { Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(card, row.Name + ": " + ConnectedApps.StatusText(status));
        return card;
    }

    private async Task OpenPpmAsync()
    {
        try { await SiblingApps.OpenPowerPlanManagerAsync(default); }
        catch (Exception ex) when (ex is ActionException or InvalidOperationException)
        { await Prompts.MessageAsync(Window.GetWindow(this), "Couldn't open Power Plan Manager", ex.Message); }
    }

    /// <summary>Starts a packaged app by its app user model id (opens or focuses it).</summary>
    private static void OpenApp(string appUserModelId)
    {
        try
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(@"shell:AppsFolder\" + appUserModelId);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static void OpenLink(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
