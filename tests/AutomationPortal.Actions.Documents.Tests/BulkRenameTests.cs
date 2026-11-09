using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

public class BulkRenameTests
{
    private static string[] Names(IEnumerable<BulkRenameStep> plan) => plan.Select(s => Path.GetFileName(s.Target)).ToArray();

    [Fact]
    public void TokensAndPadding()
    {
        using var d = new TempDir();
        var a = d.File("Trip/a.txt", "1");
        var b = d.File("Trip/b.jpg", "2");
        File.SetLastWriteTime(a, new DateTime(2024, 3, 9));
        var plan = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "{parent}_{n}_{name}_{date}.{ext}", Start = 7, Padding = 3, DateFormat = "yyyyMMdd" });
        Assert.Equal("Trip_007_a_20240309.txt", Path.GetFileName(plan[0].Target));
        Assert.StartsWith("Trip_008_b_", Path.GetFileName(plan[1].Target));
        Assert.EndsWith(".jpg", plan[1].Target);
    }

    [Fact]
    public void FindReplacePlainAndRegexAndCase()
    {
        using var d = new TempDir();
        var a = d.File("IMG_123 copy.txt", "x");
        Assert.Equal(new[] { "IMG_123.txt" }, Names(BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { Find = " COPY" })));
        Assert.Equal(new[] { "IMG_123 copy.txt" }, Names(BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { Find = " COPY", MatchCase = true })));
        Assert.Equal(new[] { "photo-123 copy.txt" }, Names(BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { Find = @"^IMG_(\d+)", ReplaceWith = "photo-$1", UseRegex = true })));
        Assert.Equal(new[] { "img_123 copy.txt" }, Names(BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { CaseStyle = "lower" })));
        Assert.Equal(new[] { "Hello World.txt" }, Names(BulkRenamer.Plan(new[] { d.File("hELLO wORLD.txt", "x") }, new BulkRenameOptions { CaseStyle = "title" })));
        Assert.Throws<ActionException>(() => BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { Find = "(", UseRegex = true }));
    }

    [Fact]
    public void CollisionAddsNumberOrSkips()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        var b = d.File("b.txt", "b");
        d.File("c.txt", "existing");
        var number = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "c.txt" });
        Assert.Equal(new[] { "c (2).txt", "c (3).txt" }, Names(number));

        var skip = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "c.txt", AddNumberOnCollision = false });
        Assert.All(skip, s => Assert.True(s.Skipped));
        BulkRenamer.Execute(skip);
        Assert.True(File.Exists(a));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(d.Path, "c.txt")));
    }

    [Fact]
    public void TwoFilesWantingTheSameNameSkipTheSecond()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        var b = d.File("b.txt", "b");
        var plan = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "z.txt", AddNumberOnCollision = false });
        Assert.False(plan[0].Skipped);
        Assert.True(plan[1].Skipped);
    }

    [Fact]
    public void NameSwapCycleWorks()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "A");
        var b = d.File("b.txt", "B");
        var plan = new[] { new BulkRenameStep(a, b, false), new BulkRenameStep(b, a, false) };
        BulkRenamer.Execute(plan);
        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));
        Assert.Empty(Directory.GetFiles(d.Path, "~automationportal*"));
    }

    [Fact]
    public void PlannedSwapViaPatternIsAllowed()
    {
        using var d = new TempDir();
        var a = d.File("1.txt", "one");
        var b = d.File("2.txt", "two");
        var plan = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "{n}.txt", Start = 2 });
        Assert.Equal(new[] { "2.txt", "3.txt" }, Names(plan));
        BulkRenamer.Execute(plan);
        Assert.Equal("one", File.ReadAllText(Path.Combine(d.Path, "2.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(d.Path, "3.txt")));
    }

    [Theory]
    [InlineData("a:b.txt")]
    [InlineData("a*.txt")]
    [InlineData("CON.txt")]
    [InlineData("nul")]
    [InlineData("name.")]
    [InlineData("a|b")]
    public void InvalidNamesAreRejected(string pattern)
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        var ex = Assert.Throws<ActionException>(() => BulkRenamer.Plan(new[] { a }, new BulkRenameOptions { Pattern = pattern }));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task PreviewOnlyDoesNotRename()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        using var host = T.Host();
        var r = await T.Run(host, new BulkRenameAction(), ContentValue.Files(new[] { a }), ("pattern", "new-{n}.{ext}"), ("previewOnly", true));
        Assert.Equal("a.txt → new-1.txt", r.ToString());
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(Path.Combine(d.Path, "new-1.txt")));
    }

    [Fact]
    public async Task ActionRenamesAndOutputsFiles()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        using var host = T.Host();
        var r = await T.Run(host, new BulkRenameAction(), ContentValue.Files(new[] { a }), ("pattern", "new-{n}.{ext}"));
        Assert.Equal(new[] { Path.Combine(d.Path, "new-1.txt") }, T.Paths(r));
        Assert.True(File.Exists(Path.Combine(d.Path, "new-1.txt")));
    }

    [Fact]
    public void FailureRollsBackEarlierRenames()
    {
        using var d = new TempDir();
        var a = d.File("a.txt", "a");
        var b = d.File("b.txt", "b");
        var plan = BulkRenamer.Plan(new[] { a, b }, new BulkRenameOptions { Pattern = "x-{name}.{ext}" });
        using var hold = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => BulkRenamer.Execute(plan));
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(Path.Combine(d.Path, "x-a.txt")));
        Assert.True(File.Exists(b));
    }
}
