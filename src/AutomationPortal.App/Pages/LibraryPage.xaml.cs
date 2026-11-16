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
    // What the result bar shows: the run its details open, and its Undo (a run's file changes, or a delete).
    private string? _barRunId;
    private Func<Task>? _barUndo;
    private int _barToken;
    private TileStyleSetting _shownStyle = Logic.TileAppearance.Style;

    public LibraryPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        TileList.ItemsSource = _items;
        _host.Library.Changed += OnLibraryChanged;
        _host.Runs.StateChanged += OnRunStateChanged;
        _host.Settings.Changed += (_, s) =>
        {
            if (s.TileStyle == _shownStyle) return;
            _shownStyle = s.TileStyle;
            Logic.TileAppearance.Style = s.TileStyle;
            Dispatcher.BeginInvoke(Reload);
        };
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
        RefreshRecentlyDeletedButton();
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
                _tiles[s.Id] = vm = new ShortcutTileViewModel(s, explicitActions: true);
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

        RefreshStartHere(searching, list.Count);

        // Empty state
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count == 0) ConfigureEmptyState(searching);
    }

    /// <summary>The "Start here" row: up to four Gallery shortcuts that need no questions and aren't in the library yet.</summary>
    private void RefreshStartHere(bool searching, int shownCount)
    {
        StartHereItems.Children.Clear();
        var show = !searching && _target.Page == AppPage.Library && !_host.Settings.Current.HideStartHere && shownCount > 0 && shownCount < 12;
        if (show)
        {
            var have = new HashSet<string>(_host.Library.All.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            var offered = new List<(string FileName, GalleryItem Item)>();
            foreach (var section in _host.GallerySections())
                foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0 && !have.Contains(i.Shortcut.Name)))
                    offered.Add((item.FilePath, item));
            foreach (var item in FirstRun.PickStarters(offered, 4)) StartHereItems.Children.Add(BuildStartCard(item));
            show = StartHereItems.Children.Count > 0;
        }
        StartHerePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildStartCard(GalleryItem item)
    {
        var s = item.Shortcut;
        var card = new Border { Width = 182, Margin = new Thickness(6, 0, 6, 8), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var stack = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Chip(s.Icon, s.Color, 28, 16));
        var name = Ui.Text(s.Name, size: 13, weight: FontWeights.SemiBold);
        name.Margin = new Thickness(8, 0, 0, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 120;
        row.Children.Add(name);
        stack.Children.Add(row);
        var desc = Ui.Text(s.Description ?? "", secondary: true, size: 12);
        desc.MaxHeight = 34; desc.TextTrimming = TextTrimming.CharacterEllipsis; desc.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(desc);
        var add = new Wpf.Ui.Controls.Button { Content = "Add", Icon = new SymbolIcon(SymbolRegular.Add16), Padding = new Thickness(12, 3, 12, 3), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetName(add, "Add " + s.Name);
        add.Click += (_, _) => _ = NewShortcutMenu.AddGalleryItemAsync(_host, item, Main);
        stack.Children.Add(add);
        card.Child = stack;
        return card;
    }

    private void OnHideStartHere(object sender, RoutedEventArgs e)
    {
        _host.Settings.Update(x => x.HideStartHere = true);
        StartHerePanel.Visibility = Visibility.Collapsed;
    }

    private void ConfigureEmptyState(bool searching)
    {
        EmptyButtons.Visibility = Visibility.Visible;
        if (searching)
        {
            EmptyIcon.Symbol = SymbolRegular.SearchInfo24;
            EmptyTitle.Text = Strings.Get("LibraryPage_NothingMatchesYourSearch");
            EmptyText.Text = Strings.Get("LibraryPage_CheckTheSpellingOr");
            return;
        }
        switch (_target.Page)
        {
            case AppPage.Pinned:
                EmptyIcon.Symbol = SymbolRegular.Pin24;
                EmptyTitle.Text = Strings.Get("LibraryPage_NothingIsPinnedYet");
                EmptyText.Text = Strings.Get("LibraryPage_PinTheShortcutsYou");
                EmptyButtons.Visibility = Visibility.Collapsed;
                break;
            case AppPage.Folder:
                EmptyIcon.Symbol = SymbolRegular.Folder24;
                EmptyTitle.Text = Strings.Get("LibraryPage_ThisFolderIsEmpty");
                EmptyText.Text = Strings.Get("LibraryPage_DragShortcutsHereFrom");
                break;
            default:
                EmptyIcon.Symbol = SymbolRegular.Flash24;
                EmptyTitle.Text = Strings.Get("LibraryPage_LetsMakeSomethingHappen");
                EmptyText.Text = Strings.Get("LibraryPage_AShortcutIsA");
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

    private void OnTileSelect(object? sender, ShortcutTileViewModel tile)
    {
        foreach (var t in _tiles.Values) t.IsSelected = ReferenceEquals(t, tile);
    }

    private void OnTileEdit(object? sender, ShortcutTileViewModel tile) => _host.EditorLauncher.Open(tile.Shortcut.Id);

    /// <summary>One-time sheet before the first run of a shortcut that changes files. Opening it first runs nothing.</summary>
    private async Task RunAfterCheckAsync(ShortcutTileViewModel tile, IReadOnlyList<string> steps)
    {
        var s = tile.Shortcut;
        var dontShow = new System.Windows.Controls.CheckBox { Content = Strings.Get("LibraryPage_DontShowThisAgain"), Margin = new Thickness(0, 14, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(Ui.Text(FileChangeCheck.Message(s.Name, steps)));
        var preview = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(preview);
        body.Children.Add(dontShow);
        var dlg = new FlowDialog("Before the first run", body, "Run", "Cancel", width: 520);
        var openFirst = false;
        dlg.AddLeftButton("Open it first", () => { openFirst = true; dlg.Close(); });
        // Lists what it would move, rename or delete right now; only reads, changes nothing.
        dlg.AddLeftButton("Show what it would do", () => _ = FilePlanView.ShowPreviewAsync(preview, _host.Runner, s, ContentValue.Empty));
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
        // A run that changed files reports here while the library is on screen, wherever it was started from (tile, tray,
        // hotkey); when the window is hidden the app shows the same sentence as a notification instead.
        if (e.State == TileRunState.Succeeded && e.Result is { } done && IsVisible && RunResultText.Reports(e.Source)
            && await Task.Run(() => _host.DescribeRun(done)) is { } bar)
            ShowRunBar(bar);
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
                    // What it changed before it stopped can be undone from here too.
                    var failed = e.Result;
                    var partial = await Task.Run(() => _host.DescribeRun(failed));
                    ShowBar(BarLook.Problem, $"\"{e.ShortcutName}\" didn't finish",
                        (failed.ErrorMessage ?? "Something went wrong.") + (partial is null ? "" : $" Before it stopped, it {partial.Summary}."),
                        partial is { CanUndo: true } ? () => UndoRunAsync(failed.RunId) : null, failed.RunId);
                }
                await Task.Delay(3000);
                if (vm.State == TileRunState.Failed) vm.State = TileRunState.Idle;
                break;
        }
    }

    private void OnSeeDetails(object sender, RoutedEventArgs e) => _host.OpenRunHistory(_barRunId);

    private void OnDismissResult(object sender, RoutedEventArgs e)
    {
        _barToken++;
        _barUndo = null;
        ResultBar.Visibility = Visibility.Collapsed;
    }

    private enum BarLook { Done, Problem, Deleted }

    /// <summary>
    /// Shows the bar above the tiles. <paramref name="undo"/> adds an Undo button that works once; <paramref name="runId"/> adds
    /// See details; <paramref name="hideAfter"/> hides it unless something else was shown meanwhile.
    /// </summary>
    private void ShowBar(BarLook look, string title, string? message, Func<Task>? undo, string? runId, TimeSpan? hideAfter = null)
    {
        var token = ++_barToken;
        ResultBar.SetResourceReference(Border.BackgroundProperty, look == BarLook.Problem ? "SystemFillColorCriticalBackgroundBrush" : "CardBackgroundFillColorDefaultBrush");
        ResultIcon.Symbol = look switch { BarLook.Problem => SymbolRegular.ErrorCircle24, BarLook.Deleted => SymbolRegular.Delete24, _ => SymbolRegular.CheckmarkCircle24 };
        ResultIcon.SetResourceReference(ForegroundProperty, look switch
        {
            BarLook.Problem => "SystemFillColorCriticalBrush", BarLook.Deleted => "TextFillColorSecondaryBrush", _ => "SystemFillColorSuccessBrush",
        });
        ResultTitle.Text = title;
        ResultMessage.Text = message ?? "";
        ResultMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        _barUndo = undo;
        ResultUndo.IsEnabled = true;
        ResultUndo.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        _barRunId = runId;
        ResultDetails.Visibility = runId is null ? Visibility.Collapsed : Visibility.Visible;
        ResultBar.Visibility = Visibility.Visible;
        if (hideAfter is { } wait) _ = HideBarLaterAsync(token, wait);
    }

    private async Task HideBarLaterAsync(int token, TimeSpan wait)
    {
        await Task.Delay(wait);
        if (token != _barToken) return;
        _barUndo = null;
        ResultBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>"Tidy Downloads" moved 14 files. Undo, See details. Undo only when this run's record has changes it can put back.</summary>
    private void ShowRunBar(RunResultBarModel bar) =>
        ShowBar(BarLook.Done, bar.Title, bar.Message, bar.CanUndo ? () => UndoRunAsync(bar.RunId) : null, bar.RunId);

    private async Task UndoRunAsync(string runId)
    {
        var (title, message) = await RunUndo.UndoWithoutAskingAsync(_host.Paths, runId);
        ShowBar(title == "Undone" ? BarLook.Done : BarLook.Problem, title, message, null, runId);
    }

    private async void OnResultUndo(object sender, RoutedEventArgs e)
    {
        // One click, no question: the undo is taken off the bar first, so a second click can't run it twice.
        if (_barUndo is not { } undo) return;
        _barUndo = null;
        ResultUndo.IsEnabled = false;
        _barToken++;
        try { await undo(); }
        catch (Exception ex)
        {
            AppLog.Write("Undo from the result bar failed: " + ex.Message);
            ShowBar(BarLook.Problem, "Couldn't undo", ex.Message, null, _barRunId);
        }
    }

    private void RefreshRecentlyDeletedButton()
    {
        int count;
        try { count = _host.LibraryStore.RecentlyDeleted.List().Count; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException) { count = 0; }
        RecentlyDeletedButton.Content = $"Recently deleted ({count})";
        RecentlyDeletedButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- header buttons --------------------------------------------------------------------

    private void OnNewShortcut(object sender, RoutedEventArgs e)
    {
        var folder = _target.Page == AppPage.Folder ? _target.FolderId : null;
        if (sender is FrameworkElement anchor) Dialogs.NewShortcutMenu.Show(_host, anchor, folder);
        else _host.EditorLauncher.OpenNew(folder);
    }

    private void OnDescribeIt(object sender, RoutedEventArgs e)
    {
        var folder = _target.Page == AppPage.Folder ? _target.FolderId : null;
        if (sender is FrameworkElement anchor) Dialogs.NewShortcutMenu.Show(_host, anchor, folder, describeFirst: true);
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

        var move = new Wpf.Ui.Controls.MenuItem { Header = Strings.Get("LibraryPage_MoveToFolder"), Icon = new SymbolIcon(SymbolRegular.FolderArrowRight24) };
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
        var dlg = new SaveFileDialog { Title = Strings.Get("LibraryPage_ExportShortcut"), FileName = Sanitize(s.Name) + ".autoportal", Filter = "Automation Portal shortcut (*.autoportal)|*.autoportal" };
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
        // Deleting is recoverable, so it happens at once with an Undo; the question stays only when automations depend on it.
        var automations = _host.Library.Automations.Count(a => a.ShortcutId == s.Id);
        if (automations > 0)
        {
            var msg = $"Move \"{s.Name}\" to Recently deleted? It's kept on this PC for {RecentlyDeleted.KeepFor.Days} days, so you can restore it until then.";
            msg += $"\n\n{(automations == 1 ? "An automation uses it. It stays in your list but won't run" : automations + " automations use it. They stay in your list but won't run")} while the shortcut is deleted. Restoring the shortcut makes {(automations == 1 ? "it" : "them")} work again.";
            if (!await Prompts.ConfirmAsync(Main, "Delete shortcut", msg, "Delete", danger: true)) return;
        }
        var since = DateTimeOffset.Now;
        try { _host.Library.Delete(s.Id); }
        catch (LibraryException ex) { await Prompts.MessageAsync(Main, "Couldn't delete the shortcut", ex.Message); return; }
        RefreshRecentlyDeletedButton();
        var id = s.Id;
        var name = s.Name;
        Func<Task> undoDelete = () =>
        {
            _undoDelete = null;
            var (ok, title, message) = DeleteUndo.Undo(_host.LibraryStore, id, name, since);
            ShowBar(ok ? BarLook.Done : BarLook.Problem, title, message, null, null, ok ? DeleteUndo.BarTime : null);
            RefreshRecentlyDeletedButton();
            return Task.CompletedTask;
        };
        _undoDelete = undoDelete;
        _undoDeleteAt = DateTimeOffset.Now;
        ShowBar(BarLook.Deleted, $"Deleted \"{name}\".", $"It stays in Recently deleted for {RecentlyDeleted.KeepFor.Days} days.", undoDelete, null, DeleteUndo.BarTime);
    }

    private async void OnRecentlyDeleted(object sender, RoutedEventArgs e)
    {
        await RecentlyDeletedDialog.ShowAsync(Main, _host.LibraryStore);
        RefreshRecentlyDeletedButton();
    }

    private Func<Task>? _undoDelete;
    private DateTimeOffset _undoDeleteAt;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused) return;
        // Ctrl+Z puts back the shortcut deleted a moment ago (the same Undo as the bar), unless a text box wants the key.
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && focused is not System.Windows.Controls.Primitives.TextBoxBase
            && _undoDelete is { } undo && DateTimeOffset.Now - _undoDeleteAt < DeleteUndo.BarTime)
        {
            _undoDelete = null;
            _ = undo();
            e.Handled = true;
            return;
        }
        if (Ui.FindAncestor<ShortcutTile>(focused)?.Tile is not { } tile) return;
        if (e.Key == Key.Delete) { _ = DeleteAsync(tile.Shortcut); e.Handled = true; }
        else if (e.Key == Key.F2) { _ = RenameAsync(tile.Shortcut); e.Handled = true; }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control) { _host.EditorLauncher.Open(tile.Shortcut.Id); e.Handled = true; }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control) { Duplicate(tile.Shortcut); e.Handled = true; }
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

    private static List<string> DroppedShortcutFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(ShortcutPackage.HasPackageExtension).ToList()
            : new List<string>();

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e))
        {
            // A dropped .autoportal file is imported through the usual review; anything else is ignored.
            if (DroppedShortcutFiles(e).Count > 0) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            return;
        }
        e.Effects = string.IsNullOrWhiteSpace(_target.Query) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (!IsOurDrag(e))
        {
            var files = DroppedShortcutFiles(e);
            if (files.Count == 0) return;
            e.Handled = true;
            foreach (var f in files) await _host.ImportFromFileAsync(f, fromLink: false);
            return;
        }
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
