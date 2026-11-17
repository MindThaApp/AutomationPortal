using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Cli.Tests;

public class ImportGateTests
{
    private static (List<string> Added, Func<string, Task<int>> One) Importer()
    {
        var added = new List<string>();
        return (added, text =>
        {
            if (text == "bad") return Task.FromResult(5);
            added.Add(text);
            return Task.FromResult(0);
        });
    }

    private static string[] Texts(int n) => Enumerable.Range(1, n).Select(i => "s" + i).ToArray();

    [Fact]
    public async Task Cap_reached_imports_nothing_and_exits_non_zero()
    {
        var (added, one) = Importer();
        var output = new StringWriter();
        var exit = await ImportGate.ImportUpToAsync(Texts(2), Tier.Free, 5, one, output, CliExit.Problem);
        Assert.Empty(added);
        Assert.Equal(CliExit.Problem, exit);
        Assert.Contains("Imported 0 of 2. Open Automation Portal to add more.", output.ToString());
    }

    [Fact]
    public async Task Partial_import_stops_at_the_cap_and_exits_zero()
    {
        var (added, one) = Importer();
        var output = new StringWriter();
        var exit = await ImportGate.ImportUpToAsync(Texts(5), Tier.Free, 2, one, output, CliExit.Problem);
        Assert.Equal(new[] { "s1", "s2", "s3" }, added);
        Assert.Equal(0, exit);
        Assert.Equal("Imported 3 of 5. Open Automation Portal to add more.", output.ToString().Trim());
    }

    [Fact]
    public async Task Someone_already_over_the_cap_can_import_nothing_new()
    {
        var (added, one) = Importer();
        var exit = await ImportGate.ImportUpToAsync(Texts(1), Tier.Free, 9, one, new StringWriter(), CliExit.Problem);
        Assert.Empty(added);
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Unpackaged_build_has_no_limit()
    {
        var tier = ImportGate.TierFor(packaged: false, owned: null);
        Assert.Equal(Tier.Pro, tier);
        var (added, one) = Importer();
        var output = new StringWriter();
        var exit = await ImportGate.ImportUpToAsync(Texts(40), tier, 100, one, output, CliExit.Problem);
        Assert.Equal(40, added.Count);
        Assert.Equal(0, exit);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task A_file_that_fails_does_not_use_up_room()
    {
        var (added, one) = Importer();
        var exit = await ImportGate.ImportUpToAsync(new[] { "bad", "a", "b" }, Tier.Free, 3, one, new StringWriter(), CliExit.Problem);
        Assert.Equal(new[] { "a", "b" }, added);
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Only_a_failure_keeps_its_own_exit_code()
    {
        var (_, one) = Importer();
        var exit = await ImportGate.ImportUpToAsync(new[] { "bad" }, Tier.Free, 0, one, new StringWriter(), CliExit.Problem);
        Assert.Equal(5, exit);
    }

    [Theory]
    [InlineData(null, Tier.Free)]
    [InlineData("none", Tier.Free)]
    [InlineData(AddOnIds.Plus, Tier.Plus)]
    [InlineData(AddOnIds.Pro, Tier.Pro)]
    public void Packaged_tier_comes_from_the_cached_add_ons(string? id, Tier expected)
    {
        var owned = id is null || id == "none" ? (id is null ? null : new List<string>()) : new List<string> { id };
        Assert.Equal(expected, ImportGate.TierFor(packaged: true, owned));
    }

    [Fact]
    public void Cached_add_ons_are_read_from_app_ui_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AutomationPortalPaths.ForRoot(root);
            Directory.CreateDirectory(paths.RoamingRoot);
            Assert.Null(ImportGate.ReadOwned(paths));
            File.WriteAllText(Path.Combine(paths.RoamingRoot, "app-ui.json"), "{\"LastPage\":\"Library\",\"OwnedAddOns\":[\"" + AddOnIds.Plus + "\"]}");
            var owned = ImportGate.ReadOwned(paths);
            Assert.Equal(Tier.Plus, ImportGate.TierFor(true, owned));
            File.WriteAllText(Path.Combine(paths.RoamingRoot, "app-ui.json"), "not json");
            Assert.Null(ImportGate.ReadOwned(paths));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
