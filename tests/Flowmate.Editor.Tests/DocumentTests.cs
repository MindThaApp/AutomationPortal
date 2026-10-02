using Flowmate.Core;
using Flowmate.Editor.Logic;
using Xunit;

namespace Flowmate.Editor.Tests;

public class DocumentTests
{
    private static readonly ActionRegistry Registry = ActionRegistry.CreateWithBuiltIns();

    private static EditorDocument NewDoc() => new(new Shortcut { Name = "T" }, Registry);

    private static ActionDescriptor D(string id) => Registry.Describe(id)!;

    private static List<string> Ids(EditorDocument d) => d.Actions.Select(a => a.ActionId + (a.Flow is null ? "" : ":" + a.Flow.Role)).ToList();

    [Fact]
    public void Insert_plain_actions_in_order()
    {
        var doc = NewDoc();
        doc.InsertAction(D("content.text"), 0);
        doc.InsertAction(D("ui.showResult"), 1);
        doc.InsertAction(D("content.number"), 1);
        Assert.Equal(new[] { "content.text", "content.number", "ui.showResult" }, doc.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public void Insert_if_creates_whole_group_and_menu_has_two_options()
    {
        var doc = NewDoc();
        doc.InsertAction(D("flow.if"), 0);
        Assert.Equal(new[] { "flow.if:Start", "flow.if:Middle", "flow.if:End" }, Ids(doc));
        doc.InsertAction(D("flow.menu"), 3);
        Assert.Equal(4, doc.Actions.Skip(3).Count());
        Assert.Equal(new[] { "Option 1", "Option 2" }, doc.Actions.Where(a => a.Flow is { Role: FlowRole.Middle } f && a.ActionId == "flow.menu").Select(a => a.Flow!.Label));
        var items = (ListValue)doc.Actions[3].Parameters["items"];
        Assert.Equal(2, items.Items.Count);
        Assert.Empty(ShortcutValidator.Validate(doc.Shortcut, Registry).Where(i => i.Severity == IssueSeverity.Error && i.Code is "brokenGroup"));
    }

    [Fact]
    public void Cannot_insert_between_menu_start_and_first_option()
    {
        var doc = NewDoc();
        doc.InsertAction(D("flow.menu"), 0);
        var made = doc.InsertAction(D("content.text"), 1);
        Assert.Equal(2, doc.IndexOf(made[0]));
        Assert.Equal(FlowRole.Middle, doc.Actions[1].Flow!.Role);
        Assert.DoesNotContain(ShortcutValidator.Validate(doc.Shortcut, Registry), i => i.Code == "brokenGroup");
    }

    [Fact]
    public void Delete_start_removes_whole_group_with_children()
    {
        var doc = NewDoc();
        doc.InsertAction(D("content.text"), 0);
        var group = doc.InsertAction(D("flow.repeat"), 1);
        doc.InsertAction(D("content.number"), 2);          // inside the repeat
        doc.InsertAction(D("ui.showResult"), 5);
        Assert.Equal(5, doc.Actions.Count);
        doc.Delete(new[] { group[0] });
        Assert.Equal(new[] { "content.text", "ui.showResult" }, doc.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public void Delete_end_bar_also_removes_the_group()
    {
        var doc = NewDoc();
        var group = doc.InsertAction(D("flow.if"), 0);
        doc.Delete(new[] { group[^1] });
        Assert.Empty(doc.Actions);
    }

    [Fact]
    public void Delete_menu_option_removes_its_bar_and_actions_and_syncs_items()
    {
        var doc = NewDoc();
        var menu = doc.InsertAction(D("flow.menu"), 0);        // Start, Opt1, Opt2, End
        doc.InsertAction(D("content.text"), 2);                // inside option 1
        Assert.Equal(5, doc.Actions.Count);
        doc.Delete(new[] { menu[1] });
        Assert.Equal(3, doc.Actions.Count);
        Assert.Equal(new[] { "Option 2" }, ((ListValue)doc.Actions[0].Parameters["items"]).Items.Select(i => i.ToLiteralText()));
    }

    [Fact]
    public void Move_group_keeps_children_together_and_is_group_aware()
    {
        var doc = NewDoc();
        var a = doc.InsertAction(D("content.text"), 0)[0];
        var rep = doc.InsertAction(D("flow.repeat"), 1);       // Start, End
        var inner = doc.InsertAction(D("content.number"), 2)[0];
        var last = doc.InsertAction(D("ui.showResult"), 4)[0];
        Assert.Equal(new[] { a, rep[0], inner, rep[1], last }, doc.Actions);

        Assert.True(doc.Move(new[] { rep[0] }, 0));            // move the whole repeat above "a"
        Assert.Equal(new[] { rep[0], inner, rep[1], a, last }, doc.Actions);

        Assert.True(doc.Move(new[] { rep[1] }, 5));            // moving the End bar moves the whole group to the end
        Assert.Equal(new[] { a, last, rep[0], inner, rep[1] }, doc.Actions);
    }

    [Fact]
    public void Move_into_itself_is_refused()
    {
        var doc = NewDoc();
        var rep = doc.InsertAction(D("flow.repeat"), 0);
        doc.InsertAction(D("content.text"), 1);
        Assert.False(doc.Move(new[] { rep[0] }, 2));
        Assert.Equal(3, doc.Actions.Count);
    }

    [Fact]
    public void Move_action_into_group_nests_it()
    {
        var doc = NewDoc();
        var x = doc.InsertAction(D("content.text"), 0)[0];
        var rep = doc.InsertAction(D("flow.repeat"), 1);
        Assert.True(doc.Move(new[] { x }, 2));                 // between Start and End of the repeat
        Assert.Equal(1, FlowGroups.Depths(doc.Actions)[1]);
        Assert.Equal(x, doc.Actions[1]);
    }

    [Fact]
    public void MoveBy_swaps_with_sibling_group()
    {
        var doc = NewDoc();
        var a = doc.InsertAction(D("content.text"), 0)[0];
        var rep = doc.InsertAction(D("flow.repeat"), 1);
        Assert.True(doc.MoveBy(new[] { a }, +1));
        Assert.Equal(new[] { rep[0], rep[1], a }, doc.Actions);
        Assert.True(doc.MoveBy(new[] { a }, -1));
        Assert.Equal(new[] { a, rep[0], rep[1] }, doc.Actions);
        Assert.False(doc.MoveBy(new[] { a }, -1));
    }

    [Fact]
    public void Undo_redo_restores_structure_and_parameters()
    {
        var doc = NewDoc();
        var t = doc.InsertAction(D("content.text"), 0)[0];
        doc.SetParameter(t, "text", ParamValue.Text("hello"));
        doc.InsertAction(D("ui.showResult"), 1);
        Assert.Equal(2, doc.Actions.Count);

        Assert.True(doc.Undo());
        Assert.Single(doc.Actions);
        Assert.Equal("hello", doc.Actions[0].Parameters["text"].ToLiteralText());
        Assert.True(doc.Undo());
        Assert.False(doc.Actions[0].Parameters.ContainsKey("text"));
        Assert.True(doc.Redo());
        Assert.Equal("hello", doc.Actions[0].Parameters["text"].ToLiteralText());
        Assert.True(doc.Redo());
        Assert.Equal(2, doc.Actions.Count);
        Assert.False(doc.Redo());
    }

    [Fact]
    public void Typing_in_one_field_is_one_undo_step()
    {
        var doc = NewDoc();
        var t = doc.InsertAction(D("content.text"), 0)[0];
        foreach (var s in new[] { "h", "he", "hel", "hell", "hello" }) doc.SetParameter(t, "text", ParamValue.Text(s));
        doc.Undo();
        Assert.False(doc.Actions[0].Parameters.ContainsKey("text"));
    }

    [Fact]
    public void Duplicate_remaps_uids_groups_and_internal_references()
    {
        var doc = NewDoc();
        var text = doc.InsertAction(D("content.text"), 0)[0];
        var show = doc.InsertAction(D("ui.showResult"), 1)[0];
        doc.SetParameter(show, "content", ParamValue.Output(text.Uid));
        var copies = doc.Duplicate(new[] { text, show });
        Assert.Equal(4, doc.Actions.Count);
        Assert.Equal(2, copies.Count);
        Assert.All(copies, c => Assert.DoesNotContain(c.Uid, new[] { text.Uid, show.Uid }));
        var refVar = ((VarValue)copies[1].Parameters["content"]).Ref;
        Assert.Equal(copies[0].Uid, refVar.Uid);
        Assert.Empty(ShortcutValidator.Validate(doc.Shortcut, Registry).Where(i => i.Code is "duplicateUid" or "removedVariable"));
    }

    [Fact]
    public void Add_menu_item_and_otherwise()
    {
        var doc = NewDoc();
        var menu = doc.InsertAction(D("flow.menu"), 0);
        var bar = doc.AddMenuItem(menu[0].Flow!.GroupId)!;
        Assert.Equal("Option 3", bar.Flow!.Label);
        Assert.Equal(FlowRole.End, doc.Actions[^1].Flow!.Role);
        Assert.Equal(3, ((ListValue)doc.Actions[0].Parameters["items"]).Items.Count);
        doc.SetMenuLabel(bar, "Pizza");
        Assert.Equal("Pizza", ((ListValue)doc.Actions[0].Parameters["items"]).Items[2].ToLiteralText());

        var iff = doc.InsertAction(D("flow.if"), doc.Actions.Count);
        doc.Delete(new[] { iff[1] });
        Assert.Null(doc.Actions.FirstOrDefault(a => a.Flow is { Role: FlowRole.Middle } && a.ActionId == "flow.if"));
        Assert.NotNull(doc.AddOtherwise(iff[0].Flow!.GroupId));
        Assert.Null(doc.AddOtherwise(iff[0].Flow!.GroupId));
    }

    [Fact]
    public void Depths_nest_correctly()
    {
        var doc = NewDoc();
        var outer = doc.InsertAction(D("flow.repeat"), 0);
        var inner = doc.InsertAction(D("flow.if"), 1);
        doc.InsertAction(D("content.text"), 2);
        var depths = FlowGroups.Depths(doc.Actions);
        Assert.Equal(new[] { 0, 1, 2, 1, 1, 0 }, depths);
        Assert.Equal(0, FlowGroups.TopLevelStart(doc.Actions, 3));
        Assert.Equal((1, 4), FlowGroups.GroupRange(doc.Actions, 1));
    }
}
