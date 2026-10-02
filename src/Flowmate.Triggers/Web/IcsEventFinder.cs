using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Flowmate.Triggers;

/// <summary>One occurrence of a calendar event (recurring events give one per repeat).</summary>
public sealed record CalendarOccurrence(string Uid, string Title, DateTimeOffset Start, DateTimeOffset End, string? Location, string? Description, bool AllDay)
{
    /// <summary>Stable id of this particular occurrence, used so each one only fires once.</summary>
    public string Key => $"{Uid}@{Start.UtcDateTime:O}";
}

/// <summary>Reads an .ics calendar (as published by Google, Outlook.com, iCloud …) and lists event occurrences, recurrences included.</summary>
public static class IcsEventFinder
{
    public static IReadOnlyList<CalendarOccurrence> GetOccurrences(string ics, DateTimeOffset from, DateTimeOffset to)
    {
        var calendar = Calendar.Load(ics);
        if (calendar is null) return Array.Empty<CalendarOccurrence>();
        var result = new List<CalendarOccurrence>();
        var window = new CalDateTime(from.UtcDateTime, "UTC");
        foreach (var o in calendar.GetOccurrences<CalendarEvent>(window))
        {
            if (o.Source is not CalendarEvent ev) continue;
            var start = ToOffset(o.Period.StartTime);
            if (start >= to) break;
            if (start < from.AddDays(-1)) continue;
            var end = o.Period.EndTime is { } e ? ToOffset(e) : start;
            result.Add(new CalendarOccurrence(ev.Uid ?? ev.Summary ?? "event", string.IsNullOrWhiteSpace(ev.Summary) ? "Calendar event" : ev.Summary,
                start, end, Blank(ev.Location), Blank(ev.Description), ev.IsAllDay));
        }
        return result.OrderBy(r => r.Start).ToList();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTimeOffset ToOffset(CalDateTime d)
    {
        if (d.IsFloating) return new DateTimeOffset(DateTime.SpecifyKind(d.Value, DateTimeKind.Local));
        return d.AsUtc;
    }

    /// <summary>
    /// Occurrences whose reminder moment (start minus <paramref name="minutesBefore"/>) has arrived but whose start hasn't,
    /// and that haven't fired yet.
    /// </summary>
    public static IReadOnlyList<CalendarOccurrence> Due(IEnumerable<CalendarOccurrence> occurrences, DateTimeOffset now, int minutesBefore, ISet<string> alreadyFired)
    {
        return occurrences
            .Where(o => !o.AllDay && o.Start > now && o.Start - TimeSpan.FromMinutes(minutesBefore) <= now && !alreadyFired.Contains(o.Key))
            .OrderBy(o => o.Start).ToList();
    }
}
