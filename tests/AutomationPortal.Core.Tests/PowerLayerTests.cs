using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class PowerLayerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-tests", Guid.NewGuid().ToString("N")[..8]);
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Shortcut Make(string name) { var b = new ShortcutBuilder(name); b.Add("flow.nothing"); return b.Build(); }

    // In this table "|" stands for a backslash, so the escaping is readable.
    [Theory]
    [InlineData("Good Morning", "ap run \"Good Morning\"")]
    [InlineData("Tidy", "ap run \"Tidy\"")]
    [InlineData("Say \"hi\"", "ap run \"Say |\"hi|\"\"")]
    [InlineData("Ends with slash|", "ap run \"Ends with slash||\"")]
    [InlineData("C:|temp|x", "ap run \"C:|temp|x\"")]
    [InlineData("quote after slash|\"", "ap run \"quote after slash|||\"\"")]
    public void Command_QuotesTheNameSoItStaysOneArgument(string name, string expected) =>
        Assert.Equal(expected.Replace('|', (char)92), RunFromElsewhere.Command(name.Replace('|', (char)92)));

    [Theory]
    [InlineData("Good Morning", "automationportal://run?name=Good%20Morning")]
    [InlineData("Tom & Jerry #1", "automationportal://run?name=Tom%20%26%20Jerry%20%231")]
    public void Link_EncodesTheName_AndTheAppReadsItBack(string name, string expected)
    {
        var link = RunFromElsewhere.Link(name);
        Assert.Equal(expected, link);
        Assert.Equal(name, LaunchArguments.Parse(new[] { link }).RunTarget);
    }

    [Fact]
    public void NameClash_NumbersWithBracketsAndSkipsTakenNumbers()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Report", "Report (2)" };
        Assert.Equal("Report (3)", NameClash.Unique("Report", taken.Contains));
        Assert.Equal("Fresh", NameClash.Unique("  Fresh ", taken.Contains));
        Assert.Equal("Imported Shortcut", NameClash.Unique(" ", taken.Contains));
    }

    [Fact]
    public void Import_NeverReplaces_ClashingNameAndIdGetNewOnes()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var mine = Make("Tidy");
        lib.Save(mine);
        var before = File.ReadAllBytes(Paths.ShortcutFile(mine.Id));

        var theirs = Make("Tidy");
        theirs.Id = mine.Id;   // same id and same name as one already here
        var r1 = lib.Import(theirs);
        var r2 = lib.Import(theirs);

        Assert.NotEqual(mine.Id, r1.Shortcut.Id);
        Assert.Equal("Tidy (2)", r1.Shortcut.Name);
        Assert.Equal("Tidy", r1.RenamedFrom);
        Assert.Equal("Tidy (3)", r2.Shortcut.Name);
        Assert.Equal(3, lib.All.Count);
        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(mine.Id)));
        Assert.Equal("Tidy", lib.GetById(mine.Id)!.Name);
    }

    [Fact]
    public void ReadShortcuts_ListsTheBackup_AndRestoreAddsAlongsideWithoutChangingTheLibrary()
    {
        var a = Make("Resize images"); var b = Make("Clean desktop");
        string zip;
        using (var lib = new JsonShortcutLibrary(Paths, null, watch: false))
        {
            lib.Save(a); lib.Save(b);
            zip = LibraryBackup.Create(Paths, Path.Combine(_root, "backups"), new DateTime(2026, 11, 16, 8, 0, 0));
        }
        var zipBytes = File.ReadAllBytes(zip);

        var found = LibraryBackup.ReadShortcuts(zip);
        Assert.Equal(new[] { "Clean desktop", "Resize images" }, found.Select(s => s.Name).OrderBy(n => n));
        Assert.Equal(zipBytes, File.ReadAllBytes(zip));

        using var current = new JsonShortcutLibrary(Paths, null, watch: false);
        var onDisk = File.ReadAllBytes(Paths.ShortcutFile(a.Id));
        foreach (var s in found) current.Import(s);

        Assert.Equal(4, current.All.Count);
        Assert.Contains(current.All, s => s.Name == "Resize images (2)");
        Assert.Contains(current.All, s => s.Name == "Clean desktop (2)");
        Assert.Equal(onDisk, File.ReadAllBytes(Paths.ShortcutFile(a.Id)));
    }

    [Fact]
    public void ReadShortcuts_ARandomFileIsAFriendlyError()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "not-a-zip.zip");
        File.WriteAllText(path, "hello");
        Assert.Throws<LibraryException>(() => LibraryBackup.ReadShortcuts(path));
    }
}
