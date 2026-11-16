using System.IO;
using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>The result bar after a run, its one-click Undo report, and Undo after deleting a shortcut. Everything stays under %TEMP%.</summary>
public class TrustBarTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-tests", "trust-" + Guid.NewGuid().ToString("N")[..8]);
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);

    public void Dispose()
    {
        // Only the folder this test made under %TEMP%.
        try { if (_root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true); } catch { }
    }

    private static JournaledRun Run(string id, params FileChange[] changes) => new() { RunId = id, Changes = changes };

    [Fact]
    public void Bar_offers_Undo_only_when_the_run_recorded_changes_undo_can_put_back()
    {
        var moved = Run("r1",
            new FileChange { Kind = FileChangeKind.Moved, From = @"C:\in\a.txt", Path = @"C:\out\a.txt" },
            new FileChange { Kind = FileChangeKind.Moved, From = @"C:\in\b.txt", Path = @"C:\out\b.txt" });
        var bar = RunResultText.ForRun("Tidy Downloads", "r1", moved, _ => true);
        Assert.NotNull(bar);
        Assert.True(bar!.CanUndo);
        Assert.Equal("\"Tidy Downloads\" moved 2 files.", bar.Title);

        // Nothing changed: no bar at all.
        Assert.Null(RunResultText.ForRun("Tidy", "r2", Run("r2"), _ => true));
        Assert.Null(RunResultText.ForRun("Tidy", "r2", null, _ => true));

        // Only things undo can't do by itself: the bar says what happened, without Undo.
        var recycled = RunResultText.ForRun("Clean", "r3", Run("r3", new FileChange { Kind = FileChangeKind.Recycled, Path = @"C:\in\old.txt" }), _ => true);
        Assert.False(recycled!.CanUndo);
        Assert.Contains("sent 1 file to the Recycle Bin", recycled.Title);
        Assert.Contains("can't be undone from here", recycled.Message);

        // A new file on a drive without a Recycle Bin can't be undone safely.
        var noBin = RunResultText.ForRun("Make", "r4", Run("r4", new FileChange { Kind = FileChangeKind.Created, Path = @"C:\out\new.txt" }), _ => false);
        Assert.False(noBin!.CanUndo);

        // Already undone, or the record belongs to another run: no Undo.
        var undone = new JournaledRun { RunId = "r1", Changes = moved.Changes, UndoneAt = DateTimeOffset.Now };
        Assert.False(RunResultText.ForRun("Tidy", "r1", undone, _ => true)!.CanUndo);
        Assert.Null(RunResultText.ForRun("Tidy", "other", moved, _ => true));
    }

    [Fact]
    public void Summary_reads_as_a_sentence()
    {
        var changes = new[]
        {
            new FileChange { Kind = FileChangeKind.Moved, Path = @"C:\a" },
            new FileChange { Kind = FileChangeKind.Renamed, Path = @"C:\b" },
            new FileChange { Kind = FileChangeKind.Moved, Path = @"C:\c" },
            new FileChange { Kind = FileChangeKind.Created, Path = @"C:\d" },
        };
        Assert.Equal("moved 2 files, renamed 1 file and made 1 file", RunResultText.Summary(changes));
    }

    [Fact]
    public void Partial_undo_says_exactly_what_was_and_wasnt_put_back_and_overwrites_nothing()
    {
        var inDir = Directory.CreateDirectory(Path.Combine(_root, "in")).FullName;
        var outDir = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var a = Path.Combine(outDir, "a.txt");
        var b = Path.Combine(outDir, "b.txt");
        var c = Path.Combine(outDir, "c.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        File.WriteAllText(c, "c");
        // Something else now has c.txt's old name, so it must not be put back over it.
        File.WriteAllText(Path.Combine(inDir, "c.txt"), "someone else's file");

        var store = new FileJournal(Paths);
        const string runId = "run-partial-1";
        store.Start(runId, DateTimeOffset.Now);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            store.Append(runId, new FileChange { Kind = FileChangeKind.Moved, From = Path.Combine(inDir, name), Path = Path.Combine(outDir, name), At = DateTimeOffset.Now });
        store.Seal(runId, new[] { a, b, c }.Select(p => FileFingerprint.Of(p)!));
        File.AppendAllText(b, " changed after the run");

        var run = store.Read(runId)!;
        Assert.True(RunResultText.ForRun("Tidy", runId, run, _ => true)!.CanUndo);
        var results = FileUndo.Execute(run, store, _ => false, default, _ => true);
        var (title, message) = RunResultText.UndoReport(results);

        Assert.Equal("Put back 1 of 3 changes", title);
        Assert.Contains("'b.txt': It has changed since the run", message);
        Assert.Contains("'c.txt': Something called 'c.txt' is already in", message);
        Assert.DoesNotContain("'a.txt'", message);
        Assert.Contains("Nothing was overwritten.", message);
        Assert.True(File.Exists(Path.Combine(inDir, "a.txt")));
        Assert.True(File.Exists(b));
        Assert.Equal("someone else's file", File.ReadAllText(Path.Combine(inDir, "c.txt")));
        Assert.Equal("c", File.ReadAllText(c));
    }

    [Fact]
    public void Undo_report_when_everything_or_nothing_was_put_back()
    {
        var plan = FileUndo.Plan(Run("r1", new FileChange { Kind = FileChangeKind.Moved, From = @"C:\in\a.txt", Path = @"C:\out\a.txt" }), _ => true);
        Assert.Equal(("Undone", "The change was put back."), RunResultText.UndoReport(new[] { new UndoOutcome(plan[0], true, null) }));
        var none = RunResultText.UndoReport(new[] { new UndoOutcome(plan[0], false, "It isn't there any more.") });
        Assert.Equal("Nothing was put back", none.Title);
        Assert.Contains("'a.txt': It isn't there any more.", none.Message);
    }

    [Fact]
    public void Delete_then_Undo_brings_the_shortcut_back()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var b = new ShortcutBuilder("Clean Link");
        b.Add("flow.nothing");
        var s = b.Build();
        lib.Save(s);
        var since = DateTimeOffset.Now;

        lib.Delete(s.Id);
        Assert.Null(lib.GetById(s.Id));

        var (ok, title, _) = DeleteUndo.Undo(lib, s.Id, s.Name, since);
        Assert.True(ok);
        Assert.Equal("Restored \"Clean Link\".", title);
        Assert.NotNull(lib.GetById(s.Id));
        Assert.Empty(lib.RecentlyDeleted.List());

        // A second Undo finds nothing to restore and says so.
        var again = DeleteUndo.Undo(lib, s.Id, s.Name, since);
        Assert.False(again.Ok);
        Assert.Contains("isn't in Recently deleted", again.Title + again.Message);
    }

    [Fact]
    public void Delete_undo_ignores_older_deletes_of_the_same_shortcut()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var b = new ShortcutBuilder("Old one");
        b.Add("flow.nothing");
        var s = b.Build();
        lib.Save(s);
        lib.Delete(s.Id);
        Assert.Null(DeleteUndo.FindEntry(lib.RecentlyDeleted.List(), s.Id, DateTimeOffset.Now.AddMinutes(5)));
        Assert.NotNull(DeleteUndo.FindEntry(lib.RecentlyDeleted.List(), s.Id, DateTimeOffset.Now.AddMinutes(-1)));
    }
}
