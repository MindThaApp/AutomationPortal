using Flowmate.Triggers;

namespace Flowmate.Triggers.Tests;

public class IcsEventFinderTests
{
    private const string Calendar = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:single-1
        DTSTAMP:20260101T000000Z
        DTSTART:20261002T140000Z
        DTEND:20261002T150000Z
        SUMMARY:Dentist
        LOCATION:High Street
        DESCRIPTION:Bring the form
        END:VEVENT
        BEGIN:VEVENT
        UID:daily-1
        DTSTAMP:20260101T000000Z
        DTSTART:20260928T083000Z
        DTEND:20260928T090000Z
        RRULE:FREQ=DAILY
        SUMMARY:Stand-up
        END:VEVENT
        BEGIN:VEVENT
        UID:weekly-1
        DTSTAMP:20260101T000000Z
        DTSTART:20260105T180000Z
        DTEND:20260105T190000Z
        RRULE:FREQ=WEEKLY;BYDAY=MO
        SUMMARY:Choir
        END:VEVENT
        BEGIN:VEVENT
        UID:allday-1
        DTSTAMP:20260101T000000Z
        DTSTART;VALUE=DATE:20261002
        DTEND;VALUE=DATE:20261003
        SUMMARY:Holiday
        END:VEVENT
        END:VCALENDAR
        """;

    private static readonly DateTimeOffset From = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Finds_single_events_with_their_details()
    {
        var all = IcsEventFinder.GetOccurrences(Calendar, From, From.AddDays(1));
        var dentist = Assert.Single(all, o => o.Title == "Dentist");
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero), dentist.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), dentist.End);
        Assert.Equal("High Street", dentist.Location);
        Assert.Equal("Bring the form", dentist.Description);
    }

    [Fact]
    public void Expands_daily_recurrence_one_occurrence_per_day()
    {
        var all = IcsEventFinder.GetOccurrences(Calendar, From, From.AddDays(3)).Where(o => o.Title == "Stand-up").ToList();
        Assert.Equal(3, all.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.Zero), all[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 8, 30, 0, TimeSpan.Zero), all[1].Start);
        Assert.Equal(3, all.Select(o => o.Key).Distinct().Count());
    }

    [Fact]
    public void Expands_weekly_recurrence_on_the_right_weekday()
    {
        // 5 Oct 2026 is a Monday.
        var all = IcsEventFinder.GetOccurrences(Calendar, From, From.AddDays(11)).Where(o => o.Title == "Choir").ToList();
        Assert.Equal(new[] { new DateTime(2026, 10, 5), new DateTime(2026, 10, 12) }, all.Select(o => o.Start.UtcDateTime.Date).ToArray());
    }

    [Fact]
    public void Results_are_sorted_by_start_time()
    {
        var all = IcsEventFinder.GetOccurrences(Calendar, From, From.AddDays(2));
        Assert.Equal(all.OrderBy(o => o.Start).Select(o => o.Key), all.Select(o => o.Key));
    }

    [Fact]
    public void Due_fires_inside_the_reminder_window_and_only_once()
    {
        var occ = IcsEventFinder.GetOccurrences(Calendar, From, From.AddDays(1));
        var fired = new HashSet<string>();
        var dentist = occ.Single(o => o.Title == "Dentist");

        Assert.DoesNotContain(dentist, IcsEventFinder.Due(occ, dentist.Start.AddMinutes(-11), 10, fired));
        var due = IcsEventFinder.Due(occ, dentist.Start.AddMinutes(-9), 10, fired);
        Assert.Contains(dentist, due);

        fired.Add(dentist.Key);
        Assert.DoesNotContain(dentist, IcsEventFinder.Due(occ, dentist.Start.AddMinutes(-5), 10, fired));
        Assert.DoesNotContain(dentist, IcsEventFinder.Due(occ, dentist.Start.AddMinutes(1), 10, new HashSet<string>()));
    }

    [Fact]
    public void All_day_events_are_not_reminded()
    {
        var occ = IcsEventFinder.GetOccurrences(Calendar, From.AddHours(-1), From.AddDays(1));
        var holiday = occ.Single(o => o.Title == "Holiday");
        Assert.True(holiday.AllDay);
        Assert.Empty(IcsEventFinder.Due(new[] { holiday }, holiday.Start.AddMinutes(-5), 10, new HashSet<string>()));
    }

    [Fact]
    public void Garbage_gives_no_events_or_a_clear_error()
    {
        IReadOnlyList<CalendarOccurrence>? result = null;
        try { result = IcsEventFinder.GetOccurrences("this is not a calendar", From, From.AddDays(1)); }
        catch (Exception) { return; }
        Assert.Empty(result);
    }
}

public class WeatherAlertEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 30, 0);

    private static IReadOnlyList<HourlyForecast> Day(Func<int, (double Temp, double Rain, double Snow)> f) =>
        Enumerable.Range(0, 48).Select(h =>
        {
            var (t, r, s) = f(h);
            return new HourlyForecast(new DateTime(2026, 10, 2).AddHours(h), t, r, s);
        }).ToList();

    [Fact]
    public void Rain_inside_the_window_triggers_an_alert()
    {
        var forecast = Day(h => (15, h == 14 ? 1.2 : 0, 0));
        var alert = WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Rain, 0, 6, Now);
        Assert.NotNull(alert);
        Assert.Equal(new DateTime(2026, 10, 2, 14, 0, 0), alert!.At);
        Assert.StartsWith("Rain is expected", alert.Message);
    }

    [Fact]
    public void Rain_after_the_window_is_ignored()
    {
        var forecast = Day(h => (15, h == 20 ? 5 : 0, 0));
        Assert.Null(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Rain, 0, 6, Now));
    }

    [Fact]
    public void A_light_drizzle_below_the_threshold_is_not_rain()
    {
        var forecast = Day(h => (15, 0.1, 0));
        Assert.Null(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Rain, 0, 12, Now));
        Assert.NotNull(WeatherAlertEvaluator.Evaluate(Day(h => (15, WeatherAlertEvaluator.RainThreshold, 0)), WeatherCondition.Rain, 0, 12, Now));
    }

    [Fact]
    public void The_current_hour_counts_even_though_it_started_before_now()
    {
        var forecast = Day(h => (15, h == 9 ? 2 : 0, 0));
        Assert.NotNull(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Rain, 0, 3, Now));
    }

    [Fact]
    public void Past_hours_are_ignored()
    {
        var forecast = Day(h => (15, h == 3 ? 9 : 0, 0));
        Assert.Null(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Rain, 0, 24, Now));
    }

    [Fact]
    public void Snow_uses_snowfall_not_rain()
    {
        var forecast = Day(h => (-2, 3, h == 12 ? 1 : 0));
        Assert.NotNull(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Snow, 0, 6, Now));
        Assert.Null(WeatherAlertEvaluator.Evaluate(Day(h => (-2, 3, 0)), WeatherCondition.Snow, 0, 6, Now));
    }

    [Fact]
    public void Temperature_above_and_below_use_strict_comparisons()
    {
        var forecast = Day(h => (h == 12 ? 30 : 20, 0, 0));
        Assert.NotNull(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Above, 25, 6, Now));
        Assert.Null(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Above, 30, 6, Now));
        Assert.Null(WeatherAlertEvaluator.Evaluate(forecast, WeatherCondition.Below, 20, 6, Now));
        var cold = Day(h => (h == 11 ? -3 : 5, 0, 0));
        var alert = WeatherAlertEvaluator.Evaluate(cold, WeatherCondition.Below, 0, 6, Now, "°C");
        Assert.NotNull(alert);
        Assert.Contains("colder than 0°C", alert!.Message);
        Assert.Contains("-3°C", alert.Message);
    }

    [Fact]
    public void Parses_an_Open_Meteo_response()
    {
        const string json = """
            {"latitude":51.5,"longitude":-0.12,"hourly":{"time":["2026-10-02T09:00","2026-10-02T10:00","2026-10-02T11:00"],
            "temperature_2m":[12.5,13.0,null],"rain":[0.0,0.6,0.0],"snowfall":[0,0,0.2]}}
            """;
        var hours = WeatherAlertEvaluator.ParseHourly(json);
        Assert.Equal(3, hours.Count);
        Assert.Equal(new DateTime(2026, 10, 2, 10, 0, 0), hours[1].Time);
        Assert.Equal(0.6, hours[1].Rain);
        Assert.Equal(0, hours[2].Temperature);
        Assert.Equal(0.2, hours[2].Snowfall);
        Assert.NotNull(WeatherAlertEvaluator.Evaluate(hours, WeatherCondition.Rain, 0, 3, new DateTime(2026, 10, 2, 9, 5, 0)));
    }

    [Fact]
    public void A_response_without_hourly_data_gives_nothing() =>
        Assert.Empty(WeatherAlertEvaluator.ParseHourly("{\"error\":true}"));
}

public class FeedParserTests
{
    private const string Rss = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel><title>Daily News</title>
          <item><title>Second &amp; better</title><link>https://example.com/2</link><guid>g2</guid><pubDate>Fri, 02 Oct 2026 08:00:00 GMT</pubDate><description>&lt;p&gt;Hello &lt;b&gt;world&lt;/b&gt;&lt;/p&gt;</description></item>
          <item><title>First</title><link>https://example.com/1</link><guid>g1</guid></item>
        </channel></rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Blog</title>
          <entry><title>Post B</title><id>urn:b</id><link rel="alternate" href="https://example.com/b"/><updated>2026-10-02T08:00:00Z</updated><summary>Summary B</summary></entry>
          <entry><title>Post A</title><id>urn:a</id><link href="https://example.com/a"/></entry>
        </feed>
        """;

    [Fact]
    public void Reads_rss()
    {
        var feed = FeedParser.Parse(Rss);
        Assert.Equal("Daily News", feed.Title);
        Assert.Equal(2, feed.Items.Count);
        Assert.Equal("Second & better", feed.Items[0].Title);
        Assert.Equal("https://example.com/2", feed.Items[0].Link);
        Assert.Equal("Hello world", feed.Items[0].Summary);
        Assert.Equal("g2", feed.Items[0].Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), feed.Items[0].Published);
    }

    [Fact]
    public void Reads_atom()
    {
        var feed = FeedParser.Parse(Atom);
        Assert.Equal("Blog", feed.Title);
        Assert.Equal(new[] { "Post B", "Post A" }, feed.Items.Select(i => i.Title));
        Assert.Equal("https://example.com/b", feed.Items[0].Link);
        Assert.Equal("urn:a", feed.Items[1].Id);
    }

    [Fact]
    public void Rejects_things_that_are_not_xml() =>
        Assert.ThrowsAny<Exception>(() => FeedParser.Parse("<html><body>nope"));

    [Fact]
    public void First_look_at_a_feed_reports_nothing_new()
    {
        var feed = FeedParser.Parse(Rss);
        Assert.Empty(FeedParser.NewItems(feed.Items, null));
    }

    [Fact]
    public void Later_looks_report_only_unseen_items_oldest_first()
    {
        var feed = FeedParser.Parse(Rss);
        var fresh = FeedParser.NewItems(feed.Items, new[] { "something-else" });
        Assert.Equal(new[] { "First", "Second & better" }, fresh.Select(i => i.Title));
        Assert.Equal(new[] { "Second & better" }, FeedParser.NewItems(feed.Items, new[] { "g1" }).Select(i => i.Title));
        Assert.Empty(FeedParser.NewItems(feed.Items, new[] { "g1", "g2" }));
    }

    [Fact]
    public void Page_text_drops_scripts_styles_and_markup()
    {
        const string html = "<html><head><style>p{color:red}</style><script>var x=1;</script></head><body><h1>In   stock</h1><p>Only <b>3</b> left</p></body></html>";
        Assert.Equal("In stock Only 3 left", FeedParser.PageText(html));
    }
}
