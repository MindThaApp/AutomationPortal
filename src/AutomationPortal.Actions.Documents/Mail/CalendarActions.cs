using System.Diagnostics;

namespace Flowmate.Actions.Documents;

public sealed class AddCalendarEventAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "cal.addEvent", Title = "Add Calendar Event", Category = ActionCategories.NotesAndReminders, Icon = "CalendarAdd24",
        Keywords = new[] { "event", "appointment", "meeting", "outlook", "ics", "schedule" },
        Summary = "Add the event {title} on {start}",
        Description = "Adds an event to your calendar. With classic Outlook installed it goes straight into your Outlook calendar; otherwise Flow Mate opens an .ics file in your default calendar app so you can confirm it.",
        OutputKind = ContentKind.Dictionary, OutputName = "Calendar Event",
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", "Title", null, "e.g. Team lunch") with { Required = true },
            ParameterDescriptor.Of("start", "Starts", ParamType.DateTime) with { Required = true },
            ParameterDescriptor.Duration("length", "Length", 3600),
            ParameterDescriptor.Bool("allDay", "All day", false),
            ParameterDescriptor.Text("location", "Place"),
            ParameterDescriptor.MultilineText("notes", "Notes"),
            ParameterDescriptor.Number("alert", "Remind me before", 15, 0, 10080, "minutes") with { Hint = "0 means no reminder." },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var title = (await ctx.GetTextAsync("title")).Trim();
        if (title.Length == 0) throw new ActionException("Type a title for the event.");
        var start = (await ctx.GetDateAsync("start") ?? throw new ActionException("Choose when the event starts.")).LocalDateTime;
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
                ctx.ReportProgress("Added to your Outlook calendar");
                return summary;
            }
            catch (ActionException ex)
            {
                ctx.Log($"Outlook couldn't add the event ({ex.Message}); opening a calendar file instead.");
            }
        }

        var ics = IcsBuilder.Build(title, start, end, allDay, location, notes, alert, DateTime.UtcNow);
        var folder = Path.Combine(Path.GetTempPath(), "Flowmate", "events");
        Directory.CreateDirectory(folder);
        CleanOld(folder);
        var path = OutputTarget.UniquePath(folder, ActionContext.SanitizeFileName(title) + ".ics");
        await File.WriteAllTextAsync(path, ics, new System.Text.UTF8Encoding(false), ctx.CancellationToken);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException("Couldn't open the calendar event. Choose a calendar app for .ics files in Windows Settings › Default apps.", ex);
        }
        ctx.ReportProgress("Opened the event in your calendar app");
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
        Id = "cal.getEvents", Title = "Get Calendar Events", Category = ActionCategories.NotesAndReminders, Icon = "CalendarDate24",
        Keywords = new[] { "agenda", "schedule", "outlook", "meetings", "today" },
        Summary = "Get events for the next {days} days",
        Description = "Reads upcoming events from your calendar. This only works with classic Outlook installed — the new Outlook for Windows can't be read.",
        OutputKind = ContentKind.Dictionary, OutputName = "Calendar Events",
        Parameters = new[]
        {
            ParameterDescriptor.Number("days", "Days", 7, 1, 366, "days"),
            ParameterDescriptor.Of("from", "Starting", ParamType.Date) with { Hint = "Leave empty to start from today." },
            ParameterDescriptor.Number("max", "At most", 50, 1, 500, "events"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        if (!await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
            throw new ActionException("Reading calendar events needs classic Outlook, which isn't installed on this PC. (The new Outlook for Windows can't be read by other apps.)");
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
