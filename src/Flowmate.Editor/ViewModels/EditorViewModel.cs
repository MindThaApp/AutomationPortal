using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Flowmate.Core;
using Flowmate.Editor.Logic;
using Flowmate.Editor.UI;

namespace Flowmate.Editor.ViewModels;

internal sealed record InspectorItem(string Name, string Icon, string ColorKey, string Kind, string Preview, ContentValue Value);

/// <summary>State and operations of one editor window (everything except drawing).</summary>
internal sealed partial class EditorViewModel : ObservableObject
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _validateTimer;
    private readonly DispatcherTimer _runTimer;
    private readonly Dictionary<ActionInstance, ActionRowViewModel> _rowByAction = new();
    private readonly Dictionary<string, ActionRowViewModel> _rowByUid = new();
    private readonly HashSet<string> _pendingStepUids = new();
    private readonly object _pendingGate = new();
    private bool _varsDirty;
    private bool _dirty;
    private bool _saving;
    private bool _existsInLibrary;

    public EditorViewModel(EditorContext context, Shortcut shortcut, bool isNew)
    {
        Context = context;
        _ui = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _existsInLibrary = !isNew;
        IsNew = isNew;
        if (isNew && context.Library.IsNameTaken(shortcut.Name, shortcut.Id)) shortcut.Name = UniqueName(shortcut.Name);
        Doc = new EditorDocument(shortcut, context.Actions);
        Env = new EditorEnv(context.Services, context.Actions, shortcut, withVariables: true, Doc);
        Prefs = new ActionPrefs(context.Services);
        Panel = new ActionPanelViewModel(context.Actions, Prefs);
        Panel.AddRequested += d => AddFromPanel(d);
        Run = new RunController();

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _validateTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(180) };
        _validateTimer.Tick += (_, _) => { _validateTimer.Stop(); Revalidate(); };
        _runTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(70) };
        _runTimer.Tick += (_, _) => FlushRunUpdates();

        Doc.Changed += OnDocChanged;
        Run.StepChanged += uid => { lock (_pendingGate) _pendingStepUids.Add(uid); };
        Run.VariablesChanged += () => _varsDirty = true;
        Run.RunStateChanged += () => _ui.BeginInvoke(new System.Action(OnRunStateChanged));
        context.Library.Changed += OnLibraryChanged;

        Reconcile();
        Revalidate();
    }

    public EditorContext Context { get; }
    public EditorDocument Doc { get; }
    public EditorEnv Env { get; }
    public ActionPrefs Prefs { get; }
    public ActionPanelViewModel Panel { get; }
    public RunController Run { get; }
    public bool IsNew { get; }
    public Shortcut Shortcut => Doc.Shortcut;
    public ObservableCollection<ActionRowViewModel> Rows { get; } = new();
    public ObservableCollection<InspectorItem> Inspector { get; } = new();

    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private RunResult? _lastResult;

    public event System.Action? MetadataChanged;
    public event System.Action? SelectionChanged;
    public event System.Action? DeletedExternally;

    public string Name => Shortcut.Name;
    public string Icon => Shortcut.Icon;
    public string Color => Shortcut.Color;

    private string UniqueName(string name)
    {
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (!Context.Library.IsNameTaken(candidate, Shortcut.Id)) return candidate;
        }
    }

    // ---- rows ----------------------------------------------------------------------------------

    public IEnumerable<ActionRowViewModel> SelectedRows => Rows.Where(r => r.IsSelected);
    public IReadOnlyList<ActionInstance> SelectedActions => Rows.Where(r => r.IsSelected).Select(r => r.Action).ToList();
    public ActionRowViewModel? FindRow(string uid) => _rowByUid.TryGetValue(uid, out var r) ? r : null;

    private void Reconcile(IReadOnlyCollection<string>? keepSelected = null)
    {
        var selectedUids = keepSelected ?? SelectedRows.Select(r => r.Uid).ToList();
        var desired = new List<ActionRowViewModel>(Doc.Actions.Count);
        var newMap = new Dictionary<ActionInstance, ActionRowViewModel>();
        foreach (var a in Doc.Actions)
        {
            if (!_rowByAction.TryGetValue(a, out var row))
            {
                row = new ActionRowViewModel(a, Context.Actions.Describe(a.ActionId), this);
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ActionRowViewModel.IsSelected)) OnRowSelectionChanged(); };
            }
            newMap[a] = row;
            desired.Add(row);
        }
        _rowByAction.Clear();
        foreach (var kv in newMap) _rowByAction[kv.Key] = kv.Value;
        _rowByUid.Clear();
        foreach (var r in desired) _rowByUid[r.Uid] = r;

        SyncRows(desired);
        var depths = FlowGroups.Depths(Doc.Actions);
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Depth = depths[i];
            Rows[i].HasOtherwise = false;
        }
        var selected = selectedUids.ToHashSet();
        foreach (var r in Rows) r.IsSelected = selected.Contains(r.Uid);
        foreach (var r in Rows)
            if (r.Role == FlowRole.Start && r.FlowKind == FlowKind.If)
                r.HasOtherwise = Doc.Actions.Any(a => a.Flow is { Role: FlowRole.Middle } f && f.GroupId == r.Action.Flow!.GroupId);
        foreach (var r in Rows)
            if (Run.StepOf(r.Uid) is { } info) r.SetRunState(info);
            else if (r.State != StepState.Idle) r.SetRunState(null);
        foreach (var r in Rows) r.RaiseRefresh();
        UpdatePanelContext();
    }

    private void SyncRows(List<ActionRowViewModel> desired)
    {
        var keep = desired.ToHashSet();
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], desired[i])) continue;
            var existing = -1;
            for (var j = i + 1; j < Rows.Count; j++) if (ReferenceEquals(Rows[j], desired[i])) { existing = j; break; }
            if (existing >= 0) Rows.Move(existing, i);
            else Rows.Insert(i, desired[i]);
        }
    }

    private void OnRowSelectionChanged()
    {
        UpdatePanelContext();
        SelectionChanged?.Invoke();
    }

    public void SelectActions(IEnumerable<ActionInstance> actions)
    {
        var set = actions.ToHashSet();
        foreach (var r in Rows) r.IsSelected = set.Contains(r.Action);
    }

    private void UpdatePanelContext()
    {
        var sel = Rows.Where(r => r.IsSelected).Select(r => Doc.IndexOf(r.Action)).DefaultIfEmpty(-1).Max();
        var index = sel >= 0 ? sel + 1 : Doc.Actions.Count;
        Panel.SetContext(OutputKindAbove(index));
    }

    /// <summary>The output kind of the nearest action above <paramref name="index"/> that produces one.</summary>
    public ContentKind? OutputKindAbove(int index)
    {
        for (var i = Math.Min(index, Doc.Actions.Count) - 1; i >= 0; i--)
        {
            var a = Doc.Actions[i];
            if (a.Disabled || a.Flow is { Role: not FlowRole.End }) continue;
            var d = Context.Actions.Describe(a.ActionId);
            if (d is { HasOutput: true }) return d.OutputKind == ContentKind.Any ? null : d.OutputKind;
        }
        return null;
    }

    // ---- document events -----------------------------------------------------------------------

    private void OnDocChanged(DocChange change)
    {
        switch (change.Kind)
        {
            case DocChangeKind.Structure:
                Reconcile();
                break;
            case DocChangeKind.Reset:
                Reconcile();
                break;
            case DocChangeKind.Parameter:
                if (change.Uid is { } pu && _rowByUid.TryGetValue(pu, out var pr)) pr.RaiseRefresh();
                break;
            case DocChangeKind.Item:
                if (change.Key is "outputName" or "bulk") foreach (var r in Rows) r.RaiseRefresh();
                else if (change.Uid is { } iu && _rowByUid.TryGetValue(iu, out var ir)) ir.RaiseRefresh();
                break;
            case DocChangeKind.Metadata:
                MetadataChanged?.Invoke();
                break;
        }
        MarkDirty();
        _validateTimer.Stop();
        _validateTimer.Start();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool CanUndo => Doc.CanUndo;
    public bool CanRedo => Doc.CanRedo;
    public bool IsEmpty => Doc.Actions.Count == 0;

    private void Revalidate()
    {
        var issues = ShortcutValidator.Validate(Doc.Shortcut, Context.Actions);
        var byUid = issues.Where(i => i.ActionUid is not null).GroupBy(i => i.ActionUid!).ToDictionary(g => g.Key, g => (IReadOnlyList<ValidationIssue>)g.ToList());
        foreach (var r in Rows)
        {
            var next = byUid.TryGetValue(r.Uid, out var list) ? list : Array.Empty<ValidationIssue>();
            if (!next.SequenceEqual(r.Issues)) r.Issues = next;
        }
        IssueCount = issues.Count(i => i.Severity == IssueSeverity.Error);
    }

    [ObservableProperty] private int _issueCount;

    // ---- inserting ------------------------------------------------------------------------------

    public int InsertIndexAfterSelection()
    {
        var sel = Rows.Where(r => r.IsSelected).Select(r => Doc.IndexOf(r.Action)).DefaultIfEmpty(-1).Max();
        return sel >= 0 ? sel + 1 : Doc.Actions.Count;
    }

    public IReadOnlyList<ActionInstance> AddFromPanel(ActionDescriptor d) => InsertAt(d, InsertIndexAfterSelection());

    public IReadOnlyList<ActionInstance> InsertAt(ActionDescriptor d, int index)
    {
        var created = Doc.InsertAction(d, index);
        Prefs.AddRecent(d.Id);
        SelectActions(created.Take(1));
        RequestScrollTo?.Invoke(created[0]);
        return created;
    }

    public event Action<ActionInstance>? RequestScrollTo;

    // ---- edit operations used by keyboard and menus -----------------------------------------------

    public void DeleteSelection()
    {
        var sel = SelectedActions;
        if (sel.Count == 0) return;
        var first = Doc.IndexOf(sel[0]);
        Doc.Delete(sel);
        if (Doc.Actions.Count > 0) SelectActions(new[] { Doc.Actions[Math.Min(first, Doc.Actions.Count - 1)] });
    }

    public void DuplicateSelection()
    {
        var sel = SelectedActions;
        if (sel.Count == 0) return;
        var copies = Doc.Duplicate(sel);
        SelectActions(copies);
        if (copies.Count > 0) RequestScrollTo?.Invoke(copies[0]);
    }

    public bool CopySelection()
    {
        var sel = Doc.BlocksOf(SelectedActions);
        if (sel.Count == 0) return false;
        return TrySetClipboard(ActionClipboard.Serialize(sel));
    }

    public void CutSelection()
    {
        if (CopySelection()) DeleteSelection();
    }

    public bool CanPaste()
    {
        try { return Clipboard.ContainsText() && ActionClipboard.TryDeserialize(Clipboard.GetText()) is not null; }
        catch { return false; }
    }

    public void Paste()
    {
        string? text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { return; }
        var items = ActionClipboard.TryDeserialize(text);
        if (items is null) return;
        var pasted = ActionClipboard.Remap(items);
        Doc.InsertInstances(pasted, InsertIndexAfterSelection());
        SelectActions(pasted);
        RequestScrollTo?.Invoke(pasted[0]);
    }

    private static bool TrySetClipboard(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
        }
        return false;
    }

    public void SelectAll() { foreach (var r in Rows) r.IsSelected = true; }

    public void Undo() { if (Doc.CanUndo) Doc.Undo(); }
    public void Redo() { if (Doc.CanRedo) Doc.Redo(); }

    public void MoveSelection(int direction)
    {
        var sel = SelectedActions;
        if (sel.Count == 0) return;
        var blocks = Doc.BlocksOf(sel);
        if (Doc.MoveBy(sel, direction)) { SelectActions(blocks); RequestScrollTo?.Invoke(blocks[0]); }
    }

    public void ToggleDisabled(IEnumerable<ActionInstance> actions)
    {
        var list = actions.ToList();
        if (list.Count == 0) return;
        var disable = !list[0].Disabled;
        Doc.SetDisabled(list, disable);
    }

    // ---- metadata -------------------------------------------------------------------------------

    /// <summary>Renames the shortcut. Returns an error message to show, or null when applied.</summary>
    public string? TryRename(string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return "A shortcut needs a name.";
        if (newName == Shortcut.Name) return null;
        if (Context.Library.IsNameTaken(newName, Shortcut.Id)) return $"You already have a shortcut called \"{newName}\". Pick another name.";
        Shortcut.Name = newName;
        MetadataEdited();
        return null;
    }

    public void SetAppearance(string icon, string color)
    {
        Shortcut.Icon = IconNames.Normalize(icon);
        Shortcut.Color = Palette.IsValid(color) ? color : Shortcut.Color;
        MetadataEdited();
    }

    /// <summary>Call after changing any shortcut-level property (name, icon, details sheet fields).</summary>
    public void MetadataEdited()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Color));
        MarkDirty();
        MetadataChanged?.Invoke();
    }

    // ---- saving ---------------------------------------------------------------------------------

    private void MarkDirty()
    {
        _dirty = true;
        SaveStatus = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now. Called by the timer, before running and when the window closes.</summary>
    public bool SaveNow()
    {
        _saveTimer.Stop();
        if (!_dirty) return true;
        _saving = true;
        try
        {
            Context.Library.Save(Doc.Shortcut);
            _dirty = false;
            _existsInLibrary = true;
            SaveError = null;
            SaveStatus = "Saved";
            return true;
        }
        catch (LibraryException ex) { SaveError = ex.Message; SaveStatus = "Not saved"; return false; }
        catch (Exception ex) { SaveError = "Couldn't save this shortcut: " + ex.Message; SaveStatus = "Not saved"; return false; }
        finally { _saving = false; }
    }

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e)
    {
        if (!e.What.HasFlag(LibraryChange.Shortcuts)) return;
        _ui.BeginInvoke(new System.Action(() =>
        {
            if (_saving) return;
            var lib = Context.Library.GetById(Shortcut.Id);
            if (lib is null)
            {
                if (_existsInLibrary) DeletedExternally?.Invoke();
                return;
            }
            if (_dirty) return;
            if (Same(lib)) return;
            ReloadFrom(lib);
        }));
    }

    private bool Same(Shortcut lib)
    {
        var a = JsonSerializer.Serialize(lib.Actions, FlowmateJson.Options);
        var b = JsonSerializer.Serialize(Doc.Actions, FlowmateJson.Options);
        return a == b && lib.Name == Shortcut.Name && lib.Icon == Shortcut.Icon && lib.Color == Shortcut.Color && lib.Alias == Shortcut.Alias
            && lib.Hotkey == Shortcut.Hotkey && lib.Pinned == Shortcut.Pinned && lib.Description == Shortcut.Description;
    }

    private void ReloadFrom(Shortcut lib)
    {
        var copy = lib.Clone();
        Shortcut.Name = copy.Name; Shortcut.Icon = copy.Icon; Shortcut.Color = copy.Color; Shortcut.Alias = copy.Alias;
        Shortcut.Description = copy.Description; Shortcut.Hotkey = copy.Hotkey; Shortcut.Pinned = copy.Pinned;
        Shortcut.ShowInQuickRun = copy.ShowInQuickRun; Shortcut.ShowInSendTo = copy.ShowInSendTo;
        Shortcut.AcceptsInput = copy.AcceptsInput; Shortcut.WhenNoInput = copy.WhenNoInput; Shortcut.FolderId = copy.FolderId;
        Shortcut.ImportQuestions = copy.ImportQuestions;
        Doc.ReplaceAll(copy.Actions);
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(Color));
        MetadataChanged?.Invoke();
        _dirty = false;
        SaveStatus = "Saved";
    }

    /// <summary>Replaces the whole shortcut content (used by "Describe it").</summary>
    public void ApplyDraft(Shortcut draft)
    {
        Doc.ReplaceActionsUndoable(draft.Actions.Select(a => a.Clone()).ToList());
        if (!string.IsNullOrWhiteSpace(draft.Name) && draft.Name != Shortcut.Name && !Context.Library.IsNameTaken(draft.Name, Shortcut.Id)) Shortcut.Name = draft.Name;
        if (IconNames.IsValid(draft.Icon)) Shortcut.Icon = draft.Icon;
        if (Palette.IsValid(draft.Color)) Shortcut.Color = draft.Color;
        MetadataEdited();
    }

    public void Detach()
    {
        _saveTimer.Stop(); _validateTimer.Stop(); _runTimer.Stop();
        Context.Library.Changed -= OnLibraryChanged;
        Run.Cancel();
    }

    // ---- running --------------------------------------------------------------------------------

    public async Task RunAsync(int? fromIndex = null)
    {
        if (Run.IsRunning) return;
        SaveNow();
        var working = Doc.Shortcut.Clone();
        IsRunning = true;
        StatusText = fromIndex is null ? "Running…" : "Running from here…";
        _runTimer.Start();
        try { await Run.RunAsync(working, Context.Runner, fromIndex).ConfigureAwait(true); }
        finally
        {
            _runTimer.Stop();
            FlushRunUpdates();
            OnRunStateChanged();
        }
    }

    public void Stop() => Run.Cancel();

    private void OnRunStateChanged()
    {
        IsRunning = Run.IsRunning;
        var r = Run.LastResult;
        if (Run.IsRunning) return;
        LastResult = r;
        if (r is null) { StatusText = "Ready"; return; }
        StatusText = r.Status switch
        {
            RunStatus.Succeeded => $"Finished in {ActionRowViewModel.FormatDuration(r.Duration)}",
            RunStatus.Failed => r.ErrorMessage ?? "Something went wrong.",
            RunStatus.Cancelled => "Stopped",
            RunStatus.Stopped => $"Stopped by the shortcut after {ActionRowViewModel.FormatDuration(r.Duration)}",
            _ => "Done",
        };
    }

    private void FlushRunUpdates()
    {
        string[] uids;
        lock (_pendingGate) { uids = _pendingStepUids.ToArray(); _pendingStepUids.Clear(); }
        foreach (var uid in uids)
            if (_rowByUid.TryGetValue(uid, out var row)) row.SetRunState(Run.StepOf(uid));
        if (_varsDirty || (uids.Length > 0 && Inspector.Count == 0))
        {
            _varsDirty = false;
            RebuildInspector();
        }
    }

    private void RebuildInspector()
    {
        var items = new List<InspectorItem>();
        var outputs = Run.Outputs();
        foreach (var a in Doc.Actions)
        {
            if (!outputs.TryGetValue(a.Uid, out var value)) continue;
            var d = Context.Actions.Describe(a.ActionId);
            if (d is null || !d.HasOutput || a.Flow is { Role: not FlowRole.Start }) continue;
            items.Add(Item(a.OutputName is { Length: > 0 } n ? n : d.EffectiveOutputName, d.Icon, d.Color, value));
        }
        foreach (var (name, value) in Run.NamedVariables().OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase))
            items.Add(Item(name, "BracesVariable24", "orange", value));
        Inspector.Clear();
        foreach (var i in items) Inspector.Add(i);
    }

    private static InspectorItem Item(string name, string icon, string color, ContentValue value)
    {
        var kind = value.Count == 0 ? "Nothing" : value.Count == 1 ? KindName(value[0].Kind) : $"{value.Count} items";
        var preview = value.Count == 0 ? "(empty)" : ContentFormat.Truncate(value.AsText(", ").Replace("\n", " "), 120);
        return new InspectorItem(name, icon, color, kind, preview, value);
    }

    public static string KindName(ContentKind k) => k switch
    {
        ContentKind.Url => "Link", ContentKind.Boolean => "Yes / No", ContentKind.Any => "Anything", _ => k.ToString(),
    };

    public void ClearResults()
    {
        Run.ClearResults();
        foreach (var r in Rows) r.SetRunState(null);
        Inspector.Clear();
        LastResult = null;
        StatusText = "Ready";
    }
}
