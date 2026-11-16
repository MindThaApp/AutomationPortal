using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

/// <summary>Result files are created new: a name taken after it was picked moves on to "name (2)", and the other file is never touched.</summary>
public class NewFileTests
{
    [Fact]
    public void WriteMovesOnToTheNextNameWhenTheFileAppearedMeanwhile()
    {
        using var d = new TempDir();
        var picked = d.File("out/report.txt", "someone else's");
        d.File("out/report (2).txt", "also someone else's");
        var written = NewFile.Write(picked, s => s.Write("mine"u8));
        Assert.Equal(Path.Combine(d.Path, "out", "report (3).txt"), written);
        Assert.Equal("mine", File.ReadAllText(written));
        Assert.Equal("someone else's", File.ReadAllText(picked));
        Assert.Equal("also someone else's", File.ReadAllText(Path.Combine(d.Path, "out", "report (2).txt")));
    }

    [Fact]
    public void AFailedWriteRemovesOnlyItsOwnFile()
    {
        using var d = new TempDir();
        var picked = d.File("a.bin", "keep");
        Assert.Throws<InvalidOperationException>(() => NewFile.Write(picked, _ => throw new InvalidOperationException()));
        Assert.Equal("keep", File.ReadAllText(picked));
        Assert.False(File.Exists(Path.Combine(d.Path, "a (2).bin")));
    }

    [Fact]
    public void PdfSaveNeverReplacesAFileThatAppearedAtTheChosenName()
    {
        using var d = new TempDir();
        var dst = d.File("Doc.pdf", "not a pdf, but the user's");
        var written = PdfEngine.Make(new PdfSource[] { new PdfSource.Text("hello") }, dst, new PdfMakeOptions());
        Assert.Equal(Path.Combine(d.Path, "Doc (2).pdf"), written);
        Assert.Equal("not a pdf, but the user's", File.ReadAllText(dst));
        Assert.Equal(1, PdfEngine.PageCount(written));
    }

    [Theory, MemberData(nameof(ImageBackendParityTests.Backends), MemberType = typeof(ImageBackendParityTests))]
    public async Task PictureSaveNeverReplacesAFileThatAppearedAtTheChosenName(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.jpg"), 40, 20);
        var dst = d.File("out.png", "the user's");
        IImageBackend backend = name == "wpf" ? new AutomationPortal.Host.Wpf.WpfImageBackend() : new WinRtImageBackend();
        var r = await StaWorker.Imaging.RunAsync(() => backend.Convert(src, dst, ImageFormat.Png, 90));
        Assert.Equal(Path.Combine(d.Path, "out (2).png"), r.Path);
        Assert.Equal("the user's", File.ReadAllText(dst));
    }

    [Fact]
    public void UndoRemovesOnlyTheFileTheRunWrote()
    {
        using var d = new TempDir();
        var store = new FileJournal(Path.Combine(d.Path, "journal"));
        var journal = new RunFileJournal(store, "run1");
        var theirs = d.File("out/x.txt", "theirs");
        var written = NewFile.Write(theirs, s => s.Write("mine"u8));
        journal.Created(written);   // what OutputTarget.Record does with the path really written
        journal.Seal();
        var created = store.Read("run1")!.Changes.Where(c => c.Kind == FileChangeKind.Created).Select(c => c.Path).ToList();
        Assert.Equal(new[] { Path.GetFullPath(written) }, created);
        Assert.DoesNotContain(created, p => string.Equals(p, Path.GetFullPath(theirs), StringComparison.OrdinalIgnoreCase));
    }
}
