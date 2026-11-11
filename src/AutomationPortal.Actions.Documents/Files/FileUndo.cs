namespace AutomationPortal.Actions.Documents;

/// <summary>One line of the undo plan. <see cref="Automatic"/> is false for what the user has to do (or can't) themselves.</summary>
public sealed record UndoStep(FileChange Change, string Description, bool Automatic);

/// <summary>What happened to one step: done, or skipped with the reason.</summary>
public sealed record UndoOutcome(UndoStep Step, bool Done, string? Problem);

/// <summary>
/// Reverses a run's file changes, newest first. Nothing is forced: new files go to the Recycle Bin only when unchanged since the run,
/// items are moved back only when nothing is in the way, and whatever can't be done safely is skipped and reported.
/// </summary>
public static class FileUndo
{
    public static bool CanUndo(JournaledRun? run) => run is { UndoneAt: null } && run.Changes.Count > 0;

    public static IReadOnlyList<UndoStep> Plan(JournaledRun run) =>
        run.Changes.Reverse().Select(c => new UndoStep(c, Describe(c), c.Kind is not (FileChangeKind.Recycled or FileChangeKind.Deleted or FileChangeKind.Changed))).ToList();

    private static string Describe(FileChange c)
    {
        var name = Path.GetFileName(c.Path.TrimEnd('\\', '/'));
        var dir = Path.GetDirectoryName(c.Path.TrimEnd('\\', '/'));
        return c.Kind switch
        {
            FileChangeKind.Created => $"Send '{name}' in {dir} to the Recycle Bin, if it hasn't changed since the run.",
            FileChangeKind.Moved => $"Move '{name}' back to {Path.GetDirectoryName(c.From!.TrimEnd('\\', '/'))}.",
            FileChangeKind.Renamed => $"Rename '{name}' back to '{Path.GetFileName(c.From!.TrimEnd('\\', '/'))}' in {dir}.",
            FileChangeKind.Replaced when c.Backup is not null => $"Put back the earlier '{name}' in {dir}. The newer one goes to the Recycle Bin, if it hasn't changed since the run.",
            FileChangeKind.Replaced => $"Send the new '{name}' in {dir} to the Recycle Bin, if it hasn't changed since the run. Restore the earlier one from the Recycle Bin yourself.",
            FileChangeKind.Recycled => $"'{name}' from {dir} went to the Recycle Bin. Restore it from there yourself.",
            FileChangeKind.Deleted => $"'{name}' in {dir} was deleted permanently and can't be brought back.",
            _ => $"'{name}' in {dir} was changed in place and no copy was kept, so it stays as it is.",
        };
    }

    /// <param name="recycle">Sends a path to the Recycle Bin and returns true when it is gone. Tests pass their own.</param>
    public static IReadOnlyList<UndoOutcome> Execute(JournaledRun run, FileJournal store, Func<string, bool>? recycle = null, CancellationToken ct = default)
    {
        recycle ??= RecycleBin.TrySend;
        var expected = new Dictionary<string, FileFingerprint>(run.Final ?? new Dictionary<string, FileFingerprint>(), StringComparer.OrdinalIgnoreCase);
        var results = new List<UndoOutcome>();
        foreach (var step in Plan(run))
        {
            ct.ThrowIfCancellationRequested();
            string? problem;
            try { problem = step.Automatic ? Reverse(step.Change, expected, run.Final is not null, recycle) : "Not done automatically."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ActionException)
            {
                problem = ex.Message;
            }
            results.Add(new UndoOutcome(step, problem is null, problem));
        }
        try { store.MarkUndone(run.RunId, DateTimeOffset.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't note that a run was undone: " + ex.Message, ex); }
        return results;
    }

    /// <summary>Undoes one change; returns null when done, else why it was skipped.</summary>
    private static string? Reverse(FileChange c, Dictionary<string, FileFingerprint> expected, bool finished, Func<string, bool> recycle)
    {
        switch (c.Kind)
        {
            case FileChangeKind.Created:
            {
                if (!Exists(c.Path)) return "It isn't there any more.";
                var problem = Unchanged(c.Path, expected, finished);
                if (problem is not null) return problem;
                return Recycle(c.Path, recycle);
            }
            case FileChangeKind.Moved:
            case FileChangeKind.Renamed:
            {
                var from = c.From!;
                if (!Exists(c.Path)) return $"It isn't at {c.Path} any more.";
                var caseOnly = string.Equals(from, c.Path, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && Exists(from)) return $"Something called '{Path.GetFileName(from)}' is already in {Path.GetDirectoryName(from)}, so it was left where it is.";
                ProtectedPaths.Ensure(c.Path, "move");
                Directory.CreateDirectory(Path.GetDirectoryName(from.TrimEnd('\\', '/'))!);
                var note = Directory.Exists(c.Path) ? Transfer.MoveFolder(c.Path, from) : Transfer.MoveFile(c.Path, from);
                if (expected.Remove(c.Path, out var fp)) expected[from] = fp with { Path = from };
                return note;
            }
            case FileChangeKind.Replaced:
            {
                if (Exists(c.Path))
                {
                    var problem = Unchanged(c.Path, expected, finished);
                    if (problem is not null) return problem;
                    var recycled = Recycle(c.Path, recycle);
                    if (recycled is not null) return recycled;
                }
                if (c.Backup is null || !File.Exists(c.Backup))
                    return "The newer one went to the Recycle Bin. No copy of the earlier one was kept, so restore it from the Recycle Bin yourself.";
                if (Exists(c.Path)) return $"Something is already at {c.Path}, so the earlier version wasn't put back. It is kept at {c.Backup}.";
                File.Copy(c.Backup, c.Path, overwrite: false);
                return null;
            }
            default:
                return "Not done automatically.";
        }
    }

    private static string? Unchanged(string path, Dictionary<string, FileFingerprint> expected, bool finished)
    {
        if (!finished) return "The run didn't finish recording, so Automation Portal can't tell whether it was changed since. It was left as it is.";
        if (!expected.TryGetValue(path, out var fp) || !fp.SameContentAs(FileFingerprint.Of(path)))
            return "It has changed since the run, so it was left as it is.";
        return null;
    }

    private static string? Recycle(string path, Func<string, bool> recycle)
    {
        ProtectedPaths.Ensure(path, "delete");
        return recycle(path) ? null : "It couldn't go to the Recycle Bin (it may be open in another program), so it was left as it is.";
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
