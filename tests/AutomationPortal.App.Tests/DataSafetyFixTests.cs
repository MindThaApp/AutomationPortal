using System.IO;
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

    [Fact]
    public void Toast_undo_asks_first_when_the_run_is_older_than_ten_minutes()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        JournaledRun RunAt(params int[] minutesAgo) => new()
        {
            RunId = "r", Changes = minutesAgo.Select(m => new FileChange { Kind = FileChangeKind.Created, Path = @"C:\x", At = now.AddMinutes(-m) }).ToList(),
        };
        Assert.False(AutomationPortal.App.Services.RunUndo.AskFirst(RunAt(2), now));
        Assert.False(AutomationPortal.App.Services.RunUndo.AskFirst(RunAt(30, 9), now));   // its last change counts
        Assert.True(AutomationPortal.App.Services.RunUndo.AskFirst(RunAt(11), now));
        Assert.True(AutomationPortal.App.Services.RunUndo.AskFirst(RunAt(60 * 24 * 6), now));
        Assert.True(AutomationPortal.App.Services.RunUndo.AskFirst(null, now));
    }

    [Fact]
    public void Tidy_needs_at_least_one_day_and_its_template_leaves_out_unfinished_downloads()
    {
        var min = TemplateRules.MinimumFor("file.filter", "olderThanDays", 0);
        Assert.Equal(1, min);
        Assert.Null(TemplateRules.Accept(0, min));
        Assert.Null(TemplateRules.Accept(0.5, min));
        Assert.Equal(1, TemplateRules.Accept(1, min));
        Assert.Equal(30, TemplateRules.Accept(30, min));
        Assert.Equal(0, TemplateRules.MinimumFor("file.filter", "minSizeMb", 0));   // other blanks keep their own minimum

        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AutomationPortal.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "gallery", "template-tidy-downloads.autoportal")));
        var filter = json.RootElement.GetProperty("shortcut").GetProperty("actions").EnumerateArray()
            .Single(a => a.GetProperty("actionId").GetString() == "file.filter").GetProperty("parameters");
        Assert.True(filter.GetProperty("skipPartial").GetProperty("v").GetBoolean());
        Assert.True(filter.GetProperty("olderThanDays").GetProperty("v").GetDouble() >= 1);
    }

    [Fact]
    public void Tray_menu_hides_record_for_now()
    {
        Assert.False(AutomationPortal.App.Shell.TrayService.ShowRecordEntry);
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AutomationPortal.App", "Shell", "TrayService.cs"));
        Assert.Contains("if (ShowRecordEntry) Add(menu, Strings.Get(\"TrayService_RecordANewShortcut\")", source);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AutomationPortal.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repository root not found");
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
