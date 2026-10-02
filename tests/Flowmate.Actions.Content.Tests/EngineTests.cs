using Flowmate.Actions.Content.Actions;
using Flowmate.Core;
using Flowmate.Core.Testing;
using static Flowmate.Actions.Content.Tests.Kit;

namespace Flowmate.Actions.Content.Tests;

/// <summary>Whole shortcuts through the real engine: implicit input, magic variables, loops, errors.</summary>
public class EngineTests
{
    private static async Task<RunResult> Run(Action<ShortcutBuilder> build, ContentValue? input = null)
    {
        using var host = new TestHost().With(typeof(CombineTextAction).Assembly);
        var b = new ShortcutBuilder("Test");
        build(b);
        return await host.Runner.RunAsync(b.Build(), input ?? ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
    }

    [Fact]
    public async Task Split_sort_combine_chain_uses_implicit_input()
    {
        var r = await Run(b =>
        {
            b.Add("content.text", ("text", "pear\napple\nmango"));
            b.Add("text.split");
            b.Add("list.sort");
            b.Add("text.combine", ("separator", C("commas")));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("apple, mango, pear", r.Output.AsText());
    }

    [Fact]
    public async Task Phrase_to_date_to_text()
    {
        var r = await Run(b =>
        {
            b.Add("content.text", ("text", "Dentist tomorrow at 3pm"));
            b.Add("date.parse");
            b.Add("date.format", ("format", C("custom")), ("custom", "HH:mm"));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("15:00", r.Output.AsText());
    }

    [Fact]
    public async Task Json_to_value_to_number()
    {
        var r = await Run(b =>
        {
            b.Add("content.text", ("text", "{\"city\":\"Rome\",\"temp\":21}"));
            b.Add("dict.fromInput");
            b.Add("dict.getValue", ("key", "temp"));
            b.Add("num.calculate", ("operation", C("add")), ("operand", 1));
        });
        Assert.Equal(22, r.Output.AsNumber());
    }

    [Fact]
    public async Task Magic_variables_and_repeat_with_each()
    {
        var r = await Run(b =>
        {
            b.Add("content.text", ("text", "1\n2\n3"));
            var split = b.Add("text.split");
            b.RepeatEach(ParamValue.Output(split), inner =>
                inner.Add("num.calculate", ("number", ParamValue.Var(VarRef.RepeatItem)), ("operation", C("multiply")), ("operand", 2)));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(new double[] { 2, 4, 6 }, r.Output.AsNumbers());
    }

    [Fact]
    public async Task Property_of_a_magic_variable_flows_into_text_parameters()
    {
        var r = await Run(b =>
        {
            var dict = b.Add("dict.fromInput", ("input", "{\"name\":\"Sam\"}"));
            b.Add("text.case", ("text", ParamValue.Text("hello ", VarRef.Output(dict).WithProperty("key:name"), "!")), ("mode", C("upper")));
        });
        Assert.Equal("HELLO SAM!", r.Output.AsText());
    }

    [Fact]
    public async Task Spreadsheet_round_trip_in_a_shortcut()
    {
        var r = await Run(b =>
        {
            b.Add("content.text", ("text", "[{\"name\":\"Zed\",\"qty\":3},{\"name\":\"Amy\",\"qty\":12},{\"name\":\"Bob\",\"qty\":7}]"));
            b.Add("dict.fromInput");
            b.Add("sheet.makeCsv", ("output", C("text")));
            b.Add("sheet.read");
            b.Add("list.sort", ("order", C("propertyDesc")), ("property", "key:qty"));
            b.Add("list.getItem", ("which", C("first")));
            b.Add("dict.getValue", ("key", "name"));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("Amy", r.Output.AsText());
    }

    [Fact]
    public async Task A_failing_action_stops_the_shortcut_with_its_friendly_message_or_continues_when_asked()
    {
        var failing = await Run(b =>
        {
            b.Add("num.expression", ("expression", "2+"));
            b.Add("content.text", ("text", "never"));
        });
        Assert.Equal(RunStatus.Failed, failing.Status);
        Assert.Contains("ends too early", failing.ErrorMessage);

        var continuing = await Run(b =>
        {
            b.Add(ActionInstance.Create("num.expression", ("expression", "1/0")).OnErrorContinue());
            b.Add("content.text", ("text", "still here"));
        });
        Assert.Equal(RunStatus.Succeeded, continuing.Status);
        Assert.Equal("still here", continuing.Output.AsText());
    }

    [Fact]
    public async Task Ask_each_time_prompts_through_the_host()
    {
        using var host = new TestHost().With(typeof(CombineTextAction).Assembly);
        host.UI.Enqueue("2 + 2 * 5");
        var s = new ShortcutBuilder("Ask");
        s.Add("num.expression", ("expression", ParamValue.Ask()));
        var r = await host.Runner.RunAsync(s.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(12, r.Output.AsNumber());
        Assert.Contains("ask", host.UI.Calls);
    }

    [Fact]
    public async Task Shortcut_input_reaches_the_first_action()
    {
        var r = await Run(b => b.Add("text.case", ("mode", C("upper"))), ContentValue.Text("quiet please"));
        Assert.Equal("QUIET PLEASE", r.Output.AsText());
    }

    [Fact]
    public async Task All_my_actions_validate_in_a_shortcut_when_required_values_are_filled()
    {
        using var host = new TestHost().With(typeof(CombineTextAction).Assembly);
        var b = new ShortcutBuilder("Validate");
        b.Add("text.combine", ("text", "x"));
        b.Add("num.convertUnits", ("value", 1));
        b.Add("web.search", ("query", "x"), ("open", false));
        var issues = ShortcutValidator.Validate(b.Build(), host.Registry);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }
}
