using System.Text.Json;

namespace Flowmate.Core;

/// <summary><c>.flowmate</c> file format: {"format":"flowmate.shortcut","version":1,"shortcut":{…}}.</summary>
public static class ShortcutPackage
{
    public const string Format = "flowmate.shortcut";
    public const int Version = 1;

    public static string Serialize(Shortcut shortcut)
    {
        var obj = new Dictionary<string, object> { ["format"] = Format, ["version"] = Version, ["shortcut"] = shortcut };
        return FlowmateJson.Serialize(obj, indented: true);
    }

    public static Shortcut Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new LibraryException("This file isn't a Flowmate shortcut.");
            if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver) && ver > Version)
                throw new LibraryException("This shortcut was made with a newer version of Flowmate. Update Flowmate to open it.");
            return root.GetProperty("shortcut").Deserialize<Shortcut>(FlowmateJson.Options)
                   ?? throw new LibraryException("This shortcut file is empty.");
        }
        catch (JsonException ex) { throw new LibraryException("This file is damaged and can't be opened as a Flowmate shortcut.", ex); }
        catch (KeyNotFoundException ex) { throw new LibraryException("This file isn't a Flowmate shortcut.", ex); }
    }

    /// <summary>Destructive, advanced and unknown actions in the shortcut.</summary>
    public static IReadOnlyList<ImportWarning> Inspect(Shortcut shortcut, ActionRegistry? registry)
    {
        var list = new List<ImportWarning>();
        if (registry is null) return list;
        foreach (var a in shortcut.Actions)
        {
            if (a.Flow is { Role: not FlowRole.Start }) continue;
            var d = registry.Describe(a.ActionId);
            if (d is null) list.Add(new(a.Uid, a.ActionId, a.ActionId, ImportWarningKind.Unknown));
            else if (d.IsDestructiveFor(a)) list.Add(new(a.Uid, a.ActionId, d.Title, ImportWarningKind.Destructive));
            else if (d.IsAdvanced) list.Add(new(a.Uid, a.ActionId, d.Title, ImportWarningKind.Advanced));
        }
        return list;
    }
}

/// <summary>In-memory library logic shared by the JSON store and test fakes. Subclasses persist via the protected hooks.</summary>
public abstract class ShortcutLibraryBase : IShortcutLibrary
{
    protected readonly object Gate = new();
    protected Dictionary<Guid, Shortcut> ShortcutMap = new();
    protected List<ShortcutFolder> FolderList = new();
    protected List<Automation> AutomationList = new();
    protected List<Snippet> SnippetList = new();
    protected readonly ActionRegistry? Registry;

    protected ShortcutLibraryBase(ActionRegistry? registry) { Registry = registry; }

    public event EventHandler<LibraryChangedEventArgs>? Changed;
    protected void RaiseChanged(LibraryChange what, bool external = false) => Changed?.Invoke(this, new LibraryChangedEventArgs(what, external));

    protected virtual void PersistShortcut(Shortcut s) { }
    protected virtual void PersistShortcutDeleted(Guid id) { }
    protected virtual void PersistFolders() { }
    protected virtual void PersistAutomations() { }
    protected virtual void PersistSnippets() { }

    public IReadOnlyList<Shortcut> All { get { lock (Gate) return ShortcutMap.Values.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); } }
    public IReadOnlyList<ShortcutFolder> Folders { get { lock (Gate) return FolderList.OrderBy(f => f.SortOrder).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); } }
    public IReadOnlyList<Automation> Automations { get { lock (Gate) return AutomationList.ToList(); } }
    public IReadOnlyList<Snippet> Snippets { get { lock (Gate) return SnippetList.ToList(); } }

    public Shortcut? GetById(Guid id) { lock (Gate) return ShortcutMap.TryGetValue(id, out var s) ? s : null; }

    public Shortcut? FindByName(string name)
    {
        lock (Gate) return ShortcutMap.Values.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public bool IsNameTaken(string name, Guid? exceptId = null)
    {
        lock (Gate) return ShortcutMap.Values.Any(s => s.Id != exceptId && string.Equals(s.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public void Save(Shortcut shortcut)
    {
        lock (Gate) SaveLocked(shortcut);
        RaiseChanged(LibraryChange.Shortcuts);
    }

    public void SaveMany(IEnumerable<Shortcut> shortcuts)
    {
        var any = false;
        lock (Gate)
            foreach (var s in shortcuts) { SaveLocked(s); any = true; }
        if (any) RaiseChanged(LibraryChange.Shortcuts);
    }

    private void SaveLocked(Shortcut shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Name)) throw new LibraryException("Give the shortcut a name first.");
        if (IsNameTaken(shortcut.Name, shortcut.Id))
            throw new LibraryException($"A shortcut named \"{shortcut.Name.Trim()}\" already exists. Choose a different name.");
        shortcut.Name = shortcut.Name.Trim();
        shortcut.ModifiedAt = DateTimeOffset.Now;
        var copy = shortcut.Clone();
        ShortcutMap.TryGetValue(copy.Id, out var previous);
        ShortcutMap[copy.Id] = copy;
        try { PersistShortcut(copy); }
        catch
        {
            if (previous is null) ShortcutMap.Remove(copy.Id); else ShortcutMap[copy.Id] = previous;
            throw;
        }
    }

    public void Delete(Guid id)
    {
        lock (Gate)
        {
            if (!ShortcutMap.Remove(id, out var old)) return;
            try { PersistShortcutDeleted(id); }
            catch { ShortcutMap[id] = old; throw; }
        }
        RaiseChanged(LibraryChange.Shortcuts);
    }

    public void SaveFolder(ShortcutFolder folder)
    {
        lock (Gate)
        {
            var copy = FlowmateJson.Clone(folder);
            var before = FolderList.ToList();
            var i = FolderList.FindIndex(f => f.Id == copy.Id);
            if (i >= 0) FolderList[i] = copy; else FolderList.Add(copy);
            try { PersistFolders(); } catch { FolderList = before; throw; }
        }
        RaiseChanged(LibraryChange.Folders);
    }

    public void DeleteFolder(Guid id)
    {
        var moved = new List<Shortcut>();
        lock (Gate)
        {
            var before = FolderList.ToList();
            if (FolderList.RemoveAll(f => f.Id == id) == 0) return;
            try { PersistFolders(); } catch { FolderList = before; throw; }
            foreach (var s in ShortcutMap.Values.Where(s => s.FolderId == id)) { s.FolderId = null; moved.Add(s); }
            foreach (var s in moved) PersistShortcut(s);
        }
        RaiseChanged(LibraryChange.Folders | LibraryChange.Shortcuts);
    }

    public void SaveAutomation(Automation automation)
    {
        lock (Gate)
        {
            var copy = automation.Clone();
            var before = AutomationList.ToList();
            var i = AutomationList.FindIndex(a => a.Id == copy.Id);
            if (i >= 0) AutomationList[i] = copy; else AutomationList.Add(copy);
            try { PersistAutomations(); } catch { AutomationList = before; throw; }
        }
        RaiseChanged(LibraryChange.Automations);
    }

    public void DeleteAutomation(Guid id)
    {
        lock (Gate)
        {
            var before = AutomationList.ToList();
            if (AutomationList.RemoveAll(a => a.Id == id) == 0) return;
            try { PersistAutomations(); } catch { AutomationList = before; throw; }
        }
        RaiseChanged(LibraryChange.Automations);
    }

    public void SaveSnippet(Snippet snippet)
    {
        lock (Gate)
        {
            var copy = snippet.Clone();
            var before = SnippetList.ToList();
            var i = SnippetList.FindIndex(s => s.Id == copy.Id);
            if (i >= 0) SnippetList[i] = copy; else SnippetList.Add(copy);
            try { PersistSnippets(); } catch { SnippetList = before; throw; }
        }
        RaiseChanged(LibraryChange.Snippets);
    }

    public void DeleteSnippet(Guid id)
    {
        lock (Gate)
        {
            var before = SnippetList.ToList();
            if (SnippetList.RemoveAll(s => s.Id == id) == 0) return;
            try { PersistSnippets(); } catch { SnippetList = before; throw; }
        }
        RaiseChanged(LibraryChange.Snippets);
    }

    public ImportResult Import(Shortcut shortcut)
    {
        var copy = shortcut.Clone();
        string? renamedFrom = null;
        // Imported shortcuts are inert until the user reviews them: no global hotkey, Quick Run alias or Send To entry.
        var pendingHotkey = copy.Hotkey; var pendingAlias = copy.Alias; var pendingSendTo = copy.ShowInSendTo;
        copy.Hotkey = null; copy.Alias = null; copy.ShowInSendTo = false;
        lock (Gate)
        {
            copy.Id = Guid.NewGuid();
            copy.CreatedAt = DateTimeOffset.Now;
            var baseName = string.IsNullOrWhiteSpace(copy.Name) ? "Imported Shortcut" : copy.Name.Trim();
            var name = baseName;
            for (var n = 2; IsNameTaken(name); n++) name = $"{baseName} {n}";
            if (name != baseName) renamedFrom = baseName;
            copy.Name = name;
            if (copy.FolderId is { } fid && !FolderList.Any(f => f.Id == fid)) copy.FolderId = null;
            copy.SortOrder = ShortcutMap.Count == 0 ? 0 : ShortcutMap.Values.Max(s => s.SortOrder) + 1;
        }
        Save(copy);
        return new ImportResult { Shortcut = GetById(copy.Id)!, RenamedFrom = renamedFrom, Warnings = ShortcutPackage.Inspect(copy, Registry), PendingHotkey = pendingHotkey, PendingAlias = pendingAlias, PendingShowInSendTo = pendingSendTo };
    }

    public ImportResult ImportJson(string flowmateJson) => Import(ShortcutPackage.Parse(flowmateJson));

    public string Export(Guid id)
    {
        var s = GetById(id) ?? throw new LibraryException("That shortcut doesn't exist any more.");
        return ShortcutPackage.Serialize(s);
    }
}
