using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.App.Tests;

public sealed class DroppedFilesTests
{
    private static readonly string[] Dropped = { @"C:\Temp\drop\a.txt", @"C:\Temp\drop\Photos" };

    private static ActionRegistry Registry()
    {
        var actions = new ActionRegistry();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.Transfer).Assembly);
        return actions;
    }

    private static Shortcut With(params string[] ids)
    {
        var s = new Shortcut { Name = "Tidy", AcceptsInput = { ContentKind.File } };
        foreach (var id in ids) s.Actions.Add(ActionInstance.Create(id));
        return s;
    }

    [Fact]
    public void Only_shortcuts_that_take_files_accept_a_drop()
    {
        var paths = DroppedFiles.Paths(Dropped);
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.File } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Folder } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Image } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Any } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { WhenNoInput = WhenNoInput.AskForFiles }, paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut(), paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Text, ContentKind.Url } }, paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.File } }, DroppedFiles.Paths(Array.Empty<string>())));
    }

    [Fact]
    public void Paths_keep_full_paths_once_each_and_drop_the_rest()
    {
        var paths = DroppedFiles.Paths(new[] { @"C:\Temp\a.txt", @"c:\temp\A.TXT", "", "   ", @"relative\b.txt", @"C:\Temp\Folder" });
        Assert.Equal(new[] { @"C:\Temp\a.txt", @"C:\Temp\Folder" }, paths);
        Assert.Empty(DroppedFiles.Paths((IEnumerable<string>?)null));
    }

    [Fact]
    public void Shortcut_files_alone_are_left_to_the_library_to_add()
    {
        Assert.False(DroppedFiles.IsForTile(DroppedFiles.Paths(new[] { @"C:\Temp\Tidy.autoportal" })));
        Assert.True(DroppedFiles.IsForTile(DroppedFiles.Paths(new[] { @"C:\Temp\Tidy.autoportal", @"C:\Temp\a.txt" })));
    }

    [Fact]
    public async Task Dropped_files_become_the_Shortcut_Input_without_asking_for_files()
    {
        using var host = new TestHost();
        var b = new ShortcutBuilder("Ask");
        b.Add("content.text", ("text", ParamValue.Text(VarRef.ShortcutInput)));
        var s = b.Build();
        s.WhenNoInput = WhenNoInput.AskForFiles;

        var r = await host.Runner.RunAsync(s, DroppedFiles.Input(DroppedFiles.Paths(Dropped)), new RunOptions { Source = RunSource.Library });

        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Contains("a.txt", r.Output.AsText());
        Assert.Contains("Photos", r.Output.AsText());
        Assert.DoesNotContain("pickFiles", host.UI.Calls);
    }

    [Fact]
    public void A_drop_on_a_file_changing_shortcut_goes_to_the_check_first_with_the_dropped_files()
    {
        var s = With("file.move");
        var input = DroppedFiles.Input(DroppedFiles.Paths(Dropped));
        IReadOnlyList<string>? checkedSteps = null;
        ContentValue? checkedInput = null;
        var ran = false;

        TileRunGate.Start(s, input, new AppSettings(), Registry(), (steps, given) => { checkedSteps = steps; checkedInput = given; }, _ => ran = true);

        Assert.False(ran);
        Assert.NotNull(checkedSteps);
        Assert.NotEmpty(checkedSteps!);
        Assert.Same(input, checkedInput);
    }

    [Fact]
    public void A_drop_follows_the_same_rule_as_a_click()
    {
        var actions = Registry();
        var s = With("file.move");
        var input = DroppedFiles.Input(DroppedFiles.Paths(Dropped));

        string Route(AppSettings settings, ContentValue? given)
        {
            var route = "";
            TileRunGate.Start(s, given, settings, actions, (_, _) => route = "check", _ => route = "run");
            return route;
        }

        var fresh = new AppSettings();
        Assert.Equal(Route(fresh, null), Route(fresh, input));

        var alreadyChecked = new AppSettings();
        alreadyChecked.FileChangeCheckedShortcutIds.Add(s.Id);
        Assert.Equal("run", Route(alreadyChecked, input));
        Assert.Equal(Route(alreadyChecked, null), Route(alreadyChecked, input));

        var readsOnly = With("file.readText");
        ContentValue? ranWith = null;
        TileRunGate.Start(readsOnly, input, fresh, actions, (_, _) => Assert.Fail("A shortcut that only reads files has no check."), given => ranWith = given);
        Assert.Same(input, ranWith);
    }
}
