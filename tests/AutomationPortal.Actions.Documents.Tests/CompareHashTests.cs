using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

public class CompareHashTests
{
    [Fact]
    public async Task SameAndDifferent()
    {
        using var d = new TempDir();
        var a = d.File("a.bin"); File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 });
        var same = d.File("same.bin"); File.WriteAllBytes(same, new byte[] { 1, 2, 3, 4 });
        var size = d.File("size.bin"); File.WriteAllBytes(size, new byte[] { 1, 2, 3 });
        var bytes = d.File("bytes.bin"); File.WriteAllBytes(bytes, new byte[] { 1, 2, 3, 5 });
        Assert.True(await FileCompare.AreSameAsync(a, same));
        Assert.False(await FileCompare.AreSameAsync(a, size));
        Assert.False(await FileCompare.AreSameAsync(a, bytes));
    }

    [Fact]
    public async Task LargeFilesDifferingLate()
    {
        using var d = new TempDir();
        var x = new byte[300_000]; var y = new byte[300_000]; y[^1] = 1;
        var a = d.File("a.bin"); File.WriteAllBytes(a, x);
        var b = d.File("b.bin"); File.WriteAllBytes(b, y);
        Assert.False(await FileCompare.AreSameAsync(a, b));
    }

    [Fact]
    public async Task ActionAcceptsFolderAndShowsDiff()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = d.File("one/notes.txt", "a\nb\nc\n");
        d.File("two/notes.txt", "a\nB\nc\nd\n");
        var same = await T.Run(host, new CompareFilesAction(), ContentValue.Files(new[] { a }), ("second", Path.Combine(d.Path, "two")));
        Assert.False(((BooleanItem)same[0]).Value);
        var diff = await T.Run(host, new CompareFilesAction(), ContentValue.Files(new[] { a }), ("second", Path.Combine(d.Path, "two")), ("mode", T.Choice("diff")));
        var lines = diff.AsText().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(new[] { "- 2: b", "+ 2: B", "+ 4: d" }, lines);
    }

    [Fact]
    public void DiffRejectsBinaryAndHugeFiles()
    {
        using var d = new TempDir();
        var t = d.File("t.txt", "hello");
        var bin = d.File("b.dat"); File.WriteAllBytes(bin, new byte[] { 65, 0, 66 });
        var big = d.File("big.txt"); File.WriteAllBytes(big, new byte[FileCompare.MaxDiffBytes + 1]);
        Assert.Contains("isn't a text file", Assert.Throws<ActionException>(() => FileCompare.DiffText(t, bin)).Message);
        Assert.Contains("too big", Assert.Throws<ActionException>(() => FileCompare.DiffText(t, big)).Message);
        Assert.Equal("The files have the same text.", FileCompare.DiffText(t, t));
    }

    [Fact]
    public async Task HashKnownVectors()
    {
        using var d = new TempDir();
        var abc = d.File("abc.txt"); File.WriteAllBytes(abc, "abc"u8.ToArray());
        var empty = d.File("empty.txt", "");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await FileHasher.HashAsync(abc, "sha256"));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", await FileHasher.HashAsync(abc, "md5"));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", await FileHasher.HashAsync(abc, "sha1"));
        Assert.StartsWith("ddaf35a193617aba", await FileHasher.HashAsync(abc, "sha512"));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", await FileHasher.HashAsync(empty, "sha256"));
    }

    [Fact]
    public async Task HashActionOutputsOnePerFileAndCancels()
    {
        using var d = new TempDir(); using var host = T.Host();
        var abc = d.File("abc.txt"); File.WriteAllBytes(abc, "abc"u8.ToArray());
        var r = await T.Run(host, new GetFileHashAction(), ContentValue.Files(new[] { abc, abc }));
        Assert.Equal(2, r.Count);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", r.AsText().Split('\n')[0].Trim());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHasher.HashAsync(abc, "sha256", cts.Token));
    }
}
