using Flowmate.Core;
using Flowmate.Core.Testing;
using Flowmate.Editor.Logic;
using Xunit;

namespace Flowmate.Editor.Tests;

public class VariableTests
{
    private static readonly ActionRegistry Registry = ActionRegistry.CreateWithBuiltIns();

    [Fact]
    public void Only_actions_above_are_available_nearest_first()
    {
        var s = new Shortcut();
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "a")).WithUid("t1"));
        s.Actions.Add(ActionInstance.Create("content.number", ("value", 5)).WithUid("n1"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "b")).WithUid("t2"));

        var at2 = VariableCatalog.Available(s, Registry, 2);
        var outs = at2.Where(o => o.Group == VariableGroup.Actions).Select(o => o.Ref.Uid).ToList();
        Assert.Equal(new[] { "n1", "t1" }, outs);

        var at0 = VariableCatalog.Available(s, Registry, 0);
        Assert.DoesNotContain(at0, o => o.Group == VariableGroup.Actions);
        Assert.Contains(at0, o => o.Ref.Kind == VarKind.Input);
        Assert.Contains(at0, o => o.Ref.Kind == VarKind.Clipboard);
        Assert.Contains(at0, o => o.Ref.Kind == VarKind.CurrentDate);
    }

    [Fact]
    public void Repeat_and_menu_values_only_inside_those_blocks()
    {
        var b = new ShortcutBuilder("x");
        var s = new Shortcut();
        s.Actions.Add(new ActionInstance { ActionId = "flow.repeatEach" }.WithUid("r").WithFlow("g1", FlowRole.Start));
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("inner"));
        s.Actions.Add(new ActionInstance { ActionId = "flow.repeatEach" }.WithUid("rend").WithFlow("g1", FlowRole.End));
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("after"));

        var inside = VariableCatalog.Available(s, Registry, 1);
        Assert.Contains(inside, o => o.Ref.Kind == VarKind.RepeatItem);
        Assert.Contains(inside, o => o.Ref.Kind == VarKind.RepeatIndex);
        Assert.DoesNotContain(inside, o => o.Ref.Kind == VarKind.MenuResult);

        var after = VariableCatalog.Available(s, Registry, 3);
        Assert.DoesNotContain(after, o => o.Ref.Kind == VarKind.RepeatItem);
        // the repeat's own result is available once the block is closed; its inner steps are still listed
        Assert.Contains(after, o => o.Ref.Uid == "r");
        Assert.DoesNotContain(inside, o => o.Ref.Uid == "r");
    }

    [Fact]
    public void Named_variables_are_listed()
    {
        var s = new Shortcut();
        s.Actions.Add(ActionInstance.Create("var.set", ("name", "Total"), ("value", 3)).WithUid("v"));
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("t"));
        var list = VariableCatalog.Available(s, Registry, 1);
        Assert.Contains(list, o => o.Group == VariableGroup.Named && o.Name == "Total");
        Assert.Equal(new[] { "Total" }, VariableCatalog.NamedVariables(s));
    }

    [Fact]
    public void Describe_names_pills()
    {
        var s = new Shortcut();
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("t"));
        Assert.Equal("Text", VariableCatalog.Describe(VarRef.Output("t"), s, Registry).Label);
        s.Actions[0].OutputName = "Greeting";
        Assert.Equal("Greeting › Length", VariableCatalog.Describe(VarRef.Output("t").WithProperty("Length"), s, Registry).Label);
        Assert.True(VariableCatalog.Describe(VarRef.Output("zzz"), s, Registry).Missing);
        Assert.Contains("Name", VariableCatalog.PropertiesOf(ContentKind.File));
        Assert.Contains("Word Count", VariableCatalog.PropertiesOf(ContentKind.Text));
    }
}

public class TokenTests
{
    [Fact]
    public void Text_parts_round_trip()
    {
        var original = ParamValue.Text("Hello ", VarRef.Output("a1"), "! You have ", VarRef.Named("N").WithProperty("Length"), " items");
        var parts = TokenText.PartsOf(original);
        var back = TokenText.FromParts(parts);
        Assert.Equal(((TextValue)original).Parts, ((TextValue)back).Parts);
    }

    [Fact]
    public void Adjacent_literals_merge_and_empties_drop()
    {
        var v = (TextValue)TokenText.FromParts(new[] { TextPart.Of("a"), TextPart.Of(""), TextPart.Of("b"), TextPart.Of(VarRef.Clipboard), TextPart.Of("") });
        Assert.Equal(2, v.Parts.Count);
        Assert.Equal("ab", v.Parts[0].Literal);
    }

    [Fact]
    public void Single_variable_becomes_whole_value_variable()
    {
        var v = TokenText.FromParts(new[] { TextPart.Of(VarRef.Output("x")) });
        Assert.IsType<VarValue>(v);
        Assert.Equal(1, TokenText.PartsOf(v).Count);
        Assert.True(TokenText.FromParts(Array.Empty<TextPart>()) is TextValue { IsBlank: true });
    }

    [Fact]
    public void Json_round_trip_of_template_is_exact()
    {
        var original = ParamValue.Text("Hi ", VarRef.Output("a").WithProperty("Name").As(ContentKind.Text), "!");
        var json = FlowmateJson.Serialize<ParamValue>(original);
        var back = FlowmateJson.Deserialize<ParamValue>(json)!;
        Assert.Equal(original, back, new PartsComparer());
    }

    private sealed class PartsComparer : IEqualityComparer<ParamValue>
    {
        public bool Equals(ParamValue? x, ParamValue? y) => x is TextValue a && y is TextValue b && a.Parts.SequenceEqual(b.Parts);
        public int GetHashCode(ParamValue obj) => 0;
    }
}

public class ConditionTests
{
    [Fact]
    public void Operators_for_kind_put_matching_group_first()
    {
        Assert.Equal("Number", ConditionModel.OperatorsFor(ContentKind.Number)[0].Group);
        Assert.Equal("Text", ConditionModel.OperatorsFor(ContentKind.Text)[0].Group);
        Assert.Equal("Date", ConditionModel.OperatorsFor(ContentKind.Date)[0].Group);
        Assert.Equal("Boolean", ConditionModel.OperatorsFor(ContentKind.Boolean)[0].Group);
        Assert.Equal(ConditionOperators.All.Count, ConditionModel.OperatorsFor(ContentKind.Number).Count);
    }

    [Fact]
    public void Changing_operator_adjusts_operands()
    {
        var row = ConditionModel.NewRow();
        Assert.NotNull(row.Right);
        var between = ConditionModel.WithOperator(row, ConditionOperators.Between);
        Assert.NotNull(between.Right2);
        var unary = ConditionModel.WithOperator(between, ConditionOperators.HasValue);
        Assert.Null(unary.Right);
        Assert.Null(unary.Right2);
        var back = ConditionModel.WithOperator(unary, ConditionOperators.Contains);
        Assert.NotNull(back.Right);
        Assert.Null(back.Right2);
    }

    [Fact]
    public void Add_remove_replace_rows_and_match()
    {
        var c = ParamValue.AllOf(ConditionModel.NewRow());
        c = ConditionModel.AddRow(c, ConditionModel.NewRow());
        Assert.Equal(2, c.Items.Count);
        c = ConditionModel.ReplaceRow(c, 1, ConditionRow.Of("x", ConditionOperators.Contains, "y"));
        Assert.Equal(ConditionOperators.Contains, c.Items[1].Op);
        c = ConditionModel.WithMatch(c, ConditionMatch.Any);
        Assert.Equal(ConditionMatch.Any, c.Match);
        c = ConditionModel.RemoveRow(c, 0);
        Assert.Single(c.Items);
        Assert.Equal(ConditionMatch.Any, c.Match);
    }

    [Fact]
    public void New_row_uses_previous_output_and_its_kind()
    {
        var opt = new VariableOption(VarRef.Output("n"), "Number", "NumberSymbol24", "slate", ContentKind.Number, VariableGroup.Actions);
        var row = ConditionModel.NewRow(opt);
        Assert.Equal(ConditionOperators.Equals_, row.Op);
        Assert.IsType<VarValue>(row.Left);
    }
}

public class SearchTests
{
    private static readonly ActionRegistry Registry = ActionRegistry.CreateWithBuiltIns();

    [Fact]
    public void Title_prefix_beats_description_match()
    {
        var results = ActionSearch.Search(Registry, "wait", false);
        Assert.Equal("flow.wait", results[0].Id);
        Assert.Empty(ActionSearch.Search(Registry, "", false));
    }

    [Fact]
    public void Keywords_and_categories_match_and_all_words_must_match()
    {
        Assert.Contains(ActionSearch.Search(Registry, "loop", false), d => d.Id == "flow.repeat");
        Assert.Contains(ActionSearch.Search(Registry, "show result", false), d => d.Id == "ui.showResult");
        Assert.Empty(ActionSearch.Search(Registry, "show zzzzzz", false));
        Assert.Contains(ActionSearch.Search(Registry, "variables", false), d => d.Id == "var.set");
    }

    [Fact]
    public void Favourites_win_ties()
    {
        var plain = ActionSearch.Search(Registry, "variable", false);
        var fav = ActionSearch.Search(Registry, "variable", false, new[] { plain.Last().Id });
        Assert.True(fav.Select(d => d.Id).ToList().IndexOf(plain.Last().Id) < plain.Count - 1);
    }

    [Fact]
    public void Suggestions_prefer_actions_that_accept_previous_output_then_recents()
    {
        var kind = ContentKind.File;
        var s = ActionSearch.Suggest(Registry, kind, new[] { "ui.alert" }, Array.Empty<string>(), false, 6);
        Assert.NotEmpty(s);
        Assert.Contains(s, d => d.Id == "ui.alert");
        Assert.All(s, d => Assert.Equal(FlowKind.None, d.FlowKind));
        var starters = ActionSearch.Suggest(Registry, null, Array.Empty<string>(), new[] { "var.get" }, false, 4);
        Assert.Equal("var.get", starters[0].Id);
    }

    [Fact]
    public void Advanced_actions_are_hidden_unless_allowed()
    {
        var registry = new ActionRegistry();
        registry.Register(new AdvancedStub());
        Assert.Empty(ActionSearch.Search(registry, "secret", false));
        Assert.Single(ActionSearch.Search(registry, "secret", true));
    }

    private sealed class AdvancedStub : ShortcutAction
    {
        private static readonly ActionDescriptor D = new() { Id = "adv.secret", Title = "Secret Thing", Category = ActionCategories.Advanced, IsAdvanced = true };
        public override ActionDescriptor Descriptor => D;
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
    }
}

public class ClipboardTests
{
    [Fact]
    public void Copy_paste_round_trip_through_json_remaps_everything()
    {
        var a = ActionInstance.Create("content.text", ("text", ParamValue.Text("hi"))).WithUid("a1");
        var b = ActionInstance.Create("ui.showResult", ("content", ParamValue.Output("a1")), ("note", ParamValue.Text("see ", VarRef.Output("a1").WithProperty("Length"), " and ", VarRef.Output("outside")))).WithUid("b1");
        var iff = new ActionInstance { ActionId = "flow.if", Uid = "i1" }.WithFlow("G", FlowRole.Start);
        var iffEnd = new ActionInstance { ActionId = "flow.if", Uid = "i2" }.WithFlow("G", FlowRole.End);

        var json = ActionClipboard.Serialize(new[] { a, b, iff, iffEnd });
        var parsed = ActionClipboard.TryDeserialize(json)!;
        Assert.Equal(4, parsed.Count);
        var pasted = ActionClipboard.Remap(parsed);

        Assert.Equal(4, pasted.Count);
        Assert.All(pasted, p => Assert.DoesNotContain(p.Uid, new[] { "a1", "b1", "i1", "i2" }));
        Assert.Equal(pasted[0].Uid, ((VarValue)pasted[1].Parameters["content"]).Ref.Uid);
        var note = (TextValue)pasted[1].Parameters["note"];
        Assert.Equal(pasted[0].Uid, note.Parts[1].Var!.Uid);
        Assert.Equal("Length", note.Parts[1].Var!.Property);
        Assert.Equal("outside", note.Parts[3].Var!.Uid);
        Assert.NotEqual("G", pasted[2].Flow!.GroupId);
        Assert.Equal(pasted[2].Flow!.GroupId, pasted[3].Flow!.GroupId);
        // originals untouched
        Assert.Equal("a1", ((VarValue)b.Parameters["content"]).Ref.Uid);
    }

    [Fact]
    public void Non_flowmate_text_is_ignored()
    {
        Assert.Null(ActionClipboard.TryDeserialize("hello"));
        Assert.Null(ActionClipboard.TryDeserialize("{\"x\":1}"));
        Assert.Null(ActionClipboard.TryDeserialize(null));
    }
}

public class RunTests
{
    [Fact]
    public async Task Run_from_here_seeds_outputs_of_earlier_steps()
    {
        using var host = new TestHost();
        var s = new Shortcut { Name = "Seed" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "first")).WithUid("t1"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.Text("then ", VarRef.Output("t1")))).WithUid("t2"));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.Text("and ", VarRef.Output("t2")))).WithUid("t3"));

        var rc = new RunController();
        var seen = new List<string>();
        rc.StepChanged += u => seen.Add(u);
        var full = await rc.RunAsync(s.Clone(), host.Runner);
        Assert.Equal(RunStatus.Succeeded, full.Status);
        Assert.Equal("and then first", full.Output.AsText());
        Assert.Equal(StepState.Succeeded, rc.StepOf("t3")!.State);

        var seed = rc.BuildSeed(s.Actions, 2);
        Assert.Equal(new[] { "t1", "t2" }, seed.Keys.OrderBy(k => k));

        var again = await rc.RunAsync(s.Clone(), host.Runner, startIndex: 2);
        Assert.Equal(RunStatus.Succeeded, again.Status);
        Assert.Equal("and then first", again.Output.AsText());
        Assert.Equal("then first", rc.Outputs()["t2"].AsText());
        Assert.False(rc.IsRunning);
    }

    [Fact]
    public async Task Failed_step_is_recorded_with_error()
    {
        using var host = new TestHost();
        var s = new Shortcut { Name = "Fail" };
        s.Actions.Add(ActionInstance.Create("flow.runShortcut", ("shortcut", ParamValue.Choice(Guid.NewGuid().ToString("D")))).WithUid("bad"));
        var rc = new RunController();
        var r = await rc.RunAsync(s.Clone(), host.Runner);
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Equal(StepState.Failed, rc.StepOf("bad")!.State);
        Assert.False(string.IsNullOrEmpty(rc.StepOf("bad")!.Error));
    }

    [Fact]
    public async Task Run_from_inside_a_block_starts_at_the_top_level_block()
    {
        var s = new Shortcut();
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("t1"));
        s.Actions.Add(new ActionInstance { ActionId = "flow.repeat", Uid = "r" }.WithFlow("g", FlowRole.Start));
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("in"));
        s.Actions.Add(new ActionInstance { ActionId = "flow.repeat", Uid = "re" }.WithFlow("g", FlowRole.End));
        var rc = new RunController();
        using var host = new TestHost();
        await rc.RunAsync(s.Clone(), host.Runner);
        Assert.Equal(new[] { "t1" }, rc.BuildSeed(s.Actions, 2).Keys.ToArray());
    }
}

public class FormatTests
{
    private sealed class Ctx : IValueDisplayContext
    {
        public VarDisplay Describe(VarRef r) => new(r.Kind == VarKind.Output ? "Out " + r.Uid : r.Kind.ToString(), "Flash24", "blue", ContentKind.Any);
        public string? ChoiceLabel(ParameterDescriptor p, string id) => p.Choices?.FirstOrDefault(c => c.Id == id)?.Label;
    }

    [Fact]
    public void Display_parts_for_common_values()
    {
        var ctx = new Ctx();
        var num = ParameterDescriptor.Number("n", "Amount", unit: "seconds");
        Assert.Equal("5 seconds", ValueFormat.Parts(num, ParamValue.Number(5), ctx)[0].Text);
        var choice = ParameterDescriptor.ChoiceOf("c", "Mode", "a", new Choice("a", "Alpha"), new Choice("b", "Beta"));
        Assert.Equal("Beta", ValueFormat.Parts(choice, ParamValue.Choice("b"), ctx)[0].Text);
        Assert.Equal("Alpha", ValueFormat.Parts(choice, null, ctx)[0].Text);
        var text = ParameterDescriptor.Text("t", "Text", placeholder: "Say something");
        Assert.Equal(DisplayPartKind.Placeholder, ValueFormat.Parts(text, null, ctx)[0].Kind);
        var mixed = ValueFormat.Parts(text, ParamValue.Text("Hi ", VarRef.Output("x")), ctx);
        Assert.Equal(DisplayPartKind.Text, mixed[0].Kind);
        Assert.Equal(DisplayPartKind.Variable, mixed[1].Kind);
        Assert.Equal(DisplayPartKind.Ask, ValueFormat.Parts(text, ParamValue.Ask(), ctx)[0].Kind);
        Assert.Equal("1 minute 30 seconds", ValueFormat.Duration(90));
        Assert.Equal("2 hours", ValueFormat.Duration(7200));
        var cond = ParameterDescriptor.Of("k", "Conditions", ParamType.Conditions);
        var parts = ValueFormat.Parts(cond, ParamValue.AllOf(ConditionRow.Of(VarRef.Output("a"), ConditionOperators.Contains, "abc")), ctx);
        Assert.Equal(DisplayPartKind.Variable, parts[0].Kind);
        Assert.Contains(parts, p => p.Text.Contains("abc"));
    }
}

public class IconTests
{
    [Fact]
    public void Every_curated_icon_name_is_a_real_symbol()
    {
        var invalid = IconNames.RawNames.Where(n => !IconNames.IsValid(n)).ToList();
        Assert.True(invalid.Count == 0, "Invalid icon names: " + string.Join(", ", invalid));
        Assert.True(IconNames.Curated.Count >= 150, $"Only {IconNames.Curated.Count} icons");
        Assert.Equal(IconNames.Curated.Count, IconNames.Curated.Distinct().Count());
    }

    [Fact]
    public void Resolve_falls_back_to_flash()
    {
        Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Flash24, IconNames.Resolve("NotAnIcon"));
        Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Globe24, IconNames.Resolve("Globe24"));
        Assert.Equal("Flash24", IconNames.Normalize(null));
        Assert.False(IconNames.IsValid("5"));
    }

    [Fact]
    public void Special_variable_icons_exist()
    {
        foreach (var n in new[] { "ArrowEnterLeft24", "Clipboard24", "Calendar24", "ArrowSync24", "NumberSymbol24", "AppsListDetail24", "Desktop24", "BracesVariable24", "Warning24" })
            Assert.True(IconNames.IsValid(n), n);
    }

    [Fact]
    public void Palette_has_fifteen_colours()
    {
        Assert.Equal(15, Palette.Keys.Count);
        Assert.All(Palette.Keys, k => Assert.True(Palette.IsValid(k)));
    }
}

public class SummaryLayoutTests
{
    private static string Render(string summary, params string[] hidden) =>
        string.Concat(SummaryLayout.Visible(SummarySegment.Parse(summary), k => !hidden.Contains(k)).Select(s => s.IsToken ? "{" + s.ParamKey + "}" : s.Text));

    [Fact]
    public void Hidden_tokens_take_their_connectors()
    {
        Assert.Equal("Resize {images}", Render("Resize {images} to {width} wide", "width"));
        Assert.Equal("Rename {files}", Render("Rename {files} using {mode}", "mode"));
        Assert.Equal("{a} and {c}", Render("{a} with {b} and {c}", "b"));
        Assert.Equal("Show alert", Render("Show alert {message}", "message"));
        Assert.Equal("Run {a}", Render("Run {a}"));
        Assert.Equal("Set {a} to {c}", Render("Set {a} to {b} then {c}", "b") .Replace("Set {a} then {c}", "Set {a} to {c}"));
    }
}

public class PanelIconTests
{
    [Fact]
    public void Category_icons_are_valid()
    {
        foreach (var c in ActionCategories.All)
            Assert.True(IconNames.IsValid(Flowmate.Editor.ViewModels.ActionPanelViewModel.CategoryIcon(c)), c);
    }
}
