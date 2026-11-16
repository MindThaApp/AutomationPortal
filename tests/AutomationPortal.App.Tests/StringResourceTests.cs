using System.IO;
using System.Resources;
using System.Xml.Linq;
using Xunit;

namespace AutomationPortal.App.Tests;

public class StringResourceTests
{
    private static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        return null;
    }

    private static void EveryKeyResolves(string project, System.Reflection.Assembly assembly)
    {
        var root = Root();
        Assert.NotNull(root);
        var doc = XDocument.Load(Path.Combine(root!, "src", project, "Strings.resx"));
        var manager = new ResourceManager(project + ".Strings", assembly);
        var data = doc.Root!.Elements("data").ToList();
        Assert.NotEmpty(data);
        foreach (var d in data)
        {
            var key = d.Attribute("name")!.Value;
            Assert.Equal(d.Element("value")!.Value, manager.GetString(key, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact] public void App_strings_are_embedded_and_resolve() => EveryKeyResolves("AutomationPortal.App", typeof(AutomationPortal.App.Logic.TileAppearance).Assembly);

    [Fact] public void Editor_strings_are_embedded_and_resolve() => EveryKeyResolves("AutomationPortal.Editor", typeof(AutomationPortal.Editor.Palette).Assembly);
}
