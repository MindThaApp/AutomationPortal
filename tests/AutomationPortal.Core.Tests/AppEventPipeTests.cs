using System.IO.Pipes;
using System.Text;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class AppEventPipeTests
{
    private static ShortcutCommands Make(List<AppEvent> published, Action<AppEvent>? publish = null) =>
        new(new InMemoryShortcutLibrary(), (s, input, source, ct) => throw new InvalidOperationException("Events must never run a shortcut."))
        { PublishEvent = publish ?? published.Add };

    private static PipeRequest Event(string? name, string? input = null, bool wait = false) =>
        new() { Kind = "event", Name = name, Input = input, Source = "deskpal", Wait = wait };

    [Fact]
    public async Task An_event_is_accepted_and_handed_on_with_its_details()
    {
        var published = new List<AppEvent>();
        const string payload = "{\"event\":\"deskpal.focus.on\",\"at\":\"2026-11-12T10:00:00+01:00\",\"cause\":\"user\",\"minutes\":25}";
        var r = await Make(published).ExecuteAsync(Event("deskpal.focus.on", payload), default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        var e = Assert.Single(published);
        Assert.Equal("deskpal.focus.on", e.Name);
        Assert.Equal("deskpal", e.App);
        Assert.Equal("focus.on", e.Event);
        Assert.Equal(payload, e.Payload);
        Assert.Equal("deskpal", e.Source);
    }

    [Fact]
    public async Task An_event_without_details_is_fine()
    {
        var published = new List<AppEvent>();
        var r = await Make(published).ExecuteAsync(Event("deskpal.pause.off"), default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Null(Assert.Single(published).Payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deskpal")]
    [InlineData("DeskPal.focus.on")]
    [InlineData("deskpal.focus on")]
    [InlineData(".focus")]
    [InlineData("deskpal.")]
    [InlineData("deskpal..on")]
    [InlineData("deskpal.focus/on")]
    [InlineData("deskpal.fócus")]
    public async Task Bad_names_are_refused_and_nothing_is_handed_on(string? name)
    {
        var published = new List<AppEvent>();
        var r = await Make(published).ExecuteAsync(Event(name), default);
        Assert.Equal(CliExit.Usage, r.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.Empty(published);
    }

    [Fact]
    public async Task A_name_over_the_length_limit_is_refused()
    {
        var published = new List<AppEvent>();
        var name = "deskpal." + new string('a', AppEvents.MaxNameLength);
        Assert.Equal(CliExit.Usage, (await Make(published).ExecuteAsync(Event(name), default)).ExitCode);
        Assert.True(AppEvents.IsValidName("deskpal." + new string('a', AppEvents.MaxNameLength - 8)));
        Assert.Empty(published);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"a\":")]
    public async Task Details_that_are_not_an_object_are_refused(string input)
    {
        var published = new List<AppEvent>();
        var r = await Make(published).ExecuteAsync(Event("deskpal.focus.on", input), default);
        Assert.Equal(CliExit.Usage, r.ExitCode);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Oversized_details_are_refused()
    {
        var published = new List<AppEvent>();
        var input = "{\"x\":\"" + new string('a', AppEvents.MaxPayloadChars) + "\"}";
        var r = await Make(published).ExecuteAsync(Event("deskpal.focus.on", input), default);
        Assert.Equal(CliExit.Problem, r.ExitCode);
        Assert.Empty(published);
    }

    [Fact]
    public async Task A_failing_listener_never_turns_into_an_error_for_the_sender()
    {
        var r = await Make(new List<AppEvent>(), _ => throw new InvalidOperationException("boom")).ExecuteAsync(Event("deskpal.focus.on", wait: true), default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
    }

    [Fact]
    public async Task Unknown_event_names_are_accepted()
    {
        var published = new List<AppEvent>();
        var r = await Make(published).ExecuteAsync(Event("deskpal.something.new", "{}"), default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Single(published);
    }

    [Fact]
    public async Task Without_a_listener_events_are_accepted_and_dropped()
    {
        var commands = new ShortcutCommands(new InMemoryShortcutLibrary(), (s, i, src, ct) => Task.FromResult<RunResult?>(null));
        Assert.Equal(CliExit.Ok, (await commands.ExecuteAsync(Event("deskpal.focus.on"), default)).ExitCode);
    }

    [Fact]
    public async Task The_line_Desk_Pal_sends_works_over_the_real_pipe()
    {
        var name = "AutomationPortal-test-" + Guid.NewGuid().ToString("N");
        var published = new List<AppEvent>();
        var commands = Make(published);
        using var server = new PipeServer(name, commands.ExecuteAsync);
        server.Start();

        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000);
        const string line = "{\"kind\":\"event\",\"args\":[],\"name\":\"deskpal.focus.on\",\"input\":\"{\\\"event\\\":\\\"deskpal.focus.on\\\",\\\"at\\\":\\\"2026-11-12T10:00:00+01:00\\\",\\\"cause\\\":\\\"user\\\",\\\"minutes\\\":25}\",\"files\":[],\"wait\":false,\"json\":false,\"source\":\"deskpal\"}\n";
        await client.WriteAsync(Encoding.UTF8.GetBytes(line));
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var reply = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"exitCode\":0", reply);
        var e = Assert.Single(published);
        Assert.Equal("deskpal.focus.on", e.Name);
        Assert.Contains("\"minutes\":25", e.Payload);
    }
}
