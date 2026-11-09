using System.Globalization;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class DateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.date", Title = "Date", Category = ActionCategories.Dates, Icon = "CalendarLtr24",
        Keywords = new[] { "day", "calendar", "specific date", "time" },
        Summary = "Date {date}",
        Description = "A date and time you choose. You can also type plain phrases such as \"tomorrow 3pm\" or \"next Friday\".",
        OutputKind = ContentKind.Date, OutputName = "Date",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "date", Label = "Date", Type = ParamType.DateTime, Required = true, Placeholder = "tomorrow 3pm" },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("date");
        if (content.IsEmpty) throw new ActionException("Choose a date, or type something like \"tomorrow 3pm\".");
        var all = ActionHelpers.ToDates(content).ToList();
        if (all.Count == 0)
            throw new ActionException($"Couldn't understand '{ContentFormat.Truncate(content.AsText(), 50)}' as a date. Try something like 2026-10-02, \"tomorrow 3pm\" or \"next Friday\".");
        return new ContentValue(all.Select(d => (ContentItem)new DateItem(d)));
    }
}

public sealed class CurrentDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.current", Title = "Current Date", Category = ActionCategories.Dates, Icon = "Clock24",
        Keywords = new[] { "now", "today", "time", "date" },
        Summary = "Get the current date and time",
        Description = "Gets today's date and the time right now.",
        OutputKind = ContentKind.Date, OutputName = "Current Date",
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Date(DateTimeOffset.Now));
}

public sealed class FormatDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.format", Title = "Format Date", Category = ActionCategories.Dates, Icon = "CalendarEdit24",
        Keywords = new[] { "show", "write", "iso", "relative", "short", "long", "custom", "weekday", "text" },
        Summary = "Format {date} as {format}",
        Description = "Turns a date into text: short, long, ISO, relative (\"in 3 days\") or your own layout.",
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Text, OutputName = "Formatted Date",
        Parameters = new[]
        {
            ParameterDescriptor.Content("date", "Date", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("format", "Format", "medium",
                new Choice("short", "short date"), new Choice("medium", "medium date"), new Choice("long", "long date"),
                new Choice("time", "time only"), new Choice("dateTime", "date and time"), new Choice("weekday", "weekday name"),
                new Choice("monthYear", "month and year"), new Choice("iso", "ISO 8601"), new Choice("isoDate", "ISO date (yyyy-MM-dd)"),
                new Choice("relative", "relative (in 3 days)"), new Choice("unix", "Unix time"), new Choice("custom", "custom layout")),
            ParameterDescriptor.Text("custom", "Custom layout", "yyyy-MM-dd", "yyyy-MM-dd HH:mm") with
            {
                VisibleWhen = new VisibleWhen("format", "custom"),
                Hint = "yyyy = year, MM = month number, MMMM = month name, dd = day, dddd = weekday name, HH = hour (24h), hh = hour (12h), mm = minutes, ss = seconds, tt = AM/PM.",
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var dates = ActionHelpers.ToDates(await ctx.GetContentAsync("date")).ToList();
        if (dates.Count == 0) throw new ActionException("There is no date to format. Choose a date or use the result of an earlier action.");
        var format = await ctx.GetChoiceAsync("format", "medium");
        var custom = await ctx.GetTextAsync("custom", "yyyy-MM-dd");
        var now = DateTimeOffset.Now;
        return ContentValue.Texts(dates.Select(d => Format(d, format, custom, now, CultureInfo.CurrentCulture)));
    }

    internal static string Format(DateTimeOffset date, string format, string custom, DateTimeOffset now, CultureInfo culture)
    {
        var d = date.ToLocalTime();
        switch (format)
        {
            case "short": return d.ToString("d", culture);
            case "medium": return d.ToString("d MMM yyyy", culture);
            case "long": return d.ToString("D", culture);
            case "time": return d.ToString("t", culture);
            case "dateTime": return $"{d.ToString("d MMM yyyy", culture)}, {d.ToString("t", culture)}";
            case "weekday": return d.ToString("dddd", culture);
            case "monthYear": return d.ToString("MMMM yyyy", culture);
            case "iso": return d.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            case "isoDate": return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "relative": return DateParser.RelativeText(date, now);
            case "unix": return date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            case "custom":
                try { return d.ToString(custom.Length == 0 ? "yyyy-MM-dd" : custom, culture); }
                catch (FormatException) { throw new ActionException($"The layout '{custom}' isn't valid. Try something like yyyy-MM-dd or dddd d MMMM."); }
            default: return d.ToString("g", culture);
        }
    }
}

public sealed class AdjustDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.adjust", Title = "Adjust Date", Category = ActionCategories.Dates, Icon = "CalendarArrowRight24",
        Keywords = new[] { "add", "subtract", "plus", "minus", "start of", "end of", "week", "month", "year", "tomorrow", "yesterday" },
        Summary = "Adjust {date}: {operation} {amount} {unit}",
        Description = "Moves a date forwards or backwards, or jumps to the start or end of its day, week, month or year. If you don't give a date it uses right now.",
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Date, OutputName = "Adjusted Date",
        Parameters = new[]
        {
            ParameterDescriptor.Content("date", "Date", isInput: true),
            ParameterDescriptor.ChoiceOf("operation", "Operation", "add",
                new Choice("add", "Add"), new Choice("subtract", "Subtract"), new Choice("startOf", "Get the start of"), new Choice("endOf", "Get the end of")),
            ParameterDescriptor.Number("amount", "Amount", 1) with { VisibleWhen = new VisibleWhen("operation", "add", "subtract") },
            ParameterDescriptor.ChoiceOf("unit", "Unit", "days",
                new Choice("minutes", "minutes"), new Choice("hours", "hours"), new Choice("days", "days"),
                new Choice("weeks", "weeks"), new Choice("months", "months"), new Choice("years", "years")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("date");
        var dates = content.IsEmpty ? new List<DateTimeOffset> { DateTimeOffset.Now } : ActionHelpers.ToDates(content).ToList();
        if (dates.Count == 0) throw new ActionException($"Couldn't understand '{ContentFormat.Truncate(content.AsText(), 50)}' as a date.");
        var op = await ctx.GetChoiceAsync("operation", "add");
        var amount = await ctx.GetNumberAsync("amount", 1);
        var unit = await ctx.GetChoiceAsync("unit", "days");
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        return new ContentValue(dates.Select(d => (ContentItem)new DateItem(Adjust(d, op, amount, unit, firstDay))));
    }

    internal static DateTimeOffset Adjust(DateTimeOffset date, string op, double amount, string unit, DayOfWeek firstDayOfWeek)
    {
        switch (op)
        {
            case "add": return Shift(date, amount, unit);
            case "subtract": return Shift(date, -amount, unit);
            case "startOf": return StartOf(date, unit, firstDayOfWeek);
            case "endOf":
                {
                    var start = StartOf(date, unit, firstDayOfWeek);
                    return Shift(start, 1, unit).AddTicks(-1);
                }
            default: throw new ActionException("Choose what to do with the date.");
        }
    }

    private static DateTimeOffset Shift(DateTimeOffset d, double amount, string unit)
    {
        var whole = (int)Math.Truncate(amount);
        var frac = amount - whole;
        switch (unit)
        {
            case "minutes": return d.AddMinutes(amount);
            case "hours": return d.AddHours(amount);
            case "days": return Rebuild(d.DateTime.AddDays(amount), d);
            case "weeks": return Rebuild(d.DateTime.AddDays(amount * 7), d);
            case "months": return Rebuild(d.DateTime.AddMonths(whole).AddDays(frac * 30.4375), d);
            case "years": return Rebuild(d.DateTime.AddYears(whole).AddDays(frac * 365.25), d);
            default: throw new ActionException("Choose a unit such as days or months.");
        }
    }

    private static DateTimeOffset StartOf(DateTimeOffset d, string unit, DayOfWeek firstDay)
    {
        var local = d.DateTime;
        DateTime start;
        switch (unit)
        {
            case "minutes": start = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0); break;
            case "hours": start = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0); break;
            case "days": start = local.Date; break;
            case "weeks":
                var back = ((int)local.DayOfWeek - (int)firstDay + 7) % 7;
                start = local.Date.AddDays(-back); break;
            case "months": start = new DateTime(local.Year, local.Month, 1); break;
            case "years": start = new DateTime(local.Year, 1, 1); break;
            default: throw new ActionException("Choose a unit such as day or month.");
        }
        return Rebuild(start, d);
    }

    // Wall-clock arithmetic: dates in the machine's own time zone follow its daylight-saving rules.
    private static DateTimeOffset Rebuild(DateTime wallClock, DateTimeOffset original)
    {
        var tz = TimeZoneInfo.Local;
        var u = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (original.Offset == tz.GetUtcOffset(original) && !tz.IsInvalidTime(u)) return new DateTimeOffset(u, tz.GetUtcOffset(u));
        return new DateTimeOffset(u, original.Offset);
    }
}
public sealed class DateBetweenAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.between", Title = "Get Time Between Dates", Category = ActionCategories.Dates, Icon = "CalendarSettings24",
        Keywords = new[] { "difference", "duration", "days until", "age", "how long", "countdown", "elapsed" },
        Summary = "Get the {unit} between {from} and {to}",
        Description = "Works out how much time lies between two dates, in whole days, hours or other units, or as words like \"2 days, 3 hours\". The answer is negative if the second date is earlier.",
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Number, OutputName = "Time Between",
        Parameters = new[]
        {
            ParameterDescriptor.Content("from", "From date", isInput: true, required: true),
            ParameterDescriptor.Content("to", "To date (blank = now)"),
            ParameterDescriptor.ChoiceOf("unit", "Unit", "days",
                new Choice("seconds", "seconds"), new Choice("minutes", "minutes"), new Choice("hours", "hours"), new Choice("days", "days"),
                new Choice("weeks", "weeks"), new Choice("months", "months"), new Choice("years", "years"), new Choice("words", "in words")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var from = ActionHelpers.ToDate(await ctx.GetContentAsync("from"))
                   ?? throw new ActionException("There is no starting date. Choose a date or use the result of an earlier action.");
        var toContent = await ctx.GetContentAsync("to");
        var to = toContent.IsEmpty ? DateTimeOffset.Now
            : ActionHelpers.ToDate(toContent) ?? throw new ActionException($"Couldn't understand '{ContentFormat.Truncate(toContent.AsText(), 50)}' as a date.");
        var unit = await ctx.GetChoiceAsync("unit", "days");
        if (unit == "words") return ContentValue.Text(InWords(to - from));
        return ContentValue.Number(Between(from, to, unit));
    }

    internal static double Between(DateTimeOffset from, DateTimeOffset to, string unit)
    {
        var span = to - from;
        switch (unit)
        {
            case "seconds": return Math.Truncate(span.TotalSeconds);
            case "minutes": return Math.Truncate(span.TotalMinutes);
            case "hours": return Math.Truncate(span.TotalHours);
            case "days": return Math.Truncate(span.TotalDays);
            case "weeks": return Math.Truncate(span.TotalDays / 7);
            case "months": return WholeMonths(from.DateTime, to.DateTime);
            case "years": return Math.Truncate(WholeMonths(from.DateTime, to.DateTime) / 12.0);
            default: throw new ActionException("Choose a unit such as days or hours.");
        }
    }

    private static int WholeMonths(DateTime a, DateTime b)
    {
        var sign = 1;
        if (b < a) { (a, b) = (b, a); sign = -1; }
        var months = (b.Year - a.Year) * 12 + b.Month - a.Month;
        if (a.AddMonths(months) > b) months--;
        return sign * months;
    }

    internal static string InWords(TimeSpan span)
    {
        var negative = span < TimeSpan.Zero;
        span = span.Duration();
        var days = (long)span.TotalDays;
        var parts = new List<string>();
        if (days > 0) parts.Add(ActionHelpers.Plural(days, "day"));
        if (span.Hours > 0) parts.Add(ActionHelpers.Plural(span.Hours, "hour"));
        if (span.Minutes > 0) parts.Add(ActionHelpers.Plural(span.Minutes, "minute"));
        if (parts.Count == 0) parts.Add(ActionHelpers.Plural(span.Seconds, "second"));
        var text = string.Join(", ", parts.Take(3));
        return negative ? text + " ago" : text;
    }
}

public sealed class GetDatesFromInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.parse", Title = "Get Dates from Input", Category = ActionCategories.Dates, Icon = "CalendarClock24",
        Keywords = new[] { "parse", "tomorrow", "next friday", "in 3 days", "ago", "noon", "natural", "find dates", "understand" },
        Summary = "Get dates from {text}",
        Description = "Understands everyday date phrases like \"tomorrow at 3pm\", \"next Friday\", \"in 3 days\", \"3 days ago\", \"noon\", \"15:30\" or \"5 March 2027\", and finds all the dates in a piece of text. A time with no date means today (or tomorrow if it has already passed).",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Date, OutputName = "Dates",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true) with { Placeholder = "next Friday at 3pm" },
            new ParameterDescriptor { Key = "relativeTo", Label = "Count from (blank = now)", Type = ParamType.DateTime },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var now = await ctx.GetDateAsync("relativeTo") ?? DateTimeOffset.Now;
        var dates = new List<DateTimeOffset>();
        foreach (var item in input)
        {
            if (item is DateItem di) { dates.Add(di.Value); continue; }
            dates.AddRange(DateParser.FindAll(item.DisplayText, now));
        }
        if (dates.Count == 0)
            throw new ActionException($"Couldn't find a date in '{ContentFormat.Truncate(input.AsText().Trim(), 50)}'. Try something like \"tomorrow at 3pm\", \"next Friday\" or \"in 3 days\".");
        return new ContentValue(dates.Select(d => (ContentItem)new DateItem(d)));
    }
}
