namespace AutomationPortal.Actions.Documents;

/// <summary>One line of the undo plan. <see cref="Automatic"/> is false for what the user has to do (or can't) themselves.</summary>
public sealed record UndoStep(FileChange Change, string Description, bool Automatic);

/// <summary>What happened to one step: done, or skipped with the reason.</summary>
public sealed record UndoOutcome(UndoStep Step, bool Done, string? Problem);

/// <summary>
/// Reverses a run's file changes, newest first. Nothing is forced: items go to the Recycle Bin only when unchanged since the run and
/// only on drives that have one, items are moved back only when unchanged and nothing is in the way, and whatever can't be done
/// safely is skipped and reported. The journal is read as untrusted input: damaged entries, device paths, protected folders and
/// copies outside the run's own backup folder are refused.
/// </summary>
public static class FileUndo
{
    private static string DamagedEntry => Strings.Get("FileUndo_Message_ADamagedEntryIn");

    public static bool CanUndo(JournaledRun? run) => run is { UndoneAt: null } && run.Changes.Count > 0;

    public static IReadOnlyList<UndoStep> Plan(JournaledRun run) => Plan(run, DriveHasRecycleBin);

    /// <param name="canRecycle">Whether the drive of a path has a Recycle Bin. Tests pass their own.</param>
    public static IReadOnlyList<UndoStep> Plan(JournaledRun run, Func<string, bool> canRecycle) =>
        run.Changes.Reverse().Select(c => StepFor(c, run, canRecycle)).ToList();

    private static UndoStep StepFor(FileChange c, JournaledRun run, Func<string, bool> canRecycle)
    {
        if (!WellFormed(c)) return new UndoStep(c, DamagedEntry, false);
        if (c.Kind is FileChangeKind.Created or FileChangeKind.Replaced && !canRecycle(c.Path))
            return new UndoStep(c, NoRecycleBin(c), false);
        var automatic = c.Kind is FileChangeKind.Created or FileChangeKind.Moved or FileChangeKind.Renamed or FileChangeKind.Replaced;
        var description = Describe(c);
        if (automatic && run.Final is { } final && final.TryGetValue(c.Path, out var fp) && fp.Sha256 is null)
            description += Strings.Get("FileUndo_Message_ItIsLargeSo");
        return new UndoStep(c, description, automatic);
    }

    private static string Name(string path) => Path.GetFileName(path.TrimEnd('\\', '/'));
    private static string? Dir(string path) => Path.GetDirectoryName(path.TrimEnd('\\', '/'));

    private static string NoRecycleBin(FileChange c)
    {
        var text = Strings.Format("FileUndo_Message_InIsOnA", Name(c.Path), Dir(c.Path));
        return c.Kind == FileChangeKind.Replaced && c.Backup is not null
            ? text + Strings.Format("FileUndo_Message_ACopyOfThe", c.Backup)
            : text;
    }

    private static string Describe(FileChange c)
    {
        var name = Name(c.Path);
        var dir = Dir(c.Path);
        return c.Kind switch
        {
            FileChangeKind.Created => Strings.Format("FileUndo_Message_SendInToThe", name, dir),
            FileChangeKind.Moved => Strings.Format("FileUndo_Message_MoveBackToIf", name, Dir(c.From!)),
            FileChangeKind.Renamed => Strings.Format("FileUndo_Message_RenameBackToIn", name, Name(c.From!), dir),
            FileChangeKind.Replaced when c.Backup is not null => Strings.Format("FileUndo_Message_PutBackTheEarlier", name, dir),
            FileChangeKind.Replaced => Strings.Format("FileUndo_Message_SendTheNewIn", name, dir),
            FileChangeKind.Recycled => Strings.Format("FileUndo_Message_FromWentToThe", name, dir),
            FileChangeKind.Deleted => Strings.Format("FileUndo_Message_InWasDeletedPermanently", name, dir),
            _ => Strings.Format("FileUndo_Message_InWasChangedIn", name, dir),
        };
    }

    /// <param name="recycle">Sends a path to the Recycle Bin and returns true when it is gone. Tests pass their own.</param>
    /// <param name="canRecycle">Whether the drive of a path has a Recycle Bin. Tests pass their own.</param>
    /// <exception cref="ActionException">The run is already being undone, or was undone already.</exception>
    public static IReadOnlyList<UndoOutcome> Execute(JournaledRun run, FileJournal store, Func<string, bool>? recycle = null, CancellationToken ct = default,
        Func<string, bool>? canRecycle = null)
    {
        recycle ??= RecycleBin.TrySend;
        canRecycle ??= DriveHasRecycleBin;
        var claim = store.BeginUndo(run.RunId);
        if (claim is not null) throw new ActionException(claim);
        var finished = false;
        try
        {
            var expected = new Dictionary<string, FileFingerprint>(run.Final ?? new Dictionary<string, FileFingerprint>(), StringComparer.OrdinalIgnoreCase);
            var results = new List<UndoOutcome>();
            foreach (var step in Plan(run, canRecycle))
            {
                ct.ThrowIfCancellationRequested();
                string? problem;
                try { problem = step.Automatic ? Reverse(step.Change, run, store, expected, recycle) : Strings.Get("FileUndo_Message_NotDoneAutomatically"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ActionException)
                {
                    problem = ex.Message;
                }
                results.Add(new UndoOutcome(step, problem is null, problem));
            }
            finished = true;
            return results;
        }
        finally { store.EndUndo(run.RunId, finished); }
    }

    /// <summary>Undoes one change; returns null when done, else why it was skipped.</summary>
    private static string? Reverse(FileChange c, JournaledRun run, FileJournal store, Dictionary<string, FileFingerprint> expected, Func<string, bool> recycle)
    {
        var finished = run.Final is not null;
        switch (c.Kind)
        {
            case FileChangeKind.Created:
            {
                ProtectedPaths.Ensure(c.Path, "delete");
                if (!Exists(c.Path)) return Strings.Get("FileUndo_Message_ItIsnTThere");
                var problem = Unchanged(c.Path, expected, finished) ?? Recycle(c.Path, recycle);
                if (problem is null) expected.Remove(c.Path);
                return problem;
            }
            case FileChangeKind.Moved:
            case FileChangeKind.Renamed:
            {
                var from = c.From!;
                ProtectedPaths.Ensure(c.Path, "move");
                ProtectedPaths.Ensure(from, "move");
                if (!Exists(c.Path)) return Strings.Format("FileUndo_Message_ItIsnTAt", c.Path);
                var problem = Unchanged(c.Path, expected, finished);
                if (problem is not null) return problem;
                var caseOnly = string.Equals(from, c.Path, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && Exists(from)) return Strings.Format("FileUndo_Message_SomethingCalledIsAlready", Name(from), Dir(from));
                TestRootGuard.Check(from);
                Directory.CreateDirectory(Dir(from)!);
                var note = Directory.Exists(c.Path) ? Transfer.MoveFolder(c.Path, from) : Transfer.MoveFile(c.Path, from);
                if (expected.Remove(c.Path, out var fp)) expected[from] = fp with { Path = from };
                return note;
            }
            case FileChangeKind.Replaced:
                return PutBackEarlier(c, run, store, expected, recycle);
            default:
                return Strings.Get("FileUndo_Message_NotDoneAutomatically");
        }
    }

    /// <summary>
    /// The backup is checked before anything is touched, then copied next to the file and checked again; only then does the newer
    /// file go to the Recycle Bin and the copy take its name.
    /// </summary>
    private static string? PutBackEarlier(FileChange c, JournaledRun run, FileJournal store, Dictionary<string, FileFingerprint> expected, Func<string, bool> recycle)
    {
        var finished = run.Final is not null;
        ProtectedPaths.Ensure(c.Path, "replace");
        var current = Exists(c.Path);
        if (c.Backup is null)
        {
            if (!current) return Strings.Get("FileUndo_Message_NoCopyOfThe");
            var problem = Unchanged(c.Path, expected, finished) ?? Recycle(c.Path, recycle);
            if (problem is not null) return problem;
            expected.Remove(c.Path);
            return Strings.Get("FileUndo_Message_TheNewerOneWent");
        }

        var bad = BackupProblem(c, store, run.RunId);
        if (bad is not null) return bad;
        if (current)
        {
            var problem = Unchanged(c.Path, expected, finished);
            if (problem is not null) return problem;
        }

        TestRootGuard.Check(c.Path);
        Directory.CreateDirectory(Dir(c.Path)!);
        var staged = c.Path + ".autoportal-new-" + Guid.NewGuid().ToString("N")[..8];
        if (Exists(staged)) return Strings.Get("FileUndo_Message_CouldnTPrepareThe");
        var copied = false;
        try
        {
            File.Copy(c.Backup, staged);
            copied = true;
            if (!string.Equals(Transfer.Sha256Hex(staged), c.BackupSha256, StringComparison.OrdinalIgnoreCase))
            {
                DeleteStaged(staged);
                return Strings.Get("FileUndo_Message_TheEarlierVersionDidn");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (copied) DeleteStaged(staged);
            return Strings.Format("FileUndo_Message_CouldnTCopyThe", ex.Message);
        }

        if (current)
        {
            var problem = Recycle(c.Path, recycle);
            if (problem is not null)
            {
                DeleteStaged(staged);
                return problem;
            }
            expected.Remove(c.Path);
        }
        try { File.Move(staged, c.Path, overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Strings.Format("FileUndo_Message_SomethingIsAlreadyAt", c.Path, staged);
        }
        if (FileFingerprint.Of(c.Path) is { } now) expected[c.Path] = now;
        return null;
    }

    // Only for the file this class just made under a new unique name, before it took the place of anything.
    private static void DeleteStaged(string staged)
    {
        try { File.Delete(staged); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The backup must be a file directly in this run's own backup folder, reached without links, with the contents it had when made.</summary>
    private static string? BackupProblem(FileChange c, FileJournal store, string runId)
    {
        string Outside = Strings.Get("FileUndo_Message_TheRecordPointsTo");
        string Missing = Strings.Get("FileUndo_Message_TheCopyOfThe");
        string folder;
        try { folder = Path.GetFullPath(store.BackupFolderFor(runId)).TrimEnd('\\', '/'); }
        catch (ArgumentException) { return Outside; }
        var backup = c.Backup!;
        if (!string.Equals(Path.GetDirectoryName(backup), folder, StringComparison.OrdinalIgnoreCase)) return Outside;
        if (IsLink(store.Folder) || IsLink(folder) || IsLink(backup)) return Outside;
        if (!File.Exists(backup) || c.BackupSha256 is null) return Missing;
        try
        {
            if (!string.Equals(Transfer.Sha256Hex(backup), c.BackupSha256, StringComparison.OrdinalIgnoreCase)) return Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Missing; }
        return null;
    }

    private static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static bool WellFormed(FileChange c) =>
        SafePath(c.Path)
        && (c.Kind is not (FileChangeKind.Moved or FileChangeKind.Renamed) || SafePath(c.From))
        && (c.Backup is null || SafePath(c.Backup));

    /// <summary>A plain full path exactly as the journal writes it: no device or \\?\ paths, nothing relative, nothing left to normalise.</summary>
    internal static bool SafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\0')) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal))
            return false;
        try { return Path.IsPathFullyQualified(path) && string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Only a fixed local NTFS or ReFS drive is sure to have a Recycle Bin; elsewhere the shell would delete for good.</summary>
    internal static bool DriveHasRecycleBin(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var drive = new DriveInfo(root);
            return drive.IsReady && drive.DriveType == DriveType.Fixed
                   && (string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) || string.Equals(drive.DriveFormat, "ReFS", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static string? Unchanged(string path, Dictionary<string, FileFingerprint> expected, bool finished)
    {
        if (!finished) return Strings.Get("FileUndo_Message_TheRunDidnT");
        if (!expected.TryGetValue(path, out var fp) || !fp.Matches(path))
            return Strings.Get("FileUndo_Message_ItHasChangedSince");
        return null;
    }

    private static string? Recycle(string path, Func<string, bool> recycle)
    {
        ProtectedPaths.Ensure(path, "delete");
        return recycle(path) ? null : Strings.Get("FileUndo_Message_ItCouldnTGo");
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
