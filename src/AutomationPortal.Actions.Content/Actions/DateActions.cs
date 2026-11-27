using System.Globalization;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class DateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.date", Title = Strings.Get("DateDate_Title"), Category = ActionCategories.Dates, Icon = "CalendarLtr24",
        Keywords = new[] { "day", "calendar", "specific date", "time" },
        Summary = Strings.Get("DateDate_Summary"),
        Description = Strings.Get("DateDate_Description"),
        OutputKind = ContentKind.Date, OutputName = Strings.Get("DateDate_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "date", Label = Strings.Get("DateDate_Param_Date_Label"), Type = ParamType.DateTime, Required = true, Placeholder = Strings.Get("DateDate_Param_Date_Placeholder") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("date");
        if (content.IsEmpty) throw new ActionException(Strings.Get("DateDate_Error_ChooseADateOr"));
        var all = ActionHelpers.ToDates(content).ToList();
        if (all.Count == 0)
            throw new ActionException(Strings.Format("DateDate_Error_CouldnTUnderstand0", ContentFormat.Truncate(content.AsText(), 50)));
        return new ContentValue(all.Select(d => (ContentItem)new DateItem(d)));
    }
}

public sealed class CurrentDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.current", Title = Strings.Get("DateCurrent_Title"), Category = ActionCategories.Dates, Icon = "Clock24",
        Keywords = new[] { "now", "today", "time", "date" },
        Summary = Strings.Get("DateCurrent_Summary"),
        Description = Strings.Get("DateCurrent_Description"),
        OutputKind = ContentKind.Date, OutputName = Strings.Get("DateCurrent_OutputName"),
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Date(DateTimeOffset.Now));
}

public sealed class FormatDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.format", Title = Strings.Get("DateFormat_Title"), Category = ActionCategories.Dates, Icon = "CalendarEdit24",
        Keywords = new[] { "show", "write", "iso", "relative", "short", "long", "custom", "weekday", "text" },
        Summary = Strings.Get("DateFormat_Summary"),
        Description = Strings.Get("DateFormat_Description"),
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Text, OutputName = Strings.Get("DateFormat_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("date", Strings.Get("DateFormat_Param_Date_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("format", Strings.Get("DateFormat_Param_Format_Label"), "medium",
                new Choice("short", Strings.Get("DateFormat_Choice_Short")), new Choice("medium", Strings.Get("DateFormat_Choice_Medium")), new Choice("long", Strings.Get("DateFormat_Choice_Long")),
                new Choice("time", Strings.Get("DateFormat_Choice_Time")), new Choice("dateTime", Strings.Get("DateFormat_Choice_DateTime")), new Choice("weekday", Strings.Get("DateFormat_Choice_Weekday")),
                new Choice("monthYear", Strings.Get("DateFormat_Choice_MonthYear")), new Choice("iso", Strings.Get("DateFormat_Choice_Iso")), new Choice("isoDate", Strings.Get("DateFormat_Choice_IsoDate")),
                new Choice("relative", Strings.Get("DateFormat_Choice_Relative")), new Choice("unix", Strings.Get("DateFormat_Choice_Unix")), new Choice("custom", Strings.Get("DateFormat_Choice_Custom"))),
            ParameterDescriptor.Text("custom", Strings.Get("DateFormat_Param_Custom_Label"), "yyyy-MM-dd", Strings.Get("DateFormat_Param_Custom_Placeholder")) with
            {
                VisibleWhen = new VisibleWhen("format", "custom"),
                Hint = Strings.Get("DateFormat_Param_Custom_Hint"),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var dates = ActionHelpers.ToDates(await ctx.GetContentAsync("date")).ToList();
        if (dates.Count == 0) throw new ActionException(Strings.Get("DateFormat_Error_ThereIsNoDate"));
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
                catch (FormatException) { throw new ActionException(Strings.Format("DateFormat_Error_TheLayout0Isn", custom)); }
            default: return d.ToString("g", culture);
        }
    }
}

public sealed class AdjustDateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.adjust", Title = Strings.Get("DateAdjust_Title"), Category = ActionCategories.Dates, Icon = "CalendarArrowRight24",
        Keywords = new[] { "add", "subtract", "plus", "minus", "start of", "end of", "week", "month", "year", "tomorrow", "yesterday" },
        Summary = Strings.Get("DateAdjust_Summary"),
        Description = Strings.Get("DateAdjust_Description"),
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Date, OutputName = Strings.Get("DateAdjust_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("date", Strings.Get("DateAdjust_Param_Date_Label"), isInput: true),
            ParameterDescriptor.ChoiceOf("operation", Strings.Get("DateAdjust_Param_Operation_Label"), "add",
                new Choice("add", Strings.Get("DateAdjust_Choice_Add")), new Choice("subtract", Strings.Get("DateAdjust_Choice_Subtract")), new Choice("startOf", Strings.Get("DateAdjust_Choice_StartOf")), new Choice("endOf", Strings.Get("DateAdjust_Choice_EndOf"))),
            ParameterDescriptor.Number("amount", Strings.Get("DateAdjust_Param_Amount_Label"), 1) with { VisibleWhen = new VisibleWhen("operation", "add", "subtract") },
            ParameterDescriptor.ChoiceOf("unit", Strings.Get("DateAdjust_Param_Unit_Label"), "days",
                new Choice("minutes", Strings.Get("DateAdjust_Choice_Minutes")), new Choice("hours", Strings.Get("DateAdjust_Choice_Hours")), new Choice("days", Strings.Get("DateAdjust_Choice_Days")),
                new Choice("weeks", Strings.Get("DateAdjust_Choice_Weeks")), new Choice("months", Strings.Get("DateAdjust_Choice_Months")), new Choice("years", Strings.Get("DateAdjust_Choice_Years"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("date");
        var dates = content.IsEmpty ? new List<DateTimeOffset> { DateTimeOffset.Now } : ActionHelpers.ToDates(content).ToList();
        if (dates.Count == 0) throw new ActionException(Strings.Format("DateAdjust_Error_CouldnTUnderstand0", ContentFormat.Truncate(content.AsText(), 50)));
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
            default: throw new ActionException(Strings.Get("DateAdjust_Error_ChooseWhatToDo"));
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
            default: throw new ActionException(Strings.Get("DateAdjust_Error_ChooseAUnitSuch"));
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
            default: throw new ActionException(Strings.Get("DateAdjust_Error_ChooseAUnitSuch2"));
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
        Id = "date.between", Title = Strings.Get("DateBetween_Title"), Category = ActionCategories.Dates, Icon = "CalendarSettings24",
        Keywords = new[] { "difference", "duration", "days until", "age", "how long", "countdown", "elapsed" },
        Summary = Strings.Get("DateBetween_Summary"),
        Description = Strings.Get("DateBetween_Description"),
        InputKinds = new[] { ContentKind.Date }, OutputKind = ContentKind.Number, OutputName = Strings.Get("DateBetween_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("from", Strings.Get("DateBetween_Param_From_Label"), isInput: true, required: true),
            ParameterDescriptor.Content("to", Strings.Get("DateBetween_Param_To_Label")),
            ParameterDescriptor.ChoiceOf("unit", Strings.Get("DateBetween_Param_Unit_Label"), "days",
                new Choice("seconds", Strings.Get("DateBetween_Choice_Seconds")), new Choice("minutes", Strings.Get("DateBetween_Choice_Minutes")), new Choice("hours", Strings.Get("DateBetween_Choice_Hours")), new Choice("days", Strings.Get("DateBetween_Choice_Days")),
                new Choice("weeks", Strings.Get("DateBetween_Choice_Weeks")), new Choice("months", Strings.Get("DateBetween_Choice_Months")), new Choice("years", Strings.Get("DateBetween_Choice_Years")), new Choice("words", Strings.Get("DateBetween_Choice_Words"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var from = ActionHelpers.ToDate(await ctx.GetContentAsync("from"))
                   ?? throw new ActionException(Strings.Get("DateBetween_Error_ThereIsNoStarting"));
        var toContent = await ctx.GetContentAsync("to");
        var to = toContent.IsEmpty ? DateTimeOffset.Now
            : ActionHelpers.ToDate(toContent) ?? throw new ActionException(Strings.Format("DateBetween_Error_CouldnTUnderstand0", ContentFormat.Truncate(toContent.AsText(), 50)));
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
            default: throw new ActionException(Strings.Get("DateBetween_Error_ChooseAUnitSuch"));
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
        if (days > 0) parts.Add(ActionHelpers.Count(days, "Day"));
        if (span.Hours > 0) parts.Add(ActionHelpers.Count(span.Hours, "Hour"));
        if (span.Minutes > 0) parts.Add(ActionHelpers.Count(span.Minutes, "Minute"));
        if (parts.Count == 0) parts.Add(ActionHelpers.Count(span.Seconds, "Second"));
        var text = string.Join(", ", parts.Take(3));
        return negative ? Strings.Format("DateActions_InWords_Ago", text) : text;
    }
}

public sealed class GetDatesFromInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "date.parse", Title = Strings.Get("DateParse_Title"), Category = ActionCategories.Dates, Icon = "CalendarClock24",
        Keywords = new[] { "parse", "tomorrow", "next friday", "in 3 days", "ago", "noon", "natural", "find dates", "understand" },
        Summary = Strings.Get("DateParse_Summary"),
        Description = Strings.Get("DateParse_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Date, OutputName = Strings.Get("DateParse_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("DateParse_Param_Text_Label"), required: true) with { Placeholder = Strings.Get("DateParse_Param_Text_Placeholder") },
            new ParameterDescriptor { Key = "relativeTo", Label = Strings.Get("DateParse_Param_RelativeTo_Label"), Type = ParamType.DateTime },
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
            throw new ActionException(Strings.Format("DateParse_Error_CouldnTFindA", ContentFormat.Truncate(input.AsText().Trim(), 50)));
        return new ContentValue(dates.Select(d => (ContentItem)new DateItem(d)));
    }
}
