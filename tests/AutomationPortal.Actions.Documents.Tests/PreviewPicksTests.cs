using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

/// <summary>
/// "Show what it would do", then Run: a step that asks for a folder (or the shortcut's own file chooser) during the preview gives
/// the real run that same pick, once, for the same steps, only while the picked items are unchanged, and never for an unattended run.
/// </summary>
public class PreviewPicksTests
{
    private static (Shortcut Shortcut, string A, string B, string Out) Setup(TempDir d)
    {
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var outDir = d.Folder("out");
        var s = new Shortcut { Name = "Copy", WhenNoInput = WhenNoInput.AskForFiles };
        s.Actions.Add(ActionInstance.Create("file.copy", ("folder", ParamValue.Ask())));
        return (s, a, b, outDir);
    }

    private static async Task<FileChangePlan> PreviewAsync(TestHost host, Shortcut s, string[] files, string folder)
    {
        host.UI.Enqueue(ContentValue.Files(files));
        host.UI.Enqueue(folder);
        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Empty);
        Assert.Equal(2, host.UI.Calls.Count(c => c is "pickFiles" or "pickFolder"));
        return plan;
    }

    private static Task<RunResult> RunAsync(TestHost host, Shortcut s, PreviewPicks? picks, RunSource source = RunSource.Library, ContentValue? input = null) =>
        host.Runner.RunAsync(s, input ?? ContentValue.Empty, new RunOptions { Source = source, RecordHistory = false, PreviewPicks = picks });

    private static int Asks(TestHost host) => host.UI.Calls.Count(c => c is "pickFiles" or "pickFolder");

    [Fact]
    public async Task The_real_run_uses_the_files_and_the_folder_picked_in_the_preview_without_asking()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        Assert.Equal(2, plan.Picks!.Count);
        Assert.Empty(Directory.GetFiles(outDir));   // the preview changed nothing

        var result = await RunAsync(host, s, plan.Picks);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, Asks(host));                // nothing asked again
        var planned = plan.Changes.Where(c => c.Kind == PlannedChangeKind.Copy).Select(c => c.Target).ToList();
        Assert.Equal(2, planned.Count);
        Assert.Equal(planned, T.Paths(result.Output));
        Assert.True(plan.Picks.IsUsed);
    }

    [Fact]
    public async Task The_picks_apply_to_one_run_only()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        Assert.True((await RunAsync(host, s, plan.Picks)).Succeeded);

        var other = d.Folder("other");
        host.UI.Enqueue(ContentValue.Files(new[] { a })).Enqueue(other);
        var second = await RunAsync(host, s, plan.Picks);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(4, Asks(host));                // asked again
        Assert.Single(Directory.GetFiles(other));
    }

    [Fact]
    public async Task An_edit_after_the_preview_or_another_shortcut_means_asking_again()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);

        var edited = s.Clone();
        edited.Actions[0].Parameters["ifExists"] = ParamValue.Choice("skip");
        var other = d.Folder("other");
        host.UI.Enqueue(ContentValue.Files(new[] { a })).Enqueue(other);
        Assert.True((await RunAsync(host, edited, plan.Picks)).Succeeded);
        Assert.Equal(4, Asks(host));
        Assert.False(plan.Picks!.IsUsed);           // not spent on a run it didn't apply to

        var copy = s.Clone();
        copy.Id = Guid.NewGuid();
        host.UI.Enqueue(ContentValue.Files(new[] { b })).Enqueue(other);
        Assert.True((await RunAsync(host, copy, plan.Picks)).Succeeded);
        Assert.Equal(6, Asks(host));
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Theory]
    [InlineData(RunSource.Automation)]
    [InlineData(RunSource.Reminder)]
    [InlineData(RunSource.Hotkey)]
    public async Task Runs_other_than_library_or_editor_never_use_the_picks(RunSource source)
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);

        var other = d.Folder("other");
        host.UI.Enqueue(ContentValue.Files(new[] { a })).Enqueue(other);
        await RunAsync(host, s, plan.Picks, source);
        Assert.Equal(4, Asks(host));
        Assert.Empty(Directory.GetFiles(outDir));
        Assert.False(plan.Picks!.IsUsed);
    }

    [Fact]
    public async Task A_picked_file_that_changed_since_the_preview_is_told_and_asked_for_again()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        File.AppendAllText(a, " and more");

        var other = d.Folder("other");
        host.UI.Enqueue(true).Enqueue(ContentValue.Files(new[] { b })).Enqueue(other);
        var result = await RunAsync(host, s, plan.Picks);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains("alert", host.UI.Calls);
        Assert.Contains(host.UI.Prompts, p => p.Contains("a.txt") && p.Contains("Since the preview"));
        Assert.Equal(4, Asks(host));
        Assert.Empty(Directory.GetFiles(outDir));
        Assert.Single(Directory.GetFiles(other));
    }

    [Fact]
    public async Task A_picked_item_that_is_gone_stops_the_run_when_the_person_cancels()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        File.Delete(b);

        host.UI.Enqueue(false);
        var result = await RunAsync(host, s, plan.Picks);
        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Equal(2, Asks(host));
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public async Task A_changed_folder_with_nobody_to_ask_fails_plainly_and_changes_nothing()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        File.WriteAllText(Path.Combine(outDir, "new.txt"), "x");
        Directory.SetLastWriteTimeUtc(outDir, DateTime.UtcNow.AddMinutes(5));

        host.Services.UserInteraction = null!;
        var result = await RunAsync(host, s, plan.Picks);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("Since the preview", result.ErrorMessage);
        Assert.Equal(new[] { "new.txt" }, Directory.GetFiles(outDir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_file_added_inside_a_subfolder_of_the_picked_folder_is_told_and_asked_for_again()
    {
        using var d = new TempDir();
        var (s, a, b, outDir) = Setup(d);
        d.File("out/sub/old.txt", "old");
        var host = T.Host();
        var plan = await PreviewAsync(host, s, new[] { a, b }, outDir);
        var folderTime = Directory.GetLastWriteTimeUtc(outDir);
        File.WriteAllText(Path.Combine(outDir, "sub", "new.txt"), "x");
        Directory.SetLastWriteTimeUtc(outDir, folderTime);   // the picked folder itself looks untouched

        var other = d.Folder("other");
        host.UI.Enqueue(true).Enqueue(ContentValue.Files(new[] { b })).Enqueue(other);
        var result = await RunAsync(host, s, plan.Picks);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(host.UI.Prompts, p => p.Contains("'out'") && p.Contains("Since the preview"));
        Assert.Equal(4, Asks(host));
        Assert.Empty(Directory.GetFiles(outDir));
        Assert.Single(Directory.GetFiles(other));
    }

    [Fact]
    public async Task A_step_asked_for_inside_a_repeat_is_kept_per_item()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var outA = d.Folder("outA");
        var outB = d.Folder("outB");
        var builder = new ShortcutBuilder("Each");
        builder.RepeatEach(ParamValue.Var(VarRef.ShortcutInput), x => x.Add("file.copy", ("folder", ParamValue.Ask())));
        var s = builder.Build();
        var input = ContentValue.Files(new[] { a, b });

        var host = T.Host();
        host.UI.Enqueue(outA).Enqueue(outB);
        var plan = await FilePreview.RunAsync(host.Runner, s, input);
        Assert.Equal(2, plan.Picks!.Count);
        Assert.Equal(2, Asks(host));

        var result = await RunAsync(host, s, plan.Picks, input: input);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, Asks(host));
        Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(outA).Select(Path.GetFileName));
        Assert.Equal(new[] { "b.txt" }, Directory.GetFiles(outB).Select(Path.GetFileName));
    }

    private static async Task<(Shortcut Shortcut, FileChangePlan Plan, string A, string B, string Out)> PreviewFromClipboardAsync(TempDir d, TestHost host)
    {
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var outDir = d.Folder("out");
        var s = new Shortcut { Name = "Copy", WhenNoInput = WhenNoInput.UseClipboard };
        s.Actions.Add(ActionInstance.Create("file.copy", ("folder", ParamValue.Ask())));
        host.Clipboard.Value = ContentValue.Files(new[] { a });
        host.UI.Enqueue(outDir);
        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Empty);
        Assert.Equal(1, Asks(host));
        return (s, plan, a, b, outDir);
    }

    [Fact]
    public async Task The_clipboard_from_the_preview_is_used_while_it_still_holds_the_same_content()
    {
        using var d = new TempDir();
        var host = T.Host();
        var (s, plan, _, _, outDir) = await PreviewFromClipboardAsync(d, host);
        var result = await RunAsync(host, s, plan.Picks);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.DoesNotContain("alert", host.UI.Calls);
        Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(outDir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_clipboard_that_changed_since_the_preview_is_told_and_its_new_content_used_only_when_the_person_agrees()
    {
        using var d = new TempDir();
        var host = T.Host();
        var (s, plan, _, b, outDir) = await PreviewFromClipboardAsync(d, host);
        host.Clipboard.Value = ContentValue.Files(new[] { b });

        host.UI.Enqueue(true);
        var result = await RunAsync(host, s, plan.Picks);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(host.UI.Prompts, p => p.Contains("what's on the clipboard changed"));
        Assert.Equal(new[] { "b.txt" }, Directory.GetFiles(outDir).Select(Path.GetFileName));   // never the preview's a.txt
    }

    [Fact]
    public async Task A_clipboard_that_changed_since_the_preview_stops_the_run_when_declined_or_nobody_can_be_asked()
    {
        using var d = new TempDir();
        var host = T.Host();
        var (s, plan, _, _, outDir) = await PreviewFromClipboardAsync(d, host);
        host.Clipboard.Value = ContentValue.Text("something else");
        host.Services.UserInteraction = null!;
        var result = await RunAsync(host, s, plan.Picks);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("what's on the clipboard changed", result.ErrorMessage);
        Assert.Contains("Nothing was changed", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(outDir));

        using var d2 = new TempDir();
        var host2 = T.Host();
        var (s2, plan2, _, b2, out2) = await PreviewFromClipboardAsync(d2, host2);
        host2.Clipboard.Value = ContentValue.Files(new[] { b2 });
        host2.UI.Enqueue(false);
        var declined = await RunAsync(host2, s2, plan2.Picks);
        Assert.Equal(RunStatus.Cancelled, declined.Status);
        Assert.Empty(Directory.GetFiles(out2));
    }

    private static Shortcut CopyEachToAskedFolder()
    {
        var builder = new ShortcutBuilder("Each");
        builder.RepeatEach(ParamValue.Var(VarRef.ShortcutInput), x => x.Add("file.copy", ("folder", ParamValue.Ask())));
        return builder.Build();
    }

    [Fact]
    public async Task A_pick_changed_by_an_earlier_step_of_the_run_is_told_and_asked_for_again_when_it_is_used()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var shared = d.Folder("shared");
        var other = d.Folder("other");
        var s = CopyEachToAskedFolder();
        var input = ContentValue.Files(new[] { a, b });

        var host = T.Host();
        host.UI.Enqueue(shared).Enqueue(shared);
        var plan = await FilePreview.RunAsync(host.Runner, s, input);
        Assert.Equal(2, Asks(host));

        // copying a.txt into the folder changes it before b.txt's pick of that same folder is used
        host.UI.Enqueue(true).Enqueue(other);
        var result = await RunAsync(host, s, plan.Picks, input: input);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(3, Asks(host));
        Assert.Contains(host.UI.Prompts, p => p.Contains("'shared'") && p.Contains("Since the preview"));
        Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(shared).Select(Path.GetFileName));
        Assert.Equal(new[] { "b.txt" }, Directory.GetFiles(other).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_pick_declined_after_a_change_during_the_run_cancels_it()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var shared = d.Folder("shared");
        var s = CopyEachToAskedFolder();
        var input = ContentValue.Files(new[] { a, b });

        var host = T.Host();
        host.UI.Enqueue(shared).Enqueue(shared);
        var plan = await FilePreview.RunAsync(host.Runner, s, input);

        host.UI.Enqueue(false);
        var result = await RunAsync(host, s, plan.Picks, input: input);
        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Equal(2, Asks(host));
        Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(shared).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_repeat_over_different_items_than_the_preview_asks_again()
    {
        using var d = new TempDir();
        var a = d.File("in/a.txt", "a");
        var b = d.File("in/b.txt", "b");
        var outA = d.Folder("outA");
        var outB = d.Folder("outB");
        var builder = new ShortcutBuilder("Each");
        builder.RepeatEach(ParamValue.Var(VarRef.ShortcutInput), x => x.Add("file.copy", ("folder", ParamValue.Ask())));
        var s = builder.Build();

        var host = T.Host();
        host.UI.Enqueue(outA).Enqueue(outB);
        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Files(new[] { a, b }));

        // the same files in the other order: the folder picked for a.txt must not be used for b.txt
        host.UI.Enqueue(outB).Enqueue(outA);
        var result = await RunAsync(host, s, plan.Picks, input: ContentValue.Files(new[] { b, a }));
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(4, Asks(host));
        Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(outA).Select(Path.GetFileName));
        Assert.Equal(new[] { "b.txt" }, Directory.GetFiles(outB).Select(Path.GetFileName));
    }
}
