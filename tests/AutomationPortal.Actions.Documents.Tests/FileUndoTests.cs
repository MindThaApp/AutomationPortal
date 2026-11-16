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
        FileUndo.Execute(store.Read(runId)!, store, FakeBin(d), canRecycle: _ => true);

    private static string[] InBin(TempDir d) =>
        Directory.Exists(Path.Combine(d.Path, "bin")) ? Directory.GetFileSystemEntries(Path.Combine(d.Path, "bin")) : Array.Empty<string>();

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
        Assert.Equal(Path.Combine(d.Path, "out", "a (2).txt"), first);
        Assert.Equal(Path.Combine(d.Path, "out", "a (3).txt"), second);
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
        Assert.EndsWith("a (2).txt", r.Paths[0]);
        Assert.EndsWith("a (3).txt", r.Paths[1]);
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
            File.SetLastWriteTimeUtc(Path.Combine(store.Folder, $"run{i:00}.jsonl"), now.UtcDateTime.AddDays(-2).AddMinutes(i));
        }
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "run24.jsonl"), now.UtcDateTime.AddDays(-8));
        var backup = store.KeepBackup("run00", d.File("keep/me.txt", "copy me"))!;
        Directory.SetLastWriteTimeUtc(store.BackupFolderFor("run00"), now.UtcDateTime.AddDays(-2));
        var stranger = d.File("journal/notes.txt", "not the journal's");

        // The first purge only notes what has expired; a purge a day later removes it.
        store.Purge(now);
        Assert.Equal(25, Directory.GetFiles(store.Folder, "*.jsonl").Length);
        store.Purge(now.AddHours(25));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacedUndoWithAMissingOrDamagedBackupTouchesNothing(bool damaged)
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var target = d.File("out/a.txt", "old version");
        var backup = j.BackUp(target)!;
        File.WriteAllText(target, "new version");
        j.Replaced(target, backup);
        j.Seal();
        if (damaged) File.WriteAllText(backup, "old versioN"); else File.Delete(backup);

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.Contains("missing or damaged", result.Problem);
        Assert.Equal("new version", File.ReadAllText(target));
        Assert.Empty(InBin(d));
        Assert.Single(Directory.GetFiles(Path.Combine(d.Path, "out")));
    }

    [Fact]
    public async Task AppendKeepsACheckedBackupThatUndoPutsBack()
    {
        using var d = new TempDir();
        var path = d.File("notes.txt", "first line\n");
        using var host = T.Host();
        var action = new AppendToFileAction();
        var ctx = AutomationPortal.Core.Testing.ActionTestHarness.CreateContext(host, action, ContentValue.Text("added"), ("file", ParamValue.File(path)));
        await action.RunAsync(ctx);
        ctx.Journal.Seal();
        Assert.Contains("added", File.ReadAllText(path));

        var store = new FileJournal(ctx.Services.Paths);
        var change = store.Read(ctx.RunId)!.Changes.Single();
        Assert.Equal(FileChangeKind.Replaced, change.Kind);
        Assert.Equal(Transfer.Sha256Hex(change.Backup!), change.BackupSha256);

        Assert.True(Undo(store, ctx.RunId, d).Single().Done);
        Assert.Equal("first line\n", File.ReadAllText(path));
        Assert.Single(InBin(d));
    }

    [Fact]
    public async Task AnAppendToAFileTheRunMovedIsRecordedAndUndoneBeforeTheMove()
    {
        using var d = new TempDir();
        var src = d.File("in/log.txt", "start\n");
        var moved = Path.Combine(d.Path, "out", "log.txt");
        using var host = T.Host();
        var move = new MoveFilesAction();
        var ctx = AutomationPortal.Core.Testing.ActionTestHarness.CreateContext(host, move, null,
            ("files", ParamValue.File(src)), ("folder", ParamValue.File(Path.Combine(d.Path, "out"))));
        await move.RunAsync(ctx);
        var append = new AppendToFileAction();
        var appendCtx = new ActionContext(ctx.Scope, ActionInstance.Create(append.Descriptor.Id, ("file", ParamValue.File(moved))), append.Descriptor, ContentValue.Text("more"));
        await append.RunAsync(appendCtx);
        ctx.Journal.Seal();

        var store = new FileJournal(ctx.Services.Paths);
        Assert.Equal(new[] { FileChangeKind.Moved, FileChangeKind.Replaced }, store.Read(ctx.RunId)!.Changes.Select(c => c.Kind));
        Assert.All(Undo(store, ctx.RunId, d), o => Assert.True(o.Done, o.Problem));
        Assert.Equal("start\n", File.ReadAllText(src));
        Assert.False(File.Exists(moved));
    }

    private static void MakeBig(string path, byte marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.CreateNew);
        fs.SetLength(FileJournal.MaxBackupBytes + 1);
        fs.WriteByte(marker);
    }

    [Fact]
    public void ReplacingAFileOver256MbKeepsNoCopyAndIsCheckedBySizeAndDateOnly()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var src = Path.Combine(d.Path, "in", "big.bin");
        var target = Path.Combine(d.Path, "out", "big.bin");
        MakeBig(src, 1);
        MakeBig(target, 2);
        Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false, journal: j, recycle: FakeBin(d));
        j.Seal();

        var run = store.Read("run1")!;
        var change = run.Changes.Single();
        Assert.Equal(FileChangeKind.Replaced, change.Kind);
        Assert.Null(change.Backup);
        Assert.False(Directory.Exists(store.BackupFolderFor("run1")));
        Assert.Null(run.Final![target].Sha256);
        Assert.Contains("size and date only", FileUndo.Plan(run, _ => true).Single().Description);

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.Contains("restore it from the Recycle Bin yourself", result.Problem);
        Assert.False(File.Exists(target));
        Assert.True(File.Exists(src));
        Assert.Equal(2, InBin(d).Length);
    }

    [Fact]
    public void ReplacingAFolderIsUndoneByRecyclingOnlyTheNewCopy()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var srcFolder = Path.GetDirectoryName(d.File("in/Album/p.jpg", "new photo"))!;
        d.File("out/Album/old.jpg", "old photo");
        Transfer.Run(new[] { srcFolder }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false, journal: j, recycle: FakeBin(d));
        j.Seal();

        var run = store.Read("run1")!;
        var change = run.Changes.Single();
        Assert.Equal(FileChangeKind.Replaced, change.Kind);
        Assert.Null(change.Backup);
        Assert.NotNull(run.Final![change.Path].Sha256);

        var result = Undo(store, "run1", d).Single();
        Assert.Contains("restore it from the Recycle Bin yourself", result.Problem);
        Assert.False(Directory.Exists(Path.Combine(d.Path, "out", "Album")));
        Assert.Equal("new photo", File.ReadAllText(Path.Combine(srcFolder, "p.jpg")));
        Assert.Equal(2, InBin(d).Length);
    }

    [Fact]
    public void PurgeNeverTouchesARunStillGoing()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var j = new RunFileJournal(store, "going");
        var backup = j.BackUp(d.File("a.txt", "A"))!;
        Assert.True(store.Has("going"));
        var old = DateTime.UtcNow.AddDays(-10);
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "going.jsonl"), old);
        Directory.SetLastWriteTimeUtc(store.BackupFolderFor("going"), old);
        var now = DateTimeOffset.UtcNow;

        store.Purge(now);
        store.Purge(now.AddHours(25));
        store.Purge(now.AddHours(50));
        Assert.True(File.Exists(backup));
        Assert.True(store.Has("going"));

        j.Seal();
        store.Purge(now.AddHours(75));
        store.Purge(now.AddHours(100));
        Assert.False(File.Exists(backup));
        Assert.False(store.Has("going"));
    }

    [Fact]
    public void PurgeRemovesABackupFolderWithoutAJournalOnlyWhenOldAndSeenADayApart()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var orphan = d.File("journal/orphan.backup/x-a.txt", "copy");
        var fresh = d.File("journal/fresh.backup/x-b.txt", "copy");
        var now = DateTimeOffset.UtcNow;
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(orphan)!, now.UtcDateTime.AddDays(-3));

        store.Purge(now);
        Assert.True(File.Exists(orphan));
        store.Purge(now.AddHours(25));
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void APurgeWithTheClockAheadOnceRemovesNothing()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            store.Append($"run{i}", new FileChange { Kind = FileChangeKind.Created, Path = "x" });
            File.SetLastWriteTimeUtc(Path.Combine(store.Folder, $"run{i}.jsonl"), now.UtcDateTime.AddDays(-2));
        }

        store.Purge(now.AddDays(365));
        store.Purge(now.AddHours(1));
        store.Purge(now.AddHours(30));
        Assert.Equal(3, Directory.GetFiles(store.Folder, "*.jsonl").Length);
    }

    [Fact]
    public void ATamperedJournalCantPointUndoOutsideTheRunOrAtProtectedOrDevicePaths()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var target = Path.GetFullPath(d.File("out/a.txt", "current"));
        var outside = Path.GetFullPath(d.File("elsewhere/planted.txt", "planted"));
        var victim = Path.GetFullPath(d.File("in/victim.txt", "keep me"));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        store.Append("run1", new FileChange { Kind = FileChangeKind.Replaced, Path = target, Backup = outside, BackupSha256 = Transfer.Sha256Hex(outside) });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Replaced, Path = target, Backup = store.BackupFolderFor("run1") + @"\..\..\elsewhere\planted.txt" });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Moved, Path = victim, From = windows });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Replaced, Path = windows });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Created, Path = @"\\?\" + victim });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Created, Path = @"\\.\" + victim });
        store.Append("run1", new FileChange { Kind = FileChangeKind.Moved, Path = victim });
        File.AppendAllText(Path.Combine(store.Folder, "run1.jsonl"), "not json at all\n{\"change\":{\"kind\":\"Created\",\"pa");
        store.Seal("run1", new[] { FileFingerprint.Of(target)!, FileFingerprint.Of(victim)! });

        var run = store.Read("run1")!;
        Assert.Equal(7, run.Changes.Count);
        Assert.NotNull(run.Final);
        var results = Undo(store, "run1", d);
        Assert.All(results, o => Assert.False(o.Done));
        var planted = results.Single(o => o.Step.Change.Backup == outside).Problem!;
        Assert.True(planted.Contains("outside this run's own folder", StringComparison.Ordinal), planted);
        Assert.Equal(2, results.Count(o => o.Problem!.Contains("protected", StringComparison.Ordinal)));
        Assert.Equal(4, results.Count(o => !o.Step.Automatic && o.Step.Description.Contains("damaged entry", StringComparison.Ordinal)));
        Assert.Equal("current", File.ReadAllText(target));
        Assert.Equal("keep me", File.ReadAllText(victim));
        Assert.Equal("planted", File.ReadAllText(outside));
        Assert.Empty(InBin(d));
    }

    [Fact]
    public void ACrossDriveMoveWhoseCopyDoesntMatchKeepsTheOriginalAndCleansUp()
    {
        using var d = new TempDir();
        var src = d.File("in/movie.bin", new string('x', 10_000));
        var target = Path.Combine(d.Path, "out", "movie.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        Assert.Throws<ActionException>(() => Transfer.MoveFileByCopy(src, target, copy => File.AppendAllText(copy, "corrupted")));
        Assert.Equal(10_000, new FileInfo(src).Length);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(d.Path, "out")));

        // Nothing can write to the original between hashing and deleting it.
        Assert.Null(Transfer.MoveFileByCopy(src, target, _ => Assert.ThrowsAny<IOException>(() => File.AppendAllText(src, "late edit"))));
        Assert.False(File.Exists(src));
        Assert.Equal(10_000, new FileInfo(target).Length);
    }

    [Fact]
    public void UndoOfAMoveThatReplacedAFilePutsBothBack()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var src = d.File("in/a.txt", "moved in");
        var target = d.File("out/a.txt", "was there");
        Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: true, journal: j, recycle: FakeBin(d));
        j.Seal();
        Assert.Equal("moved in", File.ReadAllText(target));
        Assert.False(File.Exists(src));

        Assert.All(Undo(store, "run1", d), o => Assert.True(o.Done, o.Problem));
        Assert.Equal("moved in", File.ReadAllText(src));
        Assert.Equal("was there", File.ReadAllText(target));
    }

    [Fact]
    public async Task ASecondUndoOfTheSameRunIsRefusedWhileTheFirstRuns()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Seal();
        var run = store.Read("run1")!;
        var bin = FakeBin(d);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var first = Task.Run(() => FileUndo.Execute(run, store, p => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return bin(p); }, canRecycle: _ => true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(store.IsUndoing("run1"));
        Assert.Contains("already being undone", Assert.Throws<ActionException>(() => FileUndo.Execute(run, store, bin, canRecycle: _ => true)).Message);
        release.Set();

        Assert.True((await first).Single().Done);
        Assert.False(store.IsUndoing("run1"));
        Assert.Contains("already undone", Assert.Throws<ActionException>(() => FileUndo.Execute(run, store, bin, canRecycle: _ => true)).Message);
    }

    [Fact]
    public void AnUndoLeftUnfinishedByAnAppThatClosedDoesntBlockTheNextOne()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Seal();
        File.AppendAllText(Path.Combine(store.Folder, "run1.jsonl"),
            $"{{\"undoing\":{{\"at\":\"{DateTimeOffset.Now:O}\",\"processId\":{Environment.ProcessId},\"processStarted\":1}}}}\n");

        Assert.True(Undo(store, "run1", d).Single().Done);
    }

    [Fact]
    public void AChangeWithTheSameSizeAndDateIsCaughtByTheHash()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        j.Seal();
        var written = File.GetLastWriteTimeUtc(made);
        File.WriteAllText(made, "jello");
        File.SetLastWriteTimeUtc(made, written);

        var result = Undo(store, "run1", d).Single();
        Assert.False(result.Done);
        Assert.Contains("changed since the run", result.Problem);
        Assert.Equal("jello", File.ReadAllText(made));
    }

    [Fact]
    public void ARenameChainIsJournalledInOrderAndUndoneCompletely()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var a = d.File("a.txt", "A");
        var b = d.File("b.txt", "B");
        var c = Path.Combine(d.Path, "c.txt");
        var plan = RenamePlanner.Plan(new[] { (a, "b.txt"), (b, "c.txt") });
        RenamePlanner.Execute(plan);
        foreach (var s in RenameFilesAction.JournalOrder(plan)) j.Renamed(s.Source, s.Target);
        j.Seal();
        Assert.Equal("B", File.ReadAllText(c));

        Assert.All(Undo(store, "run1", d), o => Assert.True(o.Done, o.Problem));
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
        Assert.False(File.Exists(c));
    }

    [Fact]
    public void OnADriveWithoutARecycleBinNothingIsRemoved()
    {
        using var d = new TempDir();
        var (j, store) = NewJournal(d);
        var made = d.File("out/new.txt", "hello");
        j.Created(made);
        var target = d.File("out/a.txt", "old");
        var backup = j.BackUp(target);
        File.WriteAllText(target, "new");
        j.Replaced(target, backup);
        j.Seal();

        var run = store.Read("run1")!;
        var plan = FileUndo.Plan(run, _ => false);
        Assert.All(plan, s => Assert.False(s.Automatic));
        Assert.All(plan, s => Assert.Contains("without a Recycle Bin", s.Description));
        Assert.All(FileUndo.Execute(run, store, FakeBin(d), canRecycle: _ => false), o => Assert.False(o.Done));
        Assert.Equal("hello", File.ReadAllText(made));
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Empty(InBin(d));
    }
}
