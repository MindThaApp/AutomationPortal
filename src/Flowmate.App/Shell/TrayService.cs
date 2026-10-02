using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Flowmate.App.Infrastructure;
using Flowmate.App.Pages;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Wpf.Ui.Controls;

namespace Flowmate.App.Shell;

/// <summary>
/// Tray icon. Left click opens the Quick Panel, right click shows the menu (pinned shortcuts, Quick Run, Open, Pause, Exit).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly AppHost _host;
    private TaskbarIcon? _icon;

    public TrayService(AppHost host) { _host = host; }

    public void Show()
    {
        var uri = new Uri("pack://application:,,,/Assets/flowmate.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        _icon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(stream),
            ToolTipText = "Flowmate",
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
            ContextMenu = BuildMenu(),
        };
        _icon.TrayLeftMouseUp += (_, _) => _host.ShowQuickPanel(atPointer: false);
        _icon.TrayContextMenuOpen += (_, _) => RefreshMenu();
        _icon.ForceCreate(enablesEfficiencyMode: false);
        _host.Settings.Changed += (_, _) => _host.Dispatcher.BeginInvoke(UpdateTooltip);
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (_icon is null) return;
        _icon.ToolTipText = _host.Settings.Current.AutomationsPaused ? "Flowmate (automations paused)" : "Flowmate";
    }

    public void ShowBalloon(string title, string message)
    {
        _host.Dispatcher.BeginInvoke(() =>
        {
            try { _icon?.ShowNotification(title, message, NotificationIcon.Info); }
            catch { }
        });
    }

    private void RefreshMenu()
    {
        if (_icon is null) return;
        _icon.ContextMenu = BuildMenu();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        Wpf.Ui.Controls.MenuItem Add(string header, SymbolRegular icon, Action action)
        {
            var mi = new Wpf.Ui.Controls.MenuItem { Header = header, Icon = new SymbolIcon(icon) };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }

        var pinned = _host.Library.All.Where(s => s.Pinned).Take(10).ToList();
        foreach (var s in pinned)
        {
            var shortcut = s;
            var mi = Add(s.Name, Icons.Parse(s.Icon), () => _host.Runs.Toggle(shortcut, Flowmate.Core.RunSource.Library));
            mi.Icon = new SymbolIcon(Icons.Parse(s.Icon)) { Foreground = Palette.Solid(s.Color) };
        }
        if (pinned.Count > 0) menu.Items.Add(new Separator());

        Add("Quick Run", SymbolRegular.Search24, () => _host.ShowQuickRun());
        Add("Open Flowmate", SymbolRegular.Window24, () => _host.ShowMainWindow());
        var paused = _host.Settings.Current.AutomationsPaused;
        var pause = new Wpf.Ui.Controls.MenuItem { Header = paused ? "Resume automations" : "Pause automations", Icon = new SymbolIcon(paused ? SymbolRegular.Play24 : SymbolRegular.Pause24) };
        pause.Click += (_, _) => _host.ToggleAutomationsPaused();
        menu.Items.Add(pause);
        menu.Items.Add(new Separator());
        Add("Exit Flowmate", SymbolRegular.Power24, () => _host.Exit());
        return menu;
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch { }
        _icon = null;
    }
}
