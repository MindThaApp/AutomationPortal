using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public sealed class DataSafetyFixTests
{
    [Fact]
    public void UnattendedBackupFailureSaysBackupDidntRun()
    {
        var backup = new Shortcut { Name = "Back Up Photos" };
        var (title, body) = UnattendedFailure.Describe(backup.Name, backup, RunSource.Automation, "Access to the path is denied.");
        Assert.Equal("Backup didn't run", title);
        Assert.StartsWith("Backup didn't run: Access to the path is denied.", body);

        var byShape = new Shortcut { Name = "Nightly" };
        byShape.Actions.Add(new ActionInstance { ActionId = "file.newFolder" });
        byShape.Actions.Add(new ActionInstance { ActionId = "file.copy" });
        Assert.Equal("Backup didn't run", UnattendedFailure.Describe("Nightly", byShape, RunSource.Reminder, null).Title);

        // the same shortcut run by hand, or another shortcut, keeps the usual text
        Assert.Equal("\"Back Up Photos\" didn't finish", UnattendedFailure.Describe(backup.Name, backup, RunSource.Library, "x").Title);
        Assert.Equal("\"Other\" didn't finish", UnattendedFailure.Describe("Other", new Shortcut { Name = "Other" }, RunSource.Automation, "x").Title);
    }

    private static ActionRegistry AllActions()
    {
        var actions = ActionRegistry.CreateWithBuiltIns();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.Transfer).Assembly);
        return actions;
    }

    [Fact]
    public void Every_registered_step_is_read_only_listed_or_checked_before_it_runs()
    {
        var actions = AllActions();
        Assert.True(actions.All.Count > 100);
        var unchecked_ = new List<string>();
        foreach (var action in actions.All)
        {
            var id = action.Descriptor.Id;
            if (FilePreview.ReadOnlyActionIds.Contains(id)) { Assert.False(FileChangeCheck.ChangesFiles(id)); continue; }
            if (FilePreview.DoesNotChangeFiles(id) || id is FlowIds.Comment or FlowIds.If or FlowIds.Menu or FlowIds.Repeat or FlowIds.RepeatEach) continue;
            Assert.True(FileChangeCheck.ChangesFiles(id), id);
            var s = new Shortcut { Name = "One step" };
            s.Actions.Add(ActionInstance.Create(id));
            if (FileChangeCheck.CheckedSteps(s, actions).Count == 0) unchecked_.Add(id);
        }
        Assert.Empty(unchecked_);

        // the steps the review found missing
        foreach (var id in new[] { "file.renamePattern", "file.append", "file.unzip", "file.save", "img.resize", "pdf.merge", "sheet.addRow", "flow.runShortcut" })
            if (actions.Get(id) is not null) Assert.True(FileChangeCheck.ChangesFiles(id), id);
        Assert.Contains(actions.All, a => (a.Descriptor.Id.StartsWith("img.") || a.Descriptor.Id.StartsWith("pdf.")) && FileChangeCheck.ChangesFiles(a.Descriptor.Id));
        // reading steps stay quiet
        Assert.False(FileChangeCheck.ChangesFiles("file.readText"));
        Assert.False(FileChangeCheck.ChangesFiles("ui.notify"));
    }
}
