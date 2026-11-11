using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class OfficeGalleryTests
{
    private static string Gallery()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "gallery")) && File.Exists(Path.Combine(dir.FullName, "AutomationPortal.sln"))) return Path.Combine(dir.FullName, "gallery");
        throw new InvalidOperationException("gallery folder not found");
    }

    [Fact]
    public void Office_starters_are_listed_and_only_use_known_actions()
    {
        var actions = ActionRegistry.CreateWithBuiltIns();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.AiService).Assembly);

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(Gallery(), "index.json")));
        var office = index.RootElement.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Office");
        var files = office.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToList();
        Assert.True(files.Count >= 5);

        foreach (var file in files)
        {
            var shortcut = ShortcutPackage.Parse(File.ReadAllText(Path.Combine(Gallery(), file)));
            Assert.False(string.IsNullOrWhiteSpace(shortcut.Description), file);
            Assert.All(shortcut.Actions, a => Assert.NotNull(actions.Describe(a.ActionId)));
            Assert.DoesNotContain(ShortcutValidator.Validate(shortcut, actions), i => i.Severity.ToString() == "Error");
        }
    }
}
