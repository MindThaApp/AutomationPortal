namespace AutomationPortal.Actions.Documents;

internal static class ReminderHelpers
{
    public static Guid? ResolveShortcut(ActionContext ctx, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (Guid.TryParse(v, out var id)) return id;
        var found = ctx.Services.Library.FindByName(v);
        return found?.Id ?? throw new ActionException(Strings.Format("ReminderHelpers_Error_CouldnTFindA", v));
    }
}

public sealed class RemindMeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "rem.add", Title = Strings.Get("RemindMeAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "Alert24",
        Keywords = new[] { "reminder", "alarm", "notify", "later", "alert" },
        Summary = Strings.Get("RemindMeAction_Summary"),
        Description = Strings.Get("RemindMeAction_Description"),
        OutputKind = ContentKind.Date, OutputName = Strings.Get("RemindMeAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", Strings.Get("RemindMeAction_Param_title_Label"), null, Strings.Get("RemindMeAction_Param_title_Placeholder")) with { Required = true },
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("RemindMeAction_Param_mode_Label"), "in", new Choice("in", Strings.Get("RemindMeAction_Param_mode_Choice_in")), new Choice("at", Strings.Get("RemindMeAction_Param_mode_Choice_at"))),
            ParameterDescriptor.Of("when", Strings.Get("RemindMeAction_Param_when_Label"), ParamType.DateTime) with { VisibleWhen = new VisibleWhen("mode", "at") },
            ParameterDescriptor.Duration("after", Strings.Get("RemindMeAction_Param_after_Label"), 600) with { VisibleWhen = new VisibleWhen("mode", "in") },
            ParameterDescriptor.MultilineText("notes", Strings.Get("RemindMeAction_Param_notes_Label")),
            ParameterDescriptor.Of("shortcut", Strings.Get("RemindMeAction_Param_shortcut_Label"), ParamType.Shortcut),
            ParameterDescriptor.Bool("sound", Strings.Get("RemindMeAction_Param_sound_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var title = (await ctx.GetTextAsync("title")).Trim();
        if (title.Length == 0) throw new ActionException(Strings.Get("RemindMeAction_Error_TypeWhatYouWant"));
        var mode = await ctx.GetChoiceAsync("mode", "in");
        DateTimeOffset when;
        if (mode == "at")
        {
            when = await ctx.GetDateAsync("when") ?? throw new ActionException(Strings.Get("RemindMeAction_Error_ChooseTheDateAnd"));
            if (when <= DateTimeOffset.Now.AddSeconds(-1))
                throw new ActionException(Strings.Get("RemindMeAction_Error_ThatTimeHasAlready"));
        }
        else
        {
            var after = await ctx.GetDurationAsync("after", TimeSpan.FromMinutes(10));
            if (after <= TimeSpan.Zero) throw new ActionException(Strings.Get("RemindMeAction_Error_ChooseHowLongFrom"));
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
        Id = "rem.timer", Title = Strings.Get("StartTimerAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "Timer24",
        Keywords = new[] { "countdown", "pomodoro", "stopwatch", "minutes", "alarm" },
        Summary = Strings.Get("StartTimerAction_Summary"),
        Description = Strings.Get("StartTimerAction_Description"),
        OutputKind = ContentKind.Date, OutputName = Strings.Get("StartTimerAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Duration("duration", Strings.Get("StartTimerAction_Param_duration_Label"), 300) with { Required = true },
            ParameterDescriptor.Text("label", Strings.Get("StartTimerAction_Param_label_Label"), "Timer"),
            ParameterDescriptor.Of("shortcut", Strings.Get("StartTimerAction_Param_shortcut_Label"), ParamType.Shortcut),
            ParameterDescriptor.Bool("sound", Strings.Get("StartTimerAction_Param_sound_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var length = await ctx.GetDurationAsync("duration", TimeSpan.FromMinutes(5));
        if (length <= TimeSpan.Zero) throw new ActionException(Strings.Get("StartTimerAction_Error_ChooseHowLongThe"));
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
        Id = "rem.list", Title = Strings.Get("ListRemindersAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "AlertOn24",
        Keywords = new[] { "reminders", "timers", "list", "upcoming", "scheduled" },
        Summary = Strings.Get("ListRemindersAction_Summary"),
        Description = Strings.Get("ListRemindersAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("ListRemindersAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Number("days", Strings.Get("ListRemindersAction_Param_days_Label"), 0, 0, 3650, "days") with { Hint = Strings.Get("ListRemindersAction_Param_days_Hint") },
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
