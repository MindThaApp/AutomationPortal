using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Flowmate.Hosting.Tests;

/// <summary>The shared libraries must be usable from a WinUI (or any non-WPF) app.</summary>
public class ArchitectureTests
{
    private static readonly string[] SharedLibraries =
    {
        "Flowmate.Core", "Flowmate.Actions.Content", "Flowmate.Actions.Documents", "Flowmate.Actions.Windows", "Flowmate.Triggers", "Flowmate.Ai.Windows",
    };

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "System.Xaml", "UIAutomationClient", "UIAutomationTypes",
    };

    private static IEnumerable<string> References(string path)
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata) yield break;
        var md = pe.GetMetadataReader();
        foreach (var h in md.AssemblyReferences) yield return md.GetString(md.GetAssemblyReference(h).Name);
    }

    [Fact]
    public void Shared_libraries_do_not_reference_WPF_or_WinForms_directly_or_transitively()
    {
        var dir = AppContext.BaseDirectory;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, string Chain)>(SharedLibraries.Select(n => (n, n)));
        var problems = new List<string>();
        while (queue.Count > 0)
        {
            var (name, chain) = queue.Dequeue();
            if (!seen.Add(name)) continue;
            var file = Path.Combine(dir, name + ".dll");
            if (!File.Exists(file)) continue;   // framework assembly: its name was checked by the referrer
            foreach (var r in References(file))
            {
                if (Forbidden.Contains(r)) problems.Add($"{chain} -> {r}");
                else queue.Enqueue((r, $"{chain} -> {r}"));
            }
        }
        Assert.True(SharedLibraries.All(n => File.Exists(Path.Combine(dir, n + ".dll"))), "A shared library is missing from the test output.");
        Assert.Empty(problems);
    }

    [Fact]
    public void Nothing_in_the_chain_pulls_in_the_Windows_desktop_framework()
    {
        // UseWPF / UseWindowsForms anywhere in the referenced projects flows a Microsoft.WindowsDesktop.App framework reference into this one.
        var config = Path.Combine(AppContext.BaseDirectory, "Flowmate.Hosting.Tests.runtimeconfig.json");
        Assert.True(File.Exists(config));
        Assert.DoesNotContain("Microsoft.WindowsDesktop.App", File.ReadAllText(config));
    }
}
