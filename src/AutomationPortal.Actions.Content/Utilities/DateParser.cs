using System.Globalization;
using System.Text.RegularExpressions;

namespace AutomationPortal.Actions.Content.Utilities;

/// <summary>
/// Understands everyday date phrases: today, tomorrow, "next Friday", "in 3 days", "3 days ago", "noon", "3pm", "15:30",
/// "March 5", "5 March 2027", ISO dates and dates in the current culture's order. Finds several dates in one text.
/// Rules: a date without a time means the start of that day; a time without a date means today (or tomorrow if that
/// time has already passed); "friday" is the next Friday from today (today included), "next friday" the first Friday after today.
/// </summary>
public static class DateParser
{
    private const string Months = @"jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";
    private const string Weekdays = @"monday|tuesday|wednesday|thursday|friday|saturday|sunday|tues|thurs|thur";
    private const string WeekdayAbbr = @"mon|tue|wed|thu|fri|sat|sun";
    private const string Units = @"second|sec|minute|min|hour|hr|day|week|fortnight|month|year";
    private const string Amount = @"a|an|\d+(?:\.\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|half";

    private static readonly Regex Scanner = new(
        $@"(?<iso>\b\d{{4}}-\d{{2}}-\d{{2}}(?:[T ]\d{{1,2}}:\d{{2}}(?::\d{{2}})?(?:\.\d+)?(?:Z|[+-]\d{{2}}:?\d{{2}})?)?)
        |(?<num>\b\d{{1,2}}[/.\-]\d{{1,2}}[/.\-]\d{{2,4}}\b)
        |(?<mdy>\b(?<m1>{Months})\.?\s+(?<d1>\d{{1,2}})(?!\d)(?:st|nd|rd|th)?(?:,?\s+(?<y1>\d{{4}})\b)?)
        |(?<my>\b(?<m2>{Months})\.?,?\s+(?<y2>\d{{4}})\b)
        |(?<dmy>\b(?<d3>\d{{1,2}})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<m3>{Months})\b\.?(?:,?\s+(?<y3>\d{{4}})\b)?)
        |(?<inn>\b(?:in|within)\s+(?<n1>{Amount})\s+(?:an?\s+)?(?<u1>{Units})s?(?:\s+and\s+a\s+half)?\b)
        |(?<ago>\b(?<n2>{Amount})\s+(?<u2>{Units})s?\s+(?:ago|from\s+now|later)\b)
        |(?<dd>\bday\s+after\s+tomorrow\b|\bday\s+before\s+yesterday\b|\btoday\b|\btomorrow\b|\byesterday\b)
        |(?<per>\b(?<pm>next|last|this)\s+(?<pu>week|month|year)\b)
        |(?<wd>\b(?:(?<wm>next|last|this|coming|previous)\s+)?(?<wn>{Weekdays})\b|\b(?<wm2>next|last|this)\s+(?<wn2>{WeekdayAbbr})\b)
        |(?<time>\b(?:(?<hh>\d{{1,2}}):(?<mm>\d{{2}})(?::(?<ss>\d{{2}}))?\s*(?<ap>[ap]\.?m\.?)?|(?<h2>\d{{1,2}})\s*(?<ap2>[ap]\.?m\.?)|(?<named>noon|midday|midnight))(?![\w:]))",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex Gap = new(@"^[\s,]*(?:(?:at|on|@|around|by|from|the|of|and)[\s,]*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private enum PieceKind { Exact, Date, Weekday, Time }

    private sealed record Piece(PieceKind Kind, DateTimeOffset? Exact = null, DateOnly? Date = null, bool KeepNowTime = false, TimeOnly? Time = null);

    /// <summary>The first date found in the text, relative to <paramref name="now"/>.</summary>
    public static bool TryParse(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var all = FindAll(text, now);
        if (all.Count > 0) { result = all[0]; return true; }
        result = default;
        return false;
    }

    public static bool TryParse(string text, out DateTimeOffset result) => TryParse(text, DateTimeOffset.Now, out result);

    /// <summary>Every date found in the text, in order. "Lunch tomorrow at 1pm and dinner on Friday" gives two dates.</summary>
    public static IReadOnlyList<DateTimeOffset> FindAll(string text, DateTimeOffset now)
    {
        var results = new List<DateTimeOffset>();
        if (string.IsNullOrWhiteSpace(text)) return results;
        text = text.Trim();

        // Whole-text fallbacks first for formats the scanner would only partly understand ("Fri, 02 Oct 2026 09:00:00 GMT").
        var weekdayAbbr = Regex.Match(text, $@"^(?<n>{WeekdayAbbr})\.?$", RegexOptions.IgnoreCase);
        if (weekdayAbbr.Success)
        {
            results.Add(Resolve(new[] { Weekday(null, weekdayAbbr.Groups["n"].Value, now) }, now));
            return results;
        }

        var pieces = new List<(Piece Piece, int Start, int End)>();
        foreach (Match m in Scanner.Matches(text))
        {
            var p = ToPiece(m, now);
            if (p is not null) pieces.Add((p, m.Index, m.Index + m.Length));
        }

        if (pieces.Count == 0)
        {
            if (Regex.IsMatch(text, @"\d{4}") && (DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var whole)
                || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out whole)))
                results.Add(whole);
            return results;
        }

        var group = new List<Piece>();
        var lastEnd = -1;
        foreach (var (piece, start, end) in pieces)
        {
            if (group.Count > 0)
            {
                var gapText = text[lastEnd..start];
                if (Gap.IsMatch(gapText) && CanJoin(group, piece)) { Join(group, piece); lastEnd = end; continue; }
                results.Add(Resolve(group, now));
                group.Clear();
            }
            group.Add(piece);
            lastEnd = end;
        }
        if (group.Count > 0) results.Add(Resolve(group, now));
        return results;
    }

    // A group holds at most one date-ish piece and one time piece.
    private static bool CanJoin(List<Piece> group, Piece next)
    {
        if (group.Any(g => g.Kind == PieceKind.Exact) || next.Kind == PieceKind.Exact) return false;
        var hasTime = group.Any(g => g.Kind == PieceKind.Time);
        var hasDate = group.Any(g => g.Kind is PieceKind.Date or PieceKind.Weekday);
        if (next.Kind == PieceKind.Time) return !hasTime;
        if (hasDate)
        {
            // "Friday, 2 October 2026": the explicit date wins over the weekday name. Two explicit dates are separate.
            return group.Any(g => g.Kind == PieceKind.Weekday) || next.Kind == PieceKind.Weekday;
        }
        return true;
    }

    private static void Join(List<Piece> group, Piece next)
    {
        if (next.Kind == PieceKind.Weekday && group.Any(g => g.Kind == PieceKind.Date)) return;
        if (next.Kind == PieceKind.Date) group.RemoveAll(g => g.Kind == PieceKind.Weekday);
        group.Add(next);
    }

    private static DateTimeOffset Resolve(IReadOnlyList<Piece> group, DateTimeOffset now)
    {
        var offset = now.Offset;
        var exact = group.FirstOrDefault(g => g.Kind == PieceKind.Exact);
        if (exact is not null) return exact.Exact!.Value;

        var datePiece = group.FirstOrDefault(g => g.Kind is PieceKind.Date or PieceKind.Weekday);
        var timePiece = group.FirstOrDefault(g => g.Kind == PieceKind.Time);
        var today = DateOnly.FromDateTime(now.DateTime);

        TimeOnly? time = timePiece?.Time;
        DateOnly date;
        if (datePiece is not null) date = datePiece.Date!.Value;
        else
        {
            date = today;
            if (time is { } t && date.ToDateTime(t) < now.DateTime) date = date.AddDays(1);
        }

        if (time is null && datePiece is { KeepNowTime: true }) time = TimeOnly.FromDateTime(now.DateTime);
        var local = date.ToDateTime(time ?? TimeOnly.MinValue);
        return LocalToOffset(local, offset, now);
    }

    private static DateTimeOffset LocalToOffset(DateTime local, TimeSpan offset, DateTimeOffset now)
    {
        // Honour the machine's time zone for the real clock (daylight saving), otherwise the offset of 'now'.
        if (now.Offset == TimeZoneInfo.Local.GetUtcOffset(now))
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            var o = TimeZoneInfo.Local.IsInvalidTime(unspecified) ? offset : TimeZoneInfo.Local.GetUtcOffset(unspecified);
            return new DateTimeOffset(unspecified, o);
        }
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }

    private static Piece? ToPiece(Match m, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        if (m.Groups["iso"].Success)
        {
            if (DateTimeOffset.TryParse(m.Groups["iso"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            {
                var hasTime = m.Groups["iso"].Value.Length > 10;
                return hasTime ? new Piece(PieceKind.Exact, Exact: dto) : new Piece(PieceKind.Date, Date: DateOnly.FromDateTime(dto.DateTime));
            }
            return null;
        }
        if (m.Groups["num"].Success)
        {
            var t = m.Groups["num"].Value;
            if (DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) || DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return new Piece(PieceKind.Date, Date: DateOnly.FromDateTime(d));
            return null;
        }
        if (m.Groups["mdy"].Success)
            return MonthDay(MonthNumber(m.Groups["m1"].Value), int.Parse(m.Groups["d1"].Value), m.Groups["y1"].Success ? int.Parse(m.Groups["y1"].Value) : null, today);
        if (m.Groups["my"].Success)
        {
            var month = MonthNumber(m.Groups["m2"].Value);
            return new Piece(PieceKind.Date, Date: new DateOnly(int.Parse(m.Groups["y2"].Value), month, 1));
        }
        if (m.Groups["dmy"].Success)
            return MonthDay(MonthNumber(m.Groups["m3"].Value), int.Parse(m.Groups["d3"].Value), m.Groups["y3"].Success ? int.Parse(m.Groups["y3"].Value) : null, today);
        if (m.Groups["inn"].Success)
            return Relative(m.Groups["n1"].Value, m.Groups["u1"].Value, +1, now, m.Groups["inn"].Value.EndsWith("half", StringComparison.OrdinalIgnoreCase));
        if (m.Groups["ago"].Success)
        {
            var sign = m.Groups["ago"].Value.EndsWith("ago", StringComparison.OrdinalIgnoreCase) ? -1 : +1;
            return Relative(m.Groups["n2"].Value, m.Groups["u2"].Value, sign, now, false);
        }
        if (m.Groups["dd"].Success)
        {
            var w = Regex.Replace(m.Groups["dd"].Value.ToLowerInvariant(), @"\s+", " ");
            var offset = w switch { "tomorrow" => 1, "yesterday" => -1, "day after tomorrow" => 2, "day before yesterday" => -2, _ => 0 };
            return new Piece(PieceKind.Date, Date: today.AddDays(offset));
        }
        if (m.Groups["per"].Success)
        {
            var sign = m.Groups["pm"].Value.ToLowerInvariant() switch { "next" => 1, "last" => -1, _ => 0 };
            var date = m.Groups["pu"].Value.ToLowerInvariant() switch
            {
                "week" => today.AddDays(7 * sign),
                "month" => today.AddMonths(sign),
                _ => today.AddYears(sign),
            };
            return new Piece(PieceKind.Date, Date: date);
        }
        if (m.Groups["wd"].Success)
        {
            var mod = m.Groups["wm"].Success ? m.Groups["wm"].Value : m.Groups["wm2"].Success ? m.Groups["wm2"].Value : null;
            var name = m.Groups["wn"].Success ? m.Groups["wn"].Value : m.Groups["wn2"].Value;
            return Weekday(mod, name, now);
        }
        if (m.Groups["time"].Success) return TimePiece(m);
        return null;
    }

    private static Piece? MonthDay(int month, int day, int? year, DateOnly today)
    {
        if (year is { } y)
        {
            if (day < 1 || day > DateTime.DaysInMonth(y, month)) return null;
            return new Piece(PieceKind.Date, Date: new DateOnly(y, month, day));
        }
        if (day < 1 || day > 31) return null;
        // No year: the next time that day comes round (today counts).
        foreach (var yy in new[] { today.Year, today.Year + 1, today.Year + 2, today.Year + 3, today.Year + 4 })
        {
            if (day > DateTime.DaysInMonth(yy, month)) continue;
            var d = new DateOnly(yy, month, day);
            if (d >= today) return new Piece(PieceKind.Date, Date: d);
        }
        return null;
    }

    private static Piece Weekday(string? modifier, string name, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var target = name.ToLowerInvariant()[..3] switch
        {
            "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday,
            "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
        };
        var ahead = ((int)target - (int)today.DayOfWeek + 7) % 7;
        DateOnly date;
        switch (modifier?.ToLowerInvariant())
        {
            case "next": case "coming": date = today.AddDays(ahead == 0 ? 7 : ahead); break;
            case "last": case "previous": date = today.AddDays(ahead == 0 ? -7 : ahead - 7); break;
            default: date = today.AddDays(ahead); break;
        }
        return new Piece(PieceKind.Weekday, Date: date);
    }

    private static Piece Relative(string amountText, string unit, int sign, DateTimeOffset now, bool plusHalf)
    {
        var amount = AmountOf(amountText) + (plusHalf ? 0.5 : 0);
        unit = unit.ToLowerInvariant();
        switch (unit)
        {
            case "second": case "sec": return new Piece(PieceKind.Exact, Exact: now.AddSeconds(sign * amount));
            case "minute": case "min": return new Piece(PieceKind.Exact, Exact: now.AddMinutes(sign * amount));
            case "hour": case "hr": return new Piece(PieceKind.Exact, Exact: now.AddHours(sign * amount));
        }
        var today = DateOnly.FromDateTime(now.DateTime);
        var date = unit switch
        {
            "day" => today.AddDays((int)Math.Round(sign * amount)),
            "week" => today.AddDays((int)Math.Round(sign * amount * 7)),
            "fortnight" => today.AddDays((int)Math.Round(sign * amount * 14)),
            "month" => today.AddMonths((int)Math.Round(sign * amount)),
            _ => today.AddYears((int)Math.Round(sign * amount)),
        };
        return new Piece(PieceKind.Date, Date: date, KeepNowTime: true);
    }

    private static double AmountOf(string s) => s.ToLowerInvariant() switch
    {
        "a" or "an" or "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5, "six" => 6, "seven" => 7,
        "eight" => 8, "nine" => 9, "ten" => 10, "eleven" => 11, "twelve" => 12, "half" => 0.5,
        _ => double.Parse(s, CultureInfo.InvariantCulture),
    };

    private static Piece? TimePiece(Match m)
    {
        if (m.Groups["named"].Success)
            return new Piece(PieceKind.Time, Time: m.Groups["named"].Value.ToLowerInvariant() == "midnight" ? new TimeOnly(0, 0) : new TimeOnly(12, 0));
        int hour, minute = 0, second = 0;
        string? ap;
        if (m.Groups["hh"].Success)
        {
            hour = int.Parse(m.Groups["hh"].Value);
            minute = int.Parse(m.Groups["mm"].Value);
            if (m.Groups["ss"].Success) second = int.Parse(m.Groups["ss"].Value);
            ap = m.Groups["ap"].Success ? m.Groups["ap"].Value : null;
        }
        else
        {
            hour = int.Parse(m.Groups["h2"].Value);
            ap = m.Groups["ap2"].Value;
        }
        if (ap is not null)
        {
            if (hour < 1 || hour > 12) return null;
            var pm = char.ToLowerInvariant(ap[0]) == 'p';
            hour = hour % 12 + (pm ? 12 : 0);
        }
        if (hour > 23 || minute > 59 || second > 59) return null;
        return new Piece(PieceKind.Time, Time: new TimeOnly(hour, minute, second));
    }

    private static int MonthNumber(string name) => name.ToLowerInvariant()[..3] switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6,
        "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, _ => 12,
    };

    // ---- relative text -----------------------------------------------------------------------

    /// <summary>"in 3 days", "2 hours ago", "tomorrow", "yesterday", "today", "just now".</summary>
    public static string RelativeText(DateTimeOffset date, DateTimeOffset now)
    {
        date = date.ToOffset(now.Offset);
        var diff = date - now;
        var abs = diff.Duration();
        var future = diff > TimeSpan.Zero;
        string Phrase(long n, string unit) => Strings.Format(future ? "DateParser_Relative_Future" : "DateParser_Relative_Past", n, Strings.Get("DateParser_Unit_" + unit + (n == 1 ? "_One" : "_Other")));

        if (abs < TimeSpan.FromSeconds(45)) return Strings.Get("DateParser_Message_JustNow");
        if (abs < TimeSpan.FromMinutes(1)) return Strings.Get(future ? "DateParser_Message_InAMinute" : "DateParser_Message_AMinuteAgo");
        if (abs < TimeSpan.FromMinutes(60)) return Phrase((long)Math.Round(abs.TotalMinutes), "minute");
        var days = (date.Date - now.Date).Days;
        if (abs < TimeSpan.FromHours(24) && days == 0) return Phrase((long)Math.Round(abs.TotalHours), "hour");
        switch (days)
        {
            case 0: return Strings.Get("DateParser_Message_Today");
            case 1: return Strings.Get("DateParser_Message_Tomorrow");
            case -1: return Strings.Get("DateParser_Message_Yesterday");
        }
        var n = Math.Abs(days);
        if (n < 7) return Phrase(n, "day");
        if (n < 30) return Phrase(n / 7, "week");
        if (n < 365) return Phrase((long)(n / 30.4375), "month");
        return Phrase((long)(n / 365.25), "year");
    }
}
