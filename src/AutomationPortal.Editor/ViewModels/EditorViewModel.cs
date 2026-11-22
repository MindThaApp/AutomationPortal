using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using AutomationPortal.Editor.UI;

namespace AutomationPortal.Editor.ViewModels;

internal sealed record AiNote(string Headline, string Detail, bool IsDraft, IReadOnlyList<SuggestedAutomation> Automations,
    IReadOnlyList<string> DestructiveTitles, IReadOnlyList<string> Warnings);

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
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); if (!DraftPending && !_autosavePaused) SaveNow(); };
        _validateTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(180) };
        _validateTimer.Tick += (_, _) => { _validateTimer.Stop(); Revalidate(); };
        _runTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(70) };
        _runTimer.Tick += (_, _) => FlushRunUpdates();

        Doc.Changed += OnDocChanged;
        Run.StepChanged += uid => { lock (_pendingGate) _pendingStepUids.Add(uid); };
        Run.VariablesChanged += () => _varsDirty = true;
        Run.RunStateChanged += () => _ui.BeginInvoke(new System.Action(OnRunStateChanged));
        Run.PausedChanged += () => _ui.BeginInvoke(new System.Action(OnPausedChanged));
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
    /// <summary>A description to turn into a draft as soon as the editor opens (the library's "Describe it").</summary>
    public string? AutoDescribe { get; set; }
    /// <summary>Start recording as soon as the editor opens ("Record a new shortcut").</summary>
    public bool StartRecording { get; set; }
    /// <summary>Files to try the shortcut on: Run here hands them to it as its input. Null for none (the input is empty, as before).</summary>
    public IReadOnlyList<string>? SampleFiles { get; set; }
    /// <summary>Those of <see cref="SampleFiles"/> that couldn't be found when the editor opened.</summary>
    public IReadOnlyList<string> SampleMissing { get; set; } = Array.Empty<string>();
    /// <summary>The app that sent <see cref="SampleFiles"/>, or null.</summary>
    public string? SampleFrom { get; set; }
    /// <summary>What Run in the editor hands to the shortcut.</summary>
    public ContentValue? SampleInput => SampleFiles is { Count: > 0 } f ? ContentValue.Files(f) : null;
    public Shortcut Shortcut => Doc.Shortcut;
    public ObservableCollection<ActionRowViewModel> Rows { get; } = new();
    public ObservableCollection<InspectorItem> Inspector { get; } = new();

    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private bool _isRunning;
    /// <summary>The run is waiting before a step (step by step, or a Pause here step).</summary>
    [ObservableProperty] private bool _isPaused;
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

    /// <summary>Raised just before the list of rows changes, with the rows about to leave. For visual motion only: the change itself is never delayed.</summary>
    public event Action<IReadOnlyList<ActionRowViewModel>>? RowsChanging;

    private void SyncRows(List<ActionRowViewModel> desired)
    {
        var keep = desired.ToHashSet();
        if (Rows.Count == desired.Count && !Rows.Where((r, i) => !ReferenceEquals(r, desired[i])).Any()) return;
        RowsChanging?.Invoke(Rows.Where(r => !keep.Contains(r)).ToList());
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
            if (d is { HasOutput: true }) { var k = d.OutputKindOf(a); return k == ContentKind.Any ? null : k; }
        }
        return null;
    }

    // ---- document events -----------------------------------------------------------------------

    private void OnDocChanged(DocChange change)
    {
        if (!_applyingAi && DraftPending) DraftPending = false;
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
        StepAdded?.Invoke(created[0]);
        return created;
    }

    public event Action<ActionInstance>? RequestScrollTo;

    /// <summary>Raised after a step is added from the panel, the quick insert or an insert point, so the window can move the cursor into it.</summary>
    public event Action<ActionInstance>? StepAdded;

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
        if (DraftPending) { SaveStatus = "Draft, not saved yet"; return; }
        SaveStatus = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now. Called by the timer, before running and when the window closes.</summary>
    public bool SaveNow()
    {
        _saveTimer.Stop();
        DraftPending = false;
        if (!_dirty) return true;
        _saving = true;
        try
        {
            // Folder and order belong to the library window: keep whatever it has now instead of this window's older copy.
            if (Context.Library.GetById(Shortcut.Id) is { } latest) { Shortcut.FolderId = latest.FolderId; Shortcut.SortOrder = latest.SortOrder; }
            Context.Library.Save(Doc.Shortcut);
            _dirty = false;
            _existsInLibrary = true;
            SaveError = null;
            SaveStatus = "Saved";
            return true;
        }
        catch (LibraryException ex) { SaveError = ex.Message; SaveStatus = Strings.Get("EditorViewModel_NotSaved"); return false; }
        catch (Exception ex) { SaveError = "Couldn't save this shortcut: " + ex.Message; SaveStatus = Strings.Get("EditorViewModel_NotSaved"); return false; }
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
            if (_dirty) { Shortcut.FolderId = lib.FolderId; Shortcut.SortOrder = lib.SortOrder; return; }
            if (Same(lib)) return;
            ReloadFrom(lib);
        }));
    }

    private bool Same(Shortcut lib)
    {
        var a = JsonSerializer.Serialize(lib.Actions, AutomationPortalJson.Options);
        var b = JsonSerializer.Serialize(Doc.Actions, AutomationPortalJson.Options);
        return a == b && lib.Name == Shortcut.Name && lib.Icon == Shortcut.Icon && lib.Color == Shortcut.Color && lib.Alias == Shortcut.Alias
            && lib.Hotkey == Shortcut.Hotkey && lib.Pinned == Shortcut.Pinned && lib.Description == Shortcut.Description
            && lib.FolderId == Shortcut.FolderId && lib.SortOrder == Shortcut.SortOrder && lib.ShowInQuickRun == Shortcut.ShowInQuickRun
            && lib.ShowInSendTo == Shortcut.ShowInSendTo && lib.WhenNoInput == Shortcut.WhenNoInput;
    }

    private bool _autosavePaused;

    /// <summary>
    /// While true the save timer writes nothing (set around Version history, so steps edited before a restore can't be saved over it).
    /// Clearing it saves changes that are still pending.
    /// </summary>
    internal bool AutosavePaused
    {
        get => _autosavePaused;
        set
        {
            _autosavePaused = value;
            if (value) _saveTimer.Stop();
            else if (_dirty && !DraftPending) MarkDirty();
        }
    }

    /// <summary>
    /// Shows the library's copy (after a version was restored). When there are unsaved changes or an unkept draft, they are discarded only
    /// if <paramref name="confirmDiscard"/> agrees; otherwise the editor keeps them and returns false.
    /// </summary>
    internal bool ReloadFromLibrary(Func<bool> confirmDiscard)
    {
        _saveTimer.Stop();
        if (Context.Library.GetById(Shortcut.Id) is not { } lib) return false;
        if ((_dirty || DraftPending) && !confirmDiscard()) return false;
        DraftPending = false;
        ReloadFrom(lib);
        return true;
    }

    private void ReloadFrom(Shortcut lib)
    {
        var copy = lib.Clone();
        Shortcut.Name = copy.Name; Shortcut.Icon = copy.Icon; Shortcut.Color = copy.Color; Shortcut.Alias = copy.Alias;
        Shortcut.Description = copy.Description; Shortcut.Hotkey = copy.Hotkey; Shortcut.Pinned = copy.Pinned;
        Shortcut.ShowInQuickRun = copy.ShowInQuickRun; Shortcut.ShowInSendTo = copy.ShowInSendTo;
        Shortcut.AcceptsInput = copy.AcceptsInput; Shortcut.WhenNoInput = copy.WhenNoInput; Shortcut.FolderId = copy.FolderId; Shortcut.SortOrder = copy.SortOrder;
        Shortcut.ImportQuestions = copy.ImportQuestions;
        Doc.ReplaceAll(copy.Actions);
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(Color));
        MetadataChanged?.Invoke();
        _dirty = false;
        SaveStatus = "Saved";
    }

    // ---- AI drafts ------------------------------------------------------------------------------

    private bool _applyingAi;
    private (string Name, string Icon, string Color)? _beforeDraft;

    /// <summary>The note shown above the steps after "Describe it" / "Describe a change". Null when there is none.</summary>
    [ObservableProperty] private AiNote? _aiNote;

    /// <summary>An AI draft of a new shortcut that the user hasn't kept yet: it is not saved until they edit it, run it or choose Keep.</summary>
    public bool DraftPending { get; private set; }

    /// <summary>Steps the AI wrote or changed (they get a "check this" marker when they can change or delete things).</summary>
    public HashSet<string> AiStepUids { get; } = new();

    /// <summary>
    /// Puts an AI result into the editor as one undoable step: a whole new draft (name, icon, color, steps) or a revision of the steps.
    /// Nothing runs. A draft of a new shortcut stays unsaved until the user keeps or edits it.
    /// </summary>
    public void ApplyAiResult(AiDraftResult result, bool isChange)
    {
        if (result.Shortcut is not { } draft) return;
        var oldSteps = Doc.Actions.ToDictionary(a => a.Uid, a => JsonSerializer.Serialize(a, AutomationPortalJson.Options));
        _beforeDraft = (Shortcut.Name, Shortcut.Icon, Shortcut.Color);
        _applyingAi = true;
        try
        {
            DraftPending = !isChange && !_existsInLibrary;
            Doc.ReplaceActionsUndoable(draft.Actions.Select(a => a.Clone()).ToList());
            if (!isChange)
            {
                if (!string.IsNullOrWhiteSpace(draft.Name) && draft.Name != Shortcut.Name)
                    Shortcut.Name = Context.Library.IsNameTaken(draft.Name, Shortcut.Id) ? UniqueName(draft.Name) : draft.Name;
                if (IconNames.IsValid(draft.Icon)) Shortcut.Icon = draft.Icon;
                if (Palette.IsValid(draft.Color)) Shortcut.Color = draft.Color;
            }
            MetadataEdited();
        }
        finally { _applyingAi = false; }

        AiStepUids.Clear();
        var added = 0; var changed = 0;
        foreach (var a in Doc.Actions)
        {
            if (!oldSteps.TryGetValue(a.Uid, out var before)) { added++; AiStepUids.Add(a.Uid); }
            else if (before != JsonSerializer.Serialize(a, AutomationPortalJson.Options)) { changed++; AiStepUids.Add(a.Uid); }
        }
        var removed = oldSteps.Keys.Count(uid => Doc.Actions.All(a => a.Uid != uid));
        if (!isChange) AiStepUids.UnionWith(Doc.Actions.Select(a => a.Uid));

        var destructive = Doc.Actions.Where(a => AiStepUids.Contains(a.Uid) && Context.Actions.Describe(a.ActionId) is { } d && d.IsDestructiveFor(a))
            .Select(a => Context.Actions.Describe(a.ActionId)!.Title).Distinct().ToList();
        string headline, detail;
        if (isChange)
        {
            headline = "Changed: check the changes before you run it";
            var counts = new List<string>();
            if (added > 0) counts.Add(added == 1 ? "1 step added" : $"{added} steps added");
            if (changed > 0) counts.Add(changed == 1 ? "1 step changed" : $"{changed} steps changed");
            if (removed > 0) counts.Add(removed == 1 ? "1 step removed" : $"{removed} steps removed");
            detail = string.Join(" ", new[] { result.Summary, counts.Count > 0 ? "(" + string.Join(", ", counts) + ")" : "" }.Where(s => s.Length > 0));
        }
        else
        {
            headline = "Draft: check each step before you run it";
            detail = result.Summary;
        }
        AiNote = new AiNote(headline, detail, !isChange, isChange ? Array.Empty<SuggestedAutomation>() : result.Automations,
            destructive, result.Warnings);
        foreach (var r in Rows) r.RaiseRefresh();
    }

    /// <summary>Undo for the note's Undo button: takes back the AI step (and a draft's name, icon and color).</summary>
    public void UndoAiResult()
    {
        var wasDraft = AiNote?.IsDraft == true;
        _applyingAi = true;
        try
        {
            if (Doc.CanUndo) Doc.Undo();
            if (wasDraft && _beforeDraft is { } b) { Shortcut.Name = b.Name; Shortcut.Icon = b.Icon; Shortcut.Color = b.Color; MetadataEdited(); }
            if (wasDraft && !_existsInLibrary) { DraftPending = true; if (IsEmpty) { _dirty = false; SaveStatus = ""; _saveTimer.Stop(); } }
        }
        finally { _applyingAi = false; }
        DismissAiNote();
    }

    public void DismissAiNote()
    {
        AiNote = null;
        AiStepUids.Clear();
        foreach (var r in Rows) r.RaiseRefresh();
    }

    /// <summary>The user keeps an AI draft: it is saved like any shortcut from now on.</summary>
    public void KeepDraft()
    {
        DraftPending = false;
        if (_dirty) MarkDirty();
        DismissAiNote();
    }

    /// <summary>Forgets an unkept draft so closing the window doesn't save it.</summary>
    public void DiscardDraft()
    {
        if (!DraftPending) return;
        _dirty = false;
        _saveTimer.Stop();
    }

    /// <summary>True for an AI draft with steps that was never kept, edited or run (closing the window asks what to do with it).</summary>
    public bool HasUnkeptDraft => DraftPending && _dirty && !IsEmpty;

    /// <summary>Inserts recorded steps after the selection as one undo step.</summary>
    public IReadOnlyList<ActionInstance> InsertRecorded(IReadOnlyList<ActionInstance> steps)
    {
        if (steps.Count == 0) return steps;
        var copies = steps.Select(s => s.Clone()).ToList();
        Doc.InsertInstances(copies, InsertIndexAfterSelection());
        SelectActions(copies.Take(1));
        RequestScrollTo?.Invoke(copies[0]);
        return copies;
    }
    public void Detach()
    {
        _saveTimer.Stop(); _validateTimer.Stop(); _runTimer.Stop();
        Context.Library.Changed -= OnLibraryChanged;
        Run.Cancel();
        Run.DiscardTemp();
    }

    // ---- running --------------------------------------------------------------------------------

    /// <param name="input">What to run on instead of the sample files.</param>
    /// <param name="previewPicks">The files and folders picked for a preview just before; the run uses them instead of asking again.</param>
    public async Task RunAsync(int? fromIndex = null, ContentValue? input = null, PreviewPicks? previewPicks = null)
    {
        if (Run.IsRunning) return;
        SaveNow();
        var working = Doc.Shortcut.Clone();
        IsRunning = true;
        StatusText = fromIndex is null ? "Running…" : Strings.Get("EditorViewModel_RunningFromHere");
        _runTimer.Start();
        try { await Run.RunAsync(working, Context.Runner, fromIndex, input ?? SampleInput, previewPicks).ConfigureAwait(true); }
        finally
        {
            _runTimer.Stop();
            FlushRunUpdates();
            OnRunStateChanged();
        }
    }

    /// <summary>
    /// "Preview file changes": runs a copy of the shortcut in preview mode with the sample files, if any. Read-only steps run, file
    /// steps only say what they would do, other steps are skipped. Nothing is changed and nothing goes into Run History.
    /// </summary>
    public Task<FileChangePlan> PreviewFileChangesAsync() =>
        FilePreview.RunAsync(Context.Runner, Doc.Shortcut.Clone(), SampleInput ?? ContentValue.Empty);

    /// <summary>Runs with a pause before every step. Next step, Run to end and Stop control it.</summary>
    public Task RunStepByStepAsync(int? fromIndex = null)
    {
        if (Run.IsRunning) return Task.CompletedTask;
        Run.StepByStep = true;
        return RunAsync(fromIndex);
    }

    public void NextStep() => Run.NextStep();
    public void RunToEnd() => Run.RunToEnd();

    public void ToggleBreakpoint(string uid)
    {
        var on = !Run.HasBreakpoint(uid);
        Run.SetBreakpoint(uid, on);
        if (_rowByUid.TryGetValue(uid, out var row)) row.HasBreakpoint = on;
    }

    private void OnPausedChanged()
    {
        IsPaused = Run.IsPaused;
        if (Run.IsPaused)
        {
            var title = Run.PausedUid is { } u && _rowByUid.TryGetValue(u, out var row) ? row.Descriptor?.Title : null;
            StatusText = title is null ? "Paused. Press Next step to continue." : $"Paused before \"{title}\". Press Next step to continue.";
        }
        else if (Run.IsRunning) StatusText = "Running…";
        FlushRunUpdates();
    }

    public void Stop() => Run.Cancel();

    private void OnRunStateChanged()
    {
        IsRunning = Run.IsRunning;
        if (!Run.IsRunning) { Run.StepByStep = false; IsPaused = false; }
        var r = Run.LastResult;
        if (Run.IsRunning) return;
        LastResult = r;
        if (r is null) { StatusText = "Ready"; return; }
        StatusText = r.Status switch
        {
            RunStatus.Succeeded => $"Finished in {ActionRowViewModel.FormatDuration(r.Duration)}",
            RunStatus.Failed => r.ErrorMessage ?? Strings.Get("ResultsDrawer_SomethingWentWrong"),
            RunStatus.Cancelled => "You stopped it",
            RunStatus.Stopped => $"Stopped itself after {ActionRowViewModel.FormatDuration(r.Duration)}",
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
        ContentKind.Url => "Link", ContentKind.Boolean => Strings.Get("PillOptions_YesNo"), ContentKind.Any => "Anything", _ => k.ToString(),
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
