using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class AnswerKindTests
{
    private sealed class TypedAnswerAction : ShortcutAction
    {
        public override ActionDescriptor Descriptor { get; } = new()
        {
            Id = "test.typed", Title = "Typed", Category = ActionCategories.Ai, OutputName = "Answer",
            OutputKindFor = a => a.Parameters.TryGetValue("as", out var v) && v is ChoiceValue { Value: "yesno" } ? ContentKind.Boolean : ContentKind.Text,
            Parameters = new[] { ParameterDescriptor.ChoiceOf("as", "As", "text", new Choice("text", "text"), new Choice("yesno", "Yes / No")) },
        };
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
    }

    [Fact]
    public void Picker_pill_and_condition_follow_the_steps_own_output_kind()
    {
        var registry = ActionRegistry.CreateWithBuiltIns();
        registry.Register(new TypedAnswerAction());
        var s = new Shortcut();
        s.Actions.Add(ActionInstance.Create("test.typed", ("as", ParamValue.Choice("yesno"))).WithUid("yn"));
        s.Actions.Add(ActionInstance.Create("test.typed").WithUid("plain"));
        s.Actions.Add(ActionInstance.Create("content.text").WithUid("end"));

        var options = VariableCatalog.Available(s, registry, 2).Where(o => o.Group == VariableGroup.Actions).ToDictionary(o => o.Ref.Uid!);
        Assert.Equal(ContentKind.Boolean, options["yn"].Kind);
        Assert.Equal(ContentKind.Text, options["plain"].Kind);
        Assert.Equal(ContentKind.Boolean, VariableCatalog.Describe(options["yn"].Ref, s, registry).Kind);
        Assert.Equal("Boolean", ConditionModel.GroupFor(options["yn"].Kind));
    }
}
