using Flowmate.Core;

namespace Flowmate.Actions.Documents.Tests;

public class TransferSafetyTests
{
    [Fact]
    public void ReplaceRefusesWhenSourceIsInsideTheFolderItWouldReplace()
    {
        using var d = new TempDir();
        var inner = d.File("Proj/Proj/readme.txt", "keep me");
        var innerDir = Path.Combine(d.Path, "Proj", "Proj");
        var ex = Assert.Throws<ActionException>(() => Transfer.Run(new[] { innerDir }, d.Path, "", ConflictMode.Replace, move: true));
        Assert.Contains("inside", ex.Message);
        Assert.True(File.Exists(inner));
        Assert.Equal("keep me", File.ReadAllText(inner));
    }

    [Fact]
    public void ReplaceFileKeepsOldOneUntilNewIsInPlaceAndThenReplaces()
    {
        using var d = new TempDir();
        var src = d.File("in/a.txt", "new");
        d.File("out/a.txt", "old");
        var r = Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: true);
        Assert.Equal("new", File.ReadAllText(Path.Combine(d.Path, "out", "a.txt")));
        Assert.False(File.Exists(src));
        Assert.Empty(Directory.GetFiles(Path.Combine(d.Path, "out"), "*flowmate-old*"));
        Assert.Single(r.Paths);
    }

    [Fact]
    public void FailedReplaceRestoresTheOriginal()
    {
        using var d = new TempDir();
        var srcDir = d.Folder("in/box");
        var locked = d.File("in/box/f.txt", "x");
        d.File("out/box/old.txt", "old");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        // same-volume Directory.Move of a folder with an open file may succeed; force a failure by copying instead
        Assert.ThrowsAny<ActionException>(() => Transfer.Run(new[] { srcDir }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false));
        Assert.Equal("old", File.ReadAllText(Path.Combine(d.Path, "out", "box", "old.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(d.Path, "out"), "*flowmate-old*"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(d.Path, "out"), "*flowmate-copying*"));
    }

    [Fact]
    public void A_folder_copy_is_built_aside_and_renamed_into_place()
    {
        using var d = new TempDir();
        var srcDir = d.Folder("in/box");
        d.File("in/box/a.txt", "a");
        d.File("in/box/sub/b.txt", "b");
        var r = Transfer.Run(new[] { srcDir }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: false);
        var target = Assert.Single(r.Paths);
        Assert.Equal("b", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(d.Path, "out"), "*flowmate-copying*"));
        Assert.Empty(r.Notes);
        Assert.True(File.Exists(Path.Combine(srcDir, "a.txt")));
    }

    [Fact]
    public void MoveAndCopyAreDestructiveOnlyWhenReplacing()
    {
        var d = new MoveFilesAction().Descriptor;
        Assert.False(d.IsDestructiveFor(ActionInstance.Create("file.move", ("ifExists", ParamValue.Choice("keep")))));
        Assert.True(d.IsDestructiveFor(ActionInstance.Create("file.move", ("ifExists", ParamValue.Choice("replace")))));
        Assert.True(new CopyFilesAction().Descriptor.IsDestructiveFor(ActionInstance.Create("file.copy", ("ifExists", ParamValue.Choice("replace")))));
    }
}
