namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>Steps that control Desk Pal over its control pipe. Desk Pal must be running; it is never started from here.</summary>
public abstract class DeskPalAction : ShortcutAction
{
    internal static string Note => Strings.Get("DeskPalAction_Note");

    protected static ActionDescriptor Make(string id, string title, string summary, string description, string[] keywords, params ParameterDescriptor[] parameters) => new()
    {
        Id = "deskpal." + id, Title = title, Category = ActionCategories.AppsAndWindows, Icon = "PlugConnected24",
        Keywords = new[] { "desk pal", "deskpal", "wellbeing" }.Concat(keywords).ToArray(),
        Summary = summary, Description = description + Note, HasOutput = false, Parameters = parameters,
    };
}

public sealed class DeskPalStartBreakAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("break", Strings.Get("DeskPalStartBreakAction_Title"), Strings.Get("DeskPalStartBreakAction_Summary"),
        Strings.Get("DeskPalStartBreakAction_Description"), new[] { "break", "stretch", "water", "eyes", "walk" },
        ParameterDescriptor.ChoiceOf("kind", Strings.Get("DeskPalStartBreakAction_Param_Kind_Label"), "stretch",
            new Choice("stretch", Strings.Get("DeskPalStartBreakAction_Param_Kind_Choice_Stretch")), new Choice("water", Strings.Get("DeskPalStartBreakAction_Param_Kind_Choice_Water")), new Choice("eyes", Strings.Get("DeskPalStartBreakAction_Param_Kind_Choice_Eyes")), new Choice("walk", Strings.Get("DeskPalStartBreakAction_Param_Kind_Choice_Walk"))));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.StartBreakAsync(await ctx.GetChoiceAsync("kind", "stretch"), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalFocusOnAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("focusOn", Strings.Get("DeskPalFocusOnAction_Title"), Strings.Get("DeskPalFocusOnAction_Summary"),
        Strings.Get("DeskPalFocusOnAction_Description"),
        new[] { "focus", "do not disturb", "concentrate" },
        ParameterDescriptor.Number("minutes", Strings.Get("DeskPalFocusOnAction_Param_Minutes_Label"), 25, 5, 120, "minutes"));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.FocusOnAsync(await ctx.GetNumberAsync("minutes", 25), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalFocusOffAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("focusOff", Strings.Get("DeskPalFocusOffAction_Title"), Strings.Get("DeskPalFocusOffAction_Summary"),
        Strings.Get("DeskPalFocusOffAction_Description"), new[] { "focus", "stop focus" });
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.SendAsync("focus.off", null, true, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalPauseAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("pause", Strings.Get("DeskPalPauseAction_Title"), Strings.Get("DeskPalPauseAction_Summary"),
        Strings.Get("DeskPalPauseAction_Description"),
        new[] { "pause", "snooze", "hold reminders" },
        ParameterDescriptor.Number("minutes", Strings.Get("DeskPalPauseAction_Param_Minutes_Label"), 30, 5, 480, "minutes"));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.PauseAsync(await ctx.GetNumberAsync("minutes", 30), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalResumeAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("resume", Strings.Get("DeskPalResumeAction_Title"), Strings.Get("DeskPalResumeAction_Summary"),
        Strings.Get("DeskPalResumeAction_Description"), new[] { "resume", "unpause", "continue" });
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.SendAsync("resume", null, true, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class GetDeskPalStatusAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "deskpal.status", Title = Strings.Get("GetDeskPalStatusAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "PlugConnected24",
        Keywords = new[] { "desk pal", "deskpal", "breaks", "focus", "pause", "goals", "wellbeing" },
        Summary = Strings.Get("GetDeskPalStatusAction_Summary"),
        Description = Strings.Get("GetDeskPalStatusAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("GetDeskPalStatusAction_OutputName"),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ToContent(await DeskPalLink.GetStatusAsync(ctx.CancellationToken).ConfigureAwait(false));

    internal static ContentValue ToContent(DeskPalStatus s)
    {
        static ContentValue T(string? v) => v is null ? ContentValue.Empty : ContentValue.Text(v);
        static ContentValue N(int? v) => v is { } n ? ContentValue.Number(n) : ContentValue.Empty;
        static ContentValue D(DateTimeOffset? v) => v is { } d ? ContentValue.Date(d) : ContentValue.Empty;
        return ContentValue.Dict(new KeyValuePair<string, ContentValue>[]
        {
            new("State", T(s.State)),
            new("Focus On", ContentValue.Bool(s.FocusActive)),
            new("Focus Ends", D(s.FocusEndsAt)),
            new("Paused Until", D(s.PausedUntil)),
            new("Next Break", T(s.NextNudgeKind)),
            new("Next Break At", D(s.NextNudgeAt)),
            new("Stretches Today", N(s.Stretches)),
            new("Eye Breaks Today", N(s.EyeBreaks)),
            new("Walks Today", N(s.Walks)),
            new("Stretch Goal", N(s.StretchGoal)),
            new("Eye Break Goal", N(s.EyeBreakGoal)),
            new("Walk Goal", N(s.WalkGoal)),
            new("Control Allowed", ContentValue.Bool(s.AllowExternalControl)),
            new("Version", T(s.Version)),
            new("Edition", T(s.Edition)),
        }, s.State ?? "Desk Pal");
    }
}
