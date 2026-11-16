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

    /// <summary>
    /// What the preview worked on when it had no input and asked for files (or used the clipboard). "Run for real" after a preview
    /// runs on this same input, so it does what the preview showed. Null when the preview was given its input.
    /// </summary>
    public ContentValue? PickedInput { get; set; }

    /// <summary>
    /// Everything picked while the preview ran: the shortcut's own files and every file or folder a step asked for. Pass it to the
    /// real run right after (<see cref="RunOptions.PreviewPicks"/>) so it uses the same items. Set by the runner for a preview.
    /// </summary>
    public PreviewPicks? Picks { get; internal set; }

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
/// The files and folders picked while a preview ran, so the real run right after uses exactly those instead of asking again (and
/// perhaps getting a different answer than the preview showed). Each pick is kept by step, setting, repeat item and how many times
/// that step had asked before. A real run uses them only once, only for the same shortcut with the same steps, only when someone is
/// running it from the library or the editor, and only while every picked item is still exactly as it was when it was picked.
/// </summary>
public sealed class PreviewPicks
{
    /// <summary>Key of what the shortcut itself asked for (its "When there's no input" files or the clipboard).</summary>
    internal const string InputKey = "\u0001input";

    private readonly object _gate = new();
    private readonly Dictionary<string, (ContentValue Value, List<PathStamp> Stamps)> _picks = new(StringComparer.Ordinal);
    private int _claimed;

    internal PreviewPicks(Guid shortcutId, string stepsHash)
    {
        ShortcutId = shortcutId;
        StepsHash = stepsHash;
    }

    /// <summary>The shortcut that was previewed.</summary>
    public Guid ShortcutId { get; }
    /// <summary><see cref="LinkPolicy.StepsHash"/> of the shortcut when it was previewed; any edit since means the picks don't apply.</summary>
    public string StepsHash { get; }

    public int Count { get { lock (_gate) return _picks.Count; } }
    public bool IsEmpty => Count == 0;
    /// <summary>True once a real run has taken these picks; they never apply to a second run.</summary>
    public bool IsUsed => Volatile.Read(ref _claimed) != 0;

    /// <summary>Keeps what was picked under <paramref name="key"/>, with a stamp of every file and folder in it (a folder's covers everything inside).</summary>
    internal void Record(string key, ContentValue picked)
    {
        if (picked.IsEmpty || IsUsed) return;
        var stamps = picked.OfType<PathItem>().Select(p => PathStamp.Of(p.Path)).ToList();
        lock (_gate) _picks.TryAdd(key, (picked, stamps));
    }

    /// <summary>Hands out the pick kept under <paramref name="key"/> once.</summary>
    internal bool TryTake(string key, out ContentValue picked)
    {
        lock (_gate)
        {
            if (_picks.Remove(key, out var kept)) { picked = kept.Value; return true; }
        }
        picked = ContentValue.Empty;
        return false;
    }

    /// <summary>Claims the picks for one run of <paramref name="shortcutId"/> with steps <paramref name="stepsHash"/>: true only the first time, and only when both match.</summary>
    internal bool TryClaim(Guid shortcutId, string stepsHash)
    {
        if (shortcutId != ShortcutId || !string.Equals(stepsHash, StepsHash, StringComparison.Ordinal)) return false;
        return Interlocked.Exchange(ref _claimed, 1) == 0;
    }

    /// <summary>
    /// The picked files and folders that are gone or differ from when they were picked: a file's size or last-change time, or
    /// anything added, removed or changed anywhere inside a folder. A folder too big to sum up or partly unreadable counts as changed.
    /// </summary>
    internal IReadOnlyList<string> ChangedItems()
    {
        List<PathStamp> all;
        lock (_gate) all = _picks.Values.SelectMany(p => p.Stamps).ToList();
        return all.Where(s => !s.SameAs(PathStamp.Of(s.Path))).Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The key of one ask: the step, its setting, the repeat items it runs for and how often this exact ask came before.</summary>
    internal static string KeyFor(string stepUid, string paramKey, string repeatContext, int occurrence) =>
        string.Join('\u001f', stepUid, paramKey, repeatContext, occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// What a file or folder looked like. A file: its size and last-change time. A folder: a summary of everything inside it,
    /// however deep (how many files and folders, their total size, the newest last-change time of any of them or the folder
    /// itself), so a change anywhere in it counts. Links and junctions inside count as themselves and aren't followed.
    /// </summary>
    internal readonly record struct PathStamp(string Path, bool Exists, bool IsFolder, long Length, DateTime WriteUtc, long Entries = 0, bool Unsure = false)
    {
        /// <summary>A folder with more files and folders inside it than this isn't summed up; it counts as changed.</summary>
        internal const int MaxEntries = 50_000;

        /// <summary>True only when both stamps were read in full and match. A folder too big or partly unreadable never matches.</summary>
        public bool SameAs(PathStamp now) => !Unsure && !now.Unsure && this == now;

        public static PathStamp Of(string path) => Of(path, MaxEntries);

        internal static PathStamp Of(string path, int maxEntries)
        {
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    return new PathStamp(path, true, false, fi.Length, fi.LastWriteTimeUtc);
                }
                if (Directory.Exists(path)) return OfFolder(path, maxEntries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
            return new PathStamp(path, false, false, -1, default);
        }

        private static PathStamp OfFolder(string path, int maxEntries)
        {
            var root = new DirectoryInfo(path);
            var newest = root.LastWriteTimeUtc;
            long bytes = 0, entries = 0;
            try
            {
                // A link or junction picked as the folder itself counts as itself, like the ones inside.
                if (!root.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
                    var folders = new Stack<DirectoryInfo>();
                    folders.Push(root);
                    while (folders.Count > 0)
                    {
                        foreach (var entry in folders.Pop().EnumerateFileSystemInfos("*", options))
                        {
                            if (++entries > maxEntries) return new PathStamp(path, true, true, bytes, newest, entries, Unsure: true);
                            if (entry.LastWriteTimeUtc > newest) newest = entry.LastWriteTimeUtc;
                            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                            if (entry is DirectoryInfo dir) folders.Push(dir);
                            else bytes += ((FileInfo)entry).Length;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                return new PathStamp(path, true, true, bytes, newest, entries, Unsure: true);
            }
            return new PathStamp(path, true, true, bytes, newest, entries);
        }
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
