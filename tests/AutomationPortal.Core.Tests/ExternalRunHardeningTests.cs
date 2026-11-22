using System.Text.Json;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;
using static AutomationPortal.Core.Tests.ExternalRunRequestTests;

namespace AutomationPortal.Core.Tests;

public class ExternalRunHardeningTests
{
    // ---- --run tokens ----------------------------------------------------------------------

    [Fact]
    public void A_launch_token_is_valid_only_for_its_shortcut_and_this_install()
    {
        var secrets = new InMemorySecretStore();
        var tokens = new LaunchTokens(secrets);
        var id = Guid.NewGuid();
        var token = tokens.For(id);

        Assert.Matches("^[0-9a-f]{32}$", token);
        Assert.True(tokens.IsValid(id, token));
        Assert.True(new LaunchTokens(secrets).IsValid(id, token));   // the key is kept in the secret store
        Assert.False(tokens.IsValid(Guid.NewGuid(), token));
        Assert.False(tokens.IsValid(id, null));
        Assert.False(tokens.IsValid(id, ""));
        Assert.False(tokens.IsValid(id, token[..31] + (token[31] == '0' ? '1' : '0')));
        Assert.False(new LaunchTokens(new InMemorySecretStore()).IsValid(id, token));   // another install
    }

    [Fact]
    public void Run_arguments_carry_the_token_but_a_link_never_does()
    {
        var launch = LaunchArguments.Parse(new[] { "--run", "abc", "--token", "0123", "--files", @"C:\a.txt" });
        Assert.Equal("0123", launch.Token);
        Assert.Equal(new[] { @"C:\a.txt" }, launch.Files);
        Assert.Null(LaunchArguments.Parse(new[] { "automationportal://run?name=x", "--token", "0123" }).Token);
    }

    [Fact]
    public async Task A_run_without_a_valid_token_goes_through_the_request_sheet()
    {
        var rig = new Rig();
        var tidy = rig.Add("Tidy", "test.change");
        var notes = rig.Add("Notes", "content.text");
        var cmds = rig.Commands();

        Assert.Null(await cmds.GateLaunchAsync(tidy, null, new[] { @"C:\a.txt" }, validToken: true, default));
        Assert.Empty(rig.Asked);

        Assert.Null(await cmds.GateLaunchAsync(tidy, null, new[] { @"C:\a.txt" }, validToken: false, default));
        var p = Assert.Single(rig.Asked);
        Assert.Equal("Another program", p.CallerName);
        Assert.False(p.CanRemember);
        Assert.Equal(new[] { @"C:\a.txt" }, p.Files);

        rig.Answer = (_, _) => Task.FromResult(CallerDecision.DontRun);
        Assert.Equal(CliExit.Declined, (await cmds.GateLaunchAsync(tidy, null, Array.Empty<string>(), validToken: false, default))!.ExitCode);

        // Old entries without a token still work for a shortcut that changes nothing.
        Assert.Null(await cmds.GateLaunchAsync(notes, null, Array.Empty<string>(), validToken: false, default));
        Assert.Equal(2, rig.Asked.Count);

        Assert.Equal(new[] { ("shell entry", "ran"), ("launch", "allowed"), ("launch", "declined"), ("launch", "ran") }, rig.Log.Select(l => (l.Via, l.Outcome)));
        Assert.Contains("(launch)", rig.Log[1].Describe());
    }

    [Fact]
    public void Only_automation_portal_and_ap_from_the_app_folder_may_send_a_launch()
    {
        var folder = @"C:\Program Files\AutomationPortal";
        Assert.True(PipeClients.IsOwnLauncher(@"C:\Program Files\AutomationPortal\AutomationPortal.exe", folder));
        Assert.True(PipeClients.IsOwnLauncher(@"c:\program files\automationportal\AP.EXE", folder + "\\"));
        Assert.False(PipeClients.IsOwnLauncher(@"C:\Users\me\Downloads\ap.exe", folder));
        Assert.False(PipeClients.IsOwnLauncher(@"C:\Program Files\AutomationPortal\other.exe", folder));
        Assert.False(PipeClients.IsOwnLauncher(null, folder));
        Assert.True(PipeClients.ImagePathOf(Environment.ProcessId) is { } self && File.Exists(self));
    }

    // ---- at most two waiting requests in all ------------------------------------------------

    [Fact]
    public async Task At_most_two_requests_wait_at_once_from_all_callers()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource<CallerDecision>();
        rig.Answer = (_, _) => gate.Task;
        rig.Add("Tidy", "test.change");
        var cmds = rig.Commands();

        var a = cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "One", Kind = "ai" }), default);
        var b = cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "Two", Kind = "ai" }), default);
        await Task.Delay(100);
        var c = await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "Three", Kind = "ai" }, json: true), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CliExit.Declined, c.ExitCode);
        Assert.Contains("2 requests are already waiting", c.Message);
        using (var doc = JsonDocument.Parse(c.Output!)) Assert.Equal("busy", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains(rig.Log, l => l.Outcome == "busy" && l.Caller == "Three");

        gate.SetResult(CallerDecision.AllowOnce);
        Assert.Equal(CliExit.Ok, (await a.WaitAsync(TimeSpan.FromSeconds(5))).ExitCode);
        Assert.Equal(CliExit.Ok, (await b.WaitAsync(TimeSpan.FromSeconds(5))).ExitCode);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "Three", Kind = "ai" }), default)).ExitCode);
    }

    // ---- a name that doesn't match the caller id -------------------------------------------

    [Fact]
    public async Task A_name_that_is_not_the_caller_ids_name_is_shown_as_unverified_and_never_remembered()
    {
        var rig = new Rig();
        rig.Add("Tidy", "test.change");
        var cmds = rig.Commands();

        await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "Files Portal" }, source: "x"), default);
        await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "files portal" }, source: "filesportal"), default);
        await cmds.ExecuteAsync(Run("Tidy", source: "filesportal"), default);

        Assert.Equal("Files Portal (unverified), id x", rig.Asked[0].CallerName);
        Assert.False(rig.Asked[0].CanRemember);
        Assert.Equal("files portal", rig.Asked[1].CallerName);
        Assert.True(rig.Asked[1].CanRemember);
        Assert.Equal("Files Portal", rig.Asked[2].CallerName);
        Assert.True(rig.Asked[2].CanRemember);
    }

    // ---- ap.exe and Always allow ----------------------------------------------------------

    [Fact]
    public async Task Requests_through_ap_ignore_always_allow_and_are_never_remembered()
    {
        var rig = new Rig();
        var tidy = rig.Add("Tidy", "test.change");
        rig.Settings.Current.AllowedCallerShortcuts = new() { CallerConsent.PairKey("filesportal", tidy.Id) };
        rig.Settings.Current.TrustedStepHashes = new() { [CallerConsent.PairKey("filesportal", tidy.Id)] = LinkPolicy.StepsHash(tidy) };
        var cmds = new ShortcutCommands(rig.Library, (s, _, _, _) => Task.FromResult<RunResult?>(new RunResult { Status = RunStatus.Succeeded }))
        {
            Actions = Registry, ClientImagePath = pid => pid == 42 ? @"C:\Program Files\AutomationPortal\ap.exe" : @"C:\Program Files\FilesPortal\FilesPortal.exe",
            Consent = new CallerConsent(rig.Settings, (p, _) => { rig.Asked.Add(p); return Task.FromResult(CallerDecision.AlwaysAllow); }),
        };

        // Straight over the pipe from the app itself: the earlier answer counts.
        await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Wait = true, Source = "filesportal", Ask = true, ClientProcessId = 7 }, default);
        Assert.Empty(rig.Asked);

        // Through ap.exe (ap run --from filesportal), or ap's own "cli": asked every time, and "Don't ask again" isn't offered.
        await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Wait = true, Source = "filesportal", Ask = true, ClientProcessId = 42 }, default);
        await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Wait = true, Source = "cli", ClientProcessId = 7 }, default);
        Assert.Equal(2, rig.Asked.Count);
        Assert.All(rig.Asked, p => { Assert.True(p.IgnoreAlwaysAllow); Assert.False(p.CanRemember); });
        Assert.Single(rig.Settings.Current.AllowedCallerShortcuts!);
    }

    [Fact]
    public void Callers_allowed_for_every_shortcut_become_one_entry_per_shortcut()
    {
        var settings = new InMemorySettingsStore();
        settings.Current.AllowedCallers = new() { "filesportal", "Bad Id" };
        var a = new ShortcutBuilder("A").Build();
        var b = new ShortcutBuilder("B").Build();

        Assert.Equal(1, CallerConsent.MigrateWholeCallers(settings, new[] { a, b }));
        Assert.Empty(settings.Current.AllowedCallers);
        Assert.Equal(new[] { CallerConsent.PairKey("filesportal", a.Id), CallerConsent.PairKey("filesportal", b.Id) }, settings.Current.AllowedCallerShortcuts);
        Assert.Equal(LinkPolicy.StepsHash(a), settings.Current.TrustedStepHashes![CallerConsent.PairKey("filesportal", a.Id)]);

        var consent = new CallerConsent(settings, (_, _) => Task.FromResult(CallerDecision.DontRun));
        Assert.True(consent.IsAlwaysAllowed(new CallerPrompt("filesportal", "A", Array.Empty<string>(), false) { ShortcutId = a.Id, StepsHash = LinkPolicy.StepsHash(a) }));
        Assert.False(consent.IsAlwaysAllowed(new CallerPrompt("filesportal", "New", Array.Empty<string>(), false) { ShortcutId = Guid.NewGuid() }));
        Assert.Equal(0, CallerConsent.MigrateWholeCallers(settings, new[] { a, b }));
    }

    // ---- hosted runs for an AI caller -----------------------------------------------------

    [Fact]
    public async Task A_hosted_ai_run_is_asked_with_its_real_input_also_in_preview()
    {
        using var host = new TestHost();
        host.Registry.Register(new CountingChangeAction());
        var b = new ShortcutBuilder("Tidy");
        b.Add("test.change");
        var s = b.Build();
        var input = ContentValue.Concat(ContentValue.Text("sort by date"), ContentValue.Files(new[] { @"C:\Users\me\a.txt", @"C:\Users\me\b.txt" }));

        host.UI.Enqueue(false);
        var r = await host.Runner.RunAsync(s, input, new RunOptions { Caller = Ai });
        Assert.Equal(RunStatus.Cancelled, r.Status);
        var shown = host.UI.Prompts.Last();
        Assert.Contains(@"C:\Users\me\a.txt", shown);
        Assert.Contains(@"C:\Users\me\b.txt", shown);
        Assert.Contains("sort by date", shown);
        Assert.Contains("on 2 items", shown);

        host.UI.Enqueue(false);
        var calls = host.UI.Calls.Count;
        await host.Runner.RunAsync(s, input, new RunOptions { Caller = Ai, Preview = new FileChangePlan() });
        Assert.Equal(calls + 1, host.UI.Calls.Count);   // a preview for an AI caller is asked about too
    }

    // ---- hidden characters ------------------------------------------------------------------

    [Theory]
    [InlineData("C:\\x\\photo\u202Etxt.exe")]
    [InlineData("C:\\x\\a.txt\nC:\\Windows\\b.txt")]
    [InlineData("C:\\x\\zero\u200Bwidth.txt")]
    public async Task Paths_with_control_or_format_characters_are_refused(string path)
    {
        var rig = new Rig();
        rig.Add("Tidy", "test.change");
        rig.Add("Notes", "content.text");
        var cmds = rig.Commands();
        foreach (var name in new[] { "Tidy", "Notes" })
        {
            var r = await cmds.ExecuteAsync(Run(name, Ai, files: new[] { @"C:\ok.txt", path }), default);
            Assert.Equal(CliExit.Usage, r.ExitCode);
            Assert.Contains("Item 2", r.Message);
        }
        Assert.Equal(CliExit.Usage, (await cmds.ExecuteAsync(new PipeRequest { Kind = "preview", Name = "Tidy", Files = new[] { path } }, default)).ExitCode);
        Assert.Equal(CliExit.Usage, (await cmds.GateLaunchAsync(rig.Library.FindByName("Tidy")!, null, new[] { path }, false, default))!.ExitCode);
        Assert.Empty(rig.Asked);
        Assert.Empty(rig.Runs);
        Assert.All(rig.Log, l => Assert.Equal("blocked", l.Outcome));
    }

    [Fact]
    public async Task Format_characters_are_taken_out_of_the_text_on_the_sheet()
    {
        var rig = new Rig();
        rig.Add("Tidy", "test.change");
        await rig.Commands().ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Wait = true, Input = "rename to \u202Eexe.txt\nnext line" }, default);
        Assert.Equal("rename to exe.txt\nnext line", Assert.Single(rig.Asked).InputText);
    }

    // ---- log lines ------------------------------------------------------------------------

    [Fact]
    public async Task Names_and_sources_are_cleaned_before_they_are_logged()
    {
        var rig = new Rig();
        var cmds = rig.Commands();
        await cmds.ExecuteAsync(new PipeRequest
        {
            Kind = "run", Name = "Nope\r\n2026-01-01 Run request (pipe) from Files Portal: \"Tidy\": allowed‮", Wait = true,
            Source = "x\nforged", Caller = new CallerInfo { App = "A\u0007pp", Kind = "a\ni" },
        }, default);
        var entry = Assert.Single(rig.Log);
        var line = entry.Describe();
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('‮', line);
        Assert.DoesNotContain('\u0007', line);
        Assert.Equal("x forged", entry.Source);
        Assert.Equal("App", entry.Caller);
        Assert.StartsWith("Nope 2026-01-01", entry.Shortcut);
        Assert.Equal("notFound", entry.Outcome);
    }

    // ---- events ---------------------------------------------------------------------------

    [Fact]
    public async Task Events_are_limited_to_one_a_second_per_source_and_logged()
    {
        var now = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
        var published = new List<string>();
        var log = new List<string>();
        var cmds = new ShortcutCommands(new InMemoryShortcutLibrary(), (_, _, _, _) => Task.FromResult<RunResult?>(null))
        {
            PublishEvent = e => published.Add(e.Name), LogEvent = log.Add, Clock = () => now,
        };
        PipeRequest Ev(string source) => new() { Kind = "event", Name = "deskpal.focus.on", Source = source };

        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(Ev("deskpal"), default)).ExitCode);
        var fast = await cmds.ExecuteAsync(Ev("deskpal"), default);
        Assert.Equal(CliExit.Declined, fast.ExitCode);
        Assert.Contains("less than a second", fast.Message);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(Ev("filesportal"), default)).ExitCode);   // another source isn't held up
        now = now.AddSeconds(1);
        Assert.Equal(CliExit.Ok, (await cmds.ExecuteAsync(Ev("deskpal"), default)).ExitCode);
        Assert.Equal(CliExit.Usage, (await cmds.ExecuteAsync(new PipeRequest { Kind = "event", Name = "Bad Name\n", Source = "x" }, default)).ExitCode);

        Assert.Equal(3, published.Count);
        Assert.Equal(new[] { "accepted", "rateLimited", "accepted", "accepted", "invalid" }, log.Select(l => l[(l.LastIndexOf(": ", StringComparison.Ordinal) + 2)..]));
        Assert.All(log, l => Assert.DoesNotContain('\n', l));
    }

    [Fact]
    public async Task At_most_two_events_are_handed_over_at_once()
    {
        using var inside = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        var log = new List<string>();
        var cmds = new ShortcutCommands(new InMemoryShortcutLibrary(), (_, _, _, _) => Task.FromResult<RunResult?>(null))
        {
            PublishEvent = _ => { inside.Release(); release.Wait(TimeSpan.FromSeconds(5)); }, LogEvent = l => { lock (log) log.Add(l); },
        };
        PipeRequest Ev(string source) => new() { Kind = "event", Name = "deskpal.focus.on", Source = source };

        var a = Task.Run(() => cmds.ExecuteAsync(Ev("one"), default));
        var b = Task.Run(() => cmds.ExecuteAsync(Ev("two"), default));
        Assert.True(await inside.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await inside.WaitAsync(TimeSpan.FromSeconds(5)));
        var c = await cmds.ExecuteAsync(Ev("three"), default);
        Assert.Equal(CliExit.Declined, c.ExitCode);
        Assert.Contains("still handling other events", c.Message);
        release.Set();
        Assert.Equal(CliExit.Ok, (await a).ExitCode);
        Assert.Equal(CliExit.Ok, (await b).ExitCode);
        Assert.Contains(log, l => l.EndsWith(": busy", StringComparison.Ordinal));
    }

    [Fact]
    public void The_client_process_id_can_not_be_set_by_the_request()
    {
        var r = JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"launch\",\"clientProcessId\":4}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(0, r.ClientProcessId);
    }
}
