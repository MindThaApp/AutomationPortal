using System.Text.Json;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class CallerIdTests
{
    [Theory]
    [InlineData("filesportal")]
    [InlineData("desk-pal")]
    [InlineData("a")]
    [InlineData("app2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]   // 32
    public void Good_caller_ids(string id) => Assert.True(Callers.IsValidId(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FilesPortal")]
    [InlineData("files portal")]
    [InlineData("files_portal")]
    [InlineData("filesportal.exe")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]  // 33
    [InlineData("ünï")]
    public void Bad_caller_ids(string? id) => Assert.False(Callers.IsValidId(id));

    [Fact]
    public void Known_apps_get_their_names_and_others_keep_the_id()
    {
        Assert.Equal("Files Portal", Callers.FriendlyName("filesportal"));
        Assert.Equal("Desk Pal", Callers.FriendlyName("deskpal"));
        Assert.Equal("my-tool", Callers.FriendlyName("my-tool"));
    }

    [Fact]
    public void Headline_names_the_caller_shortcut_and_files()
    {
        Assert.Equal("Files Portal wants to run \"Tidy\" with 3 files", new CallerPrompt("filesportal", "Tidy", new[] { "a", "b", "c" }, false).Headline);
        Assert.Equal("my-tool wants to run \"Tidy\" with 1 file", new CallerPrompt("my-tool", "Tidy", new[] { "a" }, false).Headline);
        Assert.Equal("Desk Pal wants to run \"Tidy\" with some text", new CallerPrompt("deskpal", "Tidy", Array.Empty<string>(), true).Headline);
        Assert.Equal("Desk Pal wants to run \"Tidy\"", new CallerPrompt("deskpal", "Tidy", Array.Empty<string>(), false).Headline);
    }
}

public class CallerConsentTests
{
    private static CallerPrompt Prompt(string caller = "filesportal") => new(caller, "Tidy", new[] { @"C:\a.txt" }, false);

    private static CallerConsent Make(InMemorySettingsStore settings, Func<CallerPrompt, CancellationToken, Task<CallerDecision>> ask, TimeSpan? timeout = null) =>
        new(settings, ask, timeout);

    [Fact]
    public async Task Allow_once_runs_and_asks_again_next_time()
    {
        var settings = new InMemorySettingsStore();
        var asked = 0;
        var consent = Make(settings, (_, _) => { asked++; return Task.FromResult(CallerDecision.AllowOnce); });
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt(), default));
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt(), default));
        Assert.Equal(2, asked);
        Assert.Empty(settings.Current.AllowedCallers);
    }

    [Fact]
    public async Task Always_allow_is_remembered_per_caller()
    {
        var settings = new InMemorySettingsStore();
        var asked = new List<string>();
        var consent = Make(settings, (p, _) => { asked.Add(p.CallerId); return Task.FromResult(CallerDecision.AlwaysAllow); });
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt("filesportal"), default));
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt("filesportal"), default));
        Assert.Equal(new[] { "filesportal" }, settings.Current.AllowedCallers);
        Assert.Equal(new[] { "filesportal" }, asked);   // the second run wasn't asked

        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt("deskpal"), default));
        Assert.Equal(new[] { "filesportal", "deskpal" }, asked);   // another caller is still asked
        Assert.Equal(new[] { "filesportal", "deskpal" }, settings.Current.AllowedCallers);
    }

    [Fact]
    public async Task Removing_a_caller_makes_it_ask_again()
    {
        var settings = new InMemorySettingsStore();
        settings.Update(s => s.AllowedCallers.Add("filesportal"));
        var asked = 0;
        var consent = Make(settings, (_, _) => { asked++; return Task.FromResult(CallerDecision.DontRun); });
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt(), default));
        Assert.Equal(0, asked);

        CallerConsent.Forget(settings, "filesportal");
        Assert.Empty(settings.Current.AllowedCallers);
        Assert.Equal(CallerVerdict.Declined, await consent.RequestAsync(Prompt(), default));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Dont_run_declines()
    {
        var consent = Make(new InMemorySettingsStore(), (_, _) => Task.FromResult(CallerDecision.DontRun));
        var verdict = await consent.RequestAsync(Prompt(), default);
        Assert.Equal(CallerVerdict.Declined, verdict);
        var reply = consent.NotRun(verdict);
        Assert.Equal(CliExit.Declined, reply.ExitCode);
        Assert.Equal("Not run: you chose Don't run", reply.Message);
        Assert.Null(reply.Output);
    }

    [Fact]
    public async Task No_answer_in_time_times_out_and_closes_the_prompt()
    {
        var promptClosed = new TaskCompletionSource();
        var consent = Make(new InMemorySettingsStore(), async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { promptClosed.TrySetResult(); throw; }
            return CallerDecision.AllowOnce;
        }, TimeSpan.FromMilliseconds(150));
        var verdict = await consent.RequestAsync(Prompt(), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CallerVerdict.TimedOut, verdict);
        await promptClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));   // the prompt was told to close
        Assert.Equal("Not run: nobody answered in 60 seconds", new CallerConsent(new InMemorySettingsStore(), (_, _) => Task.FromResult(CallerDecision.AllowOnce)).NotRun(verdict).Message);
    }

    [Fact]
    public async Task A_prompt_that_ignores_the_deadline_still_times_out()
    {
        var never = new TaskCompletionSource<CallerDecision>();
        var consent = Make(new InMemorySettingsStore(), (_, _) => never.Task, TimeSpan.FromMilliseconds(100));
        Assert.Equal(CallerVerdict.TimedOut, await consent.RequestAsync(Prompt(), default).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task One_prompt_at_a_time_and_an_always_allow_answers_the_queued_request_too()
    {
        var settings = new InMemorySettingsStore();
        var first = new TaskCompletionSource<CallerDecision>();
        var shown = 0;
        var consent = Make(settings, (_, _) => { Interlocked.Increment(ref shown); return first.Task; });

        var a = consent.RequestAsync(Prompt(), default);
        var b = consent.RequestAsync(Prompt(), default);
        await Task.Delay(100);
        Assert.Equal(1, shown);   // the second waits for the first prompt
        Assert.False(b.IsCompleted);

        first.SetResult(CallerDecision.AlwaysAllow);
        Assert.Equal(CallerVerdict.Allowed, await a.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(CallerVerdict.Allowed, await b.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task A_queued_request_counts_its_wait_against_its_own_deadline()
    {
        var first = new TaskCompletionSource<CallerDecision>();
        var consent = Make(new InMemorySettingsStore(), (_, _) => first.Task, TimeSpan.FromMilliseconds(300));
        var a = consent.RequestAsync(Prompt(), default);
        await Task.Delay(50);
        var b = consent.RequestAsync(Prompt("deskpal"), default);
        Assert.Equal(CallerVerdict.TimedOut, await a.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(CallerVerdict.TimedOut, await b.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task When_the_caller_leaves_the_request_is_cancelled_not_declined()
    {
        using var cts = new CancellationTokenSource();
        var consent = Make(new InMemorySettingsStore(), async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return CallerDecision.AllowOnce; });
        var t = consent.RequestAsync(Prompt(), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Allowed_callers_survive_a_settings_round_trip()
    {
        var s = new AppSettings();
        s.AllowedCallers.Add("filesportal");
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(new[] { "filesportal" }, back.AllowedCallers);
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("{}")!.AllowedCallers);
    }
}

public class AskingRunTests
{
    private sealed class Recorder
    {
        public readonly List<string> Runs = new();
        public ShortcutCommands Make(InMemoryShortcutLibrary lib, CallerConsent? consent) =>
            new(lib, (s, input, source, ct) =>
            {
                lock (Runs) Runs.Add(s.Name);
                return Task.FromResult<RunResult?>(new RunResult { ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Succeeded, Output = ContentValue.Text("done") });
            }) { Consent = consent };
    }

    private static InMemoryShortcutLibrary Library()
    {
        var lib = new InMemoryShortcutLibrary();
        lib.Save(new ShortcutBuilder("Tidy").Build());
        return lib;
    }

    [Theory]
    [InlineData(CallerDecision.DontRun)]
    [InlineData(null)]   // timed out
    public async Task A_declined_or_unanswered_run_never_runs_and_says_why(CallerDecision? decision)
    {
        var rec = new Recorder();
        var consent = new CallerConsent(new InMemorySettingsStore(), async (_, ct) =>
        {
            if (decision is { } d) return d;
            await Task.Delay(Timeout.Infinite, ct);
            return CallerDecision.AllowOnce;
        }, TimeSpan.FromMilliseconds(100));
        foreach (var json in new[] { false, true })
        {
            var r = await rec.Make(Library(), consent).ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Source = "filesportal", Ask = true, Wait = false, Json = json, Files = new[] { @"C:\a.txt" } }, default);
            Assert.Equal(CliExit.Declined, r.ExitCode);
            Assert.Null(r.Output);
            Assert.Equal(decision is null ? "Not run: nobody answered in 0 seconds" : "Not run: you chose Don't run", r.Message);
        }
        Assert.Empty(rec.Runs);
    }

    [Fact]
    public async Task An_allowed_run_runs_with_the_prompt_showing_caller_shortcut_and_files()
    {
        var rec = new Recorder();
        CallerPrompt? seen = null;
        var consent = new CallerConsent(new InMemorySettingsStore(), (p, _) => { seen = p; return Task.FromResult(CallerDecision.AllowOnce); });
        var r = await rec.Make(Library(), consent).ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Source = "filesportal", Ask = true, Wait = true, Files = new[] { @"C:\a.txt", @"C:\b.txt" } }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Equal(new[] { "Tidy" }, rec.Runs);
        Assert.Equal("Files Portal wants to run \"Tidy\" with 2 files", seen!.Headline);
    }

    [Fact]
    public async Task Runs_without_ask_never_prompt_whatever_the_source()
    {
        var rec = new Recorder();
        var consent = new CallerConsent(new InMemorySettingsStore(), (_, _) => throw new InvalidOperationException("must not ask"));
        var cmds = rec.Make(Library(), consent);
        foreach (var source in new[] { "deskpal", "cli", null, "filesportal" })
            Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Source = source, Wait = true }, default)).ExitCode);
        Assert.Equal(4, rec.Runs.Count);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(new PipeRequest { Kind = "list", Ask = true, Source = "x" }, default)).ExitCode);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(new PipeRequest { Kind = "ping", Ask = true, Source = "x" }, default)).ExitCode);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(new PipeRequest { Kind = "capabilities", Ask = true, Source = "x" }, default)).ExitCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Files Portal")]
    [InlineData("")]
    public async Task Asking_needs_a_valid_caller_id(string? source)
    {
        var rec = new Recorder();
        var consent = new CallerConsent(new InMemorySettingsStore(), (_, _) => Task.FromResult(CallerDecision.AllowOnce));
        var r = await rec.Make(Library(), consent).ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Source = source, Ask = true, Wait = true }, default);
        Assert.Equal(CliExit.Usage, r.ExitCode);
        Assert.Empty(rec.Runs);
    }

    [Fact]
    public async Task Without_anyone_to_ask_a_run_that_asks_is_refused()
    {
        var rec = new Recorder();
        var r = await rec.Make(Library(), null).ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Source = "filesportal", Ask = true, Wait = true }, default);
        Assert.Equal(CliExit.Problem, r.ExitCode);
        Assert.Empty(rec.Runs);
    }

    [Fact]
    public async Task A_missing_shortcut_is_reported_before_anyone_is_asked()
    {
        var consent = new CallerConsent(new InMemorySettingsStore(), (_, _) => throw new InvalidOperationException("must not ask"));
        var r = await new Recorder().Make(Library(), consent).ExecuteAsync(new PipeRequest { Kind = "run", Name = "Nope", Source = "filesportal", Ask = true }, default);
        Assert.Equal(CliExit.NotFound, r.ExitCode);
    }

    [Fact]
    public void The_ask_field_is_optional_on_the_wire()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.False(JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"run\",\"name\":\"x\",\"source\":\"deskpal\"}", o)!.Ask);
        Assert.True(JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"run\",\"name\":\"x\",\"source\":\"filesportal\",\"ask\":true}", o)!.Ask);
    }

    [Fact]
    public async Task Capabilities_advertise_the_caller_prompt()
    {
        var r = await new Recorder().Make(Library(), null).ExecuteAsync(new PipeRequest { Kind = "capabilities" }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Contains("caller-prompt", doc.RootElement.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
    }
}
