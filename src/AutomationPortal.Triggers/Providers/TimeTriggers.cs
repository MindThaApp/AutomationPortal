using System.Globalization;
using Flowmate.Core;

namespace Flowmate.Triggers;

public sealed class DailyTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.daily", Title = "At a time of day", Category = TP.CatTime, Icon = "Clock24",
        Summary = "At {time} on {days}",
        Description = "Runs at the same time every day, or only on the days you pick. If the PC was asleep for a few minutes at that time, it still runs once when it wakes.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it ran",
        Parameters = new[] { TP.TimeParam("time", "Time", "08:00"), TP.DaysParam() },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var time = config.GetTime("time") ?? new TimeOnly(8, 0);
        var days = TP.ParseDays(config.GetStrings("days"));
        if (days.Count == 0) { sink.ReportProblem("Pick at least one day for this trigger."); return new DisposableAction(() => { }); }
        var text = $"{TP.TimeText(time)} ({TP.DaysText(days)})";
        var scheduler = new WallClockScheduler(now => ScheduleCalculator.NextDaily(now, time, days), _ => sink.Fire(TP.Now(), "It's " + text));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class MonthlyTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.monthly", Title = "Once a month", Category = TP.CatTime, Icon = "CalendarLtr24",
        Summary = "Every month {mode} at {time}",
        Description = "Runs once a month: on a day number (like the 15th), on a day like the first Monday, or on the last day of the month. Short months use their last day when the day number doesn't exist.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it ran",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "On", "day", new Choice("day", "on a day number"), new Choice("nth", "on a weekday like the first Monday"), new Choice("last", "on the last day")),
            ParameterDescriptor.Number("day", "Day of the month", 1, 1, 31) with { VisibleWhen = new VisibleWhen("mode", "day") },
            ParameterDescriptor.ChoiceOf("nth", "Which one", "1", new Choice("1", "First"), new Choice("2", "Second"), new Choice("3", "Third"), new Choice("4", "Fourth"), new Choice("5", "Last"))
                with { VisibleWhen = new VisibleWhen("mode", "nth") },
            ParameterDescriptor.ChoiceOf("weekday", "Weekday", "mon", TP.DayChoices) with { VisibleWhen = new VisibleWhen("mode", "nth") },
            TP.TimeParam("time", "Time", "09:00"),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    internal static MonthlyRule RuleFrom(TriggerConfig config)
    {
        var mode = config.GetChoice("mode", "day");
        return mode switch
        {
            "last" => new MonthlyRule(MonthlyMode.LastDay),
            "nth" => new MonthlyRule(MonthlyMode.NthWeekday, Nth: Math.Clamp(config.GetInt("nth", 1), 1, 5),
                Weekday: TP.DayFromId(config.GetChoice("weekday", "mon")) ?? DayOfWeek.Monday),
            _ => new MonthlyRule(MonthlyMode.DayOfMonth, Day: Math.Clamp(config.GetInt("day", 1), 1, 31)),
        };
    }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var rule = RuleFrom(config);
        var time = config.GetTime("time") ?? new TimeOnly(9, 0);
        var scheduler = new WallClockScheduler(now => ScheduleCalculator.NextMonthly(now, rule, time), _ => sink.Fire(TP.Now(), "It's the monthly time"));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class IntervalTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.interval", Title = "Every few minutes or hours", Category = TP.CatTime, Icon = "ArrowRepeatAll24",
        Summary = "Every {every} {unit}",
        Description = "Runs again and again at the interval you choose, starting from when Flow Mate starts. You can limit it to certain hours of the day.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it ran",
        Parameters = new[]
        {
            ParameterDescriptor.Number("every", "Every", 30, 1, 1440),
            ParameterDescriptor.ChoiceOf("unit", "Unit", "minutes", new Choice("minutes", "minutes"), new Choice("hours", "hours")),
            ParameterDescriptor.ChoiceOf("window", "Run", "always", new Choice("always", "at any time of day"), new Choice("limited", "only between set hours")),
            TP.TimeParam("from", "From", "09:00") with { VisibleWhen = new VisibleWhen("window", "limited") },
            TP.TimeParam("to", "Until", "17:00") with { VisibleWhen = new VisibleWhen("window", "limited") },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var every = Math.Max(1, config.GetNumber("every", 30));
        var interval = config.GetChoice("unit", "minutes") == "hours" ? TimeSpan.FromHours(every) : TimeSpan.FromMinutes(every);
        if (interval < TimeSpan.FromMinutes(1)) interval = TimeSpan.FromMinutes(1);
        ActiveHours? hours = config.GetChoice("window", "always") == "limited"
            ? new ActiveHours(config.GetTime("from") ?? new TimeOnly(9, 0), config.GetTime("to") ?? new TimeOnly(17, 0)) : null;
        var grace = TimeSpan.FromMinutes(10);
        var prev = DateTime.Now;
        var text = PluralAgreement.Fix($"every {every.ToString("0.##", CultureInfo.CurrentCulture)} {config.GetChoice("unit", "minutes")}");
        var scheduler = new WallClockScheduler(now =>
        {
            var next = ScheduleCalculator.NextInterval(prev, interval, hours);
            if (next <= now - grace) next = ScheduleCalculator.NextInterval(now, interval, hours); // slept through several runs: start over
            prev = next;
            return next;
        }, _ => sink.Fire(TP.Now(), "It's time again, " + text));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class OnceTrigger : TriggerProvider, IOneShotTrigger
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.once", Title = "On a date and time", Category = TP.CatTime, Icon = "CalendarClock24",
        Summary = "On {when}",
        Description = "Runs one time at the date and time you choose, then switches itself off. If the PC was off, it still runs when Flow Mate starts within 10 minutes after.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it ran",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "when", Label = "Date and time", Type = ParamType.DateTime, Required = true, AllowVariables = false, AllowAskEachTime = false },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var when = config.GetDate("when")?.LocalDateTime;
        if (when is null) { sink.ReportProblem("Choose a date and time for this trigger."); return new DisposableAction(() => { }); }
        if (when.Value < DateTime.Now - TimeSpan.FromMinutes(10)) { sink.ReportProblem("That date and time has already passed. Pick a new one to use this trigger again."); return new DisposableAction(() => { }); }
        var done = false;
        var scheduler = new WallClockScheduler(now => done ? null : when, _ => { done = true; sink.Fire(TP.Now(), "It's " + when.Value.ToString("f", CultureInfo.CurrentCulture)); });
        scheduler.Start();
        return scheduler;
    }
}

public sealed class SunTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.sun", Title = "At sunrise or sunset", Category = TP.CatTime, Icon = "WeatherSunny24",
        Summary = "At {event}",
        Description = "Runs at sunrise or sunset where you are, optionally some minutes before or after. Flow Mate works out your location from Windows, or from your internet address if location is turned off.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it ran",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("event", "At", "sunset", new Choice("sunrise", "sunrise"), new Choice("sunset", "sunset")),
            ParameterDescriptor.ChoiceOf("direction", "Timing", "at", new Choice("at", "exactly then"), new Choice("before", "minutes before"), new Choice("after", "minutes after")),
            ParameterDescriptor.Number("minutes", "Minutes", 30, 0, 360) with { VisibleWhen = new VisibleWhen("direction", "before", "after") },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var sunrise = config.GetChoice("event", "sunset") == "sunrise";
        var direction = config.GetChoice("direction", "at");
        var minutes = direction == "at" ? 0 : Math.Clamp(config.GetNumber("minutes", 30), 0, 360);
        var offset = TimeSpan.FromMinutes(direction == "before" ? -minutes : minutes);
        var label = sunrise ? "sunrise" : "sunset";
        var holder = new CompositeDisposable();
        var cts = new CancellationTokenSource();
        holder.Add(new DisposableAction(() => cts.Cancel()));

        _ = Task.Run(async () =>
        {
            try
            {
                GeoPoint? where = null;
                while (where is null && !cts.IsCancellationRequested)
                {
                    where = await env.Location.GetAsync(cts.Token);
                    if (where is null)
                    {
                        sink.ReportProblem("Flow Mate couldn't work out where you are, so it can't tell when the sun rises or sets. Turn on Windows location, or check your internet connection. It will try again in a few minutes.");
                        await Task.Delay(TimeSpan.FromMinutes(5), cts.Token);
                    }
                }
                if (where is null || cts.IsCancellationRequested) return;
                sink.ReportProblem(null);
                var scheduler = new WallClockScheduler(now => SunCalculator.NextEvent(now, where.Latitude, where.Longitude, sunrise, offset),
                    _ => sink.Fire(TP.Now(), $"It's {(offset == TimeSpan.Zero ? "" : minutes + " minutes " + (offset < TimeSpan.Zero ? "before " : "after "))}{label}"));
                holder.Add(scheduler);
                if (cts.IsCancellationRequested) scheduler.Dispose(); else scheduler.Start();
            }
            catch (OperationCanceledException) { }
        });
        return holder;
    }
}

public sealed class CalendarEventSoonTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "cal.eventSoon", Title = "Before a calendar event", Category = TP.CatTime, Icon = "CalendarClock24",
        Summary = "{minutes} minutes before an event in {url}",
        Description = "Runs a set number of minutes before each event in your calendar. Paste your calendar's private .ics link (Google Calendar: Settings > your calendar > Secret address in iCal format; Outlook.com: Publish calendar). No sign-in needed.",
        OutputKind = ContentKind.Dictionary, OutputDescription = "The event: Title, Start, End, Location, Description",
        Parameters = new[]
        {
            ParameterDescriptor.Number("minutes", "Minutes before", 10, 0, 1440),
            new ParameterDescriptor { Key = "url", Label = "Calendar link (.ics)", Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false,
                Placeholder = "https://calendar.google.com/calendar/ical/…/basic.ics" },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var url = config.GetString("url").Trim();
        if (url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase)) url = "https" + url[6..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            sink.ReportProblem("Paste the calendar's .ics link (it starts with https://).");
            return new DisposableAction(() => { });
        }
        var minutes = (int)Math.Clamp(config.GetNumber("minutes", 10), 0, 1440);
        var stateKey = "cal-" + TP.Hash(url);
        var fired = env.StateStore.Load<Dictionary<string, DateTimeOffset>>(stateKey) ?? new();
        string? ics = null;
        var lastFetch = DateTime.MinValue;
        IReadOnlyList<CalendarOccurrence> cache = Array.Empty<CalendarOccurrence>();
        var cacheFor = DateTime.MinValue;

        return TP.Poll(async ct =>
        {
            if (ics is null || DateTime.UtcNow - lastFetch > TimeSpan.FromMinutes(15))
            {
                try
                {
                    ics = await env.Services.Http.GetStringAsync(uri, ct);
                    lastFetch = DateTime.UtcNow;
                    sink.ReportProblem(null);
                    cacheFor = DateTime.MinValue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (ics is null) sink.ReportProblem("Couldn't download the calendar. Check the link is correct and the calendar is shared by link.");
                    lastFetch = DateTime.UtcNow - TimeSpan.FromMinutes(10); // retry in 5 minutes, keep using the old copy
                }
            }
            if (ics is null) return;
            var now = DateTimeOffset.Now;
            if (DateTime.UtcNow - cacheFor > TimeSpan.FromMinutes(5))
            {
                try { cache = IcsEventFinder.GetOccurrences(ics, now, now.AddMinutes(minutes).AddHours(26)); cacheFor = DateTime.UtcNow; }
                catch (Exception ex)
                {
                    sink.ReportProblem("Flow Mate couldn't read that calendar. Check the link ends in .ics and points to a calendar.");
                    TriggerLog.Write("ICS parse failed: " + ex.Message);
                    return;
                }
            }
            var due = IcsEventFinder.Due(cache, now, minutes, new HashSet<string>(fired.Keys));
            foreach (var o in due)
            {
                fired[o.Key] = o.Start;
                var output = ContentValue.Dict(
                    ("Title", ContentValue.Text(o.Title)), ("Start", ContentValue.Date(o.Start)), ("End", ContentValue.Date(o.End)),
                    ("Location", ContentValue.Text(o.Location ?? "")), ("Description", ContentValue.Text(o.Description ?? "")));
                sink.Fire(output, $"\"{o.Title}\" starts at {o.Start.LocalDateTime:t}");
            }
            if (due.Count > 0 || fired.Count > 0)
            {
                foreach (var old in fired.Where(kv => kv.Value < now.AddDays(-2)).Select(kv => kv.Key).ToList()) fired.Remove(old);
                if (due.Count > 0) env.StateStore.Save(stateKey, fired);
            }
        }, TimeSpan.FromSeconds(8), () => TimeSpan.FromSeconds(60));
    }
}
