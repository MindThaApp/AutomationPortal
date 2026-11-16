using System.Diagnostics;

namespace AutomationPortal.Actions.Documents.Tests;

public class TransferLinkTests
{
    private static bool MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        return Directory.Exists(link) && new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    [Fact]
    public void CopyingAFolderSkipsJunctionsAndSaysSo()
    {
        using var d = new TempDir();
        d.File("elsewhere/secret.txt", "not part of the backup");
        d.File("src/keep.txt", "keep");
        d.File("src/sub/inner.txt", "inner");
        var src = Path.Combine(d.Path, "src");
        Assert.True(MakeJunction(Path.Combine(src, "Linked"), Path.Combine(d.Path, "elsewhere")));
        Assert.True(MakeJunction(Path.Combine(src, "sub", "Loop"), src));   // a loop back to the top

        var r = Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.KeepBoth, move: false);

        var copy = Path.Combine(d.Path, "out", "src");
        Assert.Equal("keep", File.ReadAllText(Path.Combine(copy, "keep.txt")));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(copy, "sub", "inner.txt")));
        Assert.False(Directory.Exists(Path.Combine(copy, "Linked")));
        Assert.False(Directory.Exists(Path.Combine(copy, "sub", "Loop")));
        Assert.Empty(Directory.GetFiles(copy, "secret.txt", SearchOption.AllDirectories));
        var note = Assert.Single(r.Notes);
        Assert.Contains("Skipped 2 linked folders", note);
        Assert.Contains("'Linked'", note);
        // the originals and the junction targets are untouched
        Assert.Equal("not part of the backup", File.ReadAllText(Path.Combine(d.Path, "elsewhere", "secret.txt")));
        Assert.True(Directory.Exists(Path.Combine(src, "Linked")));
    }

    [Fact]
    public void CopyTreeSkipsSymbolicFolderLinks()
    {
        using var d = new TempDir();
        d.File("elsewhere/x.txt", "x");
        d.File("src/a.txt", "a");
        var link = Path.Combine(d.Path, "src", "sym");
        try { Directory.CreateSymbolicLink(link, Path.Combine(d.Path, "elsewhere")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // no right to make links here
        var skipped = new List<string>();
        Transfer.CopyTree(Path.Combine(d.Path, "src"), Path.Combine(d.Path, "copy"), skipped);
        Assert.True(File.Exists(Path.Combine(d.Path, "copy", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(d.Path, "copy", "sym")));
        Assert.Equal(link, Assert.Single(skipped));
    }
}
