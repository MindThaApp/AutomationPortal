using System.IO;
using System.Xml.Linq;
using Xunit;

namespace AutomationPortal.App.Tests;

public class XamlDesignTokenTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Resources(string name) => Path.Combine(Root(), "src", "AutomationPortal.Editor", "Resources", name);

    private static IEnumerable<string> ViewFiles() =>
        new[] { "AutomationPortal.App", "AutomationPortal.Editor" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(Root(), "src", p), "*.xaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static Dictionary<string, double> FontSizeTokens() =>
        XDocument.Load(Resources("Tokens.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Double" && ((string?)e.Attribute(X + "Key"))?.StartsWith("DsFontSize") == true)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => double.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture));

    private static double? Resolve(string value, Dictionary<string, double> tokens)
    {
        value = value.Trim();
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        var m = System.Text.RegularExpressions.Regex.Match(value, @"^\{(?:Static|Dynamic)Resource (\w+)\}$");
        return m.Success && tokens.TryGetValue(m.Groups[1].Value, out var t) ? t : null;
    }

    [Fact]
    public void No_FontSize_in_App_or_Editor_xaml_is_below_twelve()
    {
        var tokens = FontSizeTokens();
        var bad = new List<string>();
        foreach (var file in ViewFiles())
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var e in doc.Descendants())
            {
                var line = ((System.Xml.IXmlLineInfo)e).LineNumber;
                foreach (var a in e.Attributes().Where(a => a.Name.LocalName == "FontSize"))
                    if (Resolve(a.Value, tokens) is not { } v || v < 12) bad.Add($"{Path.GetFileName(file)}:{line} FontSize={a.Value}");
                if (e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "FontSize" && e.Attribute("Value") is { } sv)
                    if (Resolve(sv.Value, tokens) is not { } v || v < 12) bad.Add($"{Path.GetFileName(file)}:{line} Setter FontSize={sv.Value}");
            }
        }
        Assert.True(bad.Count == 0, "FontSize under 12 or not a known size: " + string.Join("; ", bad));
    }

    [Fact]
    public void The_type_ramp_matches_the_design_guide()
    {
        var t = FontSizeTokens();
        Assert.Equal(12, t["DsFontSizeCaption"]);
        Assert.Equal(13, t["DsFontSizeReadable"]);
        Assert.Equal(14, t["DsFontSizeBody"]);
        Assert.Equal(16, t["DsFontSizePanelTitle"]);
        Assert.Equal(20, t["DsFontSizeSubtitle"]);
        Assert.Equal(28, t["DsFontSizeTitle"]);
    }

    [Fact]
    public void The_ten_category_colours_match_the_design_guide()
    {
        var expected = new[] { "#0F6CBD", "#4F52D9", "#7A3FD1", "#B82D74", "#C93434", "#C2570C", "#8F6200", "#1A7F37", "#0B7A75", "#52606D" };
        var actual = XDocument.Load(Resources("Tokens.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Color" && ((string?)e.Attribute(X + "Key"))?.StartsWith("DsCategory") == true)
            .Select(e => e.Value.Trim()).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Light_dark_and_high_contrast_define_the_same_tokens()
    {
        static HashSet<string> Keys(string path) =>
            XDocument.Load(path).Descendants().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null && k.StartsWith("Ds")).Select(k => k!).ToHashSet();
        var light = Keys(Resources("Tokens.Light.xaml"));
        Assert.NotEmpty(light);
        Assert.Equal(light.OrderBy(k => k), Keys(Resources("Tokens.Dark.xaml")).OrderBy(k => k));
        Assert.Equal(light.OrderBy(k => k), Keys(Resources("Tokens.HighContrast.xaml")).OrderBy(k => k));
    }

    [Fact]
    public void Motion_tokens_stay_within_three_hundred_and_thirty_three_milliseconds()
    {
        var ms = XDocument.Load(Resources("Tokens.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Duration" && ((string?)e.Attribute(X + "Key"))?.StartsWith("DsMotion") == true)
            .Select(e => TimeSpan.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture).TotalMilliseconds).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { 83.0, 167.0, 250.0, 333.0 }, ms);
    }
}
