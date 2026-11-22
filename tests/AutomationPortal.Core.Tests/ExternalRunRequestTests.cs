using System.Text.Json;
using System.Text.RegularExpressions;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>A step that would change files; counts every real run.</summary>
public sealed class CountingChangeAction : ShortcutAction
{
    public static int Calls;
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.change", Title = "Change Files", Category = ActionCategories.Advanced, Summary = "Change the files" };
    public override Task<ContentValue> RunAsync(ActionContext ctx) { Interlocked.Increment(ref Calls); return Task.FromResult(ContentValue.Text("changed")); }
}

/// <summary>A step that only saves new files through the shared "Save result" settings.</summary>
public sealed class CountingCopyAction : ShortcutAction
{
    public static int Calls;
    public override ActionDescriptor Descriptor { get; } = new()
    {
        Id = "test.copy", Title = "Save Copies", Category = ActionCategories.Advanced,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("saveTo", "Save result", "next", new Choice("next", "Next to the original"), new Choice("folder", "In a folder I choose")),
            ParameterDescriptor.FolderParam("saveFolder", "Save in folder"),
        },
    };
    public override Task<ContentValue> RunAsync(ActionContext ctx) { Interlocked.Increment(ref Calls); return Task.FromResult(ContentValue.Empty); }
}

public class ExternalRunRequestTests
{
    private static readonly ActionRegistry Registry = MakeRegistry();

    private static ActionRegistry MakeRegistry()
    {
        var r = ActionRegistry.CreateWithBuiltIns();
        r.Register(new CountingChangeAction());
        r.Register(new CountingCopyAction());
        return r;
    }

    private sealed class Rig
    {
        public readonly InMemoryShortcutLibrary Library = new();
        public readonly InMemorySettingsStore Settings = new();
        public readonly List<string> Runs = new();
        public readonly List<CallerPrompt> Asked = new();
        public readonly List<RunRequestLog> Log = new();
        public Func<CallerPrompt, CancellationToken, Task<CallerDecision>> Answer = (_, _) => Task.FromResult(CallerDecision.AllowOnce);

        public ShortcutCommands Commands(bool withConsent = true) => new(Library, (s, input, source, ct) =>
        {
            lock (Runs) Runs.Add(s.Name);
            return Task.FromResult<RunResult?>(new RunResult { ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Succeeded, Output = ContentValue.Text("ok") });
        })
        {
            Actions = Registry,
            Consent = withConsent ? new CallerConsent(Settings, (p, ct) => { lock (Asked) Asked.Add(p); return Answer(p, ct); }, TimeSpan.FromSeconds(5)) : null,
            LogRequest = e => { lock (Log) Log.Add(e); },
        };

        public Shortcut Add(string name, params string[] actionIds) => Add(name, s => { }, actionIds);

        public Shortcut Add(string name, Action<Shortcut> setup, params string[] actionIds)
        {
            var b = new ShortcutBuilder(name);
            foreach (var id in actionIds) b.Add(id);
            var s = b.Build();
            setup(s);
            Library.Save(s);
            return s;
        }
    }

    private static PipeRequest Run(string name, CallerInfo? caller = null, bool json = false, string? source = "cli", params string[] files) =>
        new() { Kind = "run", Name = name, Wait = true, Json = json, Source = source, Caller = caller, Files = files };

    private static readonly CallerInfo Ai = new() { App = "AI Portal", Kind = "ai" };

    // ---- list -------------------------------------------------------------------------------

    [Fact]
    public async Task List_items_carry_effects_tool_name_and_an_input_schema_per_input_kind()
    {
        var rig = new Rig();
        rig.Add("Notes", s => s.AcceptsInput.Add(ContentKind.Text), "content.text");
        rig.Add("Tidy", s => { s.AcceptsInput.AddRange(new[] { ContentKind.File, ContentKind.Folder }); s.WhenNoInput = WhenNoInput.AskForFiles; }, "test.change");
        rig.Add("Resize", s => { s.AcceptsInput.Add(ContentKind.Image); s.WhenNoInput = WhenNoInput.StopAndSay; }, "test.copy");
        rig.Add("Open folder", s => s.AcceptsInput.Add(ContentKind.Folder), "content.text");
        rig.Add("Plain");

        var r = await rig.Commands().ExecuteAsync(new PipeRequest { Kind = "list", Json = true }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        var byName = doc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.Clone());

        Assert.Equal("none", byName["Notes"].GetProperty("effects").GetString());
        Assert.Equal("changesFiles", byName["Tidy"].GetProperty("effects").GetString());
        Assert.Equal("makesCopies", byName["Resize"].GetProperty("effects").GetString());
        Assert.Equal("tidy", byName["Tidy"].GetProperty("toolName").GetString());
        Assert.Equal("open_folder", byName["Open folder"].GetProperty("toolName").GetString());

        var notes = byName["Notes"].GetProperty("inputs");
        Assert.Equal("object", notes.GetProperty("type").GetString());
        Assert.Equal("string", notes.GetProperty("properties").GetProperty("text").GetProperty("type").GetString());
        Assert.False(notes.GetProperty("additionalProperties").GetBoolean());

        var tidy = byName["Tidy"].GetProperty("inputs").GetProperty("properties");
        Assert.Equal("array", tidy.GetProperty("files").GetProperty("type").GetString());
        Assert.Equal("string", tidy.GetProperty("files").GetProperty("items").GetProperty("type").GetString());
        Assert.Contains("folders", tidy.GetProperty("files").GetProperty("description").GetString());
        Assert.Contains("asks you to pick", tidy.GetProperty("files").GetProperty("description").GetString());
        Assert.False(tidy.TryGetProperty("text", out _));

        var resize = byName["Resize"].GetProperty("inputs");
        Assert.Equal(new[] { "files" }, resize.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("string", byName["Open folder"].GetProperty("inputs").GetProperty("properties").GetProperty("folder").GetProperty("type").GetString());
        Assert.Empty(byName["Plain"].GetProperty("inputs").GetProperty("properties").EnumerateObject());
        Assert.Empty(rig.Runs);
    }

    [Fact]
    public async Task A_version_1_list_consumer_still_reads_its_fields()
    {
        var rig = new Rig();
        var s = rig.Add("Tidy", "test.change");
        s.Alias = "td"; s.Description = "Sorts things"; rig.Library.Save(s);
        var r = await rig.Commands().ExecuteAsync(new PipeRequest { Kind = "list", Json = true }, default);
        var items = JsonSerializer.Deserialize<List<V1Item>>(r.Output!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var item = Assert.Single(items);
        Assert.Equal((s.Id, "Tidy", "td", "Sorts things"), (item.Id, item.Name, item.Alias, item.Description));

        var text = await rig.Commands().ExecuteAsync(new PipeRequest { Kind = "list" }, default);
        Assert.Contains("Tidy", text.Output);
        Assert.DoesNotContain("changesFiles", text.Output);
    }

    private sealed record V1Item(Guid Id, string Name, string? Folder, string? Alias, string? Hotkey, bool Pinned, string? Description);

    // ---- tool names and tools ---------------------------------------------------------------

    [Fact]
    public void Tool_names_are_sanitized_unique_short_and_stable()
    {
        var t0 = DateTimeOffset.Now.AddDays(-10);
        var list = new List<Shortcut>();
        Shortcut Make(string name, int day) { var s = new ShortcutBuilder(name).Build(); s.CreatedAt = t0.AddDays(day); list.Add(s); return s; }
        var tidy = Make("Tidy Downloads!", 0);
        var tidy2 = Make("tidy  downloads", 1);
        var cafe = Make("Café ☕ Time", 2);
        var empty = Make("  ", 3);
        var longOne = Make(new string('x', 100), 4);
        var longTwo = Make(new string('x', 100), 5);

        var names = ShortcutTools.ToolNames(list);
        Assert.All(names.Values, n => Assert.Matches(new Regex("^[a-z0-9_]{1,64}$"), n));
        Assert.Equal(names.Count, names.Values.Distinct().Count());
        Assert.Equal("tidy_downloads", names[tidy.Id]);
        Assert.Equal("tidy_downloads_" + tidy2.Id.ToString("N")[..8], names[tidy2.Id]);
        Assert.Equal("cafe_time", names[cafe.Id]);
        Assert.Equal("shortcut", names[empty.Id]);
        Assert.Equal(64, names[longOne.Id].Length);
        Assert.NotEqual(names[longOne.Id], names[longTwo.Id]);

        // A newer shortcut with the same name, added in any order, never takes an older one's name.
        var newer = Make("Tidy Downloads", 9);
        list.Reverse();
        var again = ShortcutTools.ToolNames(list);
        Assert.Equal(names[tidy.Id], again[tidy.Id]);
        Assert.Equal(names[tidy2.Id], again[tidy2.Id]);
        Assert.StartsWith("tidy_downloads_", again[newer.Id]);
    }

    [Fact]
    public async Task Tools_lists_function_definitions_and_run_accepts_the_tool_name()
    {
        var rig = new Rig();
        rig.Add("Say Hello", s => { s.AcceptsInput.Add(ContentKind.Text); s.Description = "Greets you"; }, "content.text");
        var cmds = rig.Commands();
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "tools", Json = true }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        var tool = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        var fn = tool.GetProperty("function");
        Assert.Equal("say_hello", fn.GetProperty("name").GetString());
        Assert.StartsWith("Greets you.", fn.GetProperty("description").GetString());
        Assert.Contains("doesn't change files", fn.GetProperty("description").GetString());
        Assert.Equal("object", fn.GetProperty("parameters").GetProperty("type").GetString());

        var run = await cmds.ExecuteAsync(Run("say_hello"), default);
        Assert.Equal(CliExit.Ok, run.ExitCode);
        Assert.Equal(new[] { "Say Hello" }, rig.Runs);
    }

    // ---- preview ----------------------------------------------------------------------------

    [Fact]
    public async Task Preview_describes_the_steps_and_never_runs_one()
    {
        var before = (CountingChangeAction.Calls, CountingCopyAction.Calls);
        var rig = new Rig();
        rig.Add("Tidy", s => s.AcceptsInput.Add(ContentKind.File), "content.text", "test.copy", "test.change", "ui.alert");
        var cmds = rig.Commands();

        var text = await cmds.ExecuteAsync(new PipeRequest { Kind = "preview", Name = "Tidy", Files = new[] { @"C:\Users\me\a.txt" } }, default);
        Assert.Equal(CliExit.Ok, text.ExitCode);
        Assert.Contains("Nothing has run", text.Output);
        Assert.Contains("3. Change Files (changes files)", text.Output);

        var json = await cmds.ExecuteAsync(new PipeRequest { Kind = "preview", Name = "tidy", Json = true, Files = new[] { @"C:\Users\me\a.txt" }, Caller = Ai }, default);
        using var doc = JsonDocument.Parse(json.Output!);
        var root = doc.RootElement;
        Assert.Equal("changesFiles", root.GetProperty("effects").GetString());
        Assert.True(root.GetProperty("asksFirst").GetBoolean());
        Assert.Equal(new[] { "reads", "makesCopies", "changesFiles", "none" }, root.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("effect").GetString()));
        Assert.Equal(@"C:\Users\me\a.txt", root.GetProperty("input").GetProperty("files")[0].GetString());

        Assert.Equal(before, (CountingChangeAction.Calls, CountingCopyAction.Calls));
        Assert.Empty(rig.Runs);
        Assert.Empty(rig.Asked);
        Assert.Equal(CliExit.NotFound, (await cmds.ExecuteAsync(new PipeRequest { Kind = "preview", Name = "Nope" }, default)).ExitCode);
    }

    // ---- when a run is asked first ----------------------------------------------------------

    [Fact]
    public async Task An_ai_caller_is_always_asked_even_for_a_shortcut_that_changes_nothing_and_is_never_remembered()
    {
        var rig = new Rig();
        var s = rig.Add("Notes", "content.text");
        rig.Settings.Current.AllowedCallers = new() { "aiportal" };
        rig.Answer = (_, _) => Task.FromResult(CallerDecision.AlwaysAllow);
        var cmds = rig.Commands();

        for (var i = 0; i < 2; i++)
        {
            var r = await cmds.ExecuteAsync(Run("Notes", Ai, source: "aiportal"), default);
            Assert.Equal(CliExit.Ok, r.ExitCode);
        }
        Assert.Equal(2, rig.Asked.Count);
        Assert.All(rig.Asked, p => { Assert.True(p.IsAi); Assert.False(p.CanRemember); Assert.Equal("AI Portal", p.CallerName); Assert.NotNull(p.AiLine); });
        Assert.Equal("AI Portal wants to run \"Notes\"", rig.Asked[0].Question);
        Assert.True(rig.Settings.Current.AllowedCallerShortcuts is null or { Count: 0 });
        Assert.Equal(2, rig.Runs.Count);
        Assert.False(cmds.MustAsk(Run("Notes"), s));
        Assert.True(cmds.MustAsk(Run("Notes", Ai), s));
    }

    [Fact]
    public async Task A_shortcut_that_changes_files_is_asked_for_a_caller_that_is_not_an_ai_even_without_ask()
    {
        var rig = new Rig();
        rig.Add("Tidy", "test.change");
        rig.Add("Resize", "test.copy");
        var cmds = rig.Commands();

        var r = await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "Files Portal", Kind = "app" }, files: @"C:\x\a.txt"), default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        var p = Assert.Single(rig.Asked);
        Assert.Equal(ShortcutEffect.ChangesFiles, p.Effect);
        Assert.Equal(new[] { @"C:\x\a.txt" }, p.Files);
        Assert.Contains("Change Files", p.PreviewText);
        Assert.True(p.CanRemember);

        await cmds.ExecuteAsync(Run("Resize", source: null), default);   // a raw pipe client without source or caller
        Assert.Equal(2, rig.Asked.Count);
        Assert.Equal("Another app", rig.Asked[1].CallerName);
        Assert.False(rig.Asked[1].CanRemember);
        Assert.Equal(ShortcutEffect.MakesCopies, rig.Asked[1].Effect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Declined_is_exit_6_with_a_json_reason_for_callers_that_send_caller(bool withCaller)
    {
        var rig = new Rig { Answer = (_, _) => Task.FromResult(CallerDecision.DontRun) };
        rig.Add("Tidy", "test.change");
        var r = await rig.Commands().ExecuteAsync(Run("Tidy", withCaller ? Ai : null, json: true), default);
        Assert.Equal(CliExit.Declined, r.ExitCode);
        Assert.Equal("Not run: you chose Don't run", r.Message);
        Assert.Empty(rig.Runs);
        if (!withCaller) { Assert.Null(r.Output); return; }   // version 1 replies are unchanged
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Equal("declined", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Not run: you chose Don't run", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task One_waiting_request_per_caller_and_the_rest_are_turned_away()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource<CallerDecision>();
        rig.Answer = (_, _) => gate.Task;
        rig.Add("Tidy", "test.change");
        var cmds = rig.Commands();

        var first = cmds.ExecuteAsync(Run("Tidy", Ai, json: true), default);
        await Task.Delay(100);
        var second = await cmds.ExecuteAsync(Run("Tidy", new CallerInfo { App = "ai portal", Kind = "ai" }, json: true), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CliExit.Declined, second.ExitCode);
        Assert.Contains("already has a request waiting", second.Message);
        using (var doc = JsonDocument.Parse(second.Output!)) Assert.Equal("rateLimited", doc.RootElement.GetProperty("status").GetString());

        gate.SetResult(CallerDecision.AllowOnce);
        Assert.Equal(CliExit.Ok, (await first.WaitAsync(TimeSpan.FromSeconds(5))).ExitCode);
        Assert.Single(rig.Asked);
        Assert.Single(rig.Runs);
    }

    [Fact]
    public async Task Every_request_is_logged_with_how_it_ended()
    {
        var rig = new Rig();
        rig.Add("Notes", "content.text");
        rig.Add("Tidy", "test.change");
        var cmds = rig.Commands();

        await cmds.ExecuteAsync(Run("Notes"), default);
        await cmds.ExecuteAsync(Run("Tidy", Ai), default);
        rig.Answer = (_, _) => Task.FromResult(CallerDecision.DontRun);
        await cmds.ExecuteAsync(Run("Tidy", Ai), default);
        await cmds.ExecuteAsync(Run("Missing"), default);
        await rig.Commands(withConsent: false).ExecuteAsync(Run("Tidy"), default);
        await cmds.ExecuteAsync(Run("Tidy", ask: true, source: "Not An Id"), default);

        Assert.Equal(new[] { "ran", "allowed", "declined", "notFound", "blocked", "blocked" }, rig.Log.Select(l => l.Outcome));
        Assert.Equal("AI Portal", rig.Log[1].Caller);
        Assert.Equal("ai", rig.Log[1].CallerKind);
        Assert.Equal("changesFiles", rig.Log[1].Effects);
        Assert.Contains("\"Tidy\"", rig.Log[2].Describe());
        Assert.Single(rig.Runs, "Notes");
    }

    private static PipeRequest Run(string name, bool ask, string? source) => new() { Kind = "run", Name = name, Wait = true, Ask = ask, Source = source };

    [Fact]
    public async Task Rate_limited_requests_are_logged()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource<CallerDecision>();
        rig.Answer = (_, _) => gate.Task;
        rig.Add("Tidy", "test.change");
        var cmds = rig.Commands();
        var first = cmds.ExecuteAsync(Run("Tidy", Ai), default);
        await Task.Delay(100);
        await cmds.ExecuteAsync(Run("Tidy", Ai), default);
        gate.SetResult(CallerDecision.DontRun);
        await first;
        Assert.Equal(new[] { "rateLimited", "declined" }, rig.Log.Select(l => l.Outcome));
    }

    [Fact]
    public async Task A_version_1_run_without_caller_of_a_shortcut_that_changes_nothing_runs_as_before()
    {
        var rig = new Rig { Answer = (_, _) => throw new InvalidOperationException("must not ask") };
        rig.Add("Notes", "content.text");
        var r = await rig.Commands().ExecuteAsync(new PipeRequest { Kind = "run", Name = "Notes", Wait = true, Json = true, Source = "cli" }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Equal("succeeded", doc.RootElement.GetProperty("status").GetString());
        Assert.Empty(rig.Asked);
        Assert.Equal(new[] { "Notes" }, rig.Runs);
    }

    [Fact]
    public async Task Without_the_app_a_run_that_must_be_asked_never_runs()
    {
        var rig = new Rig();
        rig.Add("Tidy", "test.change");
        rig.Add("Notes", "content.text");
        var cmds = rig.Commands(withConsent: false);
        Assert.True(cmds.NeedsConsent(Run("Tidy")));
        Assert.False(cmds.NeedsConsent(Run("Notes")));
        Assert.True(cmds.NeedsConsent(Run("Notes", Ai)));
        var r = await cmds.ExecuteAsync(Run("Tidy"), default);
        Assert.Equal(CliExit.Problem, r.ExitCode);
        Assert.Empty(rig.Runs);
    }

    // ---- hosted engine ----------------------------------------------------------------------

    [Fact]
    public async Task A_hosted_run_for_an_ai_caller_asks_first_and_runs_nothing_without_a_yes()
    {
        using var host = new TestHost();
        host.Registry.Register(new CountingChangeAction());
        var b = new ShortcutBuilder("Tidy");
        b.Add("test.change");
        var s = b.Build();
        var before = CountingChangeAction.Calls;

        host.UI.Enqueue(false);
        var declined = await host.Runner.RunAsync(s, ContentValue.Empty, new RunOptions { Caller = Ai });
        Assert.Equal(RunStatus.Cancelled, declined.Status);
        Assert.StartsWith("Not run:", declined.ErrorMessage);
        Assert.Equal(before, CountingChangeAction.Calls);
        Assert.Contains("AI Portal wants to run \"Tidy\"", host.UI.Prompts.Last());

        host.UI.Enqueue(true);
        Assert.Equal(RunStatus.Succeeded, (await host.Runner.RunAsync(s, ContentValue.Empty, new RunOptions { Caller = Ai })).Status);
        Assert.Equal(before + 1, CountingChangeAction.Calls);

        var asked = host.UI.Calls.Count;
        Assert.Equal(RunStatus.Succeeded, (await host.Runner.RunAsync(s, ContentValue.Empty, new RunOptions())).Status);
        Assert.Equal(RunStatus.Succeeded, (await host.Runner.RunAsync(s, ContentValue.Empty, new RunOptions { Caller = new CallerInfo { App = "Files Portal" } })).Status);
        Assert.Equal(asked, host.UI.Calls.Count);   // other hosted runs behave as before
    }
}
