namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>Steps that control Desk Pal over its control pipe. Desk Pal must be running; it is never started from here.</summary>
public abstract class DeskPalAction : ShortcutAction
{
    internal const string Note = " Desk Pal must be running with \"Allow Automation Portal and other apps to control Desk Pal\" turned on in its Settings. The first time, Desk Pal asks whether to allow Automation Portal.";

    protected static ActionDescriptor Make(string id, string title, string summary, string description, string[] keywords, params ParameterDescriptor[] parameters) => new()
    {
        Id = "deskpal." + id, Title = title, Category = ActionCategories.AppsAndWindows, Icon = "PlugConnected24",
        Keywords = new[] { "desk pal", "deskpal", "wellbeing" }.Concat(keywords).ToArray(),
        Summary = summary, Description = description + Note, HasOutput = false, Parameters = parameters,
    };
}

public sealed class DeskPalStartBreakAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("break", "Desk Pal: Start a Break", "Start a {kind} break in Desk Pal",
        "Shows Desk Pal's break card now, even outside working hours or during a pause.", new[] { "break", "stretch", "water", "eyes", "walk" },
        ParameterDescriptor.ChoiceOf("kind", "Kind of break", "stretch",
            new Choice("stretch", "stretch"), new Choice("water", "water"), new Choice("eyes", "eye"), new Choice("walk", "walk")));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.StartBreakAsync(await ctx.GetChoiceAsync("kind", "stretch"), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalFocusOnAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("focusOn", "Desk Pal: Focus On", "Turn on focus in Desk Pal for {minutes}",
        "Holds Desk Pal's reminders back for a while, from 5 to 120 minutes. If focus is already on, it starts again with the new length. Reminders that came due show when it ends.",
        new[] { "focus", "do not disturb", "concentrate" },
        ParameterDescriptor.Number("minutes", "Minutes", 25, 5, 120, "minutes"));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.FocusOnAsync(await ctx.GetNumberAsync("minutes", 25), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalFocusOffAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("focusOff", "Desk Pal: Focus Off", "Turn off focus in Desk Pal",
        "Ends Desk Pal's focus time now. Nothing happens if focus is off.", new[] { "focus", "stop focus" });
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.SendAsync("focus.off", null, true, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalPauseAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("pause", "Desk Pal: Pause", "Pause Desk Pal for {minutes}",
        "Pauses Desk Pal's reminders, from 5 to 480 minutes, like Pause in its tray menu. If Desk Pal is already paused, the new time replaces the old one.",
        new[] { "pause", "snooze", "hold reminders" },
        ParameterDescriptor.Number("minutes", "Minutes", 30, 5, 480, "minutes"));
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await DeskPalLink.PauseAsync(await ctx.GetNumberAsync("minutes", 30), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class DeskPalResumeAction : DeskPalAction
{
    private static readonly ActionDescriptor D = Make("resume", "Desk Pal: Resume", "Resume Desk Pal",
        "Ends a pause in Desk Pal now, like Resume in its tray menu. Nothing happens if it isn't paused.", new[] { "resume", "unpause", "continue" });
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
        Id = "deskpal.status", Title = "Get Desk Pal Status", Category = ActionCategories.AppsAndWindows, Icon = "PlugConnected24",
        Keywords = new[] { "desk pal", "deskpal", "breaks", "focus", "pause", "goals", "wellbeing" },
        Summary = "Get the Desk Pal status",
        Description = "Reads what Desk Pal is doing now: its state (active, focus, paused, break, do not disturb or off hours), when focus or a pause ends, the next break, today's stretches, eye breaks and walks, and the day's goals. Desk Pal must be running; this works even when control from other apps is off.",
        OutputKind = ContentKind.Dictionary, OutputName = "Desk Pal Status",
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
