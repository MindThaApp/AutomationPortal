namespace AutomationPortal.Core.Actions;

public sealed class CommentAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Comment, Title = Strings.Get("Action_Common_Title"), Category = ActionCategories.Scripting, Icon = "Comment24",
        Keywords = new[] { "note", "remark", "explain" },
        Summary = Strings.Get("Action_Common_Summary"),
        Description = Strings.Get("Action_Common_Description"),
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.MultilineText("text", Strings.Get("Action_Common_Param_Text_Label"), placeholder: "Write a note…") with { AllowVariables = false, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ctx.Input);
}

public sealed class IfAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.If, Title = Strings.Get("Action_Common_Title_2"), Category = ActionCategories.Scripting, Icon = "BranchFork24", FlowKind = FlowKind.If,
        Keywords = new[] { "condition", "else", "otherwise", "check", "when" },
        Summary = Strings.Get("Action_Common_Summary_2"),
        Description = Strings.Get("Action_Common_Description_2"),
        OutputName = Strings.Get("Action_Common_OutputName"),
        Parameters = new[] { new ParameterDescriptor { Key = "conditions", Label = Strings.Get("Action_Common_Param_Conditions_Label"), Type = ParamType.Conditions, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class MenuAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Menu, Title = Strings.Get("Action_Common_Title_3"), Category = ActionCategories.Scripting, Icon = "AppsListDetail24", FlowKind = FlowKind.Menu,
        Keywords = new[] { "menu", "choose", "options", "pick", "switch" },
        Summary = Strings.Get("Action_Common_Summary_3"),
        Description = Strings.Get("Action_Common_Description_3"),
        OutputName = Strings.Get("Action_Common_OutputName_2"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("prompt", Strings.Get("Action_Common_Param_Prompt_Label"), "What do you want to do?"),
            ParameterDescriptor.ListParam("items", Strings.Get("Action_Common_Param_Items_Label")) with { AllowAskEachTime = false, Hint = Strings.Get("Action_Common_Hint") },
        },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class RepeatAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Repeat, Title = Strings.Get("Action_Common_Title_4"), Category = ActionCategories.Scripting, Icon = "ArrowRepeatAll24", FlowKind = FlowKind.Repeat,
        Keywords = new[] { "loop", "times", "again" },
        Summary = Strings.Get("Action_Common_Summary_4"),
        Description = Strings.Get("Action_Common_Description_4"),
        OutputKind = ContentKind.Any, OutputName = Strings.Get("Action_Common_OutputName_3"),
        Parameters = new[] { ParameterDescriptor.Number("count", Strings.Get("Action_Common_Param_Count_Label"), 5, min: 0, max: ShortcutRunner.MaxLoopIterations) },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class RepeatEachAction : FlowControlAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.RepeatEach, Title = Strings.Get("Action_Common_Title_5"), Category = ActionCategories.Scripting, Icon = "ArrowSync24", FlowKind = FlowKind.RepeatEach,
        Keywords = new[] { "loop", "for each", "every", "items", "list" },
        Summary = Strings.Get("Action_Common_Summary_5"),
        Description = Strings.Get("Action_Common_Description_5"),
        OutputName = Strings.Get("Action_Common_OutputName_3"),
        Parameters = new[] { ParameterDescriptor.Content("items", Strings.Get("Action_Common_Param_Items_Label_2"), isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;
}

public sealed class WaitAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Wait, Title = Strings.Get("Action_Common_Title_6"), Category = ActionCategories.Scripting, Icon = "Timer24",
        Keywords = new[] { "pause", "delay", "sleep", "seconds" },
        Summary = Strings.Get("Action_Common_Summary_6"),
        Description = Strings.Get("Action_Common_Description_6"),
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Duration("seconds", Strings.Get("Action_Common_Param_Seconds_Label"), 1) },
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
        Id = FlowIds.Stop, Title = Strings.Get("Action_Common_Title_7"), Category = ActionCategories.Scripting, Icon = "Stop24",
        Keywords = new[] { "end", "exit", "quit", "halt", "return" },
        Summary = Strings.Get("Action_Common_Summary_7"),
        Description = Strings.Get("Action_Common_Description_7"),
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => throw new StopShortcutException();
}

public sealed class StopAndOutputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.Output, Title = Strings.Get("Action_Common_Title_8"), Category = ActionCategories.Scripting, Icon = "Send24",
        Keywords = new[] { "return", "result", "end", "finish" },
        Summary = Strings.Get("Action_Common_Summary_8"),
        Description = Strings.Get("Action_Common_Description_8"),
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("output", Strings.Get("Action_Common_Param_Output_Label"), isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        throw new StopShortcutException(await ctx.GetContentAsync("output"));
}

public sealed class RunShortcutAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = FlowIds.RunShortcut, Title = Strings.Get("Action_Common_Title_9"), Category = ActionCategories.Scripting, Icon = "Play24",
        Keywords = new[] { "call", "nested", "launch", "start" },
        Summary = Strings.Get("Action_Common_Summary_9"),
        Description = Strings.Get("Action_Common_Description_9"),
        OutputName = Strings.Get("Action_Common_OutputName_4"),
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "shortcut", Label = Strings.Get("Action_Common_Param_Shortcut_Label"), Type = ParamType.Shortcut, Required = true,
                ChoicesProvider = (services, _) => Task.FromResult<IReadOnlyList<Choice>>(
                    services.Library.All.Select(s => new Choice(s.Id.ToString("D"), s.Name, s.Icon)).ToList()),
            },
            ParameterDescriptor.Content("input", Strings.Get("Action_Common_Param_Input_Label"), isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var key = await ctx.GetChoiceAsync("shortcut");
        if (string.IsNullOrWhiteSpace(key)) throw new ActionException(Strings.Get("FlowActions_ChooseWhichShortcutTo"));
        var library = ctx.Services.Library;
        var target = Guid.TryParse(key, out var id) ? library.GetById(id) : library.FindByName(key);
        target ??= library.FindByName(key);
        if (target is null) throw new ActionException(Strings.Format("Action_Flow_Error_NoShortcut", key));

        var input = await ctx.GetContentAsync("input");
        var result = await ctx.Services.Runner.RunAsync(target, input, new RunOptions
        {
            Source = RunSource.Nested, Depth = ctx.Depth + 1, CancellationToken = ctx.CancellationToken, RecordHistory = false,
            SharedTempFolder = ctx.TempFolder, SharedJournal = ctx.Journal,
        });
        return result.Status switch
        {
            RunStatus.Failed => throw new ActionException(Strings.Format("Action_Flow_Error_Failed", target.Name, result.ErrorMessage)),
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
        Id = FlowIds.Nothing, Title = Strings.Get("Action_Common_Title_10"), Category = ActionCategories.Scripting, Icon = "Prohibited24",
        Keywords = new[] { "empty", "blank", "nil", "null", "clear" },
        Summary = Strings.Get("Action_Common_Summary_10"),
        Description = Strings.Get("Action_Common_Description_10"),
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
}
