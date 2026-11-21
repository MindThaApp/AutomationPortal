using System.IO;
using System.Text.RegularExpressions;
using Wpf.Ui.Controls;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>An unknown icon name only fails when the page is parsed at run time, so check every one here.</summary>
public class XamlSymbolNameTests
{
    private static readonly Regex SymbolUse = new(@"(?:SymbolIcon\s+|Symbol=""|SymbolRegular\.)([A-Za-z]+\d+)", RegexOptions.Compiled);

    private static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        return null;
    }

    [Fact]
    public void Every_symbol_name_in_App_and_Editor_source_exists()
    {
        var root = Root();
        Assert.NotNull(root);
        var unknown = new List<string>();
        foreach (var project in new[] { "AutomationPortal.App", "AutomationPortal.Editor" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "src", project), "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".xaml") || file.EndsWith(".cs")) || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in SymbolUse.Matches(File.ReadAllText(file)))
                if (!Enum.IsDefined(typeof(SymbolRegular), m.Groups[1].Value))
                    unknown.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        }
        Assert.Empty(unknown);
    }
}
