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
    public void Steps_that_control_other_apps_get_the_same_check_and_say_so()
    {
        var actions = Registry();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Windows.Apps.ClickInAppAction).Assembly);
        var s = With("uia.click", "uia.type", "text.combine");
        Assert.Empty(FileChangeCheck.FileChangingSteps(s, actions));
        Assert.Equal(new[] { "Click a Button in an App", "Type into a Field in an App" }, FileChangeCheck.CheckedSteps(s, actions));
        Assert.True(FileChangeCheck.ControlsApps(s, actions));
        Assert.True(FileChangeCheck.ShouldAsk(new AppSettings(), s.Id, FileChangeCheck.CheckedSteps(s, actions)));
        Assert.Contains("clicks and types in other apps", FileChangeCheck.Message(s.Name, FileChangeCheck.CheckedSteps(s, actions), true, false));
        Assert.Equal("This shortcut clicks and types in other apps.", new CallerPrompt("cli", "Tidy", Array.Empty<string>(), false) { ControlsApps = true }.ChangesFilesLine);
        Assert.False(FileChangeCheck.ControlsApps(With("file.move"), actions));
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
