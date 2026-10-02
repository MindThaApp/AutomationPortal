using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flowmate.App.Dialogs;
using Flowmate.App.Infrastructure;
using Flowmate.App.Logic;
using Flowmate.App.Pages;
using Flowmate.Core;
using Wpf.Ui.Controls;

namespace Flowmate.App;

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
        Nav.MenuItems.Add(new NavigationViewItemSeparator());
        Nav.MenuItems.Add(new NavigationViewItemHeader { Text = "Folders", Margin = new Thickness(14, 4, 0, 0) });
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
        Nav.MenuItems.Add(new NavigationViewItemSeparator());
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
        var icon = new Wpf.Ui.Controls.MenuItem { Header = "Change icon", Icon = new SymbolIcon(SymbolRegular.Emoji24) };
        icon.Click += async (_, _) => await ChangeFolderIconAsync(f.Id);
        var delete = new Wpf.Ui.Controls.MenuItem { Header = "Delete folder", Icon = new SymbolIcon(SymbolRegular.Delete24) };
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
        folder = FlowmateJson.Clone(folder);
        folder.Name = name;
        _host.Library.SaveFolder(folder);
    }

    private async Task ChangeFolderIconAsync(Guid id)
    {
        if (_host.Library.Folders.FirstOrDefault(f => f.Id == id) is not { } folder) return;
        var pick = await _host.EditorLauncher.PickIconAsync(this, folder.Icon, "blue");
        if (pick is null) return;
        folder = FlowmateJson.Clone(folder);
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
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
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
                _host.Tray?.ShowBalloon("Flowmate is still running", "Find it in the tray icons near the clock. Right-click it to quit.");
            }
        }
        else _host.Exit();
    }
}
