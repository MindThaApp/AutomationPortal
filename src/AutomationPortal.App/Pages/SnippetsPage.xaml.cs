using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Pages;

public sealed partial class SnippetRow : ObservableObject
{
    public SnippetRow(Snippet s) { Snippet = s; Refresh(s); }

    public Snippet Snippet { get; private set; }
    public Guid Id => Snippet.Id;
    [ObservableProperty] private string _abbreviation = "";
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string? _folder;
    [ObservableProperty] private bool _enabled;

    /// <summary>What a screen reader announces for the list entry ("gm, expands to Good morning, on").</summary>
    public override string ToString() => $"{Abbreviation}, expands to {Preview}{(Enabled ? "" : ", turned off")}";

    public Visibility FolderVisibility => string.IsNullOrWhiteSpace(Folder) ? Visibility.Collapsed : Visibility.Visible;

    public Action<SnippetRow>? EnabledChangedByUser { get; set; }
    private bool _refreshing;

    public void Refresh(Snippet s)
    {
        _refreshing = true;
        Snippet = s;
        Abbreviation = s.Abbreviation.Length == 0 ? "(no short code yet)" : s.Abbreviation;
        var oneLine = s.Text.Replace("\r", " ").Replace("\n", " ");
        Preview = oneLine.Length == 0 ? "(empty)" : oneLine.Length > 70 ? oneLine[..70] + "…" : oneLine;
        Folder = s.Folder;
        Enabled = s.Enabled;
        OnPropertyChanged(nameof(FolderVisibility));
        _refreshing = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_refreshing && value != Snippet.Enabled) EnabledChangedByUser?.Invoke(this);
    }
}

public partial class SnippetsPage : UserControl, IAppPage
{
    private const string AllFolders = "All snippets";
    private const string NoFolder = "No folder";

    private readonly AppHost _host;
    private readonly ObservableCollection<SnippetRow> _rows = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Snippet? _editing;
    private bool _loading;
    private bool _reloadQueued;
    private Guid? _selectAfterReload;
    private bool _selfSaving;

    public SnippetsPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        SnippetList.ItemsSource = _rows;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveEditing(); };
        _host.Library.Changed += (_, e) =>
        {
            if (_selfSaving || !e.What.HasFlag(LibraryChange.Snippets) || _reloadQueued) return;
            _reloadQueued = true;
            Dispatcher.BeginInvoke(() => { _reloadQueued = false; if (!_saveTimer.IsEnabled) Reload(); });
        };
    }

    public void Navigated(NavTarget target) => Reload();

    // ---- list ------------------------------------------------------------------------------

    private void Reload()
    {
        var all = _host.Library.Snippets;
        var folders = all.Select(s => s.Folder).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(f => f).ToList();

        _loading = true;
        var chosen = FolderFilter.SelectedItem as string ?? AllFolders;
        var filterItems = new List<string> { AllFolders };
        if (folders.Count > 0) { filterItems.AddRange(folders!); filterItems.Add(NoFolder); }
        FolderFilter.ItemsSource = filterItems;
        FolderFilter.SelectedItem = filterItems.Contains(chosen) ? chosen : AllFolders;
        FolderFilter.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderBox.ItemsSource = folders;

        var query = SearchBox.Text.Trim();
        var folderFilter = FolderFilter.SelectedItem as string ?? AllFolders;
        IEnumerable<Snippet> list = all;
        if (folderFilter == NoFolder) list = list.Where(s => string.IsNullOrWhiteSpace(s.Folder));
        else if (folderFilter != AllFolders) list = list.Where(s => string.Equals(s.Folder, folderFilter, StringComparison.CurrentCultureIgnoreCase));
        if (query.Length > 0)
            list = list.Where(s => s.Abbreviation.Contains(query, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        var ordered = list.OrderBy(s => s.Abbreviation, StringComparer.CurrentCultureIgnoreCase).ToList();

        var keep = _selectAfterReload ?? (SnippetList.SelectedItem as SnippetRow)?.Id;
        _selectAfterReload = null;
        _rows.Clear();
        foreach (var s in ordered)
        {
            var row = new SnippetRow(s) { EnabledChangedByUser = OnRowEnabledChanged };
            _rows.Add(row);
        }
        _loading = false;

        var empty = all.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        MainGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        SnippetList.SelectedItem = _rows.FirstOrDefault(r => r.Id == keep) ?? _rows.FirstOrDefault();
        if (SnippetList.SelectedItem is null) ShowEditor(null);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) { if (!_loading) { FlushSave(); Reload(); } }
    private void OnFolderFilterChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) { FlushSave(); Reload(); } }

    private void OnRowEnabledChanged(SnippetRow row)
    {
        var copy = row.Snippet.Clone();
        copy.Enabled = row.Enabled;
        _selfSaving = true;
        try { _host.Library.SaveSnippet(copy); } finally { _selfSaving = false; }
        if (_editing?.Id == copy.Id) _editing = copy;
    }

    private void OnSnippetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        FlushSave();
        ShowEditor((SnippetList.SelectedItem as SnippetRow)?.Snippet);
    }

    // ---- new / delete ----------------------------------------------------------------------

    private void OnAddStarters(object sender, RoutedEventArgs e)
    {
        FlushSave();
        var added = StarterSnippets.Missing(_host.Library.Snippets);
        if (added.Count == 0) { OnNewSnippet(sender, e); return; }
        _selectAfterReload = added[0].Id;
        foreach (var s in added) _host.Library.SaveSnippet(s);
        Reload();
    }

    private void OnNewSnippet(object sender, RoutedEventArgs e)
    {
        FlushSave();
        var n = 1;
        var abbr = ";snip";
        while (_host.Library.Snippets.Any(s => s.Abbreviation.Equals(abbr, StringComparison.CurrentCultureIgnoreCase))) abbr = ";snip" + ++n;
        var snippet = new Snippet { Abbreviation = abbr, Text = "", Enabled = true };
        _selectAfterReload = snippet.Id;
        SearchBox.Text = "";
        _host.Library.SaveSnippet(snippet);
        Reload();
        AbbrBox.Focus();
        AbbrBox.SelectAll();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } s) return;
        if (!await Prompts.ConfirmAsync(Window.GetWindow(this), Strings.Get("SnippetsPage_DeleteSnippet"), $"Delete the snippet \"{s.Abbreviation}\"?", "Delete", danger: true)) return;
        _saveTimer.Stop();
        _editing = null;
        _host.Library.DeleteSnippet(s.Id);
        Reload();
    }

    // ---- editor ----------------------------------------------------------------------------

    private void ShowEditor(Snippet? s)
    {
        _loading = true;
        _editing = s?.Clone();
        EditorPanel.IsEnabled = s is not null;
        EditorPanel.Opacity = s is null ? 0.4 : 1;
        AbbrBox.Text = s?.Abbreviation ?? "";
        BodyBox.Text = s?.Text ?? "";
        FolderBox.Text = s?.Folder ?? "";
        MatchCaseToggle.IsChecked = s?.MatchCase == true;
        AbbrError.Visibility = Visibility.Collapsed;
        _loading = false;
        UpdatePreview();
    }

    private void OnEditorChanged(object sender, RoutedEventArgs e) => EditorEdited();
    private void OnEditorChangedSel(object sender, SelectionChangedEventArgs e) => EditorEdited();
    private void OnEditorToggle(object sender, RoutedEventArgs e) => EditorEdited();

    private void EditorEdited()
    {
        if (_loading || _editing is null) return;
        UpdatePreview();
        var abbr = AbbrBox.Text.Trim();
        string? problem = null;
        if (abbr.Length == 0) problem = "Type the short code you want to use, like ;sig.";
        else if (abbr.Any(char.IsWhiteSpace)) problem = "A short code can't contain spaces.";
        else if (_host.Library.Snippets.Any(x => x.Id != _editing.Id && x.Abbreviation.Equals(abbr, StringComparison.CurrentCultureIgnoreCase)))
            problem = $"You already have a snippet with the short code {abbr}. Pick a different one.";
        AbbrError.Text = problem ?? "";
        AbbrError.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (problem is not null) { _saveTimer.Stop(); return; }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        if (_saveTimer.IsEnabled) { _saveTimer.Stop(); SaveEditing(); }
    }

    private void SaveEditing()
    {
        if (_editing is not { } s || AbbrError.Visibility == Visibility.Visible) return;
        s.Abbreviation = AbbrBox.Text.Trim();
        s.Text = BodyBox.Text;
        s.MatchCase = MatchCaseToggle.IsChecked == true;
        s.Folder = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text.Trim();
        _selfSaving = true;
        try { _host.Library.SaveSnippet(s); } finally { _selfSaving = false; }
        var row = _rows.FirstOrDefault(r => r.Id == s.Id);
        row?.Refresh(s.Clone());
    }

    private void UpdatePreview()
    {
        var text = BodyBox.Text;
        if (text.Length == 0) { PreviewText.Text = Strings.Get("SnippetsPage_YourTextWillShow"); return; }
        var fillIns = SnippetExpander.FillInLabels(text).ToDictionary(l => l, l => $"‹{l}›");
        var (result, cursor) = SnippetExpander.Expand(text, DateTimeOffset.Now, "(whatever you copied last)", fillIns);
        if (cursor is { } fromEnd) result = result.Insert(result.Length - fromEnd, "▌");
        PreviewText.Text = result;
    }

    // ---- placeholders ----------------------------------------------------------------------

    private void InsertAtCaret(string text)
    {
        var start = BodyBox.SelectionStart;
        BodyBox.SelectedText = text;
        BodyBox.SelectionStart = start + text.Length;
        BodyBox.SelectionLength = 0;
        BodyBox.Focus();
    }

    private void OnPlaceholder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string token }) InsertAtCaret(token);
    }

    private async void OnDateFormat(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var format = await Prompts.AskTextAsync(owner, Strings.Get("SnippetsPage_DateFormat"), Strings.Get("SnippetsPage_FormatForExampleYyyy"), "yyyy-MM-dd", "Insert");
        if (format is not null) InsertAtCaret("{date:" + format + "}");
    }

    private async void OnFillIn(object sender, RoutedEventArgs e)
    {
        var label = await Prompts.AskTextAsync(Window.GetWindow(this), Strings.Get("SnippetsPage_AskMe"), "What should Automation Portal ask you? (for example: Name)", "", "Insert");
        if (label is not null) InsertAtCaret("{input:" + label + "}");
    }
}
