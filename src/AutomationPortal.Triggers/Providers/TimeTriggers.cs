using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public sealed class DailyTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.daily", Title = Strings.Get("Trigger_TimeDaily_Title"), Category = TP.CatTime, Icon = "Clock24",
        Summary = Strings.Get("Trigger_TimeDaily_Summary"),
        Description = Strings.Get("Trigger_TimeDaily_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_TimeDaily_OutputDescription"),
        Parameters = new[] { TP.TimeParam("time", Strings.Get("Trigger_TimeDaily_Param_Time_Label"), "08:00"), TP.DaysParam() },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var time = config.GetTime("time") ?? new TimeOnly(8, 0);
        var days = TP.ParseDays(config.GetStrings("days"));
        if (days.Count == 0) { sink.ReportProblem(Strings.Get("Trigger_TimeDaily_Problem_PickAtLeastOne")); return new DisposableAction(() => { }); }
        var text = Strings.Format("Trigger_TimeDaily_Text_TimeAndDays", TP.TimeText(time), TP.DaysText(days));
        var scheduler = new WallClockScheduler(now => ScheduleCalculator.NextDaily(now, time, days), _ => sink.Fire(TP.Now(), Strings.Format("Trigger_Common_Fired_Its", text)));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class MonthlyTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.monthly", Title = Strings.Get("Trigger_TimeMonthly_Title"), Category = TP.CatTime, Icon = "CalendarLtr24",
        Summary = Strings.Get("Trigger_TimeMonthly_Summary"),
        Description = Strings.Get("Trigger_TimeMonthly_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_TimeDaily_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("Trigger_TimeMonthly_Param_Mode_Label"), "day", new Choice("day", Strings.Get("Trigger_TimeMonthly_Param_Mode_Choice_Day")), new Choice("nth", Strings.Get("Trigger_TimeMonthly_Param_Mode_Choice_Nth")), new Choice("last", Strings.Get("Trigger_TimeMonthly_Param_Mode_Choice_Last"))),
            ParameterDescriptor.Number("day", Strings.Get("Trigger_TimeMonthly_Param_Day_Label"), 1, 1, 31) with { VisibleWhen = new VisibleWhen("mode", "day") },
            ParameterDescriptor.ChoiceOf("nth", Strings.Get("Trigger_TimeMonthly_Param_Nth_Label"), "1", new Choice("1", Strings.Get("Trigger_TimeMonthly_Param_Nth_Choice_1")), new Choice("2", Strings.Get("Trigger_TimeMonthly_Param_Nth_Choice_2")), new Choice("3", Strings.Get("Trigger_TimeMonthly_Param_Nth_Choice_3")), new Choice("4", Strings.Get("Trigger_TimeMonthly_Param_Nth_Choice_4")), new Choice("5", Strings.Get("Trigger_TimeMonthly_Param_Nth_Choice_5")))
                with { VisibleWhen = new VisibleWhen("mode", "nth") },
            ParameterDescriptor.ChoiceOf("weekday", Strings.Get("Trigger_TimeMonthly_Param_Weekday_Label"), "mon", TP.DayChoices) with { VisibleWhen = new VisibleWhen("mode", "nth") },
            TP.TimeParam("time", Strings.Get("Trigger_TimeMonthly_Param_Time_Label"), "09:00"),
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
        var scheduler = new WallClockScheduler(now => ScheduleCalculator.NextMonthly(now, rule, time), _ => sink.Fire(TP.Now(), Strings.Get("Trigger_TimeMonthly_Fired_ItSTheMonthly")));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class IntervalTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.interval", Title = Strings.Get("Trigger_TimeInterval_Title"), Category = TP.CatTime, Icon = "ArrowRepeatAll24",
        Summary = Strings.Get("Trigger_TimeInterval_Summary"),
        Description = Strings.Get("Trigger_TimeInterval_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_TimeDaily_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.Number("every", Strings.Get("Trigger_TimeInterval_Param_Every_Label"), 30, 1, 1440),
            ParameterDescriptor.ChoiceOf("unit", Strings.Get("Trigger_TimeInterval_Param_Unit_Label"), "minutes", new Choice("minutes", Strings.Get("Trigger_TimeInterval_Param_Unit_Choice_Minutes")), new Choice("hours", Strings.Get("Trigger_TimeInterval_Param_Unit_Choice_Hours"))),
            ParameterDescriptor.ChoiceOf("window", Strings.Get("Trigger_TimeInterval_Param_Window_Label"), "always", new Choice("always", Strings.Get("Trigger_TimeInterval_Param_Window_Choice_Always")), new Choice("limited", Strings.Get("Trigger_TimeInterval_Param_Window_Choice_Limited"))),
            TP.TimeParam("from", Strings.Get("Trigger_TimeInterval_Param_From_Label"), "09:00") with { VisibleWhen = new VisibleWhen("window", "limited") },
            TP.TimeParam("to", Strings.Get("Trigger_TimeInterval_Param_To_Label"), "17:00") with { VisibleWhen = new VisibleWhen("window", "limited") },
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
        var text = PluralAgreement.Fix(Strings.Format("Trigger_TimeInterval_Text_Every", every.ToString("0.##", CultureInfo.CurrentCulture), Strings.Get(config.GetChoice("unit", "minutes") == "hours" ? "Trigger_TimeInterval_Param_Unit_Choice_Hours" : "Trigger_TimeInterval_Param_Unit_Choice_Minutes")));
        var scheduler = new WallClockScheduler(now =>
        {
            var next = ScheduleCalculator.NextInterval(prev, interval, hours);
            if (next <= now - grace) next = ScheduleCalculator.NextInterval(now, interval, hours); // slept through several runs: start over
            prev = next;
            return next;
        }, _ => sink.Fire(TP.Now(), Strings.Format("Trigger_TimeInterval_Fired_Again", text)));
        scheduler.Start();
        return scheduler;
    }
}

public sealed class OnceTrigger : TriggerProvider, IOneShotTrigger
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.once", Title = Strings.Get("Trigger_TimeOnce_Title"), Category = TP.CatTime, Icon = "CalendarClock24",
        Summary = Strings.Get("Trigger_TimeOnce_Summary"),
        Description = Strings.Get("Trigger_TimeOnce_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_TimeDaily_OutputDescription"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "when", Label = Strings.Get("Trigger_TimeOnce_Param_When_Label"), Type = ParamType.DateTime, Required = true, AllowVariables = false, AllowAskEachTime = false },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var when = config.GetDate("when")?.LocalDateTime;
        if (when is null) { sink.ReportProblem(Strings.Get("Trigger_TimeOnce_Problem_ChooseADateAnd")); return new DisposableAction(() => { }); }
        if (when.Value < DateTime.Now - TimeSpan.FromMinutes(10)) { sink.ReportProblem(Strings.Get("Trigger_TimeOnce_Problem_ThatDateAndTime")); return new DisposableAction(() => { }); }
        var done = false;
        var scheduler = new WallClockScheduler(now => done ? null : when, _ => { done = true; sink.Fire(TP.Now(), Strings.Format("Trigger_Common_Fired_Its", when.Value.ToString("f", CultureInfo.CurrentCulture))); });
        scheduler.Start();
        return scheduler;
    }
}

public sealed class SunTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "time.sun", Title = Strings.Get("Trigger_TimeSun_Title"), Category = TP.CatTime, Icon = "WeatherSunny24",
        Summary = Strings.Get("Trigger_TimeSun_Summary"),
        Description = Strings.Get("Trigger_TimeSun_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_TimeDaily_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_TimeSun_Param_Event_Label"), "sunset", new Choice("sunrise", Strings.Get("Trigger_TimeSun_Param_Event_Choice_Sunrise")), new Choice("sunset", Strings.Get("Trigger_TimeSun_Param_Event_Choice_Sunset"))),
            ParameterDescriptor.ChoiceOf("direction", Strings.Get("Trigger_TimeSun_Param_Direction_Label"), "at", new Choice("at", Strings.Get("Trigger_TimeSun_Param_Direction_Choice_At")), new Choice("before", Strings.Get("Trigger_TimeSun_Param_Direction_Choice_Before")), new Choice("after", Strings.Get("Trigger_TimeSun_Param_Direction_Choice_After"))),
            ParameterDescriptor.Number("minutes", Strings.Get("Trigger_TimeSun_Param_Minutes_Label"), 30, 0, 360) with { VisibleWhen = new VisibleWhen("direction", "before", "after") },
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
        var label = Strings.Get(sunrise ? "Trigger_TimeSun_Param_Event_Choice_Sunrise" : "Trigger_TimeSun_Param_Event_Choice_Sunset");
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
                        sink.ReportProblem(Strings.Get("Trigger_TimeSun_Problem_AutomationPortalCouldnT"));
                        await Task.Delay(TimeSpan.FromMinutes(5), cts.Token);
                    }
                }
                if (where is null || cts.IsCancellationRequested) return;
                sink.ReportProblem(null);
                var scheduler = new WallClockScheduler(now => SunCalculator.NextEvent(now, where.Latitude, where.Longitude, sunrise, offset),
                    _ => sink.Fire(TP.Now(), offset == TimeSpan.Zero ? Strings.Format("Trigger_Common_Fired_Its", label) : Strings.Format(offset < TimeSpan.Zero ? "Trigger_TimeSun_Fired_MinutesBefore" : "Trigger_TimeSun_Fired_MinutesAfter", minutes, label)));
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
        Id = "cal.eventSoon", Title = Strings.Get("Trigger_CalEventSoon_Title"), Category = TP.CatTime, Icon = "CalendarClock24",
        Summary = Strings.Get("Trigger_CalEventSoon_Summary"),
        Description = Strings.Get("Trigger_CalEventSoon_Description"),
        OutputKind = ContentKind.Dictionary, OutputDescription = Strings.Get("Trigger_CalEventSoon_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.Number("minutes", Strings.Get("Trigger_CalEventSoon_Param_Minutes_Label"), 10, 0, 1440),
            new ParameterDescriptor { Key = "url", Label = Strings.Get("Trigger_CalEventSoon_Param_Url_Label"), Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false,
                Placeholder = Strings.Get("Trigger_CalEventSoon_Param_Url_Placeholder") },
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
            sink.ReportProblem(Strings.Get("Trigger_CalEventSoon_Problem_PasteTheCalendarS"));
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
                    if (ics is null) sink.ReportProblem(Strings.Get("Trigger_CalEventSoon_Problem_CouldnTDownloadThe"));
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
                    sink.ReportProblem(Strings.Get("Trigger_CalEventSoon_Problem_AutomationPortalCouldnT"));
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
                sink.Fire(output, Strings.Format("Trigger_CalEventSoon_Fired_Starts", o.Title, o.Start.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)));
            }
            if (due.Count > 0 || fired.Count > 0)
            {
                foreach (var old in fired.Where(kv => kv.Value < now.AddDays(-2)).Select(kv => kv.Key).ToList()) fired.Remove(old);
                if (due.Count > 0) env.StateStore.Save(stateKey, fired);
            }
        }, TimeSpan.FromSeconds(8), () => TimeSpan.FromSeconds(60));
    }
}
