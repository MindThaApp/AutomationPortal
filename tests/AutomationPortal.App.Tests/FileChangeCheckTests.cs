using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public sealed class FileChangeCheckTests
{
    private static ActionRegistry Registry()
    {
        var actions = new ActionRegistry();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.Transfer).Assembly);
        return actions;
    }

    private static Shortcut With(params string[] ids)
    {
        var s = new Shortcut { Name = "Tidy" };
        foreach (var id in ids) s.Actions.Add(ActionInstance.Create(id));
        return s;
    }

    [Fact]
    public void Moving_renaming_and_deleting_count_as_changing_files()
    {
        var steps = FileChangeCheck.FileChangingSteps(With("file.move", "file.rename", "file.delete"), Registry());
        Assert.Equal(3, steps.Count);
    }

    [Fact]
    public void Reading_files_does_not()
    {
        Assert.Empty(FileChangeCheck.FileChangingSteps(With("file.readText", "file.selected"), Registry()));
    }

    [Fact]
    public void Asks_once_per_shortcut_and_not_after_dont_show_again()
    {
        var id = Guid.NewGuid();
        var steps = new[] { "Move Files" };
        var settings = new AppSettings();
        Assert.True(FileChangeCheck.ShouldAsk(settings, id, steps));
        Assert.False(FileChangeCheck.ShouldAsk(settings, id, Array.Empty<string>()));
        settings.FileChangeCheckedShortcutIds.Add(id);
        Assert.False(FileChangeCheck.ShouldAsk(settings, id, steps));
        Assert.True(FileChangeCheck.ShouldAsk(settings, Guid.NewGuid(), steps));
        settings.AskBeforeFileChangingRuns = false;
        Assert.False(FileChangeCheck.ShouldAsk(settings, Guid.NewGuid(), steps));
    }
}
