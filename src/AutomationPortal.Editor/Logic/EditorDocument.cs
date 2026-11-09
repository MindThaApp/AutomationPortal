using System.Text.Json;
using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

public enum DocChangeKind
{
    /// <summary>Actions were added, removed or moved: rebuild the row list.</summary>
    Structure,
    /// <summary>One parameter value changed.</summary>
    Parameter,
    /// <summary>Another property of one action changed (disabled, output name, error policy, menu label …).</summary>
    Item,
    /// <summary>Undo / redo replaced the actions.</summary>
    Reset,
    /// <summary>Name, icon, colour or other shortcut-level settings changed.</summary>
    Metadata,
}

public sealed record DocChange(DocChangeKind Kind, string? Uid = null, string? Key = null);

/// <summary>
/// The shortcut being edited plus every edit operation (insert / move / delete with group awareness, undo / redo).
/// No UI in here: the editor view model and the tests drive it directly.
/// </summary>
public sealed class EditorDocument
{
    private readonly ActionRegistry _registry;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string? _lastKey;
    private DateTime _lastCheckpoint;
    private const int MaxUndo = 200;

    public EditorDocument(Shortcut shortcut, ActionRegistry registry)
    {
        Shortcut = shortcut;
        _registry = registry;
    }

    public Shortcut Shortcut { get; }
    public List<ActionInstance> Actions => Shortcut.Actions;
    public int Revision { get; private set; }
    public event Action<DocChange>? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public int IndexOf(ActionInstance a)
    {
        var i = Actions.IndexOf(a);
        return i >= 0 ? i : Actions.FindIndex(x => x.Uid == a.Uid);
    }

    private void Raise(DocChange change) { Revision++; Changed?.Invoke(change); }

    // ---- undo / redo ---------------------------------------------------------------------------

    private string Snapshot() => JsonSerializer.Serialize(Actions, AutomationPortalJson.Options);

    private void Checkpoint(string? coalesceKey = null)
    {
        var now = DateTime.UtcNow;
        if (coalesceKey is not null && coalesceKey == _lastKey && _undo.Count > 0 && now - _lastCheckpoint < TimeSpan.FromSeconds(1.5))
        {
            _lastCheckpoint = now;
            _redo.Clear();
            return;
        }
        _undo.Add(Snapshot());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _lastKey = coalesceKey;
        _lastCheckpoint = now;
    }

    private void Restore(string snapshot)
    {
        var restored = JsonSerializer.Deserialize<List<ActionInstance>>(snapshot, AutomationPortalJson.Options) ?? new();
        Actions.Clear();
        Actions.AddRange(restored);
        _lastKey = null;
        Raise(new DocChange(DocChangeKind.Reset));
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Snapshot());
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(s);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Snapshot());
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(s);
        return true;
    }

    // ---- structure ---------------------------------------------------------------------------

    /// <summary>Adds a new action (or a whole If / Menu / Repeat group) at <paramref name="index"/> and returns the created instances.</summary>
    public IReadOnlyList<ActionInstance> InsertAction(ActionDescriptor descriptor, int index)
    {
        index = FlowGroups.ClampInsertIndex(Actions, index);
        var previous = index > 0 ? VariableCatalog.Available(Shortcut, _registry, index).FirstOrDefault(o => o.Group == VariableGroup.Actions) : null;
        var created = ActionFactory.Create(descriptor, previous);
        InsertInstances(created, index);
        return created;
    }

    /// <summary>Inserts ready-made instances (paste, duplicate, drop). Uids must already be unique.</summary>
    public void InsertInstances(IReadOnlyList<ActionInstance> items, int index)
    {
        if (items.Count == 0) return;
        Checkpoint();
        index = FlowGroups.ClampInsertIndex(Actions, index);
        Actions.InsertRange(index, items);
        Raise(new DocChange(DocChangeKind.Structure));
    }

    /// <summary>The given actions expanded to whole groups, in list order.</summary>
    public IReadOnlyList<ActionInstance> BlocksOf(IEnumerable<ActionInstance> items)
    {
        var ranges = FlowGroups.ExpandToBlocks(Actions, items.Select(IndexOf));
        return ranges.SelectMany(r => Actions.GetRange(r.Start, r.End - r.Start + 1)).ToList();
    }

    /// <summary>
    /// Deletes actions. A Start or End takes its whole group; an Otherwise / menu option takes its bar and the actions under it.
    /// Returns what was removed.
    /// </summary>
    public IReadOnlyList<ActionInstance> Delete(IEnumerable<ActionInstance> items)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var i in items.Select(IndexOf).Where(i => i >= 0).Distinct())
            ranges.Add(Actions[i].Flow is { Role: FlowRole.Middle } ? FlowGroups.BranchRange(Actions, i) : FlowGroups.GroupRange(Actions, i));
        if (ranges.Count == 0) return Array.Empty<ActionInstance>();
        ranges = ranges.OrderBy(r => r.Start).ThenByDescending(r => r.End).ToList();
        var merged = new List<(int Start, int End)>();
        foreach (var r in ranges)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End) { if (r.End > merged[^1].End) merged[^1] = (merged[^1].Start, r.End); continue; }
            merged.Add(r);
        }
        Checkpoint();
        var removed = new List<ActionInstance>();
        var menusToSync = new HashSet<string>();
        for (var m = merged.Count - 1; m >= 0; m--)
        {
            var (s, e) = merged[m];
            var slice = Actions.GetRange(s, e - s + 1);
            foreach (var a in slice)
                if (a.Flow is { Role: FlowRole.Middle } f && FlowIds.KindOf(a.ActionId) == FlowKind.Menu) menusToSync.Add(f.GroupId);
            Actions.RemoveRange(s, e - s + 1);
            removed.InsertRange(0, slice);
        }
        foreach (var g in menusToSync) SyncMenuItems(g);
        Raise(new DocChange(DocChangeKind.Structure));
        return removed;
    }

    /// <summary>
    /// Moves whole groups so they start at <paramref name="destIndex"/> (an insertion point in the current list).
    /// False when nothing moved (dropping onto itself, or inside the moved group).
    /// </summary>
    public bool Move(IEnumerable<ActionInstance> items, int destIndex)
    {
        var ranges = FlowGroups.ExpandToBlocks(Actions, items.Select(IndexOf));
        if (ranges.Count == 0) return false;
        destIndex = FlowGroups.ClampInsertIndex(Actions, destIndex);
        foreach (var (s, e) in ranges)
            if (destIndex > s && destIndex <= e) return false;
        if (ranges.Count == 1 && (destIndex == ranges[0].Start || destIndex == ranges[0].End + 1)) return false;

        Checkpoint();
        var moved = new List<ActionInstance>();
        var before = 0;
        foreach (var (s, e) in ranges)
        {
            moved.AddRange(Actions.GetRange(s, e - s + 1));
            if (e < destIndex) before += e - s + 1;
        }
        for (var m = ranges.Count - 1; m >= 0; m--) Actions.RemoveRange(ranges[m].Start, ranges[m].End - ranges[m].Start + 1);
        var at = FlowGroups.ClampInsertIndex(Actions, destIndex - before);
        Actions.InsertRange(at, moved);
        Raise(new DocChange(DocChangeKind.Structure));
        return true;
    }

    /// <summary>Moves the groups up or down by one sibling. Returns false at the edge.</summary>
    public bool MoveBy(IEnumerable<ActionInstance> items, int direction)
    {
        var ranges = FlowGroups.ExpandToBlocks(Actions, items.Select(IndexOf));
        if (ranges.Count == 0) return false;
        if (direction < 0)
        {
            var s = ranges[0].Start;
            if (s == 0) return false;
            var prevStart = FlowGroups.GroupRange(Actions, s - 1).Start;
            if (Actions[s - 1].Flow is { Role: not FlowRole.End } and not null) return false;
            return Move(items, prevStart);
        }
        var e = ranges[^1].End;
        if (e >= Actions.Count - 1) return false;
        if (Actions[e + 1].Flow is { Role: not FlowRole.Start } and not null) return false;
        var nextEnd = FlowGroups.GroupRange(Actions, e + 1).End;
        return Move(items, nextEnd + 1);
    }

    /// <summary>Copies whole groups right below the originals (fresh uids, internal references follow). Returns the copies.</summary>
    public IReadOnlyList<ActionInstance> Duplicate(IEnumerable<ActionInstance> items)
    {
        var blocks = BlocksOf(items);
        if (blocks.Count == 0) return Array.Empty<ActionInstance>();
        var copies = ActionClipboard.Remap(blocks);
        var last = blocks.Max(IndexOf);
        InsertInstances(copies, last + 1);
        return copies;
    }

    // ---- single-action edits -----------------------------------------------------------------

    public void SetParameter(ActionInstance action, string key, ParamValue? value)
    {
        Checkpoint($"p:{action.Uid}:{key}");
        if (value is null) action.Parameters.Remove(key); else action.Parameters[key] = value;
        Raise(new DocChange(DocChangeKind.Parameter, action.Uid, key));
    }

    public void SetDisabled(ActionInstance action, bool disabled)
    {
        Checkpoint();
        action.Disabled = disabled;
        Raise(new DocChange(DocChangeKind.Item, action.Uid));
    }

    public void SetOutputName(ActionInstance action, string? name)
    {
        Checkpoint($"o:{action.Uid}");
        action.OutputName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Raise(new DocChange(DocChangeKind.Item, action.Uid, "outputName"));
    }

    public void SetOnError(ActionInstance action, ErrorMode mode, int retries = 2, double delaySeconds = 1)
    {
        Checkpoint();
        action.OnError = new ErrorPolicy { Mode = mode, Retries = retries, RetryDelaySeconds = delaySeconds };
        Raise(new DocChange(DocChangeKind.Item, action.Uid));
    }

    public void SetDisabled(IEnumerable<ActionInstance> actions, bool disabled)
    {
        Checkpoint();
        foreach (var a in BlocksOf(actions)) a.Disabled = disabled;
        Raise(new DocChange(DocChangeKind.Item, null, "bulk"));
    }

    // ---- menus & otherwise -------------------------------------------------------------------

    public void SetMenuLabel(ActionInstance middle, string label)
    {
        if (middle.Flow is null) return;
        Checkpoint($"l:{middle.Uid}");
        middle.Flow.Label = label;
        SyncMenuItems(middle.Flow.GroupId);
        Raise(new DocChange(DocChangeKind.Item, middle.Uid, "label"));
    }

    public ActionInstance? AddMenuItem(string groupId)
    {
        var start = Actions.FindIndex(a => a.Flow is { Role: FlowRole.Start } f && f.GroupId == groupId);
        if (start < 0) return null;
        var (_, end) = FlowGroups.GroupRange(Actions, start);
        Checkpoint();
        var count = Actions.Count(a => a.Flow is { Role: FlowRole.Middle } f && f.GroupId == groupId);
        var bar = new ActionInstance { ActionId = Actions[start].ActionId }.WithFlow(groupId, FlowRole.Middle, $"Option {count + 1}");
        Actions.Insert(end, bar);
        SyncMenuItems(groupId);
        Raise(new DocChange(DocChangeKind.Structure));
        return bar;
    }

    /// <summary>Adds the Otherwise bar to an If that doesn't have one.</summary>
    public ActionInstance? AddOtherwise(string groupId)
    {
        var start = Actions.FindIndex(a => a.Flow is { Role: FlowRole.Start } f && f.GroupId == groupId);
        if (start < 0) return null;
        if (Actions.Any(a => a.Flow is { Role: FlowRole.Middle } f && f.GroupId == groupId)) return null;
        var (_, end) = FlowGroups.GroupRange(Actions, start);
        Checkpoint();
        var bar = new ActionInstance { ActionId = Actions[start].ActionId }.WithFlow(groupId, FlowRole.Middle);
        Actions.Insert(end, bar);
        Raise(new DocChange(DocChangeKind.Structure));
        return bar;
    }

    /// <summary>Keeps the menu's "items" list equal to its option bars (the engine reads the bars; the list is for display and import).</summary>
    private void SyncMenuItems(string groupId)
    {
        var start = Actions.FirstOrDefault(a => a.Flow is { Role: FlowRole.Start } f && f.GroupId == groupId);
        if (start is null || FlowIds.KindOf(start.ActionId) != FlowKind.Menu) return;
        var labels = Actions.Where(a => a.Flow is { Role: FlowRole.Middle } f && f.GroupId == groupId).Select(a => a.Flow!.Label ?? "").ToArray();
        start.Parameters["items"] = ParamValue.List(labels);
    }

    /// <summary>Replaces every action and forgets the undo history (an external change was loaded).</summary>
    public void ReplaceAll(IEnumerable<ActionInstance> actions)
    {
        var list = actions.ToList();
        Actions.Clear();
        Actions.AddRange(list);
        _undo.Clear();
        _redo.Clear();
        _lastKey = null;
        Raise(new DocChange(DocChangeKind.Reset));
    }

    /// <summary>Replaces every action as one undoable step ("Describe it" drafts).</summary>
    public void ReplaceActionsUndoable(IEnumerable<ActionInstance> actions)
    {
        var list = actions.ToList();
        Checkpoint();
        Actions.Clear();
        Actions.AddRange(list);
        Raise(new DocChange(DocChangeKind.Reset));
    }

    // ---- shortcut-level ----------------------------------------------------------------------

    public void NotifyMetadataChanged() => Raise(new DocChange(DocChangeKind.Metadata));
}
