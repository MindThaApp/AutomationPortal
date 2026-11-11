using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

/// <summary>The per-run file journal and "Undo file changes". Every test works in its own temp folder and never uses the real Recycle Bin.</summary>
public class FileUndoTests
{
    private static (RunFileJournal Journal, FileJournal Store) NewJournal(TempDir d, string runId = "run1")
    {
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        return (new RunFileJournal(store, runId), store);
    }

    /// <summary>Stands in for the Recycle Bin: moves the item into a "bin" folder of the test.</summary>
    private static Func<string, bool> FakeBin(TempDir d)
    {
        var bin = Path.Combine(d.Path, "bin");
        Directory.CreateDirectory(bin);
        return p =>
        {
            var target = Path.Combine(bin, Guid.NewGuid().ToString("N")[..6] + "-" + Path.GetFileName(p));
            if (Directory.Exists(p)) Directory.Move(p, target); else File.Move(p, target);
            return true;
        };
    }

    private static IReadOnlyList<UndoOutcome> Undo(FileJournal store, string runId, TempDir d) =>
        FileUndo.Execute(store.Read(runId)!, store, FakeBin(d));

    [Fact]
    public void JournalKeepsChangesInOrderWithTheirStateAtTheEnd()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Moved(Path.Combine(d.Path, "a.txt"), Path.Combine(d.Path, "b.txt"));
        j.Seal();

        var run = store.Read("run1")!;
        Assert.Equal(new[] { FileChangeKind.Created, FileChangeKind.Moved }, run.Changes.Select(c => c.Kind));
        Assert.Equal(5, run.Final![Path.GetFullPath(made)].Length);
        Assert.True(FileUndo.CanUndo(run));
        Assert.Null(store.Read("other"));
        Assert.Throws<ArgumentException>(() => store.Append(@"..\x", new FileChange()));
    }

    [Fact]
    public void UndoSendsAnUnchangedNewFileToTheRecycleBin()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Seal();

        var result = Undo(store, "run1", d);
        Assert.True(result.Single().Done);
        Assert.False(File.Exists(made));
        Assert.Single(Directory.GetFiles(Path.Combine(d.Path, "bin")));
        Assert.False(FileUndo.CanUndo(store.Read("run1")));
    }

    [Fact]
    public void UndoLeavesANewFileThatChangedSinceTheRun()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Seal();
        File.WriteAllText(made, "hello, edited afterwards");

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.Contains("changed since the run", result.Problem);
        Assert.Equal("hello, edited afterwards", File.ReadAllText(made));
    }

    [Fact]
    public void UndoLeavesANewFileWhenTheRunNeverFinishedRecording()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.True(File.Exists(made));
    }

    [Fact]
    public void UndoMovesFilesAndFoldersBack()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var file = d.File("in/a.txt", "A");
        var folder = Path.GetDirectoryName(d.File("in/Album/p.jpg", "photo"))!;
        var dest = Path.Combine(d.Path, "out");
        var r = Transfer.Run(new[] { file, folder }, dest, "", ConflictMode.KeepBoth, move: true, journal: j);
        j.Seal();
        Assert.False(File.Exists(file));

        Assert.All(Undo(store, "run1", d), o => Assert.True(o.Done, o.Problem));
        Assert.Equal("A", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(folder, "p.jpg")));
        Assert.All(r.Paths, p => Assert.False(File.Exists(p) || Directory.Exists(p)));
    }

    [Fact]
    public void UndoNeverMovesOverSomethingNowAtTheOriginalPlace()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var file = d.File("in/a.txt", "A");
        var moved = Transfer.Run(new[] { file }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: true, journal: j).Paths.Single();
        j.Seal();
        File.WriteAllText(file, "a new file with the old name");

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.Contains("already in", result.Problem);
        Assert.Equal("a new file with the old name", File.ReadAllText(file));
        Assert.Equal("A", File.ReadAllText(moved));
    }

    [Fact]
    public void UndoRenamesBackIncludingASwap()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var a = d.File("a.txt", "A");
        var b = d.File("b.txt", "B");
        var plan = RenamePlanner.Plan(new[] { (a, "b.txt"), (b, "a.txt") });
        RenamePlanner.Execute(plan);
        foreach (var s in plan) j.Renamed(s.Source, s.Target);
        j.Seal();
        Assert.Equal("B", File.ReadAllText(a));

        var results = Undo(store, "run1", d);
        // A swap can't be undone one rename at a time without overwriting, so both are skipped and nothing is lost.
        Assert.All(results, o => Assert.False(o.Done));
        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));
    }

    [Fact]
    public void UndoRenameBackToTheOldName()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var a = d.File("a.txt", "A");
        var plan = RenamePlanner.Plan(new[] { (a, "Holiday 001.txt") });
        RenamePlanner.Execute(plan);
        j.Renamed(plan[0].Source, plan[0].Target);
        j.Seal();

        Assert.True(Undo(store, "run1", d).Single().Done);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.False(File.Exists(plan[0].Target));
    }

    [Fact]
    public void CopyKeepsBothWithUniqueNamesAndUndoRemovesOnlyTheCopies()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var src = d.File("in/a.txt", "new");
        var existing = d.File("out/a.txt", "old");
        var first = Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: false, journal: j).Paths.Single();
        var second = Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: false, journal: j).Paths.Single();
        j.Seal();
        Assert.Equal(Path.Combine(d.Path, "out", "a 2.txt"), first);
        Assert.Equal(Path.Combine(d.Path, "out", "a 3.txt"), second);
        Assert.Equal("old", File.ReadAllText(existing));

        Assert.All(Undo(store, "run1", d), o => Assert.True(o.Done, o.Problem));
        Assert.False(File.Exists(first) || File.Exists(second));
        Assert.Equal("old", File.ReadAllText(existing));
        Assert.Equal("new", File.ReadAllText(src));
    }

    [Fact]
    public void ReplaceKeepsABackupThatUndoPutsBack()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var src = d.File("in/a.txt", "new version");
        var target = d.File("out/a.txt", "old version");
        // The replaced file goes to the Recycle Bin during the run too; this test only checks the journal's copy.
        var backup = j.BackUp(target);
        Assert.NotNull(backup);
        File.WriteAllText(target, "new version");
        j.Replaced(target, backup);
        j.Seal();

        var result = Undo(store, "run1", d).Single();
        Assert.True(result.Done, result.Problem);
        Assert.Equal("old version", File.ReadAllText(target));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void RecycledAndPermanentDeletesAreReportedNotForced()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        j.Recycled(Path.Combine(d.Path, "gone.txt"));
        j.Deleted(Path.Combine(d.Path, "really-gone.txt"));
        j.Seal();

        var plan = FileUndo.Plan(store.Read("run1")!);
        Assert.All(plan, s => Assert.False(s.Automatic));
        Assert.Contains("deleted permanently", plan[0].Description);
        Assert.Contains("Restore it from there", plan[1].Description);
        Assert.All(Undo(store, "run1", d), o => Assert.False(o.Done));
    }

    [Fact]
    public void CrossDriveMoveCopiesVerifiesThenDeletesTheOriginal()
    {
        using var d = new TempDir();
        var src = d.File("in/movie.bin", new string('x', 100_000));
        var target = Path.Combine(d.Path, "out", "movie.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        Assert.Null(Transfer.MoveFileByCopy(src, target));
        Assert.False(File.Exists(src));
        Assert.Equal(100_000, new FileInfo(target).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(d.Path, "out"), "*.autoportal-new-*"));

        // Never over an existing file: the original stays where it is.
        var again = d.File("in/movie.bin", "other");
        Assert.ThrowsAny<IOException>(() => Transfer.MoveFileByCopy(again, target));
        Assert.Equal("other", File.ReadAllText(again));
        Assert.Equal(100_000, new FileInfo(target).Length);
    }

    [Fact]
    public void PreviewListsTheStepsAndChangesNothing()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "A");
        var b = d.File("in/sub/a.txt", "A2");
        d.File("out/a.txt", "there already");
        var lines = new List<string>();
        var r = Transfer.Run(new[] { a, b }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: true, preview: lines);

        Assert.Equal(2, lines.Count);
        Assert.EndsWith("a 2.txt", r.Paths[0]);
        Assert.EndsWith("a 3.txt", r.Paths[1]);
        Assert.True(File.Exists(a) && File.Exists(b));
        Assert.Single(Directory.GetFiles(Path.Combine(d.Path, "out")));
    }

    [Fact]
    public void PurgeKeepsTheLatestRunsOfTheLastWeekAndOnlyItsOwnFiles()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
        {
            store.Append($"run{i:00}", new FileChange { Kind = FileChangeKind.Created, Path = "x" });
            File.SetLastWriteTimeUtc(Path.Combine(store.Folder, $"run{i:00}.jsonl"), now.UtcDateTime.AddMinutes(i - 30));
        }
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "run24.jsonl"), now.UtcDateTime.AddDays(-8));
        var backup = store.KeepBackup("run00", d.File("keep/me.txt", "copy me"))!;
        var stranger = d.File("journal/notes.txt", "not the journal's");

        store.Purge(now);

        var left = Directory.GetFiles(store.Folder, "*.jsonl").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.Equal(20, left.Count);
        Assert.DoesNotContain("run24", left);
        Assert.DoesNotContain("run00", left);
        Assert.False(File.Exists(backup));
        Assert.True(File.Exists(stranger));
        Assert.True(File.Exists(Path.Combine(d.Path, "keep", "me.txt")));
    }

    [Fact]
    public async Task ActionsInARunAreJournalledUnderItsRunId()
    {
        using var d = new TempDir();
        var src = d.File("in/a.txt", "A");
        using var host = T.Host();
        var action = new CopyFilesAction();
        var ctx = AutomationPortal.Core.Testing.ActionTestHarness.CreateContext(host, action, null,
            ("files", ParamValue.File(src)), ("folder", ParamValue.File(Path.Combine(d.Path, "out"))));
        await action.RunAsync(ctx);
        ctx.Journal.Seal();

        var run = new FileJournal(ctx.Services.Paths).Read(ctx.RunId)!;
        Assert.Equal(FileChangeKind.Created, run.Changes.Single().Kind);
        Assert.Equal(Path.Combine(d.Path, "out", "a.txt"), run.Changes.Single().Path);
    }
}
