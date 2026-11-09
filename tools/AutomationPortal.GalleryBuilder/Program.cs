using System.Reflection;
using System.Text.Json;
using AutomationPortal.Core;
using IOFile = System.IO.File;

namespace AutomationPortal.GalleryBuilder;

internal static class Catalog
{
    public static readonly string[] ActionAssemblies = { "AutomationPortal.Actions.Content", "AutomationPortal.Actions.Windows", "AutomationPortal.Actions.Documents" };

    public static ActionRegistry CreateRegistry()
    {
        var r = ActionRegistry.CreateWithBuiltIns();
        foreach (var n in ActionAssemblies) r.RegisterAssembly(Assembly.Load(n));
        return r;
    }

    public static TriggerRegistry CreateTriggers()
    {
        var t = new TriggerRegistry();
        t.RegisterAssembly(Assembly.Load("AutomationPortal.Triggers"));
        return t;
    }
}

internal static class Program
{
    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (IOFile.Exists(Path.Combine(d.FullName, "AutomationPortal.sln"))) return d.FullName;
        throw new InvalidOperationException("Run this from inside the Automation Portal repository (AutomationPortal.sln not found).");
    }

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var noWrite = args.Contains("--no-write");
        var outDir = Path.Combine(FindRoot(), "gallery");

        var registry = Catalog.CreateRegistry();
        var triggers = Catalog.CreateTriggers();
        foreach (var d in registry.Diagnostics.Concat(triggers.Diagnostics)) Console.WriteLine("registry: " + d);
        Console.WriteLine($"{registry.All.Count} actions and {triggers.All.Count} triggers registered.");

        var icons = new HashSet<string>(Enum.GetNames<Wpf.Ui.Controls.SymbolRegular>());
        var starters = Starters.All();

        var problems = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in starters)
        {
            var errors = Validation.Check(s, registry, triggers, icons);
            if (!names.Add(s.Shortcut.Name)) errors.Add("Duplicate name.");
            if (!Starters.SectionOrder.Contains(s.Section)) errors.Add($"Unknown section '{s.Section}'.");
            if (errors.Count == 0) Console.WriteLine($"  OK    {s.Section,-17} {s.Shortcut.Name}  ({s.Shortcut.Actions.Count} steps, {s.Shortcut.ImportQuestions.Count} questions)");
            foreach (var e in errors) { problems++; Console.WriteLine($"  FAIL  {s.Shortcut.Name}: {e}"); }
        }
        if (problems > 0)
        {
            Console.WriteLine($"{problems} problem(s). Nothing was written.");
            return 1;
        }

        if (!noWrite)
        {
            Directory.CreateDirectory(outDir);
            var keep = new HashSet<string>();
            foreach (var s in starters)
            {
                var file = s.Slug + ".autoportal";
                keep.Add(file);
                IOFile.WriteAllText(Path.Combine(outDir, file), ShortcutPackage.Serialize(s.Shortcut) + Environment.NewLine);
            }
            foreach (var old in Directory.GetFiles(outDir, "*.autoportal").Where(f => !keep.Contains(Path.GetFileName(f)))) IOFile.Delete(old);

            var index = new
            {
                sections = Starters.SectionOrder.Select(title => new
                {
                    title,
                    files = starters.Where(s => s.Section == title).Select(s => s.Slug + ".autoportal").ToArray(),
                }).Where(s => s.files.Length > 0).ToArray(),
            };
            IOFile.WriteAllText(Path.Combine(outDir, "index.json"),
                JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine($"Wrote {starters.Count} shortcuts and index.json to {outDir}");
        }

        if (dryRun)
        {
            Console.WriteLine("Dry runs (fake services, temp copies):");
            var failed = await DryRun.RunAllAsync(starters, registry);
            return failed == 0 ? 0 : 2;
        }
        return 0;
    }
}
