using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.App.Controls;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.App.ViewModels;
using AutomationPortal.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

public partial class LibraryPage : UserControl, IAppPage
{
    public const string DragFormat = "autoportal.shortcut.id";

    private readonly AppHost _host;
    private readonly ObservableCollection<object> _items = new();
    private readonly Dictionary<Guid, ShortcutTileViewModel> _tiles = new();
    private readonly NewShortcutTile _newTile = new();
    private NavTarget _target = new(AppPage.Library);
    private bool _reloadQueued;
    private Point _dragStart;
    private ShortcutTileViewModel? _dragTile;
    private ShortcutTileViewModel? _dropTile;
    private RunResult? _lastFailure;

    public LibraryPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        TileList.ItemsSource = _items;
        _host.Library.Changed += OnLibraryChanged;
        _host.Runs.StateChanged += OnRunStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private MainWindow? Main => Window.GetWindow(this) as MainWindow ?? _host.MainWindow;

    public void Navigated(NavTarget target)
    {
        _target = target;
        ResultBar.Visibility = Visibility.Collapsed;
        Reload();
    }

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e)
    {
        if (!e.What.HasFlag(LibraryChange.Shortcuts) && !e.What.HasFlag(LibraryChange.Folders)) return;
        if (_reloadQueued) return;
        _reloadQueued = true;
        Dispatcher.BeginInvoke(() => { _reloadQueued = false; Reload(); });
    }

    // ---- loading ---------------------------------------------------------------------------

    private IReadOnlyList<Shortcut> VisibleShortcuts()
    {
        IEnumerable<Shortcut> all = _host.Library.All;
        all = _target.Page switch
        {
            AppPage.Pinned => all.Where(s => s.Pinned),
            AppPage.Folder => all.Where(s => s.FolderId == _target.FolderId),
            _ => all,
        };
        if (!string.IsNullOrWhiteSpace(_target.Query))
        {
            var q = _target.Query!;
            return _host.Library.All
                .Select(s => (s, score: FuzzyMatcher.ScoreNameAndAlias(q, s.Name, s.Alias)))
                .Where(x => x.score is not null).OrderByDescending(x => x.score).Select(x => x.s).ToList();
        }
        return all.ToList();
    }

    private void Reload()
    {
        var list = VisibleShortcuts();
        var searching = !string.IsNullOrWhiteSpace(_target.Query);

        // Keep existing view models so a running spinner survives a reload.
        var keep = new HashSet<Guid>(list.Select(s => s.Id));
        foreach (var gone in _tiles.Keys.Where(k => !keep.Contains(k)).ToList()) _tiles.Remove(gone);
        _items.Clear();
        foreach (var s in list)
        {
            if (_tiles.TryGetValue(s.Id, out var vm)) vm.Update(s);
            else
            {
                _tiles[s.Id] = vm = new ShortcutTileViewModel(s);
                if (_host.Runs.IsRunning(s.Id)) vm.State = TileRunState.Running;
            }
            _items.Add(vm);
        }
        if (!searching && list.Count > 0 && _target.Page != AppPage.Pinned) _items.Add(_newTile);

        // Header
        var folder = _target.Page == AppPage.Folder ? _host.Library.Folders.FirstOrDefault(f => f.Id == _target.FolderId) : null;
        TitleText.Text = searching ? "Search results" : _target.Page switch
        {
            AppPage.Pinned => "Pinned",
            AppPage.Folder => folder?.Name ?? "Folder",
            _ => "All Shortcuts",
        };
        SubtitleText.Text = searching
            ? (list.Count == 1 ? $"1 shortcut matches \"{_target.Query}\"" : $"{list.Count} shortcuts match \"{_target.Query}\"")
            : (list.Count == 1 ? "1 shortcut" : $"{list.Count} shortcuts");
        ClearSearchButton.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;

        // Empty state
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count == 0) ConfigureEmptyState(searching);
    }

    private void ConfigureEmptyState(bool searching)
    {
        EmptyButtons.Visibility = Visibility.Visible;
        if (searching)
        {
            EmptyIcon.Symbol = SymbolRegular.SearchInfo24;
            EmptyTitle.Text = "Nothing matches your search";
            EmptyText.Text = "Check the spelling, or try a shorter word. You can also look for a ready-made shortcut in the Gallery.";
            return;
        }
        switch (_target.Page)
        {
            case AppPage.Pinned:
                EmptyIcon.Symbol = SymbolRegular.Pin24;
                EmptyTitle.Text = "Nothing is pinned yet";
                EmptyText.Text = "Pin the shortcuts you use most. Right-click a shortcut and choose Pin. Pinned shortcuts also show up when you click the Automation Portal icon near the clock.";
                EmptyButtons.Visibility = Visibility.Collapsed;
                break;
            case AppPage.Folder:
                EmptyIcon.Symbol = SymbolRegular.Folder24;
                EmptyTitle.Text = "This folder is empty";
                EmptyText.Text = "Drag shortcuts here from the list, or create a new one.";
                break;
            default:
                EmptyIcon.Symbol = SymbolRegular.Flash24;
                EmptyTitle.Text = "Let's make something happen";
                EmptyText.Text = "A shortcut is a set of steps Automation Portal does for you with one click. Start with a ready-made one from the Gallery, or build your own.";
                break;
        }
    }

    // ---- run -------------------------------------------------------------------------------

    private void OnTileRun(object? sender, ShortcutTileViewModel tile)
    {
        ResultBar.Visibility = Visibility.Collapsed;
        var steps = FileChangeCheck.FileChangingSteps(tile.Shortcut, _host.Actions);
        if (FileChangeCheck.ShouldAsk(_host.Settings.Current, tile.Shortcut.Id, steps)) { _ = RunAfterCheckAsync(tile, steps); return; }
        _host.Runs.Toggle(tile.Shortcut, RunSource.Library);
    }

    private void OnTileEdit(object? sender, ShortcutTileViewModel tile) => _host.EditorLauncher.Open(tile.Shortcut.Id);

    /// <summary>One-time sheet before the first run of a shortcut that changes files. Opening it first runs nothing.</summary>
    private async Task RunAfterCheckAsync(ShortcutTileViewModel tile, IReadOnlyList<string> steps)
    {
        var s = tile.Shortcut;
        var dontShow = new System.Windows.Controls.CheckBox { Content = "Don't show this again", Margin = new Thickness(0, 14, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(Ui.Text(FileChangeCheck.Message(s.Name, steps)));
        body.Children.Add(dontShow);
        var dlg = new FlowDialog("Before the first run", body, "Run", "Cancel", width: 460);
        var openFirst = false;
        dlg.AddLeftButton("Open it first", () => { openFirst = true; dlg.Close(); });
        var run = await dlg.ShowTopmostAsync(default, Main);
        if (openFirst) { _host.EditorLauncher.Open(s.Id); return; }
        if (!run) return;
        var off = dontShow.IsChecked == true;
        _host.Settings.Update(x =>
        {
            if (!x.FileChangeCheckedShortcutIds.Contains(s.Id)) x.FileChangeCheckedShortcutIds.Add(s.Id);
            if (off) x.AskBeforeFileChangingRuns = false;
        });
        _host.Runs.Toggle(s, RunSource.Library);
    }

    private async void OnRunStateChanged(object? sender, RunStateChangedEventArgs e)
    {
        if (!_tiles.TryGetValue(e.ShortcutId, out var vm)) return;
        vm.State = e.State;
        switch (e.State)
        {
            case TileRunState.Succeeded:
                await Task.Delay(1800);
                if (vm.State == TileRunState.Succeeded) vm.State = TileRunState.Idle;
                break;
            case TileRunState.Failed:
                if (e.Source == RunSource.Library && e.Result is not null)
                {
                    _lastFailure = e.Result;
                    ResultTitle.Text = $"\"{e.ShortcutName}\" didn't finish";
                    ResultMessage.Text = e.Result.ErrorMessage ?? "Something went wrong.";
                    ResultBar.Visibility = Visibility.Visible;
                }
                await Task.Delay(3000);
                if (vm.State == TileRunState.Failed) vm.State = TileRunState.Idle;
                break;
        }
    }

    private void OnSeeDetails(object sender, RoutedEventArgs e) => _host.OpenRunHistory(_lastFailure?.RunId);
    private void OnDismissResult(object sender, RoutedEventArgs e) => ResultBar.Visibility = Visibility.Collapsed;

    // ---- header buttons --------------------------------------------------------------------

    private void OnNewShortcut(object sender, RoutedEventArgs e)
    {
        var folder = _target.Page == AppPage.Folder ? _target.FolderId : null;
        if (sender is FrameworkElement anchor) Dialogs.NewShortcutMenu.Show(_host, anchor, folder);
        else _host.EditorLauncher.OpenNew(folder);
    }

    private void OnBrowseGallery(object sender, RoutedEventArgs e) => Main?.NavigateTo(new NavTarget(AppPage.Gallery));

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        Main?.ClearSearch();
        Main?.NavigateTo(_target with { Query = null });
    }

    // ---- context menu ----------------------------------------------------------------------

    private void OnTileMenu(object? sender, (ShortcutTileViewModel Tile, FrameworkElement Anchor, bool AtPointer) e)
    {
        var menu = BuildMenu(e.Tile);
        menu.PlacementTarget = e.Anchor;
        menu.Placement = e.AtPointer ? System.Windows.Controls.Primitives.PlacementMode.MousePoint : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu(ShortcutTileViewModel tile)
    {
        var s = tile.Shortcut;
        var menu = new ContextMenu();
        Wpf.Ui.Controls.MenuItem Add(string header, SymbolRegular icon, Action action, string? gesture = null)
        {
            var mi = new Wpf.Ui.Controls.MenuItem { Header = header, Icon = new SymbolIcon(icon), InputGestureText = gesture ?? "" };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }

        Add("Edit", SymbolRegular.Edit24, () => _host.EditorLauncher.Open(s.Id), "Ctrl+E");
        Add("Duplicate", SymbolRegular.Copy24, () => Duplicate(s));
        Add("Rename", SymbolRegular.Rename24, () => _ = RenameAsync(s), "F2");
        Add("Change icon", SymbolRegular.Emoji24, () => _ = ChangeIconAsync(s));
        menu.Items.Add(new Separator());
        Add(s.Pinned ? "Unpin" : "Pin", s.Pinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () => Mutate(s, c => c.Pinned = !c.Pinned));

        var move = new Wpf.Ui.Controls.MenuItem { Header = "Move to folder", Icon = new SymbolIcon(SymbolRegular.FolderArrowRight24) };
        void AddFolder(string name, Guid? id)
        {
            var mi = new Wpf.Ui.Controls.MenuItem { Header = name, IsCheckable = false };
            if (s.FolderId == id) mi.Icon = new SymbolIcon(SymbolRegular.Checkmark20);
            mi.Click += (_, _) => Mutate(s, c => c.FolderId = id);
            move.Items.Add(mi);
        }
        AddFolder("No folder", null);
        foreach (var f in _host.Library.Folders) AddFolder(f.Name, f.Id);
        menu.Items.Add(move);

        Add("Hotkey…", SymbolRegular.Keyboard24, () => _ = SetHotkeyAsync(s));
        menu.Items.Add(new Separator());
        Add("Add to desktop", SymbolRegular.Desktop24, () => _ = AddToDesktopAsync(s));
        Add("Export…", SymbolRegular.ArrowExport24, () => Export(s));
        menu.Items.Add(new Separator());
        Add("Delete", SymbolRegular.Delete24, () => _ = DeleteAsync(s), "Del");
        return menu;
    }

    private void Mutate(Shortcut s, Action<Shortcut> change)
    {
        var copy = (_host.Library.GetById(s.Id) ?? s).Clone();
        change(copy);
        try { _host.Library.Save(copy); }
        catch (LibraryException ex) { _ = Prompts.MessageAsync(Main, "Couldn't save", ex.Message); }
    }

    private void Duplicate(Shortcut s)
    {
        var copy = s.Clone();
        copy.Id = Guid.NewGuid();
        copy.Pinned = false;
        copy.Hotkey = null;
        copy.Alias = null;
        copy.CreatedAt = DateTimeOffset.Now;
        var name = s.Name + " copy";
        for (var n = 2; _host.Library.IsNameTaken(name); n++) name = $"{s.Name} copy {n}";
        copy.Name = name;
        copy.SortOrder = _host.Library.All.Count == 0 ? 0 : _host.Library.All.Max(x => x.SortOrder) + 1;
        try { _host.Library.Save(copy); }
        catch (LibraryException ex) { _ = Prompts.MessageAsync(Main, "Couldn't duplicate", ex.Message); }
    }

    private async Task RenameAsync(Shortcut s)
    {
        var name = await Prompts.AskTextAsync(Main, "Rename shortcut", "Name", s.Name, "Rename",
            n => _host.Library.IsNameTaken(n, s.Id) ? $"A shortcut named \"{n}\" already exists. Choose a different name." : null);
        if (name is not null) Mutate(s, c => c.Name = name);
    }

    private async Task ChangeIconAsync(Shortcut s)
    {
        var owner = Main ?? (Window)Window.GetWindow(this)!;
        var pick = await _host.EditorLauncher.PickIconAsync(owner, s.Icon, s.Color);
        if (pick is not null) Mutate(s, c => { c.Icon = pick.Icon; c.Color = pick.Color; });
    }

    private async Task SetHotkeyAsync(Shortcut s)
    {
        var recorder = new HotkeyRecorderBox { Value = s.Hotkey ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
        var body = new StackPanel();
        body.Children.Add(Ui.Text("Press the keys you want to use to run this shortcut from anywhere. Use at least one of Ctrl, Alt or Win.", secondary: true, size: 13));
        recorder.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(recorder);
        var dlg = new FlowDialog("Hotkey", body, "Save", "Cancel", width: 440);
        dlg.AddLeftButton("Clear", () => recorder.Value = "");
        dlg.FocusWhenShown(recorder);
        if (await dlg.ShowTopmostAsync(default, Main))
            Mutate(s, c => c.Hotkey = string.IsNullOrWhiteSpace(recorder.Value) ? null : recorder.Value);
    }

    private async Task AddToDesktopAsync(Shortcut s)
    {
        try { _host.Shell.AddToDesktop(s); }
        catch (NotSupportedException ex) { await Prompts.MessageAsync(Main, "Not available yet", ex.Message); }
        catch (Exception ex) { await Prompts.MessageAsync(Main, "Couldn't add it to the desktop", ex.Message); }
    }

    private void Export(Shortcut s)
    {
        var dlg = new SaveFileDialog { Title = "Export shortcut", FileName = Sanitize(s.Name) + ".autoportal", Filter = "Automation Portal shortcut (*.autoportal)|*.autoportal" };
        if (dlg.ShowDialog(Main) != true) return;
        try { File.WriteAllText(dlg.FileName, _host.Library.Export(s.Id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
        { _ = Prompts.MessageAsync(Main, "Couldn't export", $"Automation Portal couldn't save the file. {ex.Message}"); }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private async Task DeleteAsync(Shortcut s)
    {
        var automations = _host.Library.Automations.Count(a => a.ShortcutId == s.Id);
        var msg = $"Move \"{s.Name}\" to Recently deleted? It's kept on this PC for {RecentlyDeleted.KeepFor.Days} days, so you can restore it here until then.";
        if (automations > 0)
            msg += $"\n\n{(automations == 1 ? "An automation uses it. It stays in your list but won't run" : automations + " automations use it. They stay in your list but won't run")} while the shortcut is deleted. Restoring the shortcut makes {(automations == 1 ? "it" : "them")} work again.";
        if (!await Prompts.ConfirmAsync(Main, "Delete shortcut", msg, "Delete", danger: true)) return;
        try { _host.Library.Delete(s.Id); }
        catch (LibraryException ex) { await Prompts.MessageAsync(Main, "Couldn't delete the shortcut", ex.Message); }
    }

    private void OnRecentlyDeleted(object sender, RoutedEventArgs e) => _ = RecentlyDeletedDialog.ShowAsync(Main, _host.LibraryStore);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused) return;
        if (Ui.FindAncestor<ShortcutTile>(focused)?.Tile is not { } tile) return;
        if (e.Key == Key.Delete) { _ = DeleteAsync(tile.Shortcut); e.Handled = true; }
        else if (e.Key == Key.F2) { _ = RenameAsync(tile.Shortcut); e.Handled = true; }
    }

    // ---- drag and drop ---------------------------------------------------------------------

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragTile = (sender as ShortcutTile)?.Tile;
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragTile is null) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tile = _dragTile;
        _dragTile = null;
        var data = new DataObject(DragFormat, tile.Id.ToString());
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        ClearDropMarker();
    }

    private static bool IsOurDrag(DragEventArgs e) => e.Data.GetDataPresent(DragFormat);

    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e)) return;
        e.Effects = string.IsNullOrWhiteSpace(_target.Query) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        var tile = (sender as ShortcutTile)?.Tile;
        if (tile is null || tile == _dropTile) return;
        ClearDropMarker();
        _dropTile = tile;
        tile.IsDropTarget = true;
    }

    private void OnTileDragLeave(object sender, DragEventArgs e) => ClearDropMarker();

    private void ClearDropMarker()
    {
        if (_dropTile is not null) _dropTile.IsDropTarget = false;
        _dropTile = null;
    }

    private void OnTileDrop(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e)) return;
        var target = (sender as ShortcutTile)?.Tile;
        ClearDropMarker();
        if (target is null || !Guid.TryParse(e.Data.GetData(DragFormat) as string, out var movedId)) return;
        e.Handled = true;
        Reorder(movedId, target.Id);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e)) return;
        e.Effects = string.IsNullOrWhiteSpace(_target.Query) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e) || !Guid.TryParse(e.Data.GetData(DragFormat) as string, out var movedId)) return;
        e.Handled = true;
        Reorder(movedId, null);
    }

    private void Reorder(Guid movedId, Guid? beforeId)
    {
        if (!string.IsNullOrWhiteSpace(_target.Query) || movedId == beforeId) return;
        var visible = _items.OfType<ShortcutTileViewModel>().Select(t => t.Id).ToList();
        var newVisible = ShortcutOrdering.MoveBefore(visible, movedId, beforeId);
        var global = ShortcutOrdering.ApplyToGlobal(_host.Library.All.Select(s => s.Id).ToList(), newVisible);
        var changed = new List<Shortcut>();
        for (var i = 0; i < global.Count; i++)
        {
            var current = _host.Library.GetById(global[i]);
            if (current is null || current.SortOrder == i) continue;
            var copy = current.Clone();
            copy.SortOrder = i;
            changed.Add(copy);
        }
        try { _host.Library.SaveMany(changed); } catch (LibraryException) { }
    }

    /// <summary>Called when a shortcut is dropped on a folder (or All Shortcuts, with null) in the navigation pane.</summary>
    public void MoveToFolder(Guid shortcutId, Guid? folderId)
    {
        if (_host.Library.GetById(shortcutId) is not { } s || s.FolderId == folderId) return;
        Mutate(s, c => c.FolderId = folderId);
    }
}
