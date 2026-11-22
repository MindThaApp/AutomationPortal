using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.ViewModels;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Shell;

/// <summary>Stream Deck style flyout of pinned shortcuts. Opens near the tray or at the mouse pointer.</summary>
public partial class QuickPanelWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly ObservableCollection<ShortcutTileViewModel> _tiles = new();
    private DateTime _openedAt;

    public QuickPanelWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        TileList.ItemsSource = _tiles;
        Deactivated += (_, _) => { if ((DateTime.UtcNow - _openedAt).TotalMilliseconds > 400) Hide(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        _host.Settings.Changed += (_, _) => Dispatcher.BeginInvoke(RefreshPause);
        _host.Runs.StateChanged += (_, e) =>
        {
            var tile = _tiles.FirstOrDefault(t => t.Id == e.ShortcutId);
            if (tile is null) return;
            tile.State = e.State is Services.TileRunState.Running ? e.State : Services.TileRunState.Idle;
            if (e.State != Services.TileRunState.Running) tile.ProgressText = "";
        };
        // The same step line and "12 of 40" count the library tile shows, once a run has lasted long enough.
        _host.Runs.ProgressChanged += (_, e) =>
        {
            var tile = _tiles.FirstOrDefault(t => t.Id == e.ShortcutId);
            if (tile is { State: Services.TileRunState.Running }) tile.ProgressText = e.Text;
        };
    }

    public void Open(bool atPointer)
    {
        _tiles.Clear();
        foreach (var s in _host.Library.All.Where(s => s.Pinned).Take(24))
        {
            var vm = new ShortcutTileViewModel(s, actions: _host.Actions);
            if (_host.Runs.IsRunning(s.Id))
            {
                vm.State = Services.TileRunState.Running;
                if (_host.Runs.LatestProgress(s.Id) is { } p) vm.ProgressText = p.Text;
            }
            _tiles.Add(vm);
        }
        EmptyState.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPause();
        _openedAt = DateTime.UtcNow;
        _host.Theme.Refresh(this);
WindowPlacement.ShowCentered(this, w =>
        {
            if (atPointer)
            {
                var (x, y) = Monitors.CursorPixels();
                WindowPlacement.PlaceAt(w, x, y, centerOnPoint: true);
            }
            else WindowPlacement.PlaceAtTray(w);
        });
        AutomationPortal.Editor.Motion.Animate(Slide, System.Windows.Media.TranslateTransform.YProperty, 10, 0, AutomationPortal.Editor.MotionRole.EnterLong);
        _openedAt = DateTime.UtcNow;
    }

    private void RefreshPause()
    {
        var paused = _host.Settings.Current.AutomationsPaused;
        PauseButton.Content = paused ? Strings.Get("QuickPanelWindow_ResumeAutomations") : Strings.Get("TrayService_PauseAutomations");
        PauseButton.Icon = new SymbolIcon(paused ? SymbolRegular.Play24 : SymbolRegular.Pause24);
        PausedNote.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTileRun(object? sender, ShortcutTileViewModel tile)
    {
        Hide();
        _host.Runs.Toggle(tile.Shortcut, AutomationPortal.Core.RunSource.Library);
    }

    private void OnOpenMain(object sender, RoutedEventArgs e) { Hide(); _host.ShowMainWindow(); }
    private void OnQuickRun(object sender, RoutedEventArgs e) { Hide(); _host.ShowQuickRun(); }
    private void OnRecord(object sender, RoutedEventArgs e) { Hide(); _host.RecordNewShortcut(); }
    private void OnTogglePause(object sender, RoutedEventArgs e) => _host.ToggleAutomationsPaused();
}
