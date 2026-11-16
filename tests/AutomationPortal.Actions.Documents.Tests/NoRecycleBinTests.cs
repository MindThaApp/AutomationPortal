using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

public class PartialDownloadFilterTests
{
    [Fact]
    public void Unfinished_downloads_are_left_out_when_asked()
    {
        using var d = new TempDir();
        var now = DateTime.Now;
        var names = new[] { "movie.mp4.crdownload", "a.part", "b.partial", "c.download", "d.tmp", "e.opdownload", "done.pdf", "photo.jpg" };
        var paths = names.Select(n => d.File(n, "x")).ToList();
        foreach (var p in paths) File.SetLastWriteTime(p, now.AddDays(-10));

        var skip = new FileQuery { OlderThanDays = 1, SkipPartialDownloads = true };
        Assert.Equal(new[] { "done.pdf", "photo.jpg" }, paths.Where(p => skip.Matches(p, now)).Select(Path.GetFileName));
        var keep = new FileQuery { OlderThanDays = 1 };
        Assert.Equal(names.Length, paths.Count(p => keep.Matches(p, now)));   // off by default, as before
    }
}

public class PreviewThenRunTests
{
    [Fact]
    public async Task The_files_picked_for_the_preview_are_kept_for_the_real_run()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var host = T.Host();
        host.UI.Enqueue(ContentValue.Files(new[] { a, b }));
        var s = new Shortcut { Name = "Move", WhenNoInput = WhenNoInput.AskForFiles };
        var move = ActionInstance.Create("file.move", ("folder", ParamValue.Text(Path.Combine(d.Path, "out"))));
        s.Actions.Add(move);

        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Empty);
        Assert.Equal(new[] { a, b }, T.Paths(plan.PickedInput!));
        Assert.True(File.Exists(a));   // the preview changed nothing

        // the real run on that same input moves exactly the previewed files, without asking again
        var result = await host.Runner.RunAsync(s, plan.PickedInput!);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var planned = plan.Changes.Where(c => c.Kind == PlannedChangeKind.Move).Select(c => c.Target).ToList();
        Assert.Equal(planned, T.Paths(result.Output));

        // a preview that was given its input keeps nothing extra
        Assert.Null((await FilePreview.RunAsync(host.Runner, s, ContentValue.Files(new[] { d.File("c.txt", "c") }))).PickedInput);
    }
}

/// <summary>A preview never reaches the web: web items are listed, not downloaded.</summary>
public class PreviewWebItemTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        }
    }

    [Fact]
    public async Task Preview_lists_web_items_and_downloads_nothing()
    {
        using var d = new TempDir();
        var local = d.File("a.txt", "x");
        var handler = new CountingHandler();
        var host = T.Host();
        host.Services.Http = new HttpClient(handler);
        var s = new Shortcut { Name = "Details" };
        s.Actions.Add(ActionInstance.Create("file.details"));
        var input = ContentValue.FromItems(new ContentItem[] { new UrlItem(new Uri("https://example.invalid/photo.png")), new FileItem(local) });

        var plan = await FilePreview.RunAsync(host.Runner, s, input);

        Assert.Equal(0, handler.Requests);
        Assert.Contains(plan.Changes, c => c.Kind == PlannedChangeKind.CantPreview
            && c.Describe().EndsWith("can't preview https://example.invalid/photo.png (web item)", StringComparison.Ordinal));
    }
}

/// <summary>Drives without a Recycle Bin: the preview says the delete is permanent, and runs with nobody watching refuse it.</summary>
public class NoRecycleBinTests
{
    [Fact]
    public void ReplacePlanSaysDeletePermanentlyOnADriveWithoutABin()
    {
        using var d = new TempDir();
        var src = d.File("in/a.txt", "new");
        d.File("out/a.txt", "old");
        RecycleBin.TestDriveHasBin.Value = _ => false;
        try
        {
            var plan = new FileChangePlan();
            Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false, plan: plan, step: "Copy Files");
            var replaced = Assert.Single(plan.Changes, c => c.Kind is PlannedChangeKind.Delete or PlannedChangeKind.Recycle);
            Assert.Equal(PlannedChangeKind.Delete, replaced.Kind);
            Assert.EndsWith("permanently (replaced by the new one; this drive has no Recycle Bin)", replaced.Describe());
            Assert.DoesNotContain(plan.Changes, c => c.Describe().Contains("Recycle Bin", StringComparison.Ordinal) && c.Kind == PlannedChangeKind.Recycle);

            var preview = new List<string>();
            Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false, preview: preview);
            Assert.Contains("deleted permanently", Assert.Single(preview));
        }
        finally { RecycleBin.TestDriveHasBin.Value = null; }
        Assert.Equal("old", File.ReadAllText(Path.Combine(d.Path, "out", "a.txt")));
    }

    [Fact]
    public void ReplacePlanStillSaysRecycleBinWhereThereIsOne()
    {
        using var d = new TempDir();
        var src = d.File("in/a.txt", "new");
        d.File("out/a.txt", "old");
        RecycleBin.TestDriveHasBin.Value = _ => true;
        try
        {
            var plan = new FileChangePlan();
            Transfer.Run(new[] { src }, Path.Combine(d.Path, "out"), "", ConflictMode.Replace, move: false, plan: plan, step: "Copy Files");
            Assert.Contains(plan.Changes, c => c.Kind == PlannedChangeKind.Recycle);
        }
        finally { RecycleBin.TestDriveHasBin.Value = null; }
    }

    [Fact]
    public void UnattendedDeleteOnADriveWithoutABinIsRefusedAndTouchesNothing()
    {
        using var d = new TempDir();
        var f = d.File("keep.txt", "keep");
        RecycleBin.TestDriveHasBin.Value = _ => false;
        try
        {
            var ex = Assert.Throws<ActionException>(() => RecycleBin.Send(f, unattended: true));
            Assert.Contains("no Recycle Bin", ex.Message);
            Assert.False(RecycleBin.TrySendUnattended(f));
            // a replaced file is kept next to the new one instead
            var aside = Transfer.MoveAside(f);
            Transfer.DiscardAside(aside, d.Path, RecycleBin.TrySendUnattended);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(d.Path, "keep.txt (replaced)")));
        }
        finally { RecycleBin.TestDriveHasBin.Value = null; }
    }
}

/// <summary>
/// Runs with nobody watching delete only what certainly goes to the Recycle Bin: an item too big for the bin, a bin set to
/// delete right away, or settings that can't be read leave the item untouched, with no prompt and nothing deleted for good.
/// </summary>
public class UnattendedRecycleBinCheckTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void OnlyADefiniteFitLetsTheDeleteGoAhead()
    {
        var bin = new RecycleBin.BinSettings(false, 100 * MB);
        Assert.Equal(RecycleBin.BinCheck.Fits, RecycleBin.Check(true, bin, 50 * MB));
        Assert.Equal(RecycleBin.BinCheck.Fits, RecycleBin.Check(true, bin, 100 * MB));
        Assert.Equal(RecycleBin.BinCheck.TooBig, RecycleBin.Check(true, bin, 100 * MB + 1));
        Assert.Equal(RecycleBin.BinCheck.NoBin, RecycleBin.Check(false, bin, 1));
        Assert.Equal(RecycleBin.BinCheck.DeletesRightAway, RecycleBin.Check(true, new RecycleBin.BinSettings(true, 100 * MB), 1));
        Assert.Equal(RecycleBin.BinCheck.DeletesRightAway, RecycleBin.Check(true, new RecycleBin.BinSettings(true, null), null));
        // anything unknown is refused
        Assert.Equal(RecycleBin.BinCheck.Unknown, RecycleBin.Check(true, default, 1));
        Assert.Equal(RecycleBin.BinCheck.Unknown, RecycleBin.Check(true, new RecycleBin.BinSettings(false, null), 1));
        Assert.Equal(RecycleBin.BinCheck.Unknown, RecycleBin.Check(true, bin, null));
        Assert.Equal(RecycleBin.BinCheck.Unknown, RecycleBin.Check(true, new RecycleBin.BinSettings(false, 0), 1));
    }

    [Fact]
    public void AFolderCountsEverythingInsideIt()
    {
        using var d = new TempDir();
        d.File("f/a.bin", new string('x', 10_000));
        d.File("f/sub/deeper/b.bin", new string('y', 5_000));
        var folder = Path.Combine(d.Path, "f");
        var size = RecycleBin.ItemSize(folder, long.MaxValue);
        Assert.NotNull(size);
        Assert.True(size >= 15_000, $"size {size}");
        // stops early once it is clearly too big
        Assert.True(RecycleBin.ItemSize(folder, 100) > 100);
        Assert.Equal(4096, RecycleBin.ItemSize(d.File("tiny.txt", "x"), long.MaxValue));
        Assert.Null(RecycleBin.ItemSize(Path.Combine(d.Path, "missing"), long.MaxValue));
    }

    private static T WithBin<T>(RecycleBin.BinSettings settings, Func<string, ShellRecycle.Outcome> shell, Func<T> body)
    {
        RecycleBin.TestDriveHasBin.Value = _ => true;
        RecycleBin.TestBinSettings.Value = _ => settings;
        RecycleBin.TestSendGuarded.Value = shell;
        try { return body(); }
        finally
        {
            RecycleBin.TestDriveHasBin.Value = null;
            RecycleBin.TestBinSettings.Value = null;
            RecycleBin.TestSendGuarded.Value = null;
        }
    }

    [Theory]
    [InlineData(false, 4096L, "too big for the Recycle Bin")]          // bigger than the bin may grow
    [InlineData(true, 100L * MB, "set to remove files right away")]     // "Don't move files to the Recycle Bin"
    [InlineData(null, null, "couldn't check that it fits")]            // settings can't be read
    public void ARefusedItemIsLeftUntouchedAndTheShellIsNeverAsked(bool? rightAway, long? capacity, string expected)
    {
        using var d = new TempDir();
        var f = d.File("big.bin", new string('x', 20_000));
        var shellCalls = 0;
        var ex = WithBin(new RecycleBin.BinSettings(rightAway, capacity), _ => { shellCalls++; return ShellRecycle.Outcome.Recycled; }, () =>
        {
            var e = Assert.Throws<ActionException>(() => RecycleBin.Send(f, unattended: true));
            Assert.False(RecycleBin.TrySendUnattended(f));
            // a replaced item is kept next to the new one instead
            var aside = Transfer.MoveAside(f);
            Transfer.DiscardAside(aside, d.Path, RecycleBin.TrySendUnattended);
            return e;
        });
        Assert.Contains(expected, ex.Message);
        Assert.Contains("Run the shortcut yourself to choose", ex.Message);
        Assert.Equal(0, shellCalls);
        Assert.Equal(20_000, File.ReadAllText(Path.Combine(d.Path, "big.bin (replaced)")).Length);
    }

    [Fact]
    public void AFolderTooBigForTheBinIsRefused()
    {
        using var d = new TempDir();
        d.File("photos/a.jpg", new string('x', 6_000));
        d.File("photos/2024/b.jpg", new string('x', 6_000));
        var folder = Path.Combine(d.Path, "photos");
        var ex = WithBin(new RecycleBin.BinSettings(false, 8192), _ => throw new InvalidOperationException("the shell must not be asked"),
            () => Assert.Throws<ActionException>(() => RecycleBin.Send(folder, unattended: true)));
        Assert.Contains("too big for the Recycle Bin", ex.Message);
        Assert.Equal(2, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void WhenTheShellWouldStillDeleteForGoodTheRunFailsAndKeepsTheItem()
    {
        using var d = new TempDir();
        var f = d.File("keep.txt", "keep");
        var ex = WithBin(new RecycleBin.BinSettings(false, 100 * MB), _ => ShellRecycle.Outcome.WouldBePermanent, () =>
        {
            Assert.False(RecycleBin.TrySendUnattended(f));
            return Assert.Throws<ActionException>(() => RecycleBin.Send(f, unattended: true));
        });
        Assert.Contains("can't go to the Recycle Bin, so it would be deleted for good", ex.Message);
        Assert.Equal("keep", File.ReadAllText(f));
    }

    [Fact]
    public void AnItemThatFitsGoesToTheShellWithNoWindow()
    {
        using var d = new TempDir();
        var f = d.File("old.txt", "old");
        var asked = new List<string>();
        WithBin(new RecycleBin.BinSettings(false, 100 * MB), p => { asked.Add(p); return ShellRecycle.Outcome.Recycled; }, () =>
        {
            RecycleBin.Send(f, unattended: true);
            Assert.True(RecycleBin.TrySendUnattended(f));
            return 0;
        });
        Assert.Equal(new[] { Path.GetFullPath(f), Path.GetFullPath(f) }, asked);
    }

    [Fact]
    public void AShellFailureIsReportedWithoutBlamingTheUser()
    {
        using var d = new TempDir();
        var f = d.File("open.txt", "x");
        var ex = WithBin(new RecycleBin.BinSettings(false, 100 * MB), _ => ShellRecycle.Outcome.Failed,
            () => Assert.Throws<ActionException>(() => RecycleBin.Send(f, unattended: true)));
        Assert.Contains("Nothing was deleted for good", ex.Message);
        Assert.DoesNotContain("you chose", ex.Message);
    }
}