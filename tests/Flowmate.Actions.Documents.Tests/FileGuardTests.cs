using System.IO.Compression;
using Flowmate.Core;

namespace Flowmate.Actions.Documents.Tests;

/// <summary>Guards that keep file actions from destroying more than was meant. Every test works in its own temp folder.</summary>
public class FileGuardTests
{
    // A share on a host that can't exist: protected (a share root), and harmless even if a guard ever failed.
    private const string NoShare = @"\\no-such-host.invalid\share";

    [Fact]
    public void ProtectedPathsRefuseRootsAndKnownFoldersWithAClearMessage()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var ex = Assert.Throws<ActionException>(() => ProtectedPaths.Ensure(docs, "delete"));
        Assert.Equal($"Flow Mate won't delete {docs} itself (a protected folder). Check the path, and any variables in it, still point to what you meant.", ex.Message);
        Assert.Throws<ActionException>(() => ProtectedPaths.Ensure(@"E:\", "delete"));
        Assert.Throws<ActionException>(() => ProtectedPaths.Ensure("E:", "move"));
        // Contents are fine. (A temp folder: a path that isn't protected by its text is opened to follow links.)
        ProtectedPaths.Ensure(Path.Combine(Path.GetTempPath(), "Old invoices " + Guid.NewGuid().ToString("N")[..6]), "delete");
    }

    [Fact]
    public void RecycleBinNeverTurnsADriveRootIntoADriveRelativePath()
    {
        Assert.Equal(@"E:\", RecycleBin.ShellPath(@"E:\"));
        Assert.Equal(@"E:\Photos", RecycleBin.ShellPath(@"E:\Photos\"));
        Assert.Throws<ActionException>(() => RecycleBin.Send(@"E:\"));
    }

    [Fact]
    public void RenameRefusesAProtectedFolder()
    {
        var ex = Assert.Throws<ActionException>(() => RenamePlanner.Execute(new[] { new RenameStep(NoShare, NoShare + " old") }));
        Assert.Contains("protected", ex.Message);
        ex = Assert.Throws<ActionException>(() => BulkRenamer.Execute(new[] { new BulkRenameStep(NoShare, NoShare + " old", false) }));
        Assert.Contains("protected", ex.Message);
    }

    [Fact]
    public async Task DeleteWithAnEmptyVariableInThePathStopsAndDeletesNothing()
    {
        using var d = new TempDir();
        var album = d.File("Pictures/Album/a.jpg", "photo");
        var pictures = Path.Combine(d.Path, "Pictures");
        using var host = T.Host();
        var action = new DeleteFilesAction();
        var ctx = Flowmate.Core.Testing.ActionTestHarness.CreateContext(host, action, null,
            ("files", ParamValue.Text(pictures + @"\", VarRef.Named("Album"))), ("permanently", true));

        var ex = await Assert.ThrowsAsync<ActionException>(() => action.RunAsync(ctx));
        Assert.Contains("'Album' is empty", ex.Message);
        Assert.True(File.Exists(album));
    }

    [Fact]
    public void ReplaceRefusesToPutAFileWhereAFolderIs()
    {
        using var d = new TempDir();
        var src = d.File("in/Photos", "a file with no extension");
        var kept = d.File("out/Photos/beach.jpg", "photo");
        var ex = Assert.Throws<ActionException>(() => Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: true));
        Assert.Contains("Keep both", ex.Message);
        Assert.Equal("photo", File.ReadAllText(kept));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void ReplaceRefusesToPutAFolderWhereAFileIs()
    {
        using var d = new TempDir();
        var srcDir = d.Folder("in/report");
        d.File("in/report/page.txt", "x");
        var kept = d.File("out/report", "the real report");
        Assert.Throws<ActionException>(() => Transfer.Run(new[] { srcDir }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false));
        Assert.Equal("the real report", File.ReadAllText(kept));
    }

    [Fact]
    public void MeasureCountsEveryFileAndByteLikeTheCopy()
    {
        using var d = new TempDir();
        d.File("t/a.txt", "12345");
        d.File("t/sub/b.txt", "123");
        d.File("t/sub/deeper/c.txt", "");
        Assert.Equal((3, 8L), Transfer.Measure(Path.Combine(d.Path, "t")));
    }

    [Fact]
    public async Task SaveFileReplaceIsDestructiveAndNeverReplacesAFolder()
    {
        var desc = new SaveFileAction().Descriptor;
        Assert.True(desc.IsDestructiveFor(ActionInstance.Create("file.save", ("ifExists", ParamValue.Choice("replace")))));
        Assert.False(desc.IsDestructiveFor(ActionInstance.Create("file.save", ("ifExists", ParamValue.Choice("keep")))));

        using var d = new TempDir();
        var dest = d.Folder("out");
        var folder = d.File("out/note.txt/inside.txt", "keep me");
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new SaveFileAction(), ContentValue.Text("new"),
            ("folder", ParamValue.File(dest)), ("name", "note.txt"), ("ifExists", T.Choice("replace"))));
        Assert.Contains("won't replace a folder", ex.Message);
        Assert.Equal("keep me", File.ReadAllText(folder));
    }

    [Fact]
    public void SaveFileReplaceFailsCleanlyWhenTheSourceCantBeRead()
    {
        using var d = new TempDir();
        var src = d.File("src.txt", "new");
        var target = d.File("out/report.txt", "old");
        using (new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<ActionException>(() => SaveFileAction.CopyTo(src, target, replace: true));
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal(new[] { "report.txt" }, Directory.GetFiles(Path.Combine(d.Path, "out")).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void MakeZipNeverDeletesAFileThatWasAlreadyThere()
    {
        using var d = new TempDir();
        var item = d.File("a.txt", "a");
        var existing = d.File("Archive.zip", "someone else's file");
        Assert.ThrowsAny<IOException>(() => ZipTools.Create(new[] { item }, existing));
        Assert.Equal("someone else's file", File.ReadAllText(existing));
    }

    [Fact]
    public void ExtractRefusesZipsOverTheSizeOrEntryLimitAndLeavesNothingBehind()
    {
        using var d = new TempDir();
        var zipPath = Path.Combine(d.Path, "big.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            for (var i = 0; i < 3; i++)
                using (var w = new StreamWriter(zip.CreateEntry($"f{i}.txt").Open())) w.Write(new string('x', 1000));

        var tooBig = Assert.Throws<ActionException>(() => ZipTools.Extract(zipPath, Path.Combine(d.Path, "out1"), default, maxBytes: 2500));
        Assert.Contains("wasn't extracted", tooBig.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(d.Path, "out1")));

        var tooMany = Assert.Throws<ActionException>(() => ZipTools.Extract(zipPath, Path.Combine(d.Path, "out2"), default, maxEntries: 2));
        Assert.Contains("wasn't extracted", tooMany.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(d.Path, "out2")));

        Assert.Equal(3, ZipTools.Extract(zipPath, Path.Combine(d.Path, "out3")).Count);
        Assert.Equal(1000, new FileInfo(Path.Combine(d.Path, "out3", "f0.txt")).Length);
    }

    [Fact]
    public void ExtractCarriesTheMarkOfTheWebAndRefusesStreamNames()
    {
        using var d = new TempDir();
        var zipPath = Path.Combine(d.Path, "download.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("setup.cmd").Open())) w.Write("echo hi");
        const string zone = "[ZoneTransfer]\r\nZoneId=3\r\n";
        File.WriteAllText(zipPath + ":Zone.Identifier", zone);
        var extracted = Assert.Single(ZipTools.Extract(zipPath, Path.Combine(d.Path, "out")));
        Assert.Equal(zone, File.ReadAllText(extracted + ":Zone.Identifier"));

        // "existing.txt:hidden" would add a stream to a file that is already there.
        var existing = Path.Combine(d.Path, "out2", "existing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "mine");
        var streamZip = Path.Combine(d.Path, "stream.zip");
        using (var zip = ZipFile.Open(streamZip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("existing.txt:hidden").Open())) w.Write("payload");
        var ex = Assert.Throws<ActionException>(() => ZipTools.Extract(streamZip, Path.GetDirectoryName(existing)!));
        Assert.Contains("colon", ex.Message);
        Assert.False(File.Exists(existing + ":hidden"));
        Assert.Equal("mine", File.ReadAllText(existing));
    }

    [Fact]
    public void AddToNoteSwapsTheNoteInAndKeepsThePreviousVersion()
    {
        using var d = new TempDir();
        var now = new DateTime(2026, 11, 9, 8, 0, 0);
        var path = NoteStore.Add(d.Path, "Ideas", "first", atTop: false, timestamp: false, now);
        Assert.False(File.Exists(NoteStore.BackupPathFor(path)));
        NoteStore.Add(d.Path, "Ideas", "second", atTop: false, timestamp: false, now);
        Assert.Contains("second", File.ReadAllText(path));
        Assert.Equal("first" + Environment.NewLine, File.ReadAllText(NoteStore.BackupPathFor(path)));
        Assert.Equal(new[] { "Ideas.md", "Ideas.md.flowmate-bak" }, Directory.GetFiles(d.Path).Select(Path.GetFileName).Order().ToArray());
        Assert.Equal(path, NoteStore.Latest(d.Path));
    }
}
