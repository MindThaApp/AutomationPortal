using System.IO;
using System.Collections.Generic;
using System.Linq;
using AutomationPortal.Core;
using Xunit;
using AutomationPortal.Editor;
using AutomationPortal.Triggers;

namespace AutomationPortal.App.Tests;

public class IconTests
{
    private static string? RepoGallery()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "gallery")) && File.Exists(Path.Combine(dir.FullName, "AutomationPortal.sln"))) return Path.Combine(dir.FullName, "gallery");
        return null;
    }

    [Fact]
    public void Every_descriptor_and_gallery_icon_exists()
    {
        var actions = ActionRegistry.CreateWithBuiltIns();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.AiService).Assembly);
        var triggers = new TriggerRegistry(); var conditions = new ConditionRegistry();
        TriggerHost.RegisterProviders(triggers, conditions);

        var bad = new List<string>();
        foreach (var a in actions.All) if (!IconNames.IsValid(a.Descriptor.Icon)) bad.Add($"action {a.Descriptor.Id}: {a.Descriptor.Icon}");
        foreach (var t in triggers.All) if (!IconNames.IsValid(t.Descriptor.Icon)) bad.Add($"trigger {t.Descriptor.Id}: {t.Descriptor.Icon}");
        foreach (var c in conditions.All) if (!string.IsNullOrEmpty(c.Descriptor.Icon) && !IconNames.IsValid(c.Descriptor.Icon)) bad.Add($"condition {c.Descriptor.Id}: {c.Descriptor.Icon}");

        if (RepoGallery() is { } gallery)
            foreach (var file in Directory.GetFiles(gallery, "*.autoportal"))
            {
                var s = ShortcutPackage.Parse(File.ReadAllText(file));
                if (!IconNames.IsValid(s.Icon)) bad.Add($"gallery {Path.GetFileName(file)}: {s.Icon}");
            }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
