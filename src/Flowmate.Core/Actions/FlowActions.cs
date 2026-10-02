namespace Flowmate.Core.Actions;

public sealed class CommentAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Comment, Title = "Comment", Category = ActionCategories.Scripting, Icon = "Comment24",
        Keywords = new[] { "note", "remark", "explain" },
        Summary = "{text}",
        Description = "Adds a note to explain your shortcut. It doesn't do anything when the shortcut runs.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.MultilineText("text", "Comment", placeholder: "Write a note…") with { AllowVariables = false, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ctx.Input);
}

public sealed class IfAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.If, Title = "If", Category = ActionCategories.Scripting, Icon = "BranchFork24", FlowKind = FlowKind.If,
        Keywords = new[] { "condition", "else", "otherwise", "check", "when" },
        Summary = "If {conditions}",
        Description = "Runs the actions below only when the conditions are met. Add an Otherwise part for when they aren't.",
        OutputName = "If Result",
        Parameters = new[] { new ParameterDescriptor { Key = "conditions", Label = "Conditions", Type = ParamType.Conditions, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class MenuAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Menu, Title = "Choose from Menu", Category = ActionCategories.Scripting, Icon = "AppsListDetail24", FlowKind = FlowKind.Menu,
        Keywords = new[] { "menu", "choose", "options", "pick", "switch" },
        Summary = "Choose from menu {prompt}",
        Description = "Shows a menu and runs the actions under the option you pick. Use Menu Result to get the chosen option.",
        OutputName = "Menu Result",
        Parameters = new[]
        {
            ParameterDescriptor.Text("prompt", "Prompt", "What do you want to do?"),
            ParameterDescriptor.ListParam("items", "Menu items") with { AllowAskEachTime = false, Hint = "The option labels. The editor keeps these in sync with the option bars." },
        },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class RepeatAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Repeat, Title = "Repeat", Category = ActionCategories.Scripting, Icon = "ArrowRepeatAll24", FlowKind = FlowKind.Repeat,
        Keywords = new[] { "loop", "times", "again" },
        Summary = "Repeat {count} times",
        Description = "Runs the actions inside again and again. Repeat Index tells you which time it is.",
        OutputKind = ContentKind.Any, OutputName = "Repeat Results",
        Parameters = new[] { ParameterDescriptor.Number("count", "Times", 5, min: 0, max: ShortcutRunner.MaxLoopIterations) },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class RepeatEachAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.RepeatEach, Title = "Repeat with Each", Category = ActionCategories.Scripting, Icon = "ArrowSync24", FlowKind = FlowKind.RepeatEach,
        Keywords = new[] { "loop", "for each", "every", "items", "list" },
        Summary = "Repeat with each item in {items}",
        Description = "Runs the actions inside once for every item in a list. Repeat Item is the current item.",
        OutputName = "Repeat Results",
        Parameters = new[] { ParameterDescriptor.Content("items", "List", isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class WaitAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Wait, Title = "Wait", Category = ActionCategories.Scripting, Icon = "Timer24",
        Keywords = new[] { "pause", "delay", "sleep", "seconds" },
        Summary = "Wait {seconds}",
        Description = "Pauses the shortcut for a while.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Duration("seconds", "Time", 1) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var time = await ctx.GetDurationAsync("seconds", TimeSpan.FromSeconds(1));
        if (time > TimeSpan.Zero) await Task.Delay(time, ctx.CancellationToken);
        return ctx.Input;
    }
}

public sealed class StopAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Stop, Title = "Stop This Shortcut", Category = ActionCategories.Scripting, Icon = "Stop24",
        Keywords = new[] { "end", "exit", "quit", "halt", "return" },
        Summary = "Stop this shortcut",
        Description = "Ends the shortcut right here. Nothing below it runs.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => throw new StopShortcutException();
}

public sealed class StopAndOutputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Output, Title = "Stop and Output", Category = ActionCategories.Scripting, Icon = "Send24",
        Keywords = new[] { "return", "result", "end", "finish" },
        Summary = "Stop and output {output}",
        Description = "Ends the shortcut and hands back a result to whatever started it.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("output", "Output", isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        throw new StopShortcutException(await ctx.GetContentAsync("output"));
}

public sealed class RunShortcutAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.RunShortcut, Title = "Run Shortcut", Category = ActionCategories.Scripting, Icon = "Play24",
        Keywords = new[] { "call", "nested", "launch", "start" },
        Summary = "Run {shortcut} with {input}",
        Description = "Runs another shortcut and gets its result.",
        OutputName = "Shortcut Result",
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "shortcut", Label = "Shortcut", Type = ParamType.Shortcut, Required = true,
                ChoicesProvider = (services, _) => Task.FromResult<IReadOnlyList<Choice>>(
                    services.Library.All.Select(s => new Choice(s.Id.ToString("D"), s.Name, s.Icon)).ToList()),
            },
            ParameterDescriptor.Content("input", "Input", isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var key = await ctx.GetChoiceAsync("shortcut");
        if (string.IsNullOrWhiteSpace(key)) throw new ActionException("Choose which shortcut to run.");
        var library = ctx.Services.Library;
        var target = Guid.TryParse(key, out var id) ? library.GetById(id) : library.FindByName(key);
        target ??= library.FindByName(key);
        if (target is null) throw new ActionException($"Couldn't find the shortcut \"{key}\". It may have been renamed or deleted.");

        var input = await ctx.GetContentAsync("input");
        var result = await ctx.Services.Runner.RunAsync(target, input, new RunOptions
        {
            Source = RunSource.Nested, Depth = ctx.Depth + 1, CancellationToken = ctx.CancellationToken, RecordHistory = false,
        });
        return result.Status switch
        {
            RunStatus.Failed => throw new ActionException($"The shortcut \"{target.Name}\" failed: {result.ErrorMessage}"),
            RunStatus.Cancelled => throw new UserCancelledException(),
            RunStatus.Stopped => ContentValue.Empty,
            _ => result.Output,
        };
    }
}

public sealed class NothingAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Nothing, Title = "Nothing", Category = ActionCategories.Scripting, Icon = "Prohibited24",
        Keywords = new[] { "empty", "blank", "nil", "null", "clear" },
        Summary = "Do nothing",
        Description = "Does nothing and passes along nothing. Handy to clear the input or fill an empty branch.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
}
