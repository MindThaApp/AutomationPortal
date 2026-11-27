using System.Diagnostics;

namespace AutomationPortal.Actions.Documents;

public sealed class AddCalendarEventAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "cal.addEvent", Title = Strings.Get("AddCalendarEventAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "CalendarAdd24",
        Keywords = new[] { "event", "appointment", "meeting", "outlook", "ics", "schedule" },
        Summary = Strings.Get("AddCalendarEventAction_Summary"),
        Description = Strings.Get("AddCalendarEventAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("AddCalendarEventAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", Strings.Get("AddCalendarEventAction_Param_title_Label"), null, Strings.Get("AddCalendarEventAction_Param_title_Placeholder")) with { Required = true },
            ParameterDescriptor.Of("start", Strings.Get("AddCalendarEventAction_Param_start_Label"), ParamType.DateTime) with { Required = true },
            ParameterDescriptor.Duration("length", Strings.Get("AddCalendarEventAction_Param_length_Label"), 3600),
            ParameterDescriptor.Bool("allDay", Strings.Get("AddCalendarEventAction_Param_allDay_Label"), false),
            ParameterDescriptor.Text("location", Strings.Get("AddCalendarEventAction_Param_location_Label")),
            ParameterDescriptor.MultilineText("notes", Strings.Get("AddCalendarEventAction_Param_notes_Label")),
            ParameterDescriptor.Number("alert", Strings.Get("AddCalendarEventAction_Param_alert_Label"), 15, 0, 10080, "minutes") with { Hint = Strings.Get("AddCalendarEventAction_Param_alert_Hint") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var title = (await ctx.GetTextAsync("title")).Trim();
        if (title.Length == 0) throw new ActionException(Strings.Get("AddCalendarEventAction_Error_TypeATitleFor"));
        var start = (await ctx.GetDateAsync("start") ?? throw new ActionException(Strings.Get("AddCalendarEventAction_Error_ChooseWhenTheEvent"))).LocalDateTime;
        var allDay = await ctx.GetBoolAsync("allDay");
        var length = await ctx.GetDurationAsync("length", TimeSpan.FromHours(1));
        if (length <= TimeSpan.Zero) length = TimeSpan.FromHours(1);
        var end = start + length;
        var location = await ctx.GetTextAsync("location");
        var notes = await ctx.GetTextAsync("notes");
        var alert = (int)await ctx.GetNumberAsync("alert", 15);

        var summary = ContentValue.Dict(
            ("Title", ContentValue.Text(title)),
            ("Starts", ContentValue.Date(new DateTimeOffset(allDay ? start.Date : start))),
            ("Ends", ContentValue.Date(new DateTimeOffset(allDay ? start.Date.AddDays(1) : end))));

        if (await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
        {
            try
            {
                await StaWorker.Com.RunAsync(() => OutlookBridge.AddEvent(title, start, end, allDay, location, notes, alert), ctx.CancellationToken);
                ctx.ReportProgress(Strings.Get("AddCalendarEventAction_Progress_AddedToYourOutlook"));
                return summary;
            }
            catch (ActionException ex)
            {
                ctx.Log($"Outlook couldn't add the event ({ex.Message}); opening a calendar file instead.");
            }
        }

        var ics = IcsBuilder.Build(title, start, end, allDay, location, notes, alert, DateTime.UtcNow);
        var folder = Path.Combine(UserFolders.TempPath(), "AutomationPortal", "events");
        Directory.CreateDirectory(folder);
        CleanOld(folder);
        var path = OutputTarget.UniquePath(folder, ActionContext.SanitizeFileName(title) + ".ics");
        await File.WriteAllTextAsync(path, ics, new System.Text.UTF8Encoding(false), ctx.CancellationToken);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException(Strings.Get("AddCalendarEventAction_Error_CouldnTOpenThe"), ex);
        }
        ctx.ReportProgress(Strings.Get("AddCalendarEventAction_Progress_OpenedTheEventIn"));
        return summary;
    }

    private static void CleanOld(string folder)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*.ics"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-2)) File.Delete(f);
        }
        catch { /* best effort */ }
    }
}

public sealed class GetCalendarEventsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "cal.getEvents", Title = Strings.Get("GetCalendarEventsAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "CalendarDate24",
        Keywords = new[] { "agenda", "schedule", "outlook", "meetings", "today" },
        Summary = Strings.Get("GetCalendarEventsAction_Summary"),
        Description = Strings.Get("GetCalendarEventsAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("GetCalendarEventsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Number("days", Strings.Get("GetCalendarEventsAction_Param_days_Label"), 7, 1, 366, "days"),
            ParameterDescriptor.Of("from", Strings.Get("GetCalendarEventsAction_Param_from_Label"), ParamType.Date) with { Hint = Strings.Get("GetCalendarEventsAction_Param_from_Hint") },
            ParameterDescriptor.Number("max", Strings.Get("GetCalendarEventsAction_Param_max_Label"), 50, 1, 500, "events"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        if (!await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
            throw new ActionException(Strings.Get("GetCalendarEventsAction_Error_ReadingCalendarEventsNeeds"));
        var days = (int)Math.Max(1, await ctx.GetNumberAsync("days", 7));
        var from = (await ctx.GetDateAsync("from"))?.LocalDateTime.Date ?? DateTime.Today;
        var max = (int)Math.Max(1, await ctx.GetNumberAsync("max", 50));
        var events = await StaWorker.Com.RunAsync(() => OutlookBridge.GetEvents(from, from.AddDays(days), max), ctx.CancellationToken);
        return ContentValue.FromItems(events.Select(e => (ContentItem)new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
        {
            new("Title", ContentValue.Text(e.Title)),
            new("Starts", ContentValue.Date(new DateTimeOffset(e.Start))),
            new("Ends", ContentValue.Date(new DateTimeOffset(e.End))),
            new("All Day", ContentValue.Bool(e.AllDay)),
            new("Place", ContentValue.Text(e.Location)),
            new("Notes", ContentValue.Text(e.Notes)),
        })));
    }
}
