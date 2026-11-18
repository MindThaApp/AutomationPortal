using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public sealed class FailingAction : ShortcutAction
{
    public static int Calls;
    public static int FailUntil;
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.fail", Title = "Fail", Category = ActionCategories.Advanced };
    public override Task<ContentValue> RunAsync(ActionContext ctx)
    {
        Calls++;
        if (Calls <= FailUntil) throw new ActionException("Nope.");
        return Task.FromResult(ContentValue.Text("recovered"));
    }
}

public sealed class SlowAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.slow", Title = "Slow", Category = ActionCategories.Advanced };
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ctx.CancellationToken);
        return ContentValue.Empty;
    }
}

public sealed class UpperAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new()
    {
        Id = "test.upper", Title = "Upper", Category = ActionCategories.Text, Summary = "Upper {text}",
        Parameters = new[] { ParameterDescriptor.Content("text", "Text", isInput: true) },
    };
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ContentValue.Text((await ctx.GetTextAsync("text")).ToUpperInvariant());
}

public class EngineTests : IDisposable
{
    private readonly TestHost _host = new TestHost().With(typeof(EngineTests).Assembly);
    public void Dispose() => _host.Dispose();

    private Task<RunResult> Run(Shortcut s, ContentValue? input = null, RunOptions? o = null) =>
        _host.Runner.RunAsync(s, input ?? ContentValue.Empty, o ?? new RunOptions { Source = RunSource.Editor });

    [Fact]
    public async Task MagicAndNamedVariables_AndImplicitInput()
    {
        var b = new ShortcutBuilder("Vars");
        var t = b.Add("content.text", ("text", "hello"));
        b.Add("var.set", ("name", "Greeting"));                 // implicit input -> value
        b.Add("test.upper");                                     // implicit input again
        var u = b.Add("content.text", ("text", ParamValue.Text(VarRef.Named("Greeting"), " / ", VarRef.Output(t))));
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("hello / hello", r.Output.AsText());
        Assert.Contains(r.Steps, s => s.ActionId == "test.upper" && s.OutputPreview == "HELLO");
        Assert.True(r.Steps.Count >= 4);
    }

    [Fact]
    public async Task ShortcutInput_IsFirstImplicitInput_AndProperties()
    {
        var b = new ShortcutBuilder("Input");
        b.Add("content.text", ("text", ParamValue.Text(VarRef.ShortcutInput.WithProperty("Word Count"))));
        var r = await Run(b.Build(), ContentValue.Text("a b c"));
        Assert.Equal("3", r.Output.AsText());
    }

    [Fact]
    public async Task ShortcutInput_HoldsTheFilesChosenWhenAskingForFiles()
    {
        _host.UI.Enqueue(@"C:\Temp\a.txt");
        var b = new ShortcutBuilder("Ask");
        b.Add("content.text", ("text", ParamValue.Text(VarRef.ShortcutInput)));
        var s = b.Build();
        s.WhenNoInput = WhenNoInput.AskForFiles;
        var r = await Run(s);
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Contains("a.txt", r.Output.AsText());
    }

    [Fact]
    public async Task If_Otherwise()
    {
        async Task<string> Go(string text)
        {
            var b = new ShortcutBuilder("If");
            b.If(ParamValue.AllOf(ConditionRow.Of(ParamValue.ShortcutInput(), ConditionOperators.Contains, "yes")),
                s => s.Add("content.text", ("text", "then")),
                s => s.Add("content.text", ("text", "else")));
            return (await Run(b.Build(), ContentValue.Text(text))).Output.AsText();
        }
        Assert.Equal("then", await Go("oh yes"));
        Assert.Equal("else", await Go("nope"));
    }

    [Fact]
    public async Task If_AnyMatch_AndNoOtherwisePassesInputThrough()
    {
        var b = new ShortcutBuilder("If");
        b.If(ParamValue.AnyOf(
                ConditionRow.Of(ParamValue.ShortcutInput(), ConditionOperators.Is, "a"),
                ConditionRow.Of(ParamValue.ShortcutInput(), ConditionOperators.Is, "b")),
            s => s.Add("content.text", ("text", "matched")));
        Assert.Equal("matched", (await Run(b.Build(), "b")).Output.AsText());
        Assert.Equal("zzz", (await Run(b.Build(), "zzz")).Output.AsText());
    }

    [Fact]
    public async Task NestedIf_InsideRepeatWithEach()
    {
        var b2 = new ShortcutBuilder("Nested");
        var list = b2.Add("content.list", ("items", ParamValue.List("1", "2", "3", "4")));
        b2.RepeatEach(ParamValue.Output(list), s =>
        {
            s.If(ParamValue.AllOf(ConditionRow.Of(ParamValue.Var(VarRef.RepeatItem), ConditionOperators.GreaterThan, 2)),
                x => x.Add("content.text", ("text", ParamValue.Text("big", VarRef.RepeatIndex))),
                x => x.Add("content.text", ("text", ParamValue.Text("small", VarRef.RepeatIndex))));
        });
        var r = await Run(b2.Build());
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("small1\nsmall2\nbig3\nbig4", r.Output.AsText());
    }

    [Fact]
    public async Task Repeat_Count_AndIndex()
    {
        var b = new ShortcutBuilder("Repeat");
        b.Repeat(3, s => s.Add("content.text", ("text", ParamValue.Text(VarRef.RepeatIndex))));
        Assert.Equal("1\n2\n3", (await Run(b.Build())).Output.AsText());
    }

    [Fact]
    public async Task Menu_RunsChosenBranch_AndMenuResult()
    {
        var b = new ShortcutBuilder("Menu");
        b.Menu("Pick", ("Red", s => s.Add("content.text", ("text", ParamValue.Text("r:", VarRef.MenuResult)))),
                       ("Blue", s => s.Add("content.text", ("text", ParamValue.Text("b:", VarRef.MenuResult)))));
        _host.UI.Enqueue(1);
        var r = await Run(b.Build());
        Assert.Equal("b:Blue", r.Output.AsText());
        Assert.Equal("Pick", _host.UI.Prompts[0]);
    }

    [Fact]
    public async Task StopAndOutput_EndsEarly()
    {
        var b = new ShortcutBuilder("Out");
        b.Add("flow.output", ("output", "done"));
        b.Add("content.text", ("text", "never"));
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("done", r.Output.AsText());
        Assert.DoesNotContain(r.Steps, s => s.ActionId == "content.text");
    }

    [Fact]
    public async Task Stop_ReportsStopped()
    {
        var b = new ShortcutBuilder("Stop");
        b.Add("flow.stop");
        b.Add("content.text", ("text", "never"));
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Stopped, r.Status);
        Assert.DoesNotContain(r.Steps, s => s.ActionId == "content.text");
    }

    [Fact]
    public async Task OnError_Stop_Continue_Retry()
    {
        FailingAction.Calls = 0; FailingAction.FailUntil = int.MaxValue;
        var stop = new ShortcutBuilder("Stop");
        var uid = stop.Add("test.fail");
        stop.Add("content.text", ("text", "after"));
        var r = await Run(stop.Build());
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Equal("Nope.", r.ErrorMessage);
        Assert.Equal(uid, r.FailedStepUid);

        var cont = new ShortcutBuilder("Continue");
        cont.Add(ActionInstance.Create("test.fail").OnErrorContinue());
        cont.Add("content.text", ("text", "after"));
        r = await Run(cont.Build());
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("after", r.Output.AsText());

        FailingAction.Calls = 0; FailingAction.FailUntil = 2;
        var retry = new ShortcutBuilder("Retry");
        retry.Add(ActionInstance.Create("test.fail").OnErrorRetry(2, 0));
        r = await Run(retry.Build());
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("recovered", r.Output.AsText());
        Assert.Equal(3, FailingAction.Calls);

        FailingAction.Calls = 0; FailingAction.FailUntil = int.MaxValue;
        r = await Run(new ShortcutBuilder("Retry2").Also(b => b.Add(ActionInstance.Create("test.fail").OnErrorRetry(1, 0))).Build());
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Equal(2, FailingAction.Calls);
    }

    [Fact]
    public async Task RunShortcut_PassesInput_ReturnsOutput()
    {
        var child = new ShortcutBuilder("Child");
        child.Add("test.upper");
        var childShortcut = child.Build();
        _host.Library.Save(childShortcut);

        var parent = new ShortcutBuilder("Parent");
        parent.Add("flow.runShortcut", ("shortcut", ParamValue.Choice(childShortcut.Id.ToString("D"))), ("input", "abc"));
        var r = await Run(parent.Build());
        Assert.Equal("ABC", r.Output.AsText());
        Assert.Single(_host.History.Records);   // nested runs are not recorded
    }

    [Fact]
    public async Task RunShortcut_ByName_AndDepthLimit()
    {
        var loop = new ShortcutBuilder("Loop");
        loop.Add("flow.runShortcut", ("shortcut", ParamValue.Choice("Loop")));
        _host.Library.Save(loop.Build());
        var r = await Run(_host.Library.FindByName("Loop")!);
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("nested too deeply", r.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_StopsRun()
    {
        var b = new ShortcutBuilder("Slow");
        b.Add("test.slow");
        using var cts = new CancellationTokenSource();
        var task = Run(b.Build(), null, new RunOptions { CancellationToken = cts.Token });
        cts.CancelAfter(100);
        var r = await task;
        Assert.Equal(RunStatus.Cancelled, r.Status);
    }

    [Fact]
    public async Task UserCancel_EndsAsCancelled()
    {
        var b = new ShortcutBuilder("Ask");
        b.Add("ui.ask", ("prompt", "Name?"));
        _host.UI.EnqueueCancel();
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Cancelled, r.Status);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public async Task AskEachTime_PromptsAndUsesAnswer()
    {
        var b = new ShortcutBuilder("Ask");
        b.Add("content.text", ("text", ParamValue.Ask()));
        _host.UI.Enqueue("typed");
        var r = await Run(b.Build());
        Assert.Equal("typed", r.Output.AsText());
        Assert.Equal("Text:", _host.UI.Prompts[0]);
    }

    [Fact]
    public async Task SavedValues_PersistAcrossRuns()
    {
        var save = new ShortcutBuilder("Save");
        save.Add("var.saveValue", ("key", "counter"), ("value", 7));
        await Run(save.Build());
        var load = new ShortcutBuilder("Load");
        load.Add("var.loadValue", ("key", "counter"));
        Assert.Equal(7, (await Run(load.Build())).Output.AsNumber());
    }

    [Fact]
    public async Task DisabledAndComment_PassInputThrough()
    {
        var b = new ShortcutBuilder("Pass");
        b.Add("content.text", ("text", "keep"));
        b.Add(ActionInstance.Create("content.text", ("text", "skipped")).Also(a => a.Disabled = true));
        b.Comment("note");
        Assert.Equal("keep", (await Run(b.Build())).Output.AsText());
    }

    [Fact]
    public async Task BrokenGroup_FailsFriendly()
    {
        var b = new ShortcutBuilder("Broken");
        b.Add(ActionInstance.Create(FlowIds.Repeat).WithFlow("g", FlowRole.Start));
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("missing its end", r.ErrorMessage);
    }

    [Fact]
    public async Task LoopCap_IsEnforced()
    {
        var b = new ShortcutBuilder("Huge");
        b.Repeat(ShortcutRunner.MaxLoopIterations + 1, _ => { });
        var r = await Run(b.Build());
        Assert.Equal(RunStatus.Failed, r.Status);
    }

    [Fact]
    public async Task History_IsRecorded_AndObserverNotified()
    {
        var obs = new RecordingObserver();
        var b = new ShortcutBuilder("Obs");
        var uid = b.Add("content.text", ("text", "x"));
        await Run(b.Build(), null, new RunOptions { Observer = obs });
        Assert.Contains(uid, obs.Started);
        Assert.Contains(uid, obs.Finished);
        Assert.Single(_host.History.Records);
    }

    [Fact]
    public async Task TempFolder_IsDeleted()
    {
        var b = new ShortcutBuilder("Temp");
        b.Add("test.temp");
        var r = await Run(b.Build());
        Assert.False(Directory.Exists(_host.Services.Paths.RunTempFolder(r.RunId)));
        Assert.Equal("true", r.Output.AsText());
    }

    [Fact]
    public async Task StartAtUid_SkipsEarlierSteps_UsingSeededOutputs()
    {
        var b = new ShortcutBuilder("From");
        var a = b.Add("content.text", ("text", "first"));
        var c = b.Add("test.upper");
        var d = b.Add("content.text", ("text", ParamValue.Text(VarRef.Output(a), "+", VarRef.Output(c))));
        var r = await Run(b.Build(), null, new RunOptions
        {
            StartAtUid = c,
            SeedOutputs = new() { [a] = ContentValue.Text("seeded") },
        });
        Assert.Equal("seeded+SEEDED", r.Output.AsText());
        Assert.DoesNotContain(r.Steps, s => s.Uid == a);
    }

    [Fact]
    public async Task NotifyWithButtons_ReturnsPressedLabel()
    {
        var b = new ShortcutBuilder("Notify");
        b.Add("ui.notify", ("body", "Hi"), ("buttons", ParamValue.List("Yes", "No")));
        _host.Notifications.InteractiveAnswer = new NotificationResponse("No", null, false);
        var r = await Run(b.Build());
        Assert.Equal("No", r.Output.AsText());
    }

    [Fact]
    public async Task AskMany_ReturnsDictionary()
    {
        var b = new ShortcutBuilder("Form");
        b.Add("ui.askMany", ("fields", ParamValue.List(
            ParamValue.Dict(("label", "Name"), ("kind", "text")),
            ParamValue.Dict(("label", "Age"), ("kind", "number"), ("default", "30")))));
        var r = await Run(b.Build());
        var d = r.Output.AsDictionary()!;
        Assert.Equal("30", d.Get("Age").AsText());
        Assert.Equal(new[] { "Name", "Age" }, d.Keys);
    }

    [Fact]
    public void Registry_FindsBuiltIns_Searches_AndGroups()
    {
        var reg = ActionRegistry.CreateWithBuiltIns();
        Assert.NotNull(reg.Get("flow.if"));
        Assert.Equal(26, reg.All.Count);
        Assert.Contains(reg.Search("notif"), d => d.Id == "ui.notify");
        Assert.Contains(reg.ByCategory(ActionCategories.Scripting), d => d.Id == "flow.wait");
        Assert.Empty(reg.Diagnostics);
        foreach (var a in reg.All)
            foreach (var seg in a.Descriptor.SummarySegments.Where(s => s.IsToken))
                Assert.NotNull(a.Descriptor.FindParameter(seg.ParamKey!));
    }

    private sealed class RecordingObserver : IRunObserver
    {
        public List<string> Started { get; } = new();
        public List<string> Finished { get; } = new();
        public void OnStepStarted(string uid) => Started.Add(uid);
        public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error) => Finished.Add(uid);
        public void OnVariableSet(string name, ContentValue value) { }
        public void OnLog(string message) { }
    }
}

public sealed class TempAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.temp", Title = "Temp", Category = ActionCategories.Advanced };
    public override Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var f = ctx.WriteTempFile("a.txt", "x");
        return Task.FromResult(ContentValue.Bool(File.Exists(f.Path)));
    }
}

internal static class Ext
{
    public static T Also<T>(this T value, Action<T> a) { a(value); return value; }
}
