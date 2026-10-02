using System.Globalization;
using Flowmate.Actions.Content.Actions;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;
using static Flowmate.Actions.Content.Tests.Kit;

namespace Flowmate.Actions.Content.Tests;

public class DateParserTests
{
    // "now" is Friday 2 October 2026, 09:00 local time.
    private static DateTime? Parse(string text) => DateParser.TryParse(text, Now, out var d) ? d.DateTime : null;

    [Theory]
    [InlineData("today", 2026, 10, 2, 0, 0)]
    [InlineData("tomorrow", 2026, 10, 3, 0, 0)]
    [InlineData("yesterday", 2026, 10, 1, 0, 0)]
    [InlineData("day after tomorrow", 2026, 10, 4, 0, 0)]
    [InlineData("friday", 2026, 10, 2, 0, 0)]
    [InlineData("this friday", 2026, 10, 2, 0, 0)]
    [InlineData("next friday", 2026, 10, 9, 0, 0)]
    [InlineData("last friday", 2026, 9, 25, 0, 0)]
    [InlineData("monday", 2026, 10, 5, 0, 0)]
    [InlineData("next monday", 2026, 10, 5, 0, 0)]
    [InlineData("last monday", 2026, 9, 28, 0, 0)]
    [InlineData("Wednesday", 2026, 10, 7, 0, 0)]
    [InlineData("next fri", 2026, 10, 9, 0, 0)]
    [InlineData("sat", 2026, 10, 3, 0, 0)]
    [InlineData("next week", 2026, 10, 9, 0, 0)]
    [InlineData("next month", 2026, 11, 2, 0, 0)]
    [InlineData("last year", 2025, 10, 2, 0, 0)]
    public void Day_words(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTime(y, m, d, h, min, 0), Parse(text));

    [Theory]
    [InlineData("in 3 days", 2026, 10, 5, 9, 0)]
    [InlineData("in 2 weeks", 2026, 10, 16, 9, 0)]
    [InlineData("in 1 month", 2026, 11, 2, 9, 0)]
    [InlineData("in a year", 2027, 10, 2, 9, 0)]
    [InlineData("in 3 hours", 2026, 10, 2, 12, 0)]
    [InlineData("in an hour", 2026, 10, 2, 10, 0)]
    [InlineData("in 30 minutes", 2026, 10, 2, 9, 30)]
    [InlineData("in half an hour", 2026, 10, 2, 9, 30)]
    [InlineData("in two days", 2026, 10, 4, 9, 0)]
    [InlineData("3 days ago", 2026, 9, 29, 9, 0)]
    [InlineData("2 hours ago", 2026, 10, 2, 7, 0)]
    [InlineData("a week ago", 2026, 9, 25, 9, 0)]
    [InlineData("5 days from now", 2026, 10, 7, 9, 0)]
    public void Relative_phrases(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTime(y, m, d, h, min, 0), Parse(text));

    [Theory]
    [InlineData("noon", 2026, 10, 2, 12, 0)]
    [InlineData("midnight", 2026, 10, 3, 0, 0)]
    [InlineData("3pm", 2026, 10, 2, 15, 0)]
    [InlineData("3 PM", 2026, 10, 2, 15, 0)]
    [InlineData("8am", 2026, 10, 3, 8, 0)]
    [InlineData("12am", 2026, 10, 3, 0, 0)]
    [InlineData("12pm", 2026, 10, 2, 12, 0)]
    [InlineData("15:30", 2026, 10, 2, 15, 30)]
    [InlineData("3:45pm", 2026, 10, 2, 15, 45)]
    [InlineData("07:15", 2026, 10, 3, 7, 15)]
    public void Times_without_a_date_mean_the_next_time_it_happens(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTime(y, m, d, h, min, 0), Parse(text));

    [Theory]
    [InlineData("tomorrow at 3pm", 2026, 10, 3, 15, 0)]
    [InlineData("3pm tomorrow", 2026, 10, 3, 15, 0)]
    [InlineData("tomorrow 3pm", 2026, 10, 3, 15, 0)]
    [InlineData("next friday at 5:30pm", 2026, 10, 9, 17, 30)]
    [InlineData("friday 17:00", 2026, 10, 2, 17, 0)]
    [InlineData("in 3 days at noon", 2026, 10, 5, 12, 0)]
    [InlineData("yesterday at 8am", 2026, 10, 1, 8, 0)]
    [InlineData("today at 10:00", 2026, 10, 2, 10, 0)]
    public void Date_and_time_together(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTime(y, m, d, h, min, 0), Parse(text));

    [Theory]
    [InlineData("2026-12-25", 2026, 12, 25, 0, 0)]
    [InlineData("2026-12-25T18:30", 2026, 12, 25, 18, 30)]
    [InlineData("2026-12-25 18:30", 2026, 12, 25, 18, 30)]
    [InlineData("March 5", 2027, 3, 5, 0, 0)]
    [InlineData("Oct 10", 2026, 10, 10, 0, 0)]
    [InlineData("Oct 2", 2026, 10, 2, 0, 0)]
    [InlineData("March 5, 2027", 2027, 3, 5, 0, 0)]
    [InlineData("March 5th 2028", 2028, 3, 5, 0, 0)]
    [InlineData("5 March 2027", 2027, 3, 5, 0, 0)]
    [InlineData("25 December", 2026, 12, 25, 0, 0)]
    [InlineData("1st of January 2030", 2030, 1, 1, 0, 0)]
    [InlineData("March 2027", 2027, 3, 1, 0, 0)]
    [InlineData("Friday, 2 October 2026 09:00", 2026, 10, 2, 9, 0)]
    [InlineData("December 25 at 6pm", 2026, 12, 25, 18, 0)]
    public void Calendar_dates(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTime(y, m, d, h, min, 0), Parse(text));

    [Theory]
    [InlineData("hello world")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I may go to the market")]
    [InlineData("the sun was out")]
    [InlineData("25:99")]
    [InlineData("Feb 30")]
    [InlineData("13pm")]
    public void No_date_found(string text) => Assert.Null(Parse(text));

    [Fact]
    public void Finds_several_dates_in_one_text()
    {
        var all = DateParser.FindAll("Lunch tomorrow at 1pm and dinner on Monday at 7:30pm, flight on 2026-12-24", Now);
        Assert.Equal(new[]
        {
            new DateTime(2026, 10, 3, 13, 0, 0), new DateTime(2026, 10, 5, 19, 30, 0), new DateTime(2026, 12, 24, 0, 0, 0),
        }, all.Select(d => d.DateTime));
    }

    [Fact]
    public void Two_explicit_dates_are_two_results_but_weekday_plus_date_is_one()
    {
        Assert.Equal(2, DateParser.FindAll("March 5 and March 6", Now).Count);
        Assert.Single(DateParser.FindAll("Monday 5 October", Now));
        Assert.Equal(new DateTime(2026, 10, 5), DateParser.FindAll("Monday 5 October", Now)[0].DateTime);
    }

    [Fact]
    public void Falls_back_to_culture_dates_for_unusual_formats()
    {
        var d = DateParser.FindAll("Sat, 03 Oct 2026 10:00:00 GMT", Now);
        Assert.Single(d);
    }

    [Fact]
    public void Time_zone_follows_the_machine_for_the_real_clock()
    {
        Assert.True(DateParser.TryParse("tomorrow at noon", out var d));
        Assert.Equal(12, d.Hour);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(d.DateTime), d.Offset);
    }

    [Theory]
    [InlineData(3 * 24 * 60, "in 3 days")]
    [InlineData(2 * 60, "in 2 hours")]
    [InlineData(-2 * 60, "2 hours ago")]
    [InlineData(-5, "5 minutes ago")]
    [InlineData(1, "in 1 minute")]
    [InlineData(14 * 24 * 60, "in 2 weeks")]
    [InlineData(45 * 24 * 60, "in 1 month")]
    [InlineData(-400 * 24 * 60, "1 year ago")]
    [InlineData(-24 * 60, "yesterday")]
    [InlineData(24 * 60, "tomorrow")]
    [InlineData(-8 * 24 * 60, "1 week ago")]
    public void Relative_text(int minutesFromNow, string expected) =>
        Assert.Equal(expected, DateParser.RelativeText(Now.AddMinutes(minutesFromNow), Now));

    [Fact]
    public void Relative_text_around_now_and_midnight()
    {
        Assert.Equal("just now", DateParser.RelativeText(Now.AddSeconds(10), Now));
        Assert.Equal("just now", DateParser.RelativeText(Now.AddSeconds(-30), Now));
        Assert.Equal("in a minute", DateParser.RelativeText(Now.AddSeconds(50), Now));
        Assert.Equal("a minute ago", DateParser.RelativeText(Now.AddSeconds(-50), Now));
        // 00:30 tomorrow is 15.5 hours away but already "tomorrow"; 23:00 tonight is 14 hours away
        Assert.Equal("tomorrow", DateParser.RelativeText(L(2026, 10, 3, 0, 30), Now));
        Assert.Equal("in 14 hours", DateParser.RelativeText(L(2026, 10, 2, 23, 0), Now));
    }
}

public class DateActionTests
{
    [Fact]
    public async Task Date_accepts_iso_phrases_and_date_values()
    {
        var iso = await Run(new DateAction(), null, ("date", "2026-10-02"));
        Assert.Equal(new DateTime(2026, 10, 2), iso.AsDate()!.Value.DateTime);
        var phrase = await Run(new DateAction(), null, ("date", "tomorrow 3pm"));
        Assert.Equal(15, phrase.AsDate()!.Value.Hour);
        var given = await Run(new DateAction(), null, ("date", ParamValue.Date(Now)));
        Assert.Equal(Now, given.AsDate());
        await Fails(Run(new DateAction(), null, ("date", "banana")));
        await Fails(Run(new DateAction()));
    }

    [Fact]
    public async Task Current_date_is_now()
    {
        var r = await Run(new CurrentDateAction());
        Assert.InRange((DateTimeOffset.Now - r.AsDate()!.Value).TotalSeconds, -2, 5);
    }

    private static string Fmt(string format, string custom = "yyyy-MM-dd") =>
        FormatDateAction.Format(Now, format, custom, Now, CultureInfo.InvariantCulture);

    [Fact]
    public void Formats_dates()
    {
        Assert.Equal("10/02/2026", Fmt("short"));
        Assert.Equal("2 Oct 2026", Fmt("medium"));
        Assert.Equal("Friday, 02 October 2026", Fmt("long"));
        Assert.Equal("09:00", Fmt("time"));
        Assert.Equal("2 Oct 2026, 09:00", Fmt("dateTime"));
        Assert.Equal("Friday", Fmt("weekday"));
        Assert.Equal("October 2026", Fmt("monthYear"));
        Assert.Equal("2026-10-02", Fmt("isoDate"));
        Assert.StartsWith("2026-10-02T09:00:00", Fmt("iso"));
        Assert.Matches(@"[+-]\d\d:\d\d$", Fmt("iso"));
        Assert.Equal("just now", Fmt("relative"));
        Assert.Equal("in 3 days", FormatDateAction.Format(Now.AddDays(3), "relative", "", Now, CultureInfo.InvariantCulture));
        Assert.Equal(Now.ToUnixTimeSeconds().ToString(), Fmt("unix"));
        Assert.Equal("Fri 02/10/26 09:00", Fmt("custom", "ddd dd/MM/yy HH:mm"));
        var ex = Assert.Throws<ActionException>(() => Fmt("custom", "%"));
        Assert.Contains("isn't valid", ex.Message);
    }

    [Fact]
    public async Task Format_action_formats_each_date_and_understands_text()
    {
        var r = await Run(new FormatDateAction(), ContentValue.Concat(ContentValue.Date(L(2026, 1, 5)), ContentValue.Text("2026-02-06")),
            ("format", C("custom")), ("custom", "yyyy/MM/dd"));
        Assert.Equal(new[] { "2026/01/05", "2026/02/06" }, Lines(r));
        await Fails(Run(new FormatDateAction(), ContentValue.Empty));
    }

    private static DateTime Adjust(DateTimeOffset d, string op, double amount, string unit, DayOfWeek first = DayOfWeek.Monday) =>
        AdjustDateAction.Adjust(d, op, amount, unit, first).DateTime;

    [Fact]
    public void Adjusts_dates()
    {
        Assert.Equal(new DateTime(2026, 10, 5, 9, 0, 0), Adjust(Now, "add", 3, "days"));
        Assert.Equal(new DateTime(2026, 9, 29, 9, 0, 0), Adjust(Now, "subtract", 3, "days"));
        Assert.Equal(new DateTime(2026, 10, 2, 11, 30, 0), Adjust(Now, "add", 150, "minutes"));
        Assert.Equal(new DateTime(2026, 10, 16, 9, 0, 0), Adjust(Now, "add", 2, "weeks"));
        Assert.Equal(new DateTime(2026, 2, 28, 10, 0, 0), Adjust(L(2026, 3, 31, 10), "subtract", 1, "months"));
        Assert.Equal(new DateTime(2027, 2, 28, 0, 0, 0), Adjust(L(2024, 2, 29), "add", 3, "years"));
        Assert.Equal(new DateTime(2026, 10, 2, 7, 0, 0), Adjust(Now, "subtract", 2, "hours"));
    }

    [Fact]
    public void Start_and_end_of_periods()
    {
        Assert.Equal(new DateTime(2026, 10, 2), Adjust(Now, "startOf", 0, "days"));
        Assert.Equal(new DateTime(2026, 10, 2, 9, 0, 0), Adjust(Now.AddMinutes(25), "startOf", 0, "hours"));
        Assert.Equal(new DateTime(2026, 9, 28), Adjust(Now, "startOf", 0, "weeks", DayOfWeek.Monday));
        Assert.Equal(new DateTime(2026, 9, 27), Adjust(Now, "startOf", 0, "weeks", DayOfWeek.Sunday));
        Assert.Equal(new DateTime(2026, 10, 1), Adjust(Now, "startOf", 0, "months"));
        Assert.Equal(new DateTime(2026, 1, 1), Adjust(Now, "startOf", 0, "years"));
        Assert.Equal(new DateTime(2026, 10, 31, 23, 59, 59).AddTicks(9_999_999), Adjust(Now, "endOf", 0, "months"));
        Assert.Equal(new DateTime(2026, 10, 2, 23, 59, 59).AddTicks(9_999_999), Adjust(Now, "endOf", 0, "days"));
        Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59).AddTicks(9_999_999), Adjust(Now, "endOf", 0, "years"));
        Assert.Equal(new DateTime(2026, 10, 4, 23, 59, 59).AddTicks(9_999_999), Adjust(Now, "endOf", 0, "weeks", DayOfWeek.Monday));
    }

    [Fact]
    public async Task Adjust_action_defaults_to_now_and_handles_lists()
    {
        var r = await Run(new AdjustDateAction(), null, ("operation", C("add")), ("amount", 1), ("unit", C("days")));
        Assert.InRange((r.AsDate()!.Value - DateTimeOffset.Now).TotalHours, 23.9, 24.1);
        var list = await Run(new AdjustDateAction(), ContentValue.Concat(ContentValue.Date(L(2026, 1, 1)), ContentValue.Date(L(2026, 2, 1))),
            ("operation", C("add")), ("amount", 1), ("unit", C("days")));
        Assert.Equal(new[] { new DateTime(2026, 1, 2), new DateTime(2026, 2, 2) }, list.AsDates().Select(d => d.DateTime));
        await Fails(Run(new AdjustDateAction(), ContentValue.Text("not a date")));
    }

    [Fact]
    public void Time_between_dates()
    {
        Assert.Equal(10, DateBetweenAction.Between(L(2026, 10, 1), L(2026, 10, 11), "days"));
        Assert.Equal(-10, DateBetweenAction.Between(L(2026, 10, 11), L(2026, 10, 1), "days"));
        Assert.Equal(1, DateBetweenAction.Between(L(2026, 10, 1), L(2026, 10, 11), "weeks"));
        Assert.Equal(150, DateBetweenAction.Between(L(2026, 10, 1, 9), L(2026, 10, 1, 11, 30), "minutes"));
        Assert.Equal(2, DateBetweenAction.Between(L(2026, 10, 1, 9), L(2026, 10, 1, 11, 30), "hours"));
        Assert.Equal(1, DateBetweenAction.Between(L(2026, 1, 31), L(2026, 3, 1), "months"));
        Assert.Equal(2, DateBetweenAction.Between(L(2026, 1, 31), L(2026, 3, 31), "months"));
        Assert.Equal(6, DateBetweenAction.Between(L(2020, 2, 29), L(2026, 2, 28), "years"));
        Assert.Equal(-1, DateBetweenAction.Between(L(2026, 3, 1), L(2026, 1, 31), "months"));
        Assert.Equal(3600, DateBetweenAction.Between(L(2026, 1, 1, 0), L(2026, 1, 1, 1), "seconds"));
        Assert.Equal("2 days, 3 hours", DateBetweenAction.InWords(TimeSpan.FromHours(51)));
        Assert.Equal("1 day, 1 hour, 1 minute", DateBetweenAction.InWords(TimeSpan.FromMinutes(1440 + 61)));
        Assert.Equal("30 seconds", DateBetweenAction.InWords(TimeSpan.FromSeconds(30)));
        Assert.Equal("2 days ago", DateBetweenAction.InWords(TimeSpan.FromDays(-2)));
    }

    [Fact]
    public async Task Between_action_defaults_to_now_and_reads_text()
    {
        var r = await Run(new DateBetweenAction(), null, ("from", ParamValue.Date(DateTimeOffset.Now.AddDays(-3).AddHours(-1))), ("unit", C("days")));
        Assert.Equal(3, r.AsNumber());
        var words = await Run(new DateBetweenAction(), null, ("from", "2026-10-01"), ("to", "2026-10-03 06:00"), ("unit", C("words")));
        Assert.Equal("2 days, 6 hours", words.AsText());
        await Fails(Run(new DateBetweenAction(), ContentValue.Empty));
        await Fails(Run(new DateBetweenAction(), null, ("from", "2026-10-01"), ("to", "gibberish")));
    }

    [Fact]
    public async Task Parse_action_finds_dates_relative_to_a_given_moment()
    {
        var r = await Run(new GetDatesFromInputAction(), null, ("text", "Call mum tomorrow at 3pm, and again in 3 days"), ("relativeTo", ParamValue.Date(Now)));
        Assert.Equal(new[] { new DateTime(2026, 10, 3, 15, 0, 0), new DateTime(2026, 10, 5, 9, 0, 0) }, r.AsDates().Select(d => d.DateTime));
        var ex = await Fails(Run(new GetDatesFromInputAction(), null, ("text", "no date in here")));
        Assert.Contains("Couldn't find a date", ex.Message);
        var passthrough = await Run(new GetDatesFromInputAction(), ContentValue.Date(Now));
        Assert.Equal(Now, passthrough.AsDate());
    }
}
