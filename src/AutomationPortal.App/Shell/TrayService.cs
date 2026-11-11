using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Pages;
using AutomationPortal.App.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Shell;

/// <summary>
/// Tray icon. Left click opens the Quick Panel, right click shows the menu (last failure, pinned shortcuts, Quick Run, Open, Record, Pause, Exit).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly AppHost _host;
    private TaskbarIcon? _icon;
    private System.Drawing.Icon? _normalIcon;
    private System.Drawing.Icon? _pausedIcon;
    private bool _showingPausedIcon;
    private LastFailure? _lastFailure;
    private DispatcherTimer? _timer;

    public TrayService(AppHost host) { _host = host; }

    public void Show()
    {
        var uri = new Uri("pack://application:,,,/Assets/automationportal.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        _normalIcon = new System.Drawing.Icon(stream);
        _icon = new TaskbarIcon
        {
            Icon = _normalIcon,
            ToolTipText = "Automation Portal",
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
            ContextMenu = BuildMenu(),
        };
        _icon.TrayLeftMouseUp += (_, _) => _host.ShowQuickPanel(atPointer: false);
        _icon.TrayContextMenuOpen += (_, _) => { ResumeIfDue(); RefreshMenu(); };
        _icon.ForceCreate(enablesEfficiencyMode: false);
        _host.Settings.Changed += (_, _) => _host.Dispatcher.BeginInvoke(UpdateTooltip);
        _host.Runs.StateChanged += OnRunStateChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => { ResumeIfDue(); UpdateTooltip(); };
        _timer.Start();
        ResumeIfDue();
        UpdateTooltip();
    }

    private void ResumeIfDue()
    {
        var s = _host.Settings.Current;
        if (s.AutomationsPaused && s.AutomationsPausedUntil is { } until && until <= DateTimeOffset.Now)
            _host.ResumeAutomations();
    }

    private void OnRunStateChanged(object? sender, RunStateChangedEventArgs e)
    {
        _lastFailure = TrayMenuLogic.NextFailure(_lastFailure, e.ShortcutId, e.ShortcutName,
            e.State == TileRunState.Failed && e.Result is not null, e.State == TileRunState.Succeeded, e.Result?.RunId);
    }

    private bool IsPaused()
    {
        var s = _host.Settings.Current;
        return TrayMenuLogic.IsPaused(s.AutomationsPaused, s.AutomationsPausedUntil, DateTimeOffset.Now);
    }

    private void UpdateTooltip()
    {
        if (_icon is null) return;
        var s = _host.Settings.Current;
        var paused = IsPaused();
        var until = s.AutomationsPausedUntil;
        _icon.ToolTipText = paused
            ? "Automation Portal - " + TrayMenuLogic.PauseStatus(true, until, DateTimeOffset.Now)
            : "Automation Portal";
        if (paused != _showingPausedIcon)
        {
            _showingPausedIcon = paused;
            _pausedIcon ??= MakePausedIcon(_normalIcon!);
            _icon.Icon = paused ? _pausedIcon : _normalIcon;
        }
    }

    /// <summary>A grey, faded copy of the normal icon with two pause bars in the corner.</summary>
    private static System.Drawing.Icon MakePausedIcon(System.Drawing.Icon source)
    {
        const int size = 32;
        using var src = new Icon(source, size, size);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            var matrix = new ColorMatrix(new[]
            {
                new[] { 0.3f, 0.3f, 0.3f, 0f, 0f },
                new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
                new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0.55f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(matrix);
            using var img = src.ToBitmap();
            g.DrawImage(img, new Rectangle(0, 0, size, size), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
            using var bg = new SolidBrush(System.Drawing.Color.FromArgb(230, 40, 40, 40));
            using var fg = new SolidBrush(System.Drawing.Color.White);
            g.FillEllipse(bg, 15, 15, 17, 17);
            g.FillRectangle(fg, 19, 19, 3, 9);
            g.FillRectangle(fg, 25, 19, 3, 9);
        }
        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { Native.DestroyIcon(hIcon); }
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
        Wpf.Ui.Controls.MenuItem Add(ItemsControl parent, string header, SymbolRegular icon, Action action)
        {
            var mi = new Wpf.Ui.Controls.MenuItem { Header = header, Icon = new SymbolIcon(icon) };
            mi.Click += (_, _) => action();
            parent.Items.Add(mi);
            return mi;
        }

        var s = _host.Settings.Current;
        var now = DateTimeOffset.Now;
        var paused = TrayMenuLogic.IsPaused(s.AutomationsPaused, s.AutomationsPausedUntil, now);
        var pinned = _host.Library.All.Where(x => x.Pinned).ToList();
        var model = TrayMenuLogic.Build(paused, s.AutomationsPausedUntil, now, _host.Runs.RunningCount, _lastFailure,
            pinned.Select(x => x.Id).ToList());

        if (model.FailedHeader is not null)
        {
            var failure = _lastFailure!;
            Add(menu, model.FailedHeader, SymbolRegular.ErrorCircle24, () =>
            {
                _lastFailure = null;
                _host.OpenRunHistory(failure.RunId);
            });
            menu.Items.Add(new Separator());
        }

        foreach (var id in model.PinnedShown)
        {
            var shortcut = pinned.First(x => x.Id == id);
            var mi = Add(menu, shortcut.Name, Icons.Parse(shortcut.Icon), () => _host.Runs.Toggle(shortcut, AutomationPortal.Core.RunSource.Library));
            mi.Icon = new SymbolIcon(Icons.Parse(shortcut.Icon)) { Foreground = Palette.Solid(shortcut.Color) };
        }
        if (model.ShowMorePinned)
            Add(menu, "More pinned…", SymbolRegular.Pin24, () => _host.ShowMainWindow(new NavTarget(AppPage.Pinned)));
        if (pinned.Count > 0) menu.Items.Add(new Separator());

        if (model.StopHeader is not null)
        {
            Add(menu, model.StopHeader, SymbolRegular.Stop24, () => _host.Runs.CancelAll());
            menu.Items.Add(new Separator());
        }

        Add(menu, "Quick Run", SymbolRegular.Search24, () => _host.ShowQuickRun());
        Add(menu, "Open Automation Portal", SymbolRegular.Window24, () => _host.ShowMainWindow());
        Add(menu, "Record a new shortcut", SymbolRegular.Record24, () => _host.RecordNewShortcut());

        if (paused)
        {
            Add(menu, "Resume now", SymbolRegular.Play24, () => _host.ResumeAutomations());
            menu.Items.Add(new Wpf.Ui.Controls.MenuItem { Header = model.PauseStatus, IsEnabled = false });
        }
        else
        {
            var sub = new Wpf.Ui.Controls.MenuItem { Header = "Pause automations", Icon = new SymbolIcon(SymbolRegular.Pause24) };
            void Choice(string header, PauseChoice c) =>
                Add(sub, header, SymbolRegular.Clock24, () => _host.PauseAutomations(TrayMenuLogic.PauseUntil(c, DateTimeOffset.Now)));
            Choice("For 30 minutes", PauseChoice.ThirtyMinutes);
            Choice("For 1 hour", PauseChoice.OneHour);
            Choice("Until tomorrow", PauseChoice.UntilTomorrow);
            Choice("Until I resume", PauseChoice.UntilResumed);
            menu.Items.Add(sub);
        }
        menu.Items.Add(new Separator());
        Add(menu, "Exit Automation Portal", SymbolRegular.Power24, () => _host.Exit());
        return menu;
    }

    public void Dispose()
    {
        _timer?.Stop();
        try { _icon?.Dispose(); } catch { }
        _icon = null;
    }
}
