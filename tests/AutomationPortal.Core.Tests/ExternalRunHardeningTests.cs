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

    [Fact]
    public void The_client_process_id_can_not_be_set_by_the_request()
    {
        var r = JsonSerializer.Deserialize<PipeRequest>("{\"kind\":\"launch\",\"clientProcessId\":4}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(0, r.ClientProcessId);
    }
}
