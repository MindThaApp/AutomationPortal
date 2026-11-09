using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class PartialDownloadFilterTests
{
    [Theory]
    [InlineData("C:\\d\\movie.mp4.crdownload", true)]
    [InlineData("C:\\d\\file.zip.part", true)]
    [InlineData("C:\\d\\FILE.ZIP.PART", true)]
    [InlineData("C:\\d\\a.tmp", true)]
    [InlineData("C:\\d\\a.download", true)]
    [InlineData("C:\\d\\a.opdownload", true)]
    [InlineData("C:\\d\\~$report.docx", true)]
    [InlineData("C:\\d\\desktop.ini", true)]
    [InlineData("C:\\d\\report.pdf", false)]
    [InlineData("C:\\d\\partial notes.txt", false)]
    [InlineData("C:\\d\\movie.mp4", false)]
    public void Spots_unfinished_files(string path, bool partial) => Assert.Equal(partial, PartialDownloadFilter.IsPartial(path));
}

public class FileMatchFilterTests
{
    [Fact]
    public void Parses_extension_lists()
    {
        Assert.Equal(new[] { "pdf", "docx", "txt" }, FileMatchFilter.ParseExtensions("pdf, .docx; *.TXT"));
        Assert.Empty(FileMatchFilter.ParseExtensions(null));
    }

    [Fact]
    public void Filters_by_kind_and_extension()
    {
        Assert.True(FileMatchFilter.Accepts("a.jpg", "Image", Array.Empty<string>()));
        Assert.False(FileMatchFilter.Accepts("a.pdf", "Image", Array.Empty<string>()));
        Assert.True(FileMatchFilter.Accepts("a.pdf", "any", Array.Empty<string>()));
        Assert.True(FileMatchFilter.Accepts("a.PDF", "any", new[] { "pdf" }));
        Assert.False(FileMatchFilter.Accepts("a.doc", "any", new[] { "pdf" }));
        Assert.False(FileMatchFilter.Accepts("a.png", "Image", new[] { "jpg" }));
    }
}

public class FolderChangeBatcherTests
{
    private sealed class FakeProbe : IFileProbe
    {
        public Dictionary<string, long> Sizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => Sizes.ContainsKey(path);
        public long? Size(string path) => Sizes.TryGetValue(path, out var s) ? s : null;
        public bool CanOpen(string path) => !Locked.Contains(path);
    }

    private sealed class Rig
    {
        public DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        public FakeProbe Probe = new();
        public FolderChangeBatcher Batcher;
        public Rig(double debounceSeconds = 3, Func<string, bool>? accept = null) =>
            Batcher = new FolderChangeBatcher(TimeSpan.FromSeconds(debounceSeconds), Probe, accept, () => Now);
        public FileChangeBatch? Advance(double seconds) { Now += TimeSpan.FromSeconds(seconds); return Batcher.Poll(); }
        /// <summary>Polls every second until a batch comes out (what the 500 ms timer does in real life).</summary>
        public FileChangeBatch? Until(int maxSeconds = 20)
        {
            for (var i = 0; i < maxSeconds; i++) if (Advance(1) is { } b) return b;
            return null;
        }
    }

    [Fact]
    public void A_finished_file_is_reported_once_after_the_debounce()
    {
        var r = new Rig();
        r.Probe.Sizes["a.pdf"] = 100;
        r.Batcher.OnCreated("a.pdf");

        Assert.Null(r.Advance(1));
        Assert.Null(r.Advance(1));
        var batch = r.Until();
        Assert.NotNull(batch);
        Assert.Equal(new[] { "a.pdf" }, batch!.Added);
        Assert.Null(r.Until(10));
    }

    [Fact]
    public void A_burst_of_files_becomes_one_batch()
    {
        var r = new Rig();
        foreach (var f in new[] { "1.jpg", "2.jpg", "3.jpg" })
        {
            r.Probe.Sizes[f] = 10;
            r.Batcher.OnCreated(f);
            Assert.Null(r.Advance(1));
        }
        var batch = r.Until();
        Assert.Equal(new[] { "1.jpg", "2.jpg", "3.jpg" }, batch!.Added);
    }

    [Fact]
    public void Partial_downloads_are_ignored_and_the_renamed_final_file_is_reported()
    {
        var r = new Rig();
        r.Probe.Sizes["movie.mp4.crdownload"] = 5;
        r.Batcher.OnCreated("movie.mp4.crdownload");
        r.Batcher.OnChanged("movie.mp4.crdownload");
        Assert.False(r.Batcher.HasPending);
        Assert.Null(r.Advance(10));

        r.Probe.Sizes.Remove("movie.mp4.crdownload");
        r.Probe.Sizes["movie.mp4"] = 9000;
        r.Batcher.OnRenamed("movie.mp4.crdownload", "movie.mp4");
        var batch = r.Until();
        Assert.Equal(new[] { "movie.mp4" }, batch!.Added);
        Assert.Empty(batch.Removed);
    }

    [Fact]
    public void A_file_that_is_still_growing_waits_until_its_size_is_stable()
    {
        var r = new Rig(debounceSeconds: 1);
        r.Probe.Sizes["big.iso"] = 1;
        r.Batcher.OnCreated("big.iso");

        for (var i = 0; i < 5; i++)
        {
            r.Probe.Sizes["big.iso"] += 100;
            r.Batcher.OnChanged("big.iso");
            Assert.Null(r.Advance(1.5));
        }
        Assert.Null(r.Advance(0.5));
        var batch = r.Advance(1.5);
        Assert.Equal(new[] { "big.iso" }, batch!.Added);
    }

    [Fact]
    public void A_file_that_is_locked_is_held_back_until_it_can_be_opened()
    {
        var r = new Rig(debounceSeconds: 1);
        r.Probe.Sizes["report.docx"] = 50;
        r.Probe.Locked.Add("report.docx");
        r.Batcher.OnCreated("report.docx");

        Assert.Null(r.Advance(2));
        Assert.Null(r.Advance(5));
        r.Probe.Locked.Clear();
        Assert.Equal(new[] { "report.docx" }, r.Advance(1)!.Added);
    }

    [Fact]
    public void A_file_that_never_unlocks_is_dropped_after_the_give_up_time()
    {
        var probe = new FakeProbe();
        var now = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        var b = new FolderChangeBatcher(TimeSpan.FromSeconds(1), probe, null, () => now, giveUpAfter: TimeSpan.FromMinutes(1));
        probe.Sizes["stuck.bin"] = 1;
        probe.Locked.Add("stuck.bin");
        b.OnCreated("stuck.bin");
        now += TimeSpan.FromSeconds(30);
        Assert.Null(b.Poll());
        now += TimeSpan.FromSeconds(40);
        Assert.Null(b.Poll());
        Assert.False(b.HasPending);
    }

    [Fact]
    public void Created_then_changed_is_still_just_added()
    {
        var r = new Rig();
        r.Probe.Sizes["a.txt"] = 1;
        r.Batcher.OnCreated("a.txt");
        r.Batcher.OnChanged("a.txt");
        var batch = r.Until()!;
        Assert.Equal(new[] { "a.txt" }, batch.Added);
        Assert.Empty(batch.Changed);
    }

    [Fact]
    public void Changed_and_removed_files_are_reported_in_their_own_lists()
    {
        var r = new Rig();
        r.Probe.Sizes["edit.txt"] = 1;
        r.Batcher.OnChanged("edit.txt");
        r.Batcher.OnDeleted("gone.txt");
        var batch = r.Until()!;
        Assert.Equal(new[] { "edit.txt" }, batch.Changed);
        Assert.Equal(new[] { "gone.txt" }, batch.Removed);
        Assert.Empty(batch.Added);
    }

    [Fact]
    public void A_file_that_appears_and_vanishes_within_the_burst_is_not_reported()
    {
        var r = new Rig();
        r.Probe.Sizes["temp.txt"] = 1;
        r.Batcher.OnCreated("temp.txt");
        r.Probe.Sizes.Remove("temp.txt");
        r.Batcher.OnDeleted("temp.txt");
        Assert.Null(r.Advance(1));
        Assert.Null(r.Advance(5));
        Assert.False(r.Batcher.HasPending);
    }

    [Fact]
    public void A_replaced_file_counts_as_changed_not_added_and_removed()
    {
        var r = new Rig();
        r.Probe.Sizes["a.txt"] = 1;
        r.Batcher.OnDeleted("a.txt");
        r.Batcher.OnCreated("a.txt");
        var batch = r.Until()!;
        Assert.Equal(new[] { "a.txt" }, batch.Changed);
    }

    [Fact]
    public void Files_that_do_not_pass_the_filter_are_ignored()
    {
        var r = new Rig(accept: p => p.EndsWith(".pdf"));
        r.Probe.Sizes["a.txt"] = 1;
        r.Batcher.OnCreated("a.txt");
        Assert.False(r.Batcher.HasPending);
    }

    [Fact]
    public void Nothing_is_reported_while_new_events_keep_arriving()
    {
        var r = new Rig(debounceSeconds: 3);
        r.Probe.Sizes["a.txt"] = 1;
        for (var i = 0; i < 10; i++)
        {
            r.Batcher.OnChanged("a.txt");
            Assert.Null(r.Advance(2));
        }
        Assert.Null(r.Advance(2));
        Assert.NotNull(r.Advance(2));
    }
}
