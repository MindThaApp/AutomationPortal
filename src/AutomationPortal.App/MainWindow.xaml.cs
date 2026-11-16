using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Pages;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App;

public partial class MainWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly Dictionary<AppPage, FrameworkElement> _pages = new();
    private readonly NavigationViewItem _all, _pinned, _newFolder, _automations, _snippets, _gallery, _history, _settings;
    private readonly List<(NavigationViewItem Item, Guid FolderId)> _folderItems = new();
    private NavigationViewItem? _active;
    private NavTarget _current = new(AppPage.Library);
    private bool _rebuildQueued;
    private bool _sentHideHint;

    /// <summary>Set when the host replaces this window, so closing it neither hides to the tray nor exits.</summary>
    internal bool Recycling { get; set; }

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        _all = Item("All Shortcuts", "Apps24", new NavTarget(AppPage.Library));
        _pinned = Item("Pinned", "Pin24", new NavTarget(AppPage.Pinned));
        _newFolder = Item("New Folder", "FolderAdd24", null);
        _newFolder.Click += async (_, _) => await CreateFolderAsync();
        _automations = Item("Automations", "FlashAuto24", new NavTarget(AppPage.Automations));
        _snippets = Item("Snippets", "TextT24", new NavTarget(AppPage.Snippets));
        _gallery = Item("Gallery", "Library24", new NavTarget(AppPage.Gallery));
        _history = Item("Run History", "History24", new NavTarget(AppPage.History));
        _settings = Item("Settings", "Settings24", new NavTarget(AppPage.Settings));
        Nav.FooterMenuItems.Add(_settings);

        RebuildNav();
        _host.Library.Changed += OnLibraryChanged;
        Loaded += (_, _) => NavigateTo(_current);
        PreviewKeyDown += OnWindowKeyDown;
        StateChanged += (_, _) => { };
    }

    /// <summary>A divider in the navigation list, named so a screen reader doesn't read out the class name.</summary>
    private static NavigationViewItemSeparator Separator()
    {
        var s = new NavigationViewItemSeparator();
        System.Windows.Automation.AutomationProperties.SetName(s, "Separator");
        return s;
    }

    private NavigationViewItem Item(string text, string icon, NavTarget? target)
    {
        var item = new NavigationViewItem { Content = text, Icon = new SymbolIcon(Icons.Parse(icon)) };
        AutomationProperties.SetName(item, text);
        if (target is not null) item.Click += (_, _) => NavigateTo(target);
        return item;
    }

    // ---- navigation ------------------------------------------------------------------------

    public NavTarget Current => _current;

    public void NavigateTo(NavTarget target)
    {
        _current = target;
        var page = target.Page switch
        {
            AppPage.Library or AppPage.Pinned or AppPage.Folder => Page(AppPage.Library, () => new LibraryPage(_host)),
            AppPage.Automations => Page(AppPage.Automations, () => new AutomationsPage(_host)),
            AppPage.Snippets => Page(AppPage.Snippets, () => new SnippetsPage(_host)),
            AppPage.Gallery => Page(AppPage.Gallery, () => new GalleryPage(_host)),
            AppPage.History => Page(AppPage.History, () => new RunHistoryPage(_host)),
            _ => Page(AppPage.Settings, () => new SettingsPage(_host)),
        };

        var item = target.Page switch
        {
            AppPage.Library => _all,
            AppPage.Pinned => _pinned,
            AppPage.Folder => _folderItems.FirstOrDefault(f => f.FolderId == target.FolderId).Item ?? _all,
            AppPage.Automations => _automations,
            AppPage.Snippets => _snippets,
            AppPage.Gallery => _gallery,
            AppPage.History => _history,
            _ => _settings,
        };
        SetActive(item);
        Nav.ReplaceContent(page);
        (page as IAppPage)?.Navigated(target);
    }

    private FrameworkElement Page(AppPage key, Func<FrameworkElement> create)
    {
        if (!_pages.TryGetValue(key, out var p)) _pages[key] = p = create();
        return p;
    }

    private void SetActive(NavigationViewItem item)
    {
        if (_active is not null && _active != item) _active.IsActive = false;
        item.IsActive = true;
        _active = item;
    }

    public T? GetPage<T>() where T : class => _pages.Values.OfType<T>().FirstOrDefault();

    // ---- folders ---------------------------------------------------------------------------

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e)
    {
        if (!e.What.HasFlag(LibraryChange.Folders)) return;
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() => { _rebuildQueued = false; RebuildNav(); });
    }

    private void RebuildNav()
    {
        var wasActive = _active;
        Nav.MenuItems.Clear();
        _folderItems.Clear();
        Nav.MenuItems.Add(_all);
        Nav.MenuItems.Add(_pinned);
        Nav.MenuItems.Add(Separator());
        var foldersHeader = new NavigationViewItemHeader { Text = "Folders", Margin = new Thickness(14, 4, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(foldersHeader, "Folders");
        Nav.MenuItems.Add(foldersHeader);
        foreach (var f in _host.Library.Folders)
        {
            var item = Item(f.Name, f.Icon, new NavTarget(AppPage.Folder, f.Id));
            item.AllowDrop = true;
            var folderId = f.Id;
            item.DragOver += (_, e) => { if (e.Data.GetDataPresent(LibraryPage.DragFormat)) { e.Effects = DragDropEffects.Move; e.Handled = true; } };
            item.Drop += (_, e) =>
            {
                if (e.Data.GetData(LibraryPage.DragFormat) is string id && Guid.TryParse(id, out var sid))
                {
                    GetPage<LibraryPage>()?.MoveToFolder(sid, folderId);
                    e.Handled = true;
                }
            };
            item.ContextMenu = BuildFolderMenu(f);
            _folderItems.Add((item, f.Id));
            Nav.MenuItems.Add(item);
        }
        Nav.MenuItems.Add(_newFolder);
        Nav.MenuItems.Add(Separator());
        Nav.MenuItems.Add(_automations);
        Nav.MenuItems.Add(_snippets);
        Nav.MenuItems.Add(_gallery);
        Nav.MenuItems.Add(_history);

        // The folder that was open may have been deleted or renamed.
        if (_current.Page == AppPage.Folder && _folderItems.All(f => f.FolderId != _current.FolderId))
            NavigateTo(new NavTarget(AppPage.Library));
        else if (IsLoaded)
            SetActive(_current.Page == AppPage.Folder ? _folderItems.First(f => f.FolderId == _current.FolderId).Item : wasActive ?? _all);
    }

    private ContextMenu BuildFolderMenu(ShortcutFolder f)
    {
        var menu = new ContextMenu();
        var rename = new Wpf.Ui.Controls.MenuItem { Header = "Rename", Icon = new SymbolIcon(SymbolRegular.Rename24) };
        rename.Click += async (_, _) => await RenameFolderAsync(f.Id);
        var icon = new Wpf.Ui.Controls.MenuItem { Header = Strings.Get("MainWindow_ChangeIcon"), Icon = new SymbolIcon(SymbolRegular.Emoji24) };
        icon.Click += async (_, _) => await ChangeFolderIconAsync(f.Id);
        var delete = new Wpf.Ui.Controls.MenuItem { Header = Strings.Get("MainWindow_DeleteFolder"), Icon = new SymbolIcon(SymbolRegular.Delete24) };
        delete.Click += async (_, _) => await DeleteFolderAsync(f.Id);
        menu.Items.Add(rename);
        menu.Items.Add(icon);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        return menu;
    }

    public async Task CreateFolderAsync()
    {
        var previous = _current;
        var name = await Prompts.AskTextAsync(this, "New folder", "Folder name", "", "Create", n =>
            _host.Library.Folders.Any(f => f.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase)) ? "You already have a folder with that name." : null, "For example: Work");
        if (name is null) { NavigateTo(previous); return; }
        var folder = new ShortcutFolder { Name = name, SortOrder = _host.Library.Folders.Count == 0 ? 0 : _host.Library.Folders.Max(f => f.SortOrder) + 1 };
        _host.Library.SaveFolder(folder);
        RebuildNav();
        NavigateTo(new NavTarget(AppPage.Folder, folder.Id));
    }

    private async Task RenameFolderAsync(Guid id)
    {
        if (_host.Library.Folders.FirstOrDefault(f => f.Id == id) is not { } folder) return;
        var name = await Prompts.AskTextAsync(this, "Rename folder", "Folder name", folder.Name, "Rename", n =>
            _host.Library.Folders.Any(f => f.Id != id && f.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase)) ? "You already have a folder with that name." : null);
        if (name is null) return;
        folder = AutomationPortalJson.Clone(folder);
        folder.Name = name;
        _host.Library.SaveFolder(folder);
    }

    private async Task ChangeFolderIconAsync(Guid id)
    {
        if (_host.Library.Folders.FirstOrDefault(f => f.Id == id) is not { } folder) return;
        var pick = await _host.EditorLauncher.PickIconAsync(this, folder.Icon, "blue");
        if (pick is null) return;
        folder = AutomationPortalJson.Clone(folder);
        folder.Icon = pick.Icon;
        _host.Library.SaveFolder(folder);
    }

    private async Task DeleteFolderAsync(Guid id)
    {
        if (_host.Library.Folders.FirstOrDefault(f => f.Id == id) is not { } folder) return;
        var count = _host.Library.All.Count(s => s.FolderId == id);
        var msg = count == 0
            ? $"Delete the folder \"{folder.Name}\"?"
            : $"Delete the folder \"{folder.Name}\"? Its {count} shortcut{(count == 1 ? "" : "s")} won't be deleted. They move back to All Shortcuts.";
        if (await Prompts.ConfirmAsync(this, "Delete folder", msg, "Delete", danger: true))
            _host.Library.DeleteFolder(id);
    }

    // ---- search ----------------------------------------------------------------------------

    private sealed record Suggestion(string Text, Shortcut? Shortcut, Services.GalleryItem? Gallery)
    {
        public override string ToString() => Text;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        if (q.Length == 0)
        {
            sender.ItemsSource = null;
            if (_current.Query is not null) NavigateTo(_current with { Query = null });
            return;
        }
        var list = new List<Suggestion>();
        foreach (var (s, _) in _host.Library.All
                     .Select(s => (s, score: FuzzyMatcher.ScoreNameAndAlias(q, s.Name, s.Alias)))
                     .Where(x => x.score is not null).OrderByDescending(x => x.score).Take(5))
            list.Add(new Suggestion(s.Name, s, null));
        foreach (var g in _host.GallerySections().SelectMany(sec => sec.Items)
                     .Where(g => FuzzyMatcher.Score(q, g.Shortcut.Name) is not null).Take(4))
            list.Add(new Suggestion("Gallery: " + g.Shortcut.Name, null, g));
        sender.ItemsSource = list;
        sender.IsSuggestionListOpen = list.Count > 0;
    }

    private void OnSearchChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not Suggestion s) return;
        if (s.Shortcut is not null) NavigateTo(new NavTarget(AppPage.Library, Query: s.Shortcut.Name));
        else if (s.Gallery is not null) NavigateTo(new NavTarget(AppPage.Gallery, Item: Path.GetFileName(s.Gallery.FilePath)));
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var q = args.QueryText?.Trim();
        NavigateTo(new NavTarget(AppPage.Library, Query: string.IsNullOrEmpty(q) ? null : q));
    }

    public void ClearSearch() => SearchBox.Text = "";

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (AutomationPortal.Editor.UI.KeySheet.IsOpenKey(e.Key, Keyboard.Modifiers))
        {
            AutomationPortal.Editor.UI.KeySheet.Show(this);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.K)
        {
            OpenPalette();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemComma)
        {
            NavigateTo(new NavTarget(AppPage.Settings));
            e.Handled = true;
        }
        else if (ctrl && PageForDigit(e.Key) is { } page)
        {
            NavigateTo(new NavTarget(page));
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _host.EditorLauncher.OpenNew(_current.Page == AppPage.Folder ? _current.FolderId : null);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+1 to Ctrl+6: the sidebar pages in the order they are listed.</summary>
    private static AppPage? PageForDigit(Key key)
    {
        var n = key is >= Key.D1 and <= Key.D6 ? key - Key.D1 : key is >= Key.NumPad1 and <= Key.NumPad6 ? key - Key.NumPad1 : -1;
        return n < 0 ? null : new[] { AppPage.Library, AppPage.Pinned, AppPage.Automations, AppPage.Snippets, AppPage.Gallery, AppPage.History }[n];
    }

    // ---- command palette (Ctrl+K) ----------------------------------------------------------

    public void OpenPalette()
    {
        var entries = new List<Logic.PaletteEntry>();
        void Command(string key, string title, string detail, params string[] words) =>
            entries.Add(new Logic.PaletteEntry(Logic.PaletteKind.Command, title, detail, key, null, words));
        Command("new", "New shortcut", "Command · Ctrl+N", "create", "add");
        Command("automation", "New automation", "Command · run a shortcut by itself", "trigger", "schedule", "when");
        Command("page:library", "All Shortcuts", "Page · Ctrl+1", "library", "home");
        Command("page:pinned", "Pinned", "Page · Ctrl+2", "favourites");
        Command("page:automations", "Automations", "Page · Ctrl+3");
        Command("page:snippets", "Snippets", "Page · Ctrl+4", "text");
        Command("page:gallery", "Gallery", "Page · Ctrl+5", "ready-made", "starter");
        Command("page:history", "Run History", "Page · Ctrl+6", "past", "log", "results");
        Command("settings", "Settings", "Page · Ctrl+,", "options", "preferences", "links", "hotkeys");
        Command("ai", "Settings: AI", "Page", "model", "describe", "provider");
        Command("backup", "Back up library now", "Command · in Settings", "export", "save", "zip");
        Command("restore", "Restore from a backup…", "Command · adds the shortcuts, replaces nothing", "backup", "import");
        Command("import", "Import a shortcut file…", "Command · .autoportal", "open", "add");
        Command("deleted", "Recently deleted", "Command · bring a shortcut back", "trash", "undo", "restore");
        Command("keys", "Keyboard shortcuts", "Command · Ctrl+/ or F1", "keys", "help", "cheat sheet");
        foreach (var s in _host.Library.All.OrderBy(s => s.SortOrder))
            entries.Add(new Logic.PaletteEntry(Logic.PaletteKind.Shortcut, s.Name, "Shortcut · Enter runs, Ctrl+Enter edits", s.Id.ToString(), s.Alias));
        try
        {
            foreach (var section in _host.GallerySections())
                foreach (var g in section.Items)
                    entries.Add(new Logic.PaletteEntry(Logic.PaletteKind.Gallery, g.Shortcut.Name, "Gallery · ready-made shortcut", Path.GetFileName(g.FilePath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        Dialogs.CommandPaletteWindow.Open(this, entries, (entry, edit) => RunPaletteEntry(entry, edit));
    }

    private void RunPaletteEntry(Logic.PaletteEntry entry, bool edit)
    {
        switch (entry.Kind)
        {
            case Logic.PaletteKind.Shortcut when Guid.TryParse(entry.Key, out var id) && _host.Library.GetById(id) is { } s:
                if (edit) _host.EditorLauncher.Open(id); else _host.Runs.Toggle(s, RunSource.QuickRun);
                break;
            case Logic.PaletteKind.Gallery:
                NavigateTo(new NavTarget(AppPage.Gallery, Item: entry.Key));
                break;
            case Logic.PaletteKind.Command:
                switch (entry.Key)
                {
                    case "new": _host.EditorLauncher.OpenNew(_current.Page == AppPage.Folder ? _current.FolderId : null); break;
                    case "automation": _ = Dialogs.AutomationWizardWindow.ShowAsync(_host, this); break;
                    case "page:library": NavigateTo(new NavTarget(AppPage.Library)); break;
                    case "page:pinned": NavigateTo(new NavTarget(AppPage.Pinned)); break;
                    case "page:automations": NavigateTo(new NavTarget(AppPage.Automations)); break;
                    case "page:snippets": NavigateTo(new NavTarget(AppPage.Snippets)); break;
                    case "page:gallery": NavigateTo(new NavTarget(AppPage.Gallery)); break;
                    case "page:history": NavigateTo(new NavTarget(AppPage.History)); break;
                    case "settings": case "backup": NavigateTo(new NavTarget(AppPage.Settings)); break;
                    case "ai": NavigateTo(new NavTarget(AppPage.Settings, Item: "ai")); break;
                    case "restore": _ = Dialogs.ImportRestore.RestoreAsync(_host, this); break;
                    case "import": _ = Dialogs.ImportRestore.ImportFileAsync(_host, this); break;
                    case "deleted": _ = Dialogs.RecentlyDeletedDialog.ShowAsync(this, _host.LibraryStore); break;
                    case "keys": AutomationPortal.Editor.UI.KeySheet.Show(this); break;
                }
                break;
        }
    }

    // ---- closing ---------------------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_host.IsExiting) return;
        if (_host.Settings.Current.KeepRunningInTray)
        {
            e.Cancel = true;
            Hide();
            if (!_sentHideHint)
            {
                _sentHideHint = true;
                _host.Tray?.ShowBalloon("Automation Portal is still running", "Find it in the tray icons near the clock. Right-click it to quit.");
            }
        }
        else _host.Exit();
    }
}
