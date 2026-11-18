using System.IO;
using System.Xml.Linq;
using Xunit;

namespace AutomationPortal.App.Tests;

public class XamlAccessibilityTests
{
    private static readonly string[] Named = { "Button", "ToggleSwitch", "ToggleButton", "CheckBox", "RadioButton" };

    private static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        return null;
    }

    private static bool HasName(XElement e)
    {
        foreach (var a in e.Attributes())
        {
            var n = a.Name.LocalName;
            if (n == "AutomationProperties.Name" && a.Value.Length > 0) return true;
            if (n == "Content" && a.Value.Length > 0 && !a.Value.StartsWith("{")) return true;
            // A binding for Content or the name counts as text supplied at run time.
            if ((n == "Content" || n == "AutomationProperties.Name") && (a.Value.StartsWith("{Binding") || a.Value.StartsWith("{local:Str "))) return true;
        }
        // Text content as a child element, e.g. <Button>Save</Button> or <Button.Content>.
        if (e.Nodes().OfType<XText>().Any(t => t.Value.Trim().Length > 0)) return true;
        if (e.Elements().Any(c => c.Name.LocalName.EndsWith(".Content") || c.Name.LocalName.EndsWith("AutomationProperties.Name"))) return true;
        return false;
    }

    [Fact]
    public void Every_button_and_toggle_in_App_and_Editor_xaml_has_text_or_an_automation_name()
    {
        var root = Root();
        Assert.NotNull(root);
        var missing = new List<string>();
        foreach (var project in new[] { "AutomationPortal.App", "AutomationPortal.Editor" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "src", project), "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
                foreach (var e in doc.Descendants())
                {
                    if (!Named.Contains(e.Name.LocalName)) continue;
                    // Elements inside a style or control template are skipped: they are parts, not controls.
                    if (e.Ancestors().Any(a => a.Name.LocalName is "ControlTemplate" or "Style")) continue;
                    if (!HasName(e))
                        missing.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)e).LineNumber} {e.Name.LocalName}");
                }
            }
        }
        Assert.True(missing.Count == 0, "No text or AutomationProperties.Name: " + string.Join("; ", missing));
    }
}
