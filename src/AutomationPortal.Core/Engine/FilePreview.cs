namespace AutomationPortal.Core;

/// <summary>What one planned file change in a preview is.</summary>
public enum PlannedChangeKind
{
    /// <summary>A file or folder would move from <see cref="PlannedChange.Source"/> to <see cref="PlannedChange.Target"/>.</summary>
    Move,
    /// <summary>A copy of <see cref="PlannedChange.Source"/> would be made at <see cref="PlannedChange.Target"/>; the original stays.</summary>
    Copy,
    /// <summary>A file or folder would get a new name.</summary>
    Rename,
    /// <summary>A new file would be made at <see cref="PlannedChange.Target"/> (from <see cref="PlannedChange.Source"/> when there is one); the original stays.</summary>
    NewFile,
    /// <summary>A new folder would be made.</summary>
    NewFolder,
    /// <summary>A file or folder would go to the Recycle Bin.</summary>
    Recycle,
    /// <summary>A file or folder would be deleted permanently.</summary>
    Delete,
    /// <summary>Nothing would happen to this item (already there, or skipped because the name is taken).</summary>
    Skip,
    /// <summary>This step can't say in advance what it would do. It was not run.</summary>
    CantPreview,
    /// <summary>This step doesn't change files and was not run for the preview (it opens something, shows something, asks something).</summary>
    NotRun,
    /// <summary>The preview stopped here (the step would fail, or the shortcut stops).</summary>
    Stopped,
}

/// <summary>One line of a preview: what a step would do to one item.</summary>
public sealed record PlannedChange(PlannedChangeKind Kind, string Step, string? Source = null, string? Target = null, string? Note = null)
{
    /// <summary>True for the kinds that would change something on disk.</summary>
    public bool ChangesFiles => Kind is PlannedChangeKind.Move or PlannedChangeKind.Copy or PlannedChangeKind.Rename or PlannedChangeKind.NewFile
        or PlannedChangeKind.NewFolder or PlannedChangeKind.Recycle or PlannedChangeKind.Delete;

    /// <summary>One plain sentence, e.g. "Move report.pdf → C:\Users\me\Downloads\Documents\report.pdf".</summary>
    public string Describe()
    {
        var name = Source is null ? "" : NameOf(Source);
        var text = Kind switch
        {
            PlannedChangeKind.Move => $"Move {name} → {Target}",
            PlannedChangeKind.Copy => $"Copy {name} → {Target}",
            PlannedChangeKind.Rename => $"Rename {name} → {(Target is null ? "" : NameOf(Target))}",
            PlannedChangeKind.NewFile => Source is null ? $"New file {Target}" : $"New file {Target} (from {name})",
            PlannedChangeKind.NewFolder => $"New folder {Target}",
            PlannedChangeKind.Recycle => $"Send {Source} to the Recycle Bin",
            PlannedChangeKind.Delete => $"Delete {Source} permanently",
            PlannedChangeKind.Skip => $"Leave {name} as it is",
            PlannedChangeKind.CantPreview => Source is null ? $"{Step}: can't preview this step" : $"{Step}: can't preview {Source}",
            PlannedChangeKind.NotRun => $"{Step}: skipped in the preview (doesn't change files)",
            PlannedChangeKind.Stopped => $"{Step}: the preview stops here",
            _ => Step,
        };
        return string.IsNullOrEmpty(Note) ? text : $"{text} ({Note})";
    }

    private static string NameOf(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
}

/// <summary>
/// Implemented by actions that can say what they would do without doing it. <see cref="PlanAsync"/> reads the same parameters as
/// <see cref="ShortcutAction.RunAsync"/>, adds what it would change to <paramref name="plan"/> and returns the output later steps
/// would get (for example the paths files would have after a move). It must not change anything on disk, the journal or the temp folder.
/// </summary>
public interface IPlansFileChanges
{
    Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan);
}

/// <summary>The planned changes of one preview run. Thread-safe for adding.</summary>
public sealed class FileChangePlan
{
    private readonly object _gate = new();
    private readonly List<PlannedChange> _changes = new();
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PlannedChange> Changes { get { lock (_gate) return _changes.ToList(); } }

    /// <summary>How many items would change on disk.</summary>
    public int ChangeCount { get { lock (_gate) return _changes.Count(c => c.ChangesFiles); } }

    /// <summary>True when some step couldn't be previewed, so the list may be incomplete.</summary>
    public bool IsIncomplete { get { lock (_gate) return _changes.Any(c => c.Kind is PlannedChangeKind.CantPreview or PlannedChangeKind.Stopped); } }

    /// <summary>One sentence for a result bar or a set-up sheet, e.g. "23 files would move. Nothing would be deleted."</summary>
    public string Summary()
    {
        List<PlannedChange> all;
        lock (_gate) all = _changes.ToList();
        static string Files(int n) => n == 1 ? "1 item" : $"{n} items";
        var parts = new List<string>();
        void Count(PlannedChangeKind kind, string verb)
        {
            var n = all.Count(c => c.Kind == kind);
            if (n > 0) parts.Add($"{Files(n)} would {verb}");
        }
        Count(PlannedChangeKind.Move, "move");
        Count(PlannedChangeKind.Copy, "be copied");
        Count(PlannedChangeKind.Rename, "get a new name");
        Count(PlannedChangeKind.NewFile, "be made as new files");
        Count(PlannedChangeKind.NewFolder, "be made as new folders");
        Count(PlannedChangeKind.Recycle, "go to the Recycle Bin");
        Count(PlannedChangeKind.Delete, "be deleted permanently");
        var text = parts.Count == 0 ? "Nothing would change right now." : string.Join(", ", parts) + ".";
        if (parts.Count > 0 && !all.Any(c => c.Kind is PlannedChangeKind.Recycle or PlannedChangeKind.Delete)) text += " Nothing would be deleted.";
        if (all.Any(c => c.Kind is PlannedChangeKind.CantPreview or PlannedChangeKind.Stopped)) text += " Some steps can't be previewed, so this may not be everything.";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    public void Add(PlannedChange change)
    {
        lock (_gate)
        {
            _changes.Add(change);
            if (change.ChangesFiles && change.Kind != PlannedChangeKind.Recycle && change.Kind != PlannedChangeKind.Delete && change.Target is { } t)
                _reserved.Add(Full(t));
            if (change.Kind is PlannedChangeKind.Move or PlannedChangeKind.Rename or PlannedChangeKind.Recycle or PlannedChangeKind.Delete && change.Source is { } s)
                _gone.Add(Full(s));
        }
    }

    /// <summary>True when the path exists on disk or an earlier planned step would create it (and no planned step removes it).</summary>
    public bool IsTaken(string path)
    {
        var full = Full(path);
        lock (_gate)
        {
            if (_reserved.Contains(full)) return true;
            if (_gone.Contains(full)) return false;
        }
        return File.Exists(full) || Directory.Exists(full);
    }

    /// <summary>
    /// The path a new file called <paramref name="fileName"/> in <paramref name="dir"/> would get: the name itself when free, else
    /// "name (2).ext", "name (3).ext" … the same rule the actions use when they really save.
    /// </summary>
    public string UniquePath(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!IsTaken(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            path = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!IsTaken(path)) return path;
        }
    }

    private static string Full(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}

/// <summary>
/// "Show what it would do": runs a shortcut in preview mode. Steps that only read (find files, filter, count, dates, text) run for
/// real; steps that change files run their <see cref="IPlansFileChanges.PlanAsync"/> instead; every other step is skipped and
/// listed. Nothing on disk changes, no history is recorded and no undo record is written.
/// </summary>
public static class FilePreview
{
    /// <summary>Actions that only read and so run for real in a preview. Anything not listed here and without a planner is not run.</summary>
    public static IReadOnlySet<string> ReadOnlyActionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "file.getFolder", "file.get", "file.contents", "file.filter", "file.find", "file.details", "file.selected", "file.hash",
        "file.compare", "file.readText", "img.details", "pdf.details", "pdf.text", "sheet.read",
        "content.count", "content.dict", "content.list", "content.number", "content.text",
        "date.adjust", "date.between", "date.current", "date.date", "date.format", "date.parse",
        "dict.fromInput", "dict.getValue", "dict.setValue", "dict.toJson",
        "list.filter", "list.getItem", "list.sort", "list.unique",
        "num.calculate", "num.convertUnits", "num.expression", "num.format", "num.round", "num.stats",
        "text.case", "text.cleanup", "text.combine", "text.encode", "text.find", "text.fromInput", "text.getPart", "text.hash",
        "text.replace", "text.split",
        "var.get", "var.set", "var.add", "var.loadValue",
        "app.list", "app.isRunning", "win.list",
        FlowIds.Stop, FlowIds.Output, FlowIds.Nothing,
    };

    /// <summary>Steps that don't touch files and are simply left out of a preview (shown as "skipped").</summary>
    public static bool DoesNotChangeFiles(string actionId) =>
        actionId.StartsWith("ui.", StringComparison.OrdinalIgnoreCase)
        || actionId is "app.open" or "app.openFile" or "app.switch" or "app.front" or "app.reveal" or "app.revealInFilesPortal" or "app.waitFor"
            or "web.open" or "win.arrange" or "win.restoreLayout" or "win.onTop" or "win.showDesktop" or FlowIds.Wait
        || actionId.StartsWith("media.", StringComparison.OrdinalIgnoreCase) && actionId != "media.record";

    /// <summary>
    /// Runs <paramref name="shortcut"/> in preview mode and returns the plan. When the shortcut asks for files and
    /// <paramref name="input"/> is empty, the usual file picker is shown first.
    /// </summary>
    public static async Task<FileChangePlan> RunAsync(IShortcutRunner runner, Shortcut shortcut, ContentValue input, CancellationToken ct = default)
    {
        var plan = new FileChangePlan();
        var result = await runner.RunAsync(shortcut, input, new RunOptions
        {
            Source = RunSource.Editor, RecordHistory = false, CancellationToken = ct, Preview = plan,
        }).ConfigureAwait(false);
        if (result.Status == RunStatus.Failed)
        {
            var step = result.FailedStepUid is { } uid ? shortcut.FindAction(uid)?.ActionId ?? "" : "";
            plan.Add(new PlannedChange(PlannedChangeKind.Stopped, StepTitle(result, step), Note: result.ErrorMessage));
        }
        return plan;
    }

    private static string StepTitle(RunResult result, string actionId) =>
        result.Steps.LastOrDefault(s => s.Error is not null)?.Title is { Length: > 0 } t ? t : actionId.Length > 0 ? actionId : "A step";
}
