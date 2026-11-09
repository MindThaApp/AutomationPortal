using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.ViewModels;

internal abstract class PanelItem { }
internal sealed class PanelHeader : PanelItem { public required string Title { get; init; } }
internal sealed class PanelCategory : PanelItem
{
    public required string Name { get; init; }
    public required string ColorKey { get; init; }
    public required string Icon { get; init; }
    public int Count { get; init; }
    public bool Expanded { get; init; }
}
internal sealed partial class PanelAction : ObservableObject
{
    public PanelAction(ActionDescriptor descriptor, bool favourite) { Descriptor = descriptor; _isFavourite = favourite; }
    public ActionDescriptor Descriptor { get; }
    [ObservableProperty] private bool _isFavourite;
    public string ColorKey => Descriptor.Color;
}

/// <summary>Favourites and recents, backed by <see cref="AppSettings"/> when the host has settings, else kept in memory.</summary>
internal sealed class ActionPrefs
{
    private readonly AutomationPortalServices? _services;
    private readonly List<string> _favourites = new();
    private readonly List<string> _recents = new();

    public ActionPrefs(AutomationPortalServices? services) { _services = services; }

    private AppSettings? Settings { get { try { return _services?.Settings.Current; } catch { return null; } } }

    public IReadOnlyList<string> Favourites => Settings?.FavoriteActionIds ?? _favourites;
    public IReadOnlyList<string> Recents => Settings?.RecentActionIds ?? _recents;
    public bool ShowAdvanced => Settings?.ShowAdvancedActions ?? false;

    public void ToggleFavourite(string id)
    {
        void Apply(List<string> list) { if (!list.Remove(id)) list.Add(id); }
        if (Settings is not null) { try { _services!.Settings.Update(s => Apply(s.FavoriteActionIds)); return; } catch { } }
        Apply(_favourites);
    }

    public void AddRecent(string id)
    {
        void Apply(List<string> list) { list.Remove(id); list.Insert(0, id); if (list.Count > 12) list.RemoveRange(12, list.Count - 12); }
        if (Settings is not null) { try { _services!.Settings.Update(s => Apply(s.RecentActionIds)); return; } catch { } }
        Apply(_recents);
    }
}

/// <summary>The right-hand action panel: search, suggestions, favourites, recents, categories.</summary>
internal sealed partial class ActionPanelViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly ActionPrefs _prefs;
    private readonly HashSet<string> _expanded = new();
    private ContentKind? _previousKind;

    public ActionPanelViewModel(ActionRegistry registry, ActionPrefs prefs)
    {
        _registry = registry;
        _prefs = prefs;
        Rebuild();
    }

    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty] private string _query = "";

    partial void OnQueryChanged(string value) => Rebuild();

    public event Action<ActionDescriptor>? AddRequested;

    public void SetContext(ContentKind? previousOutputKind)
    {
        if (_previousKind == previousOutputKind) return;
        _previousKind = previousOutputKind;
        if (string.IsNullOrWhiteSpace(Query)) Rebuild();
    }

    public void RequestAdd(ActionDescriptor d)
    {
        _prefs.AddRecent(d.Id);
        AddRequested?.Invoke(d);
        if (string.IsNullOrWhiteSpace(Query)) Rebuild();
    }

    public void ToggleFavourite(PanelAction a)
    {
        _prefs.ToggleFavourite(a.Descriptor.Id);
        a.IsFavourite = _prefs.Favourites.Contains(a.Descriptor.Id);
        if (string.IsNullOrWhiteSpace(Query)) Rebuild();
    }

    public void ToggleCategory(PanelCategory c)
    {
        if (!_expanded.Remove(c.Name)) _expanded.Add(c.Name);
        Rebuild();
    }

    public void Refresh() => Rebuild();

    private PanelAction Wrap(ActionDescriptor d) => new(d, _prefs.Favourites.Contains(d.Id));

    private void Rebuild()
    {
        var list = new List<object>();
        var advanced = _prefs.ShowAdvanced;
        var q = Query.Trim();
        if (q.Length > 0)
        {
            var results = ActionSearch.Search(_registry, q, advanced, _prefs.Favourites.ToHashSet(), _prefs.Recents);
            list.Add(new PanelHeader { Title = results.Count == 0 ? "No actions match" : $"{results.Count} result{(results.Count == 1 ? "" : "s")}" });
            list.AddRange(results.Select(Wrap));
        }
        else
        {
            var suggestions = ActionSearch.Suggest(_registry, _previousKind, _prefs.Recents, _prefs.Favourites.ToHashSet(), advanced, 6);
            if (suggestions.Count > 0)
            {
                list.Add(new PanelHeader { Title = _previousKind is { } k && k != ContentKind.Any ? "Suggestions for what's next" : "Suggestions" });
                list.AddRange(suggestions.Select(Wrap));
            }
            var favs = _prefs.Favourites.Select(_registry.Describe).Where(d => d is not null && (advanced || !d.IsAdvanced)).Select(d => d!).ToList();
            if (favs.Count > 0)
            {
                list.Add(new PanelHeader { Title = "Favourites" });
                list.AddRange(favs.Select(Wrap));
            }
            var recents = _prefs.Recents.Select(_registry.Describe).Where(d => d is not null && (advanced || !d.IsAdvanced)).Select(d => d!).Take(5).ToList();
            if (recents.Count > 0)
            {
                list.Add(new PanelHeader { Title = "Recent" });
                list.AddRange(recents.Select(Wrap));
            }
            list.Add(new PanelHeader { Title = "All actions" });
            foreach (var cat in _registry.Categories(advanced))
            {
                var actions = _registry.ByCategory(cat, advanced);
                var expanded = _expanded.Contains(cat);
                list.Add(new PanelCategory { Name = cat, ColorKey = ActionCategories.ColorOf(cat), Icon = CategoryIcon(cat), Count = actions.Count, Expanded = expanded });
                if (expanded) list.AddRange(actions.Select(Wrap));
            }
        }
        SyncItems(list);
    }

    /// <summary>Replaces the collection content while keeping unchanged items in place, so the list doesn't flicker or jump.</summary>
    private void SyncItems(List<object> target)
    {
        static bool Same(object a, object b) => (a, b) switch
        {
            (PanelHeader x, PanelHeader y) => x.Title == y.Title,
            (PanelCategory x, PanelCategory y) => x.Name == y.Name && x.Expanded == y.Expanded && x.Count == y.Count,
            (PanelAction x, PanelAction y) => x.Descriptor.Id == y.Descriptor.Id && x.IsFavourite == y.IsFavourite,
            _ => false,
        };
        var i = 0;
        for (; i < target.Count; i++)
        {
            if (i < Items.Count && Same(Items[i], target[i])) continue;
            if (i < Items.Count) Items[i] = target[i]; else Items.Add(target[i]);
        }
        while (Items.Count > target.Count) Items.RemoveAt(Items.Count - 1);
    }

    public static string CategoryIcon(string category) => category switch
    {
        ActionCategories.Scripting => "Code24",
        ActionCategories.Variables => "BracesVariable24",
        ActionCategories.ShowAndAsk => "Chat24",
        ActionCategories.Text => "TextT24",
        ActionCategories.Numbers => "NumberSymbol24",
        ActionCategories.Dates => "Calendar24",
        ActionCategories.ListsAndDictionaries => "List24",
        ActionCategories.Web => "Globe24",
        ActionCategories.WeatherAndLocation => "WeatherSunny24",
        ActionCategories.Spreadsheets => "Table24",
        ActionCategories.FilesAndFolders => "Folder24",
        ActionCategories.Images => "Image24",
        ActionCategories.Pdf => "DocumentPdf24",
        ActionCategories.NotesAndReminders => "Note24",
        ActionCategories.EmailAndSharing => "Mail24",
        ActionCategories.Ai => "Sparkle24",
        ActionCategories.DeviceAndSettings => "Settings24",
        ActionCategories.AppsAndWindows => "Apps24",
        ActionCategories.KeyboardAndMouse => "Keyboard24",
        ActionCategories.Clipboard => "Clipboard24",
        ActionCategories.Screen => "Screenshot24",
        ActionCategories.SoundAndMedia => "MusicNote124",
        _ => "Wrench24",
    };
}
