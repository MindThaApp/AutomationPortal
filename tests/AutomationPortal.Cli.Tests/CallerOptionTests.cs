using AutomationPortal.Cli;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Cli.Tests;

[Collection("console")]
public class CallerOptionTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "ap-caller-base");

    private static Task<(PipeRequest? Request, string? Error)> ParseRun(params string[] args) =>
        Program.ParseRunAsync(args.ToList(), new StringReader(""), Base);

    [Fact]
    public async Task From_sets_the_source_and_asks_first()
    {
        var (r, error) = await ParseRun("Tidy", "--file", "a.txt", "--from", "filesportal", "--no-wait", "--json");
        Assert.Null(error);
        Assert.Equal("run", r!.Kind);
        Assert.Equal("Tidy", r.Name);
        Assert.Equal("filesportal", r.Source);
        Assert.True(r.Ask);
        Assert.False(r.Wait);   // --no-wait only skips waiting for the run; the app still answers after the user decides
        Assert.True(r.Json);
        Assert.Equal(new[] { Path.Combine(Base, "a.txt") }, r.Files);
    }

    [Fact]
    public async Task Without_from_nothing_asks()
    {
        var (r, error) = await ParseRun("Tidy", "--input", "hello");
        Assert.Null(error);
        Assert.False(r!.Ask);
        Assert.Null(r.Source);   // filled in as "cli" when it is sent
        Assert.True(r.Wait);
        Assert.Equal("hello", r.Input);
    }

    [Theory]
    [InlineData("FilesPortal")]
    [InlineData("files portal")]
    [InlineData("files_portal")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    [InlineData("")]
    public async Task A_bad_caller_id_is_a_usage_error(string caller)
    {
        var (r, error) = await ParseRun("Tidy", "--from", caller);
        Assert.Null(r);
        Assert.Contains("lower-case", error);
    }

    [Fact]
    public async Task From_without_a_value_is_a_usage_error()
    {
        var (r, error) = await ParseRun("Tidy", "--from");
        Assert.Null(r);
        Assert.Contains("--from needs", error);
    }

    [Fact]
    public async Task Run_with_a_bad_caller_exits_with_usage_before_contacting_anything()
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try { Assert.Equal(CliExit.Usage, await Program.RunAsync(new List<string> { "Tidy", "--from", "Files Portal" }, null)); }
        finally { Console.SetError(original); }
        Assert.Contains("lower-case", captured.ToString());
    }

    [Fact]
    public async Task Files_from_and_from_combine()
    {
        var (r, error) = await Program.ParseRunAsync(new List<string> { "Tidy", "--files-from", "-", "--from", "deskpal" }, new StringReader("x.txt\ny.txt\n"), Base);
        Assert.Null(error);
        Assert.Equal(new[] { Path.Combine(Base, "x.txt"), Path.Combine(Base, "y.txt") }, r!.Files);
        Assert.Equal("deskpal", r.Source);
    }

    [Fact]
    public void Only_asking_runs_and_new_need_the_app()
    {
        Assert.True(Program.NeedsApp(new PipeRequest { Kind = "run", Ask = true, Source = "filesportal" }));
        Assert.True(Program.NeedsApp(new PipeRequest { Kind = "new" }));
        Assert.False(Program.NeedsApp(new PipeRequest { Kind = "run" }));
        Assert.False(Program.NeedsApp(new PipeRequest { Kind = "list", Ask = true }));
    }
}

[Collection("console")]
public class NewCommandTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "ap-new-base");

    [Fact]
    public async Task New_maps_files_and_caller_to_a_new_request()
    {
        var (r, error) = await Program.ParseNewAsync(new List<string> { "--file", "a.jpg", "--file", @"C:\b.jpg", "--files-from", "-", "--from", "filesportal", "--json" },
            new StringReader("c.jpg\n"), Base);
        Assert.Null(error);
        Assert.Equal("new", r!.Kind);
        Assert.Equal(new[] { Path.Combine(Base, "a.jpg"), @"C:\b.jpg", Path.Combine(Base, "c.jpg") }, r.Files);
        Assert.Equal("filesportal", r.Source);
        Assert.False(r.Ask);   // nothing runs, so nothing is asked
        Assert.True(r.Json);
        Assert.Null(r.Name);
    }

    [Fact]
    public async Task New_takes_many_files()
    {
        var list = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"f{i}.txt"));
        var (r, error) = await Program.ParseNewAsync(new List<string> { "--files-from", "-" }, new StringReader(list), Base);
        Assert.Null(error);
        Assert.Equal(500, r!.Files.Length);
        Assert.Null(r.Source);
    }

    [Theory]
    [InlineData(new string[0], "Say which files")]
    [InlineData(new[] { "Tidy", "--file", "a.txt" }, "doesn't take")]
    [InlineData(new[] { "--file", "a.txt", "--from", "Files Portal" }, "lower-case")]
    public async Task New_usage_errors(string[] args, string expected)
    {
        var (r, error) = await Program.ParseNewAsync(args.ToList(), new StringReader(""), Base);
        Assert.Null(r);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task Ap_new_with_no_files_exits_with_usage()
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try { Assert.Equal(CliExit.Usage, await Program.NewAsync(new List<string>(), null)); }
        finally { Console.SetError(original); }
        Assert.Contains("Say which files", captured.ToString());
    }
}
