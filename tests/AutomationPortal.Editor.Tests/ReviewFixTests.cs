using System.IO;
using System.Windows.Threading;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Editor;
using AutomationPortal.Editor.Logic;
using AutomationPortal.Editor.ViewModels;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public sealed class MakeTempAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.makeTemp", Title = "Make temp", Category = ActionCategories.Advanced };
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Files(new[] { ctx.WriteTempFile("made.txt", "hello").Path }));
}

public class ReviewFixTests
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new System.Action(() => { }));

    [Fact]
    public void Editor_keeps_folder_and_order_changed_in_the_library_window()
    {
        using var host = new TestHost();
        var folder = new ShortcutFolder { Name = "Work" };
        host.Library.SaveFolder(folder);
        var s = new Shortcut { Name = "S" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "x")));
        host.Library.Save(s);

        var ctx = new EditorContext(host.Services, ActionRegistry.CreateWithBuiltIns());
        var vm = new EditorViewModel(ctx, host.Library.GetById(s.Id)!.Clone(), isNew: false);

        var moved = host.Library.GetById(s.Id)!.Clone();
        moved.FolderId = folder.Id; moved.SortOrder = 7;
        host.Library.Save(moved);
        Pump();

        vm.Shortcut.Description = "edited in the editor";
        vm.MetadataEdited();
        Assert.True(vm.SaveNow());
        var saved = host.Library.GetById(s.Id)!;
        Assert.Equal(folder.Id, saved.FolderId);
        Assert.Equal(7, saved.SortOrder);
        Assert.Equal("edited in the editor", saved.Description);
        vm.Detach();
    }

    [Fact]
    public async Task Run_from_here_restores_named_variables_set_above_the_start_point()
    {
        using var host = new TestHost();
        var s = new Shortcut { Name = "Vars" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "kept")).WithUid("t1"));
        s.Actions.Add(ActionInstance.Create("var.set", ("name", "X")).WithUid("v1"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.Text("got ", VarRef.Named("X")))).WithUid("t2"));
        var rc = new RunController();
        await rc.RunAsync(s.Clone(), host.Runner);
        var again = await rc.RunAsync(s.Clone(), host.Runner, startIndex: 2);
        Assert.Equal(RunStatus.Succeeded, again.Status);
        Assert.Equal("got kept", again.Output.AsText());
        rc.DiscardTemp();
    }

    [Fact]
    public async Task Run_from_here_outputs_that_are_temp_files_survive_until_discarded()
    {
        using var host = new TestHost().With(typeof(MakeTempAction).Assembly);
        var s = new Shortcut { Name = "Temp" };
        s.Actions.Add(ActionInstance.Create("test.makeTemp").WithUid("m"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "x")).WithUid("t"));
        var rc = new RunController();
        await rc.RunAsync(s.Clone(), host.Runner);
        var file = ((PathItem)rc.Outputs()["m"][0]).Path;
        Assert.True(File.Exists(file));
        rc.DiscardTemp();
        Assert.False(File.Exists(file));
    }
}
