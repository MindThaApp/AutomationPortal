using System.Windows.Threading;
using Flowmate.Core;
using Flowmate.Core.Testing;
using Flowmate.Editor;
using Flowmate.Editor.Logic;
using Flowmate.Editor.ViewModels;
using Xunit;

namespace Flowmate.Editor.Tests;

public sealed class DangerAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.danger", Title = "Wipe it", Category = ActionCategories.Advanced, IsDestructive = true };
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
}

public class AiAndRecordingTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly ActionRegistry _registry = ActionRegistry.CreateWithBuiltIns();
    private readonly List<EditorViewModel> _vms = new();

    public AiAndRecordingTests() => _registry.Register(new DangerAction());

    public void Dispose() { foreach (var v in _vms) v.Detach(); _host.Dispose(); }

    private EditorViewModel NewVm(Shortcut s, bool isNew)
    {
        var vm = new EditorViewModel(new EditorContext(_host.Services, _registry), s, isNew);
        _vms.Add(vm);
        return vm;
    }

    private static AiDraftResult Draft(string name = "From AI", params ActionInstance[] steps)
    {
        var s = new Shortcut { Name = name, Icon = "WeatherSunny24", Color = "orange" };
        s.Actions.AddRange(steps.Length > 0 ? steps : new[] { ActionInstance.Create("content.text", ("text", "hi")).WithUid("a1") });
        return new AiDraftResult { Shortcut = s, Summary = "Says hi." };
    }

    [Fact]
    public void A_draft_is_one_undo_step_and_is_not_saved_until_kept()
    {
        var s = new Shortcut { Name = "New Shortcut" };
        var vm = NewVm(s, isNew: true);
        vm.ApplyAiResult(Draft(), isChange: false);

        Assert.Equal("From AI", vm.Name);
        Assert.Equal("WeatherSunny24", vm.Icon);
        Assert.Single(vm.Doc.Actions);
        Assert.True(vm.DraftPending);
        Assert.True(vm.AiNote!.IsDraft);
        Assert.Equal("Drafted by AI — check each step before running", vm.AiNote.Headline);
        Assert.Null(_host.Library.GetById(s.Id));

        vm.KeepDraft();
        Assert.False(vm.DraftPending);
        Assert.Null(vm.AiNote);
        Assert.True(vm.SaveNow());
        Assert.NotNull(_host.Library.GetById(s.Id));
    }

    [Fact]
    public void Undo_takes_back_the_whole_draft_including_name_and_icon()
    {
        var s = new Shortcut { Name = "New Shortcut", Icon = "Flash24", Color = "blue" };
        var vm = NewVm(s, isNew: true);
        vm.ApplyAiResult(Draft(), isChange: false);
        vm.UndoAiResult();
        Assert.True(vm.IsEmpty);
        Assert.Equal("New Shortcut", vm.Name);
        Assert.Equal("Flash24", vm.Icon);
        Assert.Equal("blue", vm.Color);
        Assert.Null(vm.AiNote);
        Assert.False(vm.HasUnkeptDraft);
        vm.SaveNow();
        Assert.Null(_host.Library.GetById(s.Id));
    }

    [Fact]
    public void Editing_a_draft_keeps_it_and_closing_with_an_unkept_draft_can_discard_it()
    {
        var s = new Shortcut { Name = "New Shortcut" };
        var vm = NewVm(s, isNew: true);
        vm.ApplyAiResult(Draft(), isChange: false);
        Assert.True(vm.HasUnkeptDraft);
        vm.DiscardDraft();
        vm.SaveNow();
        Assert.Null(_host.Library.GetById(s.Id));

        var s2 = new Shortcut { Name = "Second" };
        var vm2 = NewVm(s2, isNew: true);
        vm2.ApplyAiResult(Draft("Second AI"), isChange: false);
        vm2.Doc.SetParameter(vm2.Doc.Actions[0], "text", ParamValue.Text("edited"));
        Assert.False(vm2.DraftPending);
        Assert.True(vm2.SaveNow());
        Assert.NotNull(_host.Library.GetById(s2.Id));
    }

    [Fact]
    public void Running_or_saving_a_draft_counts_as_keeping_it()
    {
        var s = new Shortcut { Name = "New Shortcut" };
        var vm = NewVm(s, isNew: true);
        vm.ApplyAiResult(Draft(), isChange: false);
        Assert.True(vm.SaveNow());
        Assert.False(vm.DraftPending);
        Assert.NotNull(_host.Library.GetById(s.Id));
    }

    [Fact]
    public void Destructive_steps_in_a_draft_are_flagged_and_listed()
    {
        var vm = NewVm(new Shortcut { Name = "N" }, isNew: true);
        vm.ApplyAiResult(Draft("D", ActionInstance.Create("content.text", ("text", "x")).WithUid("a1"), ActionInstance.Create("test.danger").WithUid("a2")), isChange: false);
        Assert.Contains("a2", vm.AiStepUids);
        Assert.Equal(new[] { "Wipe it" }, vm.AiNote!.DestructiveTitles);
    }

    [Fact]
    public void A_change_is_one_undo_step_with_a_summary_of_what_changed()
    {
        var s = new Shortcut { Name = "Existing" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "one")).WithUid("a1"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "two")).WithUid("a2"));
        _host.Library.Save(s);
        var vm = NewVm(_host.Library.GetById(s.Id)!.Clone(), isNew: false);

        var revised = s.Clone();
        revised.Actions[0].Parameters["text"] = ParamValue.Text("changed");
        revised.Actions.RemoveAt(1);
        revised.Actions.Add(ActionInstance.Create("ui.showResult").WithUid("a3"));
        vm.ApplyAiResult(new AiDraftResult { Shortcut = revised, Summary = "Shows the result." }, isChange: true);

        Assert.Equal(new[] { "a1", "a3" }, vm.Doc.Actions.Select(a => a.Uid));
        Assert.False(vm.DraftPending);
        Assert.False(vm.AiNote!.IsDraft);
        Assert.Contains("1 step added", vm.AiNote.Detail);
        Assert.Contains("1 step changed", vm.AiNote.Detail);
        Assert.Contains("1 step removed", vm.AiNote.Detail);
        Assert.Equal("Existing", vm.Name);

        vm.UndoAiResult();
        Assert.Equal(new[] { "a1", "a2" }, vm.Doc.Actions.Select(a => a.Uid));
        Assert.Equal("one", vm.Doc.Actions[0].Parameters["text"].ToLiteralText());
    }

    [Fact]
    public void Recorded_steps_go_after_the_selection_as_one_undo_step_with_fresh_copies()
    {
        var s = new Shortcut { Name = "Rec" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "first")).WithUid("a1"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "last")).WithUid("a2"));
        var vm = NewVm(s, isNew: true);
        vm.SelectActions(new[] { vm.Doc.Actions[0] });

        var recorded = new[]
        {
            ActionInstance.Create("input.click", ("x", 5), ("y", 6)).WithUid("r1"),
            ActionInstance.Create("input.type", ("text", "abc")).WithUid("r2"),
        };
        var inserted = vm.InsertRecorded(recorded);

        Assert.Equal(new[] { "a1", "r1", "r2", "a2" }, vm.Doc.Actions.Select(a => a.Uid));
        Assert.NotSame(recorded[0], inserted[0]);
        vm.Undo();
        Assert.Equal(new[] { "a1", "a2" }, vm.Doc.Actions.Select(a => a.Uid));
    }

    [Fact]
    public void Screen_readers_get_the_title_and_sentence_of_every_row()
    {
        var s = new Shortcut { Name = "Names" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "Hello there")).WithUid("a1"));
        s.Actions.Add(ActionInstance.Create("test.danger").WithUid("a2"));
        s.Actions[1].Disabled = true;
        var vm = NewVm(s, isNew: true);

        var first = vm.Rows[0].AutomationName;
        Assert.Contains("Hello there", first);
        Assert.DoesNotContain("ViewModel", first);
        var second = vm.Rows[1].AutomationName;
        Assert.StartsWith("Wipe it", second);
        Assert.Contains("turned off", second);
        Assert.Contains("can change or delete things", second);
        Assert.Equal(first, vm.Rows[0].ToString());
    }

    [Fact]
    public void Bars_are_named_too()
    {
        var s = new Shortcut { Name = "Flow" };
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "i" }.WithFlow("g", FlowRole.Start).WithParam("conditions", ParamValue.AllOf()));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "m" }.WithFlow("g", FlowRole.Middle));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "e" }.WithFlow("g", FlowRole.End));
        var vm = NewVm(s, isNew: true);
        Assert.Equal("Otherwise", vm.Rows[1].AutomationName);
        Assert.Equal("End If", vm.Rows[2].AutomationName);
    }

    [Fact]
    public void A_value_of_one_makes_the_unit_after_it_singular()
    {
        var segs = SummarySegment.Parse("Repeat {count} times");
        var one = SummaryLayout.Agree(segs, k => ParamValue.Number(1), out var tokens);
        Assert.Equal(" time", one[2].Text);
        Assert.Empty(tokens);
        var three = SummaryLayout.Agree(segs, k => ParamValue.Number(3), out _);
        Assert.Equal(" times", three[2].Text);

        var interval = SummaryLayout.Agree(SummarySegment.Parse("Every {every} {unit}"), k => k == "every" ? ParamValue.Number(1) : ParamValue.Choice("minutes"), out var singular);
        Assert.Contains("unit", singular);
        Assert.Equal(" ", interval[2].Text);
    }

    [Fact]
    public void Numbers_with_a_unit_read_correctly_in_tokens()
    {
        var p = ParameterDescriptor.Number("n", "Wait", 5, unit: "seconds");
        string Text(double v) => string.Concat(ValueFormat.Parts(p, ParamValue.Number(v), new NoEnv()).Select(x => x.Text));
        Assert.Equal("1 second", Text(1));
        Assert.Equal("2 seconds", Text(2));
    }

    private sealed class NoEnv : IValueDisplayContext
    {
        public VarDisplay Describe(VarRef reference) => throw new NotSupportedException();
        public string? ChoiceLabel(ParameterDescriptor parameter, string id) => null;
    }
}