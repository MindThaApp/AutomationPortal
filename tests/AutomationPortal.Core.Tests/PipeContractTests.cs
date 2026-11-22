using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class PipeLimitTests
{
    private static string PipeName() => "AutomationPortal-test-" + Guid.NewGuid().ToString("N");

    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000);
        return client;
    }

    private static async Task<PipeResponse?> ReadReplyAsync(Stream client)
    {
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return line is null ? null : JsonSerializer.Deserialize<PipeResponse>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task A_request_over_the_cap_gets_an_error_and_is_never_handled()
    {
        var name = PipeName();
        var handled = 0;
        using var server = new PipeServer(name, (req, ct) => { Interlocked.Increment(ref handled); return Task.FromResult(PipeResponse.Done()); }) { MaxRequestBytes = 1024 };
        server.Start();

        await using (var client = await ConnectAsync(name))
        {
            await client.WriteAsync(Encoding.UTF8.GetBytes(new string('a', 4096) + "\n"));
            var reply = await ReadReplyAsync(client);
            Assert.Equal(CliExit.Problem, reply!.ExitCode);
            Assert.Contains("too large", reply.Message);
            Assert.Null(await ReadReplyAsync(client));   // closed after the reply
        }
        Assert.Equal(0, handled);

        var ok = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "x", Input = new string('b', 500) }, TimeSpan.FromSeconds(2));
        Assert.Equal(CliExit.Ok, ok!.ExitCode);
        Assert.Equal(1, handled);
    }

    [Fact]
    public async Task A_client_that_sends_nothing_is_answered_and_dropped_after_the_timeout()
    {
        var name = PipeName();
        var handled = 0;
        using var server = new PipeServer(name, (req, ct) => { Interlocked.Increment(ref handled); return Task.FromResult(PipeResponse.Done()); }) { ReadTimeout = TimeSpan.FromMilliseconds(200) };
        server.Start();

        await using var client = await ConnectAsync(name);
        var reply = await ReadReplyAsync(client);
        Assert.Equal(CliExit.Usage, reply!.ExitCode);
        Assert.Contains("in time", reply.Message);
        Assert.Null(await ReadReplyAsync(client));
        Assert.Equal(0, handled);

        // A slow but complete request inside the time still works, and so do later clients.
        var later = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "x" }, TimeSpan.FromSeconds(2));
        Assert.Equal(CliExit.Ok, later!.ExitCode);
    }

    [Fact]
    public async Task The_client_refuses_to_send_more_than_the_app_accepts()
    {
        var name = PipeName();
        var handled = 0;
        using var server = new PipeServer(name, (req, ct) => { Interlocked.Increment(ref handled); return Task.FromResult(PipeResponse.Done()); });
        server.Start();
        var r = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "import", Input = new string('x', PipeServer.DefaultMaxRequestBytes) }, TimeSpan.FromSeconds(2));
        Assert.Equal(CliExit.Problem, r!.ExitCode);
        Assert.Equal(0, handled);
    }

    [Fact]
    public void Old_requests_without_source_still_parse()
    {
        var r = JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"run\",\"name\":\"x\",\"files\":[\"C:\\\\a.txt\"],\"wait\":true}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("run", r.Kind);
        Assert.True(r.Wait);
        Assert.Null(r.Source);
        Assert.False(JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"run\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Wait);
    }
}

public class PipeContractTests
{
    private static ShortcutCommands Make(InMemoryShortcutLibrary lib, Func<string, PipeRequest, Task<ImportResult?>>? review = null) =>
        new(lib, (s, input, source, ct) => Task.FromResult<RunResult?>(new RunResult { ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Succeeded }))
        { ReviewImport = review };

    [Theory]
    [InlineData("ping")]
    [InlineData("capabilities")]
    public async Task Ping_and_capabilities_say_hello(string kind)
    {
        var r = await Make(new InMemoryShortcutLibrary()).ExecuteAsync(new PipeRequest { Kind = kind }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Output!);
        var root = doc.RootElement;
        Assert.Equal("AutomationPortal", root.GetProperty("app").GetString());
        Assert.Equal(1, root.GetProperty("contract").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.DoesNotContain('+', root.GetProperty("version").GetString()!);
        Assert.Contains(root.GetProperty("edition").GetString(), new[] { "store", "unpackaged" });
        var kinds = root.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Equal(new[] { "ping", "capabilities", "list", "run", "import", "event", "new", "preview", "tools" }, kinds);
        Assert.Equal("1.1", root.GetProperty("contractVersion").GetString());
        var features = root.GetProperty("features").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Contains("run.wait", features);
        Assert.DoesNotContain("import.review", features);
    }

    [Fact]
    public async Task Hello_reports_import_review_when_the_app_reviews()
    {
        var r = await Make(new InMemoryShortcutLibrary(), (_, _) => Task.FromResult<ImportResult?>(null)).ExecuteAsync(new PipeRequest { Kind = "ping" }, default);
        Assert.Contains("import.review", r.Output);
    }

    private static string Package(string name)
    {
        var lib = new InMemoryShortcutLibrary();
        var s = new ShortcutBuilder(name).Build();
        lib.Save(s);
        return lib.Export(s.Id);
    }

    [Theory]
    [InlineData("cli")]
    [InlineData(null)]
    [InlineData("filesportal")]
    public async Task Pipe_imports_go_to_review_whatever_the_source(string? source)
    {
        var lib = new InMemoryShortcutLibrary();
        var reviewed = new List<string?>();
        var cmds = Make(lib, (text, req) => { reviewed.Add(req.Source); return Task.FromResult<ImportResult?>(null); });
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = Package("Tidy"), Source = source, Wait = true }, default);
        Assert.Equal(CliExit.Cancelled, r.ExitCode);
        Assert.Contains("wasn't added", r.Message);
        Assert.Equal(new[] { source }, reviewed);
        Assert.Empty(lib.All);
    }

    [Fact]
    public async Task An_accepted_review_reports_the_import_and_never_replaces_a_shortcut()
    {
        var lib = new InMemoryShortcutLibrary();
        var original = new ShortcutBuilder("Tidy").Build();
        lib.Save(original);
        var cmds = Make(lib, (text, _) => Task.FromResult<ImportResult?>(lib.ImportJson(text)));
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = Package("Tidy"), Wait = true, Json = true, Source = "cli" }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Contains("renamed", r.Message);
        Assert.Contains("\"status\": \"added\"", r.Output);
        Assert.Equal(2, lib.All.Count);
        Assert.Equal("Tidy", lib.GetById(original.Id)!.Name);
    }

    [Fact]
    public async Task Without_wait_the_reply_comes_while_the_review_is_open()
    {
        var lib = new InMemoryShortcutLibrary();
        var decision = new TaskCompletionSource<ImportResult?>();
        var cmds = Make(lib, (_, _) => decision.Task);
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = Package("Tidy"), Json = true }, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Contains("\"status\": \"review\"", r.Output);
        Assert.Empty(lib.All);
        decision.SetResult(null);
    }

    [Fact]
    public async Task A_bad_package_is_refused_before_any_review()
    {
        var called = false;
        var cmds = Make(new InMemoryShortcutLibrary(), (_, _) => { called = true; return Task.FromResult<ImportResult?>(null); });
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = "not json", Wait = true }, default);
        Assert.Equal(CliExit.Problem, r.ExitCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Without_a_window_imports_are_added_directly_and_renamed_on_a_clash()
    {
        var lib = new InMemoryShortcutLibrary();
        var original = new ShortcutBuilder("Tidy").Build();
        lib.Save(original);
        var r = await Make(lib).ExecuteAsync(new PipeRequest { Kind = "import", Input = Package("Tidy") }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Equal(2, lib.All.Count);
        Assert.Equal("Tidy", lib.GetById(original.Id)!.Name);
    }
}

public class OpenLinkTests
{
    [Theory]
    [InlineData("automationportal:")]
    [InlineData("automationportal://")]
    [InlineData("AutomationPortal:///")]
    [InlineData("automationportal://open")]
    [InlineData("automationportal://OPEN/")]
    [InlineData("automationportal://open?page=nowhere")]
    public void Bare_and_open_links_show_the_window(string link)
    {
        var c = LaunchArguments.Parse(new[] { link });
        Assert.True(c.Open);
        Assert.Null(c.OpenPage);
        Assert.Null(c.Error);
        Assert.False(c.FromLink);   // nothing runs, so nothing is asked
        Assert.False(c.IsEmpty);
    }

    [Fact]
    public void Open_can_name_a_page()
    {
        Assert.Equal("settings", LaunchArguments.Parse(new[] { "automationportal://open?page=Settings" }).OpenPage);
        Assert.Equal("gallery", LaunchArguments.Parse(new[] { "--link", "automationportal://open?page=gallery" }).OpenPage);
    }

    [Fact]
    public void Open_links_still_ignore_every_other_argument()
    {
        var c = LaunchArguments.Parse(new[] { "automationportal:", "--run", "Wipe", "--files", @"C:\Users", "--import", @"C:\x.autoportal" });
        Assert.True(c.Open);
        Assert.Null(c.RunTarget);
        Assert.Null(c.ImportFile);
        Assert.Empty(c.Files);
        var smuggled = LaunchArguments.Parse(new[] { "--run", "Wipe", "automationportal://open" });
        Assert.True(smuggled.Open);
        Assert.Null(smuggled.RunTarget);
    }

    [Fact]
    public void Other_links_are_unchanged()
    {
        Assert.NotNull(LaunchArguments.Parse(new[] { "automationportal://reboot" }).Error);
        Assert.False(LaunchArguments.Parse(new[] { "automationportal://open/extra" }).Open);
        Assert.Equal("Tidy", LaunchArguments.Parse(new[] { "automationportal://run?name=Tidy" }).RunTarget);
    }
}
