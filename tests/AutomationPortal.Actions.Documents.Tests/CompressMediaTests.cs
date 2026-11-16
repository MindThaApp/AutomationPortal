using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

public class CompressMediaTests
{
    private static ImageItem First(ContentValue v) => Assert.IsType<ImageItem>(v[0]);

    [Fact]
    public async Task Saves_a_new_copy_next_to_the_original_and_never_touches_the_original()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 400, 200, jpegQuality: 100);
        var before = File.ReadAllBytes(src);

        var r = await T.Run(host, new CompressMediaAction(), ContentValue.Images(new[] { src }), ("quality", 30), ("maxWidth", 200));

        var img = First(r);
        Assert.Equal(Path.Combine(d.Path, "a (compressed).jpg"), img.Path);
        Assert.Equal((200, 100), (img.Width, img.Height));
        Assert.True(new FileInfo(img.Path).Length < before.Length);
        Assert.Equal(before, File.ReadAllBytes(src));
    }

    [Fact]
    public async Task Never_overwrites_an_existing_compressed_file()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 100, 50);
        var existing = d.File("a (compressed).png");
        File.WriteAllText(existing, "keep me");

        var a = await T.Run(host, new CompressMediaAction(), ContentValue.Images(new[] { src }));
        var b = await T.Run(host, new CompressMediaAction(), ContentValue.Images(new[] { src }));

        Assert.Equal("a (compressed) (2).png", Path.GetFileName(First(a).Path));
        Assert.Equal("a (compressed) (3).png", Path.GetFileName(First(b).Path));
        Assert.Equal("keep me", File.ReadAllText(existing));
    }

    [Fact]
    public async Task Can_save_in_a_chosen_folder()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 100, 50);
        var folder = d.Folder("out");
        var r = await T.Run(host, new CompressMediaAction(), ContentValue.Images(new[] { src }),
            ("saveTo", T.Choice("folder")), ("saveFolder", ParamValue.File(folder)));
        Assert.Equal(folder, Path.GetDirectoryName(First(r).Path));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task Created_files_are_recorded_so_undo_can_remove_them()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 100, 50);
        var action = new CompressMediaAction();
        var ctx = ActionTestHarness.CreateContext(host, action, ContentValue.Images(new[] { src }));

        var r = await action.RunAsync(ctx);

        var run = ctx.Journal.Store.Read(ctx.Journal.RunId);
        Assert.NotNull(run);
        var change = Assert.Single(run!.Changes);
        Assert.Equal(FileChangeKind.Created, change.Kind);
        Assert.Equal(Path.GetFullPath(First(r).Path), change.Path, ignoreCase: true);
    }

    [Fact]
    public void Summary_reports_before_and_after_sizes()
    {
        var text = CompressMediaAction.Summary(2, 4096, 1024);
        Assert.Contains("2 pictures", text);
        Assert.Contains("4 KB", text);
        Assert.Contains("1 KB", text);
        Assert.Contains("75% smaller", text);
    }
}
