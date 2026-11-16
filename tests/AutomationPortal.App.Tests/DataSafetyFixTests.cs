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
}
