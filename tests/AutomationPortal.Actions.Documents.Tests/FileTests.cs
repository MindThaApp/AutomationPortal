global using System.IO;
global using System.Net.Http;
global using Xunit;
using System.IO.Compression;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

public class FindAndFilterTests
{
    private static TempDir Sample()
    {
        var d = new TempDir();
        d.File("photo.jpg", new string('a', 100));
        d.File("Picture.PNG", new string('a', 50));
        d.File("notes.txt", "hello");
        d.File("report.pdf", "%PDF-fake");
        d.File("sub/deep.jpg", new string('a', 3 * 1024 * 1024));
        d.File("sub/invoice 2025.txt", "x");
        File.SetLastWriteTime(d.File("notes.txt"), DateTime.Now.AddDays(-30));
        return d;
    }

    private static string[] Names(ContentValue v) => T.Paths(v).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n).ToArray();

    [Fact]
    public async Task FindByKindIncludesSubfolders()
    {
        using var d = Sample(); using var host = T.Host();
        var r = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("kind", T.Choice("image")));
        Assert.Equal(new[] { "deep.jpg", "photo.jpg", "Picture.PNG" }, Names(r));
        Assert.All(r.Items.Take(1), i => Assert.IsType<ImageItem>(i));
    }

    [Fact]
    public async Task FindWithoutSubfoldersAndByExtension()
    {
        using var d = Sample(); using var host = T.Host();
        var r = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("subfolders", false), ("extensions", ".jpg, png"));
        Assert.Equal(new[] { "photo.jpg", "Picture.PNG" }, Names(r));
    }

    [Fact]
    public async Task FindByNameWildcardAndText()
    {
        using var d = Sample(); using var host = T.Host();
        var wild = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("nameContains", "*.txt"));
        Assert.Equal(new[] { "invoice 2025.txt", "notes.txt" }, Names(wild));
        var text = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("nameContains", "INVOICE"));
        Assert.Equal(new[] { "invoice 2025.txt" }, Names(text));
    }

    [Fact]
    public async Task FindBySizeAndDate()
    {
        using var d = Sample(); using var host = T.Host();
        var big = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("minSizeMb", 1));
        Assert.Equal(new[] { "deep.jpg" }, Names(big));
        var old = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("olderThanDays", 7));
        Assert.Equal(new[] { "notes.txt" }, Names(old));
        var recent = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("changedWithinDays", 7), ("kind", T.Choice("text")));
        Assert.Equal(new[] { "invoice 2025.txt" }, Names(recent));
    }

    [Fact]
    public async Task FindSortsAndLimits()
    {
        using var d = Sample(); using var host = T.Host();
        var r = await T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(d.Path)), ("sortBy", T.Choice("size")), ("order", T.Choice("desc")), ("limit", 2));
        Assert.Equal(2, r.Count);
        Assert.Equal("deep.jpg", Path.GetFileName(T.Paths(r)[0]));
    }

    [Fact]
    public async Task FindInMissingFolderGivesFriendlyError()
    {
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new FindFilesAction(), null, ("folder", ParamValue.File(@"C:\no\such\folder-b3"))));
        Assert.Contains("Couldn't find the folder", ex.Message);
    }

    [Fact]
    public async Task FilterKeepsMatchingInputFiles()
    {
        using var d = Sample(); using var host = T.Host();
        var input = ContentValue.Files(new[] { d.File("photo.jpg"), d.File("notes.txt"), d.File("report.pdf") });
        var r = await T.Run(host, new FilterFilesAction(), input, ("kind", T.Choice("pdf")));
        Assert.Equal(new[] { "report.pdf" }, Names(r));
        var r2 = await T.Run(host, new FilterFilesAction(), input, ("kind", T.Choice("any")), ("sortBy", T.Choice("name")), ("order", T.Choice("desc")), ("limit", 2));
        Assert.Equal(new[] { "report.pdf", "photo.jpg" }, T.Paths(r2).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task ContentsListsFilesAndFolders()
    {
        using var d = Sample(); using var host = T.Host();
        var files = await T.Run(host, new GetFolderContentsAction(), ContentValue.Folder(d.Path));
        Assert.Equal(new[] { "notes.txt", "photo.jpg", "Picture.PNG", "report.pdf" }, Names(files));
        var all = await T.Run(host, new GetFolderContentsAction(), ContentValue.Folder(d.Path), ("include", T.Choice("all")));
        Assert.Contains(all, i => i is FolderItem);
    }

    [Fact]
    public async Task DetailsReturnsPropertiesAndEverything()
    {
        using var d = Sample(); using var host = T.Host();
        var input = ContentValue.Files(new[] { d.File("photo.jpg") });
        Assert.Equal(ContentFormat.Size(100), (await T.Run(host, new GetFileDetailsAction(), input, ("detail", T.Choice("sizeText")))).AsText());
        Assert.Equal("jpg", (await T.Run(host, new GetFileDetailsAction(), input, ("detail", T.Choice("extension")))).AsText());
        var all = (await T.Run(host, new GetFileDetailsAction(), input, ("detail", T.Choice("all"))))[0] as DictionaryItem;
        Assert.Equal("Image", all!.Get("Kind").AsText());
    }

    [Fact]
    public async Task GetFolderResolvesKnownFoldersAndChosen()
    {
        using var d = Sample(); using var host = T.Host();
        var docs = await T.Run(host, new GetFolderAction(), null, ("folder", T.Choice("documents")));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ((FolderItem)docs[0]).Path);
        var dl = await T.Run(host, new GetFolderAction(), null, ("folder", T.Choice("downloads")));
        Assert.EndsWith("Downloads", ((FolderItem)dl[0]).Path, StringComparison.OrdinalIgnoreCase);
        var chosen = await T.Run(host, new GetFolderAction(), null, ("folder", T.Choice("chosen")), ("chosen", ParamValue.File(d.Path)));
        Assert.Equal(d.Path, ((FolderItem)chosen[0]).Path);
    }

    [Fact]
    public async Task GetFileChecksExistence()
    {
        using var d = Sample(); using var host = T.Host();
        var r = await T.Run(host, new GetFileAction(), null, ("file", ParamValue.File(d.File("notes.txt"))));
        Assert.Equal("notes.txt", ((PathItem)r[0]).FullName);
        await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new GetFileAction(), null, ("file", ParamValue.File(Path.Combine(d.Path, "nope.txt")))));
    }
}

public class RenameTests
{
    [Fact]
    public async Task PatternWithCounterKeepsExtension()
    {
        using var d = new TempDir(); using var host = T.Host();
        var input = ContentValue.Files(new[] { d.File("a.jpg", "1"), d.File("b.jpg", "2"), d.File("c.txt", "3") });
        var r = await T.Run(host, new RenameFilesAction(), input, ("pattern", "Holiday {n:000}"), ("startAt", 7));
        Assert.Equal(new[] { "Holiday 007.jpg", "Holiday 008.jpg", "Holiday 009.txt" }, T.Paths(r).Select(Path.GetFileName).ToArray());
        Assert.False(File.Exists(d.File("a.jpg")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(d.Path, "Holiday 008.jpg")));
    }

    [Fact]
    public async Task TokensForNameExtAndDates()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("Trip.TXT", "x");
        File.SetLastWriteTime(f, new DateTime(2021, 3, 4, 10, 0, 0));
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { f }), ("pattern", "{modified:yyyy-MM-dd} {name}.{ext}"));
        Assert.Equal("2021-03-04 Trip.TXT", Path.GetFileName(T.Paths(r)[0]));
        var today = await T.Run(host, new RenameFilesAction(), r, ("pattern", "{date:yyyy}-{name}"));
        Assert.StartsWith(DateTime.Now.Year + "-", Path.GetFileName(T.Paths(today)[0]));
    }

    [Fact]
    public async Task ReplaceTextPrefixSuffixAndCase()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("IMG hello world.jpg", "x");
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { f }),
            ("find", "img "), ("replaceWith", ""), ("prefix", "new-"), ("suffix", "-v2"), ("caseStyle", T.Choice("title")));
        Assert.Equal("New-Hello World-V2.jpg", Path.GetFileName(T.Paths(r)[0]));
    }

    [Fact]
    public async Task TakenTokenUsesExifDateAndFallsBackToModified()
    {
        using var d = new TempDir(); using var host = T.Host();
        var photo = await T.MakeImage(d.File("p.jpg"), 20, 10, md => md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2019:07:08 09:10:11"));
        var plain = d.File("q.txt", "x");
        File.SetLastWriteTime(plain, new DateTime(2020, 1, 2));
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { photo, plain }), ("pattern", "{taken:yyyy-MM-dd}"));
        Assert.Equal(new[] { "2019-07-08.jpg", "2020-01-02.txt" }, T.Paths(r).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task CollisionsGetNumbersAndNothingIsOverwritten()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = d.File("a.txt", "A"); var b = d.File("b.txt", "B");
        d.File("same.txt", "EXISTING");
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { a, b }), ("pattern", "same"));
        Assert.Equal(new[] { "same 2.txt", "same 3.txt" }, T.Paths(r).Select(Path.GetFileName).ToArray());
        Assert.Equal("EXISTING", File.ReadAllText(d.File("same.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(d.Path, "same 2.txt")));
    }

    [Fact]
    public void SwapsWithinABatchWork()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "A"); var b = d.File("b.txt", "B");
        var plan = RenamePlanner.Plan(new[] { (a, "b.txt"), (b, "a.txt") });
        Assert.Equal(b, plan[0].Target);
        Assert.Equal(a, plan[1].Target);
        RenamePlanner.Execute(plan);
        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));
        Assert.Empty(Directory.GetFiles(d.Path, "~automationportal*"));
    }

    [Fact]
    public void ChainsAndCaseOnlyChangesWork()
    {
        using var d = new TempDir();
        var a = d.File("1.txt", "one"); var b = d.File("2.txt", "two"); var c = d.File("File.TXT", "three");
        var plan = RenamePlanner.Plan(new[] { (a, "2.txt"), (b, "3.txt"), (c, "file.txt") });
        RenamePlanner.Execute(plan);
        Assert.Equal("one", File.ReadAllText(Path.Combine(d.Path, "2.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(d.Path, "3.txt")));
        Assert.Contains(Directory.GetFiles(d.Path).Select(Path.GetFileName), n => n == "file.txt");
    }

    [Fact]
    public async Task PreviewOnlyChangesNothing()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("a.txt", "x");
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { f }), ("pattern", "new"), ("previewOnly", true));
        Assert.Equal("a.txt → new.txt", r.AsText());
        Assert.True(File.Exists(f));
    }

    [Fact]
    public async Task RenamesFoldersToo()
    {
        using var d = new TempDir(); using var host = T.Host();
        var folder = d.Folder("old.name");
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Folder(folder), ("pattern", "new"));
        Assert.IsType<FolderItem>(r[0]);
        Assert.True(Directory.Exists(Path.Combine(d.Path, "new")));
    }

    [Fact]
    public async Task InvalidCharactersFromDateFormatsAreMadeSafe()
    {
        using var d = new TempDir(); using var host = T.Host();
        var r = await T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { d.File("a.txt", "x") }), ("pattern", "{date:yyyy/MM/dd}"));
        Assert.DoesNotContain('/', Path.GetFileName(T.Paths(r)[0]));
    }

    [Fact]
    public async Task MissingFilesGiveFriendlyError()
    {
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new RenameFilesAction(), ContentValue.Files(new[] { @"C:\nope\x-b3.txt" })));
        Assert.Contains("Couldn't find", ex.Message);
    }
}

public class MoveCopyTests
{
    [Fact]
    public async Task MoveIntoKindAndYearSubfolders()
    {
        using var d = new TempDir(); using var host = T.Host();
        var photo = d.File("p.jpg", "x"); var doc = d.File("d.pdf", "y");
        File.SetLastWriteTime(photo, new DateTime(2022, 5, 6));
        File.SetLastWriteTime(doc, new DateTime(2023, 12, 1));
        var dest = d.Folder("sorted");
        var r = await T.Run(host, new MoveFilesAction(), ContentValue.Files(new[] { photo, doc }),
            ("folder", ParamValue.File(dest)), ("subfolder", "{kind}/{year}/{month}"));
        Assert.True(File.Exists(Path.Combine(dest, "Image", "2022", "05", "p.jpg")));
        Assert.True(File.Exists(Path.Combine(dest, "PDF", "2023", "12", "d.pdf")));
        Assert.False(File.Exists(photo));
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public async Task ExtensionTokenAndMonthName()
    {
        using var d = new TempDir();
        var f = d.File("a.TXT", "x");
        File.SetLastWriteTime(f, new DateTime(2024, 2, 3));
        var sub = Transfer.ExpandSubfolder("{ext}\\{monthName}", f);
        Assert.Equal(Path.Combine("txt", new DateTime(2024, 2, 1).ToString("MMMM")), sub);
        Assert.Equal("", Transfer.ExpandSubfolder("", f));
        Assert.Equal(Path.Combine("a", "b"), Transfer.ExpandSubfolder("a/../b", f));
    }

    [Fact]
    public async Task ConflictKeepBothReplaceSkip()
    {
        using var host = T.Host();
        foreach (var (mode, expectSrcExists, expectedDest, expectedExtra) in new[]
        {
            ("keep", false, "OLD", "NEW"), ("replace", false, "NEW", null), ("skip", true, "OLD", null),
        })
        {
            using var d = new TempDir();
            var src = d.File("src/a.txt", "NEW");
            var dest = d.Folder("dest");
            File.WriteAllText(Path.Combine(dest, "a.txt"), "OLD");
            var r = await T.Run(host, new MoveFilesAction(), ContentValue.Files(new[] { src }), ("folder", ParamValue.File(dest)), ("ifExists", T.Choice(mode)));
            Assert.Equal(expectSrcExists, File.Exists(src));
            Assert.Equal(expectedDest, File.ReadAllText(Path.Combine(dest, "a.txt")));
            if (expectedExtra is not null) Assert.Equal(expectedExtra, File.ReadAllText(Path.Combine(dest, "a 2.txt")));
            Assert.Equal(mode == "skip" ? 0 : 1, r.Count);
        }
    }

    [Fact]
    public async Task CopyKeepsOriginalsAndCopiesFolders()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("a.txt", "A");
        var folder = d.Folder("pics"); File.WriteAllText(Path.Combine(folder, "x.txt"), "X");
        Directory.CreateDirectory(Path.Combine(folder, "inner")); File.WriteAllText(Path.Combine(folder, "inner", "y.txt"), "Y");
        var dest = d.Folder("backup");
        var r = await T.Run(host, new CopyFilesAction(), ContentValue.FromItems(new ContentItem[] { new FileItem(f), new FolderItem(folder) }), ("folder", ParamValue.File(dest)));
        Assert.True(File.Exists(f));
        Assert.Equal("A", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("Y", File.ReadAllText(Path.Combine(dest, "pics", "inner", "y.txt")));
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public async Task CannotMoveFolderIntoItself()
    {
        using var d = new TempDir(); using var host = T.Host();
        var folder = d.Folder("a");
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new MoveFilesAction(), ContentValue.Folder(folder), ("folder", ParamValue.File(Path.Combine(folder, "inside")))));
        Assert.Contains("inside itself", ex.Message);
    }

    [Fact]
    public async Task MovingIntoSameFolderIsANoOp()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("a.txt", "A");
        var r = await T.Run(host, new MoveFilesAction(), ContentValue.Files(new[] { f }), ("folder", ParamValue.File(d.Path)));
        Assert.Equal(f, T.Paths(r)[0]);
        Assert.Equal("A", File.ReadAllText(f));
    }
}

public class DeleteFolderSaveTests
{
    [Fact]
    public void DeleteIsMarkedDestructive() => Assert.True(new DeleteFilesAction().Descriptor.IsDestructive);

    [Fact]
    public async Task PermanentDeleteRemovesFilesAndFolders()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("a.txt", "A");
        var folder = d.Folder("gone"); File.WriteAllText(Path.Combine(folder, "x.txt"), "X");
        var r = await T.Run(host, new DeleteFilesAction(), ContentValue.FromItems(new ContentItem[] { new FileItem(f), new FolderItem(folder) }), ("permanently", true));
        Assert.False(File.Exists(f));
        Assert.False(Directory.Exists(folder));
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public async Task DeleteMissingFileGivesFriendlyError()
    {
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new DeleteFilesAction(), ContentValue.Files(new[] { @"C:\nope\b3-missing.txt" }), ("permanently", true)));
        Assert.Contains("Couldn't find", ex.Message);
    }

    [Fact]
    public async Task NewFolderCreatesNestedAndIsRepeatable()
    {
        using var d = new TempDir(); using var host = T.Host();
        var r = await T.Run(host, new CreateFolderAction(), null, ("name", "2026/Invoices"), ("parent", ParamValue.File(d.Path)));
        Assert.True(Directory.Exists(Path.Combine(d.Path, "2026", "Invoices")));
        Assert.IsType<FolderItem>(r[0]);
        await T.Run(host, new CreateFolderAction(), null, ("name", "2026/Invoices"), ("parent", ParamValue.File(d.Path)));
    }

    [Fact]
    public async Task SaveFileCopiesWithNameAndKeepsBoth()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = d.File("in/doc.txt", "DATA");
        var dest = d.Folder("out");
        var r1 = await T.Run(host, new SaveFileAction(), ContentValue.Files(new[] { src }), ("folder", ParamValue.File(dest)), ("name", "report"));
        var r2 = await T.Run(host, new SaveFileAction(), ContentValue.Files(new[] { src }), ("folder", ParamValue.File(dest)), ("name", "report"));
        Assert.Equal("report.txt", Path.GetFileName(T.Paths(r1)[0]));
        Assert.Equal("report 2.txt", Path.GetFileName(T.Paths(r2)[0]));
        Assert.Equal("DATA", File.ReadAllText(Path.Combine(dest, "report.txt")));
    }

    [Fact]
    public async Task SaveFileSavesTextAsTxtAndReplaces()
    {
        using var d = new TempDir(); using var host = T.Host();
        var dest = d.Folder("out");
        await T.Run(host, new SaveFileAction(), ContentValue.Text("first"), ("folder", ParamValue.File(dest)), ("name", "note"));
        var r = await T.Run(host, new SaveFileAction(), ContentValue.Text("second"), ("folder", ParamValue.File(dest)), ("name", "note"), ("ifExists", T.Choice("replace")));
        Assert.Equal("note.txt", Path.GetFileName(T.Paths(r)[0]));
        Assert.Equal("second", File.ReadAllText(Path.Combine(dest, "note.txt")));
    }

    [Fact]
    public async Task SaveFileAsksWhereWhenNoFolder()
    {
        using var d = new TempDir(); using var host = T.Host();
        var target = d.File("chosen.txt");
        host.UI.Enqueue(target);
        var r = await T.Run(host, new SaveFileAction(), ContentValue.Text("hello"));
        Assert.Equal(target, T.Paths(r)[0]);
        Assert.Equal("hello", File.ReadAllText(target));
    }

    [Fact]
    public async Task AppendCreatesAndAddsOnNewLines()
    {
        using var d = new TempDir(); using var host = T.Host();
        var path = d.File("log/today.txt");
        await T.Run(host, new AppendToFileAction(), ContentValue.Text("one"), ("file", ParamValue.File(path)));
        await T.Run(host, new AppendToFileAction(), ContentValue.Text("two"), ("file", ParamValue.File(path)));
        Assert.Equal(new[] { "one", "two" }, File.ReadAllLines(path));
        File.WriteAllText(path, "no newline at end");
        await T.Run(host, new AppendToFileAction(), ContentValue.Text("next"), ("file", ParamValue.File(path)));
        Assert.Equal(new[] { "no newline at end", "next" }, File.ReadAllLines(path));
    }

    [Fact]
    public async Task SelectedFilesLookupRunsWithoutCrashing()
    {
        var paths = await ExplorerSelection.GetSelectedPathsAsync();
        Assert.NotNull(paths);
    }
}

public class ZipTests
{
    [Fact]
    public async Task ZipAndUnzipRoundTrip()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = d.File("src/a.txt", "AAA");
        var folder = d.Folder("src/docs"); File.WriteAllText(Path.Combine(folder, "b.txt"), "BBB");
        Directory.CreateDirectory(Path.Combine(folder, "deep")); File.WriteAllText(Path.Combine(folder, "deep", "c.txt"), "CCC");
        var zip = await T.Run(host, new MakeZipAction(), ContentValue.FromItems(new ContentItem[] { new FileItem(a), new FolderItem(folder) }), ("name", "Bundle"));
        var zipPath = ((PathItem)zip[0]).Path;
        Assert.Equal(Path.Combine(d.Path, "src", "Bundle.zip"), zipPath);

        var outDir = d.Folder("out");
        var extracted = await T.Run(host, new ExtractZipAction(), zip, ("saveTo", T.Choice("folder")), ("saveFolder", ParamValue.File(outDir)), ("intoFolder", false));
        Assert.Equal(3, extracted.Count);
        Assert.Equal("AAA", File.ReadAllText(Path.Combine(outDir, "a.txt")));
        Assert.Equal("CCC", File.ReadAllText(Path.Combine(outDir, "docs", "deep", "c.txt")));
    }

    [Fact]
    public async Task UnzipIntoNamedFolderNextToZipAndNeverOverwrites()
    {
        using var d = new TempDir(); using var host = T.Host();
        var zip = Path.Combine(d.Path, "pack.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("x.txt");
            using var w = new StreamWriter(e.Open()); w.Write("ZIPPED");
        }
        var r1 = await T.Run(host, new ExtractZipAction(), ContentValue.Files(new[] { zip }));
        var r2 = await T.Run(host, new ExtractZipAction(), ContentValue.Files(new[] { zip }));
        Assert.Equal(Path.Combine(d.Path, "pack", "x.txt"), T.Paths(r1)[0]);
        Assert.Equal(Path.Combine(d.Path, "pack 2", "x.txt"), T.Paths(r2)[0]);
        Assert.Equal("ZIPPED", File.ReadAllText(T.Paths(r2)[0]));
    }

    [Fact]
    public async Task ZipSlipEntriesAreRejected()
    {
        using var d = new TempDir(); using var host = T.Host();
        var zip = Path.Combine(d.Path, "evil.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("../escaped.txt");
            using var w = new StreamWriter(e.Open()); w.Write("bad");
        }
        var dest = d.Folder("dest");
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ExtractZipAction(), ContentValue.Files(new[] { zip }), ("saveTo", T.Choice("folder")), ("saveFolder", ParamValue.File(dest)), ("intoFolder", false)));
        Assert.Contains("leave the destination", ex.Message);
        Assert.False(File.Exists(Path.Combine(d.Path, "escaped.txt")));
    }

    [Fact]
    public async Task NotAZipGivesFriendlyError()
    {
        using var d = new TempDir(); using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ExtractZipAction(), ContentValue.Files(new[] { d.File("fake.zip", "not a zip") })));
        Assert.Contains("isn't a valid zip", ex.Message);
    }
}

public class ReadTextTests
{
    [Theory]
    [InlineData("a.txt", "plain text")]
    [InlineData("a.md", "# Title\nbody")]
    [InlineData("a.csv", "x,y\n1,2")]
    [InlineData("a.json", "{\"k\": \"v\"}")]
    public async Task ReadsTextFormats(string name, string content)
    {
        using var d = new TempDir(); using var host = T.Host();
        var r = await T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { d.File(name, content) }));
        Assert.Equal(content, r.AsText());
    }

    [Fact]
    public async Task HtmlIsReducedToText()
    {
        using var d = new TempDir(); using var host = T.Host();
        var f = d.File("p.html", "<html><head><style>p{color:red}</style><script>var x=1;</script></head><body><h1>Hello &amp; welcome</h1><p>First<br>line</p><p>Second</p></body></html>");
        var text = (await T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { f }))).AsText();
        Assert.Contains("Hello & welcome", text);
        Assert.Contains("Second", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("var x", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public async Task ReadsDocx()
    {
        using var d = new TempDir(); using var host = T.Host();
        var path = d.File("w.docx");
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body(
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("First paragraph"))),
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Second ")),
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("paragraph")))));
        }
        var r = await T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { path }));
        Assert.Equal("First paragraph\nSecond paragraph", r.AsText());
    }

    [Fact]
    public async Task ReadsPdfMadeByTheApp()
    {
        using var d = new TempDir(); using var host = T.Host();
        var pdf = Path.Combine(d.Path, "t.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("Quarterly report. Revenue grew nicely.") }, pdf, new PdfMakeOptions());
        var r = await T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { pdf }));
        Assert.Contains("Quarterly report", r.AsText());
        Assert.Contains("Revenue grew nicely", r.AsText().Replace("\n", " "));
    }

    [Fact]
    public async Task BinaryAndUnsupportedFilesGiveFriendlyErrors()
    {
        using var d = new TempDir(); using var host = T.Host();
        var bin = d.File("data.bin");
        File.WriteAllBytes(bin, new byte[] { 1, 2, 0, 0, 3 });
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { bin })));
        Assert.Contains("isn't a text file", ex.Message);
        var ex2 = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ReadTextFromDocumentAction(), ContentValue.Files(new[] { d.File("old.doc", "x") })));
        Assert.Contains(".docx", ex2.Message);
    }
}
