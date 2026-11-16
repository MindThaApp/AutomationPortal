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
