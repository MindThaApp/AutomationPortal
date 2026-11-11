using AutomationPortal.Cli;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Cli.Tests;

[Collection("console")]
public class FilesFromTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "ap-files-from-base");

    [Fact]
    public void Parse_skips_blank_lines_trims_and_resolves_relative_paths()
    {
        var paths = FileList.Parse("﻿ a.txt \r\n\r\n   \r\nsub\\b.txt\r\n" + Path.Combine(Base, "abs.txt") + "\n", Base);
        Assert.Equal(new[] { Path.Combine(Base, "a.txt"), Path.Combine(Base, "sub", "b.txt"), Path.Combine(Base, "abs.txt") }, paths);
    }

    [Fact]
    public async Task ReadAsync_reads_stdin_for_dash()
    {
        var (paths, error) = await FileList.ReadAsync("-", new StringReader("x.txt\n\ny.txt"), Base);
        Assert.Null(error);
        Assert.Equal(new[] { Path.Combine(Base, "x.txt"), Path.Combine(Base, "y.txt") }, paths);
    }

    [Fact]
    public async Task ReadAsync_reads_a_utf8_file_with_bom_and_leaves_it_alone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ap-files-from-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var list = Path.Combine(folder, "list.txt");
            var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("one.txt\r\n\r\ntwo.txt\r\n")).ToArray();
            File.WriteAllBytes(list, original);
            var (paths, error) = await FileList.ReadAsync("list.txt", TextReader.Null, folder);
            Assert.Null(error);
            Assert.Equal(new[] { Path.Combine(folder, "one.txt"), Path.Combine(folder, "two.txt") }, paths);
            Assert.Equal(original, File.ReadAllBytes(list));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task ReadAsync_reports_a_missing_file()
    {
        var (paths, error) = await FileList.ReadAsync("no-such-list.txt", TextReader.Null, Base);
        Assert.Null(paths);
        Assert.Contains("no-such-list.txt", error);
    }

    [Fact]
    public async Task Run_with_a_missing_list_file_is_a_usage_error()
    {
        var (code, err) = await RunCapturingErrorAsync("run", "Anything", "--files-from", Path.Combine(Base, "missing-" + Guid.NewGuid().ToString("N") + ".txt"));
        Assert.Equal(CliExit.Usage, code);
        Assert.Contains("doesn't exist", err);
    }

    [Fact]
    public async Task Run_with_input_dash_and_files_from_dash_is_a_usage_error()
    {
        var (code, err) = await RunCapturingErrorAsync("run", "Anything", "--input", "-", "--files-from", "-");
        Assert.Equal(CliExit.Usage, code);
        Assert.Contains("only be used once", err);
    }

    [Fact]
    public void Summarize_lists_version_kinds_and_features()
    {
        var text = Program.Summarize("{\"app\":\"AutomationPortal\",\"version\":\"1.2.3\",\"contract\":1,\"edition\":\"unpackaged\",\"kinds\":[\"ping\",\"run\"],\"features\":[\"cli.files-from\"]}");
        Assert.Contains("1.2.3", text);
        Assert.Contains("ping, run", text);
        Assert.Contains("cli.files-from", text);
    }

    private static async Task<(int Code, string Error)> RunCapturingErrorAsync(params string[] args)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try { return (await Program.RunAsync(args.Skip(1).ToList(), null), captured.ToString()); }
        finally { Console.SetError(original); }
    }
}

[CollectionDefinition("console", DisableParallelization = true)]
public class ConsoleCollection { }
