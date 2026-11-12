using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Windows.Tests;

[Collection("DeskPalStatics")]
public class DeskPalTests : IDisposable
{
    private sealed class FakeTransport : IDeskPalTransport
    {
        public Func<string, string?> Reply = _ => null;
        public Exception? Throw;
        public List<(string Line, TimeSpan Timeout)> Sent { get; } = new();

        public Task<string?> SendAsync(string line, TimeSpan readTimeout, CancellationToken ct)
        {
            Sent.Add((line, readTimeout));
            if (Throw is not null) throw Throw;
            return Task.FromResult(Reply(line));
        }
    }

    private const string StatusJson = "{\"contract\":1,\"app\":\"DeskPal\",\"version\":\"0.12.0\",\"edition\":\"portable\",\"allowExternalControl\":true," +
        "\"state\":\"focus\",\"holds\":[{\"source\":\"focus\",\"until\":\"2026-11-12T10:25:00+01:00\",\"seated\":false}]," +
        "\"focus\":{\"active\":true,\"endsAt\":\"2026-11-12T10:25:00+01:00\"},\"pausedUntil\":\"2026-11-12T11:30:00+01:00\"," +
        "\"nextNudge\":{\"kind\":\"stretch\",\"dueAt\":\"2026-11-12T10:30:00+01:00\"}," +
        "\"today\":{\"stretches\":3,\"eyeBreaks\":5,\"walks\":1},\"goals\":{\"stretches\":17,\"eyeBreaks\":25,\"walks\":5}}";

    private static string Ok(string result, string status = StatusJson) => $"{{\"ok\":true,\"result\":\"{result}\",\"status\":{status}}}";

    private static string Error(string code, string message = "") =>
        JsonSerializer.Serialize(new { ok = false, error = code, message });

    private readonly FakeTransport _pipe = new();

    public DeskPalTests() => DeskPalLink.Transport = _pipe;

    public void Dispose() => DeskPalLink.Transport = new NamedPipeDeskPalTransport();

    private static async Task<ContentValue> Run(ShortcutAction action, params (string, ParamValue)[] p)
    {
        using var host = new TestHost();
        return await ActionTestHarness.RunAsync(host, action, p);
    }

    private static async Task<ActionException> Fails(ShortcutAction action, params (string, ParamValue)[] p)
    {
        using var host = new TestHost();
        return await Assert.ThrowsAsync<ActionException>(() => ActionTestHarness.RunAsync(host, action, p));
    }

    private static JsonElement Sent(string line) => JsonDocument.Parse(line).RootElement.Clone();

    [Fact]
    public async Task Start_a_break_sends_the_kind_and_waits_long_enough_for_the_approval_card()
    {
        _pipe.Reply = _ => Ok("shown");
        await Run(new DeskPalStartBreakAction(), ("kind", ParamValue.Choice("walk")));
        var (line, timeout) = Assert.Single(_pipe.Sent);
        var req = Sent(line);
        Assert.Equal(1, req.GetProperty("v").GetInt32());
        Assert.Equal("break.start", req.GetProperty("cmd").GetString());
        Assert.Equal("walk", req.GetProperty("args").GetProperty("kind").GetString());
        Assert.True(timeout > TimeSpan.FromSeconds(30));
        Assert.DoesNotContain('\n', line);
    }

    [Theory]
    [InlineData(25, 25)]
    [InlineData(2, 5)]
    [InlineData(500, 120)]
    [InlineData(44.6, 45)]
    public async Task Focus_minutes_are_rounded_and_kept_in_range(double given, int sent)
    {
        _pipe.Reply = _ => Ok("started");
        await Run(new DeskPalFocusOnAction(), ("minutes", ParamValue.Number(given)));
        var req = Sent(_pipe.Sent.Single().Line);
        Assert.Equal("focus.on", req.GetProperty("cmd").GetString());
        Assert.Equal(sent, req.GetProperty("args").GetProperty("minutes").GetInt32());
    }

    [Fact]
    public async Task Pause_uses_its_own_range_and_default()
    {
        _pipe.Reply = _ => Ok("held");
        await Run(new DeskPalPauseAction());
        await Run(new DeskPalPauseAction(), ("minutes", ParamValue.Number(1000)));
        Assert.Equal(30, Sent(_pipe.Sent[0].Line).GetProperty("args").GetProperty("minutes").GetInt32());
        Assert.Equal(480, Sent(_pipe.Sent[1].Line).GetProperty("args").GetProperty("minutes").GetInt32());
    }

    [Fact]
    public async Task Focus_off_and_resume_send_no_args()
    {
        _pipe.Reply = _ => Ok("stopped");
        await Run(new DeskPalFocusOffAction());
        await Run(new DeskPalResumeAction());
        Assert.Equal("{\"v\":1,\"cmd\":\"focus.off\"}", _pipe.Sent[0].Line);
        Assert.Equal("{\"v\":1,\"cmd\":\"resume\"}", _pipe.Sent[1].Line);
    }

    [Fact]
    public async Task Status_gives_its_fields_as_variables_with_a_short_wait()
    {
        _pipe.Reply = _ => Ok("status");
        var r = await Run(new GetDeskPalStatusAction());
        var (line, timeout) = Assert.Single(_pipe.Sent);
        Assert.Equal("{\"v\":1,\"cmd\":\"status\"}", line);
        Assert.True(timeout <= TimeSpan.FromSeconds(5));
        var d = Assert.IsType<DictionaryItem>(r[0]);
        ContentValue F(string k) => d.Entries.Single(e => e.Key == k).Value;
        Assert.Equal("focus", F("State").AsText());
        Assert.True(((BooleanItem)F("Focus On")[0]).Value);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 11, 30, 0, TimeSpan.FromHours(1)), ((DateItem)F("Paused Until")[0]).Value);
        Assert.Equal("stretch", F("Next Break").AsText());
        Assert.Equal(5d, ((NumberItem)F("Eye Breaks Today")[0]).Value);
        Assert.Equal(17d, ((NumberItem)F("Stretch Goal")[0]).Value);
        Assert.True(((BooleanItem)F("Control Allowed")[0]).Value);
    }

    [Fact]
    public async Task Missing_status_fields_stay_empty()
    {
        _pipe.Reply = _ => Ok("status", "{\"app\":\"DeskPal\",\"state\":\"active\",\"allowExternalControl\":false}");
        var d = Assert.IsType<DictionaryItem>((await Run(new GetDeskPalStatusAction()))[0]);
        Assert.True(d.Entries.Single(e => e.Key == "Paused Until").Value.IsEmpty);
        Assert.True(d.Entries.Single(e => e.Key == "Walks Today").Value.IsEmpty);
    }

    [Fact]
    public async Task Not_running_is_a_plain_message()
    {
        _pipe.Reply = _ => null;
        Assert.Equal(DeskPalLink.NotRunning, (await Fails(new DeskPalResumeAction())).Message);
    }

    [Fact]
    public async Task No_answer_in_time_is_a_plain_message()
    {
        _pipe.Throw = new TimeoutException();
        Assert.Contains("didn't answer in time", (await Fails(new DeskPalPauseAction())).Message);
    }

    [Fact]
    public async Task Needs_approval_tells_the_user_what_to_do()
    {
        _pipe.Reply = _ => Error("needs-approval", "The approval card got no answer.");
        Assert.Equal("Approve Automation Portal in Desk Pal, then run again.", (await Fails(new DeskPalFocusOffAction())).Message);
    }

    [Fact]
    public async Task Desk_Pal_s_own_message_is_shown_when_control_is_off()
    {
        const string message = "Turn on \"Allow Automation Portal and other apps to control Desk Pal\" in Desk Pal Settings.";
        _pipe.Reply = _ => Error("not-allowed", message);
        Assert.Equal(message, (await Fails(new DeskPalFocusOnAction())).Message);
        _pipe.Reply = _ => Error("not-allowed");
        Assert.Equal(DeskPalLink.ControlOff, (await Fails(new DeskPalFocusOnAction())).Message);
    }

    [Theory]
    [InlineData("denied", "choose Allow")]
    [InlineData("busy", "busy")]
    [InlineData("rate-limited", "too many")]
    [InlineData("unknown-command", "Update Desk Pal")]
    [InlineData("unsupported-version", "Update Desk Pal")]
    [InlineData("something-new", "couldn't do that")]
    public void Error_codes_become_plain_messages(string code, string expected) =>
        Assert.Contains(expected, DeskPalLink.ErrorMessage(code, null));

    [Fact]
    public void Messages_are_kept_to_one_short_line()
    {
        var m = DeskPalLink.ErrorMessage("busy", "line one\nline two" + new string('x', 400));
        Assert.DoesNotContain('\n', m);
        Assert.True(m.Length <= 301);
    }

    [Fact]
    public async Task An_unreadable_reply_is_a_plain_message()
    {
        _pipe.Reply = _ => "not json";
        Assert.Contains("couldn't read", (await Fails(new DeskPalResumeAction())).Message);
    }

    [Theory]
    [InlineData(true, DeskPalLinkState.Running)]
    [InlineData(false, DeskPalLinkState.ControlOff)]
    public async Task Probe_reads_whether_control_is_allowed(bool allowed, DeskPalLinkState expected)
    {
        _pipe.Reply = _ => Ok("status", $"{{\"app\":\"DeskPal\",\"allowExternalControl\":{(allowed ? "true" : "false")}}}");
        Assert.Equal(expected, await SiblingApps.ProbeDeskPalAsync(default));
    }

    [Fact]
    public async Task Probe_treats_no_answer_or_another_app_as_not_running()
    {
        _pipe.Reply = _ => null;
        Assert.Equal(DeskPalLinkState.NotRunning, await SiblingApps.ProbeDeskPalAsync(default));
        _pipe.Reply = _ => Ok("status", "{\"app\":\"Other\",\"allowExternalControl\":true}");
        Assert.Equal(DeskPalLinkState.NotRunning, await SiblingApps.ProbeDeskPalAsync(default));
        _pipe.Throw = new TimeoutException();
        Assert.Equal(DeskPalLinkState.NotRunning, await SiblingApps.ProbeDeskPalAsync(default));
    }

    [Fact]
    public async Task The_real_transport_talks_one_line_each_way_with_a_fake_server()
    {
        var name = "DeskPal-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            var request = await reader.ReadLineAsync();
            var bytes = Encoding.UTF8.GetBytes(Ok("held") + "\n");
            await server.WriteAsync(bytes);
            await server.FlushAsync();
            return request;
        });

        var reply = await new NamedPipeDeskPalTransport(name).SendAsync("{\"v\":1,\"cmd\":\"pause\",\"args\":{\"minutes\":60}}", TimeSpan.FromSeconds(5), default);
        Assert.Equal("{\"v\":1,\"cmd\":\"pause\",\"args\":{\"minutes\":60}}", await serve.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("held", DeskPalLink.ParseReply(reply!).Result);
    }

    [Fact]
    public async Task The_real_transport_says_not_running_when_nobody_listens()
    {
        var reply = await new NamedPipeDeskPalTransport("DeskPal-test-" + Guid.NewGuid().ToString("N")).SendAsync("{}", TimeSpan.FromSeconds(1), default);
        Assert.Null(reply);
    }

    [Fact]
    public async Task The_real_transport_times_out_when_the_server_never_answers()
    {
        var name = "DeskPal-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var accept = server.WaitForConnectionAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => new NamedPipeDeskPalTransport(name).SendAsync("{}", TimeSpan.FromMilliseconds(300), default));
        await accept;
    }
}
