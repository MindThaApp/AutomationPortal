namespace AutomationPortal.Actions.Documents;

internal static class ReminderHelpers
{
    public static Guid? ResolveShortcut(ActionContext ctx, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (Guid.TryParse(v, out var id)) return id;
        var found = ctx.Services.Library.FindByName(v);
        return found?.Id ?? throw new ActionException($"Couldn't find a shortcut called '{v}'. Check the name.");
    }
}

public sealed class RemindMeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "rem.add", Title = "Remind Me", Category = ActionCategories.NotesAndReminders, Icon = "Alert24",
        Keywords = new[] { "reminder", "alarm", "notify", "later", "alert" },
        Summary = "Remind me {title} {mode} {when}{after}",
        Description = "Shows a notification at a time you choose, or after a number of minutes. It can also run a shortcut when the time comes. Reminders are kept even if you close Automation Portal.",
        OutputKind = ContentKind.Date, OutputName = "Reminder Time",
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", "Reminder", null, "e.g. Call the dentist") with { Required = true },
            ParameterDescriptor.ChoiceOf("mode", "When", "in", new Choice("in", "in"), new Choice("at", "at")),
            ParameterDescriptor.Of("when", "Date and time", ParamType.DateTime) with { VisibleWhen = new VisibleWhen("mode", "at") },
            ParameterDescriptor.Duration("after", "Time from now", 600) with { VisibleWhen = new VisibleWhen("mode", "in") },
            ParameterDescriptor.MultilineText("notes", "More details"),
            ParameterDescriptor.Of("shortcut", "Also run this shortcut", ParamType.Shortcut),
            ParameterDescriptor.Bool("sound", "Play a sound", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var title = (await ctx.GetTextAsync("title")).Trim();
        if (title.Length == 0) throw new ActionException("Type what you want to be reminded about.");
        var mode = await ctx.GetChoiceAsync("mode", "in");
        DateTimeOffset when;
        if (mode == "at")
        {
            when = await ctx.GetDateAsync("when") ?? throw new ActionException("Choose the date and time for the reminder.");
            if (when <= DateTimeOffset.Now.AddSeconds(-1))
                throw new ActionException("That time has already passed. Choose a time in the future.");
        }
        else
        {
            var after = await ctx.GetDurationAsync("after", TimeSpan.FromMinutes(10));
            if (after <= TimeSpan.Zero) throw new ActionException("Choose how long from now the reminder should appear.");
            when = DateTimeOffset.Now + after;
        }
        var reminder = ctx.Services.Reminders.Schedule(new Reminder
        {
            When = when,
            Title = title,
            Body = NullIfBlank(await ctx.GetTextAsync("notes")),
            ShortcutIdToRun = ReminderHelpers.ResolveShortcut(ctx, await ctx.GetTextAsync("shortcut")),
            Sound = await ctx.GetBoolAsync("sound", true),
        });
        return ContentValue.Date(reminder.When);
    }

    internal static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

public sealed class StartTimerAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "rem.timer", Title = "Start Timer", Category = ActionCategories.NotesAndReminders, Icon = "Timer24",
        Keywords = new[] { "countdown", "pomodoro", "stopwatch", "minutes", "alarm" },
        Summary = "Start a timer for {duration}",
        Description = "Starts a countdown in the background and shows a notification when it ends. The shortcut carries on straight away — use Wait if you want it to pause.",
        OutputKind = ContentKind.Date, OutputName = "Timer End",
        Parameters = new[]
        {
            ParameterDescriptor.Duration("duration", "Length", 300) with { Required = true },
            ParameterDescriptor.Text("label", "Name", "Timer"),
            ParameterDescriptor.Of("shortcut", "Also run this shortcut", ParamType.Shortcut),
            ParameterDescriptor.Bool("sound", "Play a sound", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var length = await ctx.GetDurationAsync("duration", TimeSpan.FromMinutes(5));
        if (length <= TimeSpan.Zero) throw new ActionException("Choose how long the timer should run.");
        var label = (await ctx.GetTextAsync("label", "Timer")).Trim();
        if (label.Length == 0) label = "Timer";
        var reminder = ctx.Services.Reminders.Schedule(new Reminder
        {
            When = DateTimeOffset.Now + length,
            Title = label,
            Body = "Time's up!",
            ShortcutIdToRun = ReminderHelpers.ResolveShortcut(ctx, await ctx.GetTextAsync("shortcut")),
            Sound = await ctx.GetBoolAsync("sound", true),
        });
        return ContentValue.Date(reminder.When);
    }
}

public sealed class ListRemindersAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "rem.list", Title = "Get Upcoming Reminders", Category = ActionCategories.NotesAndReminders, Icon = "AlertOn24",
        Keywords = new[] { "reminders", "timers", "list", "upcoming", "scheduled" },
        Summary = "Get upcoming reminders",
        Description = "Lists the reminders and timers that haven't gone off yet, soonest first.",
        OutputKind = ContentKind.Dictionary, OutputName = "Reminders",
        Parameters = new[]
        {
            ParameterDescriptor.Number("days", "Only the next", 0, 0, 3650, "days") with { Hint = "0 means all of them." },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var days = await ctx.GetNumberAsync("days");
        var list = ctx.Services.Reminders.List().AsEnumerable();
        if (days > 0) list = list.Where(r => r.When <= DateTimeOffset.Now.AddDays(days));
        return ContentValue.FromItems(list.OrderBy(r => r.When).Select(r => (ContentItem)new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
        {
            new("Title", ContentValue.Text(r.Title)),
            new("When", ContentValue.Date(r.When)),
            new("Details", ContentValue.Text(r.Body ?? "")),
        })));
    }
}
