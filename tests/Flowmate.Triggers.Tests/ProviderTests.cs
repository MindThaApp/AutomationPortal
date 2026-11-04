using Flowmate.Core;
using Flowmate.Core.Testing;
using Flowmate.Triggers;

namespace Flowmate.Triggers.Tests;

public class ProviderCatalogTests
{
    private static readonly string[] RequiredTriggers =
    {
        "time.daily", "time.monthly", "time.interval", "time.once", "time.sun", "cal.eventSoon",
        "call.started", "call.ended", "screen.screenshotTaken",
        "web.rssNew", "web.pageChanged", "weather.alert",
        "system.signIn", "system.wake", "system.lock", "system.unlock", "system.idle", "system.back",
        "app.opened", "app.closed", "app.focused", "file.changed", "drive.connected",
        "net.wifiJoined", "net.wifiLeft", "net.online", "net.offline",
        "power.pluggedIn", "power.unplugged", "power.battery", "power.saver", "power.plan",
        "device.bluetooth", "device.usb", "device.display", "device.audio",
        "key.shortcut", "key.typed", "clip.changed",
    };

    private static readonly string[] RequiredConditions =
    {
        "cond.timeBetween", "cond.days", "cond.power", "cond.battery", "cond.powerPlan", "cond.wifi", "cond.appRunning", "cond.appFront",
        "cond.notFullscreen", "cond.online", "cond.inCall",
    };

    private static (TriggerRegistry Triggers, ConditionRegistry Conditions) Load()
    {
        var t = new TriggerRegistry();
        var c = new ConditionRegistry();
        TriggerHost.RegisterProviders(t, c);
        return (t, c);
    }

    [Fact]
    public void Every_trigger_and_condition_from_the_spec_is_registered_without_clashes()
    {
        var (t, c) = Load();
        Assert.Empty(t.Diagnostics);
        Assert.Empty(c.Diagnostics);
        foreach (var id in RequiredTriggers) Assert.NotNull(t.Get(id));
        foreach (var id in RequiredConditions) Assert.NotNull(c.Get(id));
        Assert.Equal(RequiredTriggers.Length, t.All.Count);
        Assert.Equal(RequiredConditions.Length, c.All.Count);
    }

    [Fact]
    public void Registering_twice_does_not_create_duplicates()
    {
        var (t, c) = Load();
        TriggerHost.RegisterProviders(t, c);
        Assert.Empty(t.Diagnostics);
        Assert.Equal(RequiredTriggers.Length, t.All.Count);
    }

    [Fact]
    public void Trigger_summaries_only_use_real_parameter_keys_and_have_plain_English_text()
    {
        var (t, _) = Load();
        foreach (var d in t.All.Select(p => p.Descriptor))
        {
            var keys = d.Parameters.Select(p => p.Key).ToHashSet();
            Assert.False(string.IsNullOrWhiteSpace(d.Title), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Summary), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Icon), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.OutputDescription), d.Id);
            foreach (var seg in d.SummarySegments.Where(s => s.IsToken)) Assert.Contains(seg.ParamKey!, keys);
            AssertPlainEnglish(d.Id, new[] { d.Title, d.Summary, d.Description, d.OutputDescription! }.Concat(d.Parameters.SelectMany(Texts)));
            AssertParametersWellFormed(d.Id, d.Parameters);
        }
    }

    [Fact]
    public void Condition_summaries_only_use_real_parameter_keys_and_have_plain_English_text()
    {
        var (_, c) = Load();
        foreach (var d in c.All.Select(p => p.Descriptor))
        {
            var keys = d.Parameters.Select(p => p.Key).ToHashSet();
            Assert.False(string.IsNullOrWhiteSpace(d.Title), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Summary), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id);
            foreach (var seg in d.SummarySegments.Where(s => s.IsToken)) Assert.Contains(seg.ParamKey!, keys);
            AssertPlainEnglish(d.Id, new[] { d.Title, d.Summary, d.Description }.Concat(d.Parameters.SelectMany(Texts)));
            AssertParametersWellFormed(d.Id, d.Parameters);
        }
    }

    [Fact]
    public void Triggers_use_the_spec_categories()
    {
        var (t, _) = Load();
        var allowed = new[] { "Time", "Calls & Screen", "Web & Weather", "System", "Apps", "Files & Drives", "Network", "Power", "Devices", "Keyboard", "Clipboard" };
        Assert.All(t.All, p => Assert.Contains(p.Descriptor.Category, allowed));
        Assert.Equal(allowed.OrderBy(x => x), t.Categories.OrderBy(x => x));
    }

    private static IEnumerable<string> Texts(ParameterDescriptor p) =>
        new[] { p.Label, p.Hint ?? "", p.Placeholder ?? "" }.Concat(p.Choices?.Select(c => c.Label) ?? Enumerable.Empty<string>());

    private static void AssertPlainEnglish(string id, IEnumerable<string> texts)
    {
        foreach (var raw in texts)
            foreach (var banned in new[] { "hotkey", "regex", "directory", "GUID", "JSON", "enum" })
                Assert.False(System.Text.RegularExpressions.Regex.Replace(raw, "\\{[^}]*\\}", "").Contains(banned, StringComparison.OrdinalIgnoreCase), $"{id}: \"{raw}\" should say it in plain English (no '{banned}').");
    }

    private static void AssertParametersWellFormed(string id, IReadOnlyList<ParameterDescriptor> parameters)
    {
        Assert.Equal(parameters.Count, parameters.Select(p => p.Key).Distinct().Count());
        foreach (var p in parameters)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Label), $"{id}.{p.Key}");
            if (p.Type == ParamType.Choice)
            {
                Assert.True(p.Choices is { Count: > 0 } || p.ChoicesProvider is not null, $"{id}.{p.Key} has no choices");
                if (p.Choices is { } cs && p.Default is { } d) Assert.Contains(d.ToLiteralText(), cs.Select(c => c.Id));
            }
            if (p.VisibleWhen is { } v) Assert.Contains(v.Key, parameters.Select(x => x.Key));
        }
    }

    [Fact]
    public void Sample_input_exists_for_every_trigger_and_matches_what_the_trigger_promises()
    {
        using var host = new TestHost();
        var (t, _) = Load();
        foreach (var p in t.All)
        {
            var d = p.Descriptor;
            var sample = SampleInputs.For(d, new TriggerConfig(null, d.Parameters), host.Services.Paths);
            Assert.False(sample.IsEmpty, d.Id);
            switch (d.OutputKind)
            {
                case ContentKind.Text: Assert.IsType<TextItem>(sample[0]); break;
                case ContentKind.Number: Assert.IsType<NumberItem>(sample[0]); break;
                case ContentKind.Date: Assert.IsType<DateItem>(sample[0]); break;
                case ContentKind.Url: Assert.IsType<UrlItem>(sample[0]); break;
                case ContentKind.Dictionary: Assert.IsType<DictionaryItem>(sample[0]); break;
                case ContentKind.Image: Assert.IsType<ImageItem>(sample[0]); Assert.True(File.Exists(((ImageItem)sample[0]).Path)); break;
                case ContentKind.File: Assert.True(File.Exists(((PathItem)sample[0]).Path)); break;
            }
        }
    }

    [Fact]
    public void Sample_input_follows_the_configured_app_and_clipboard_kind()
    {
        using var host = new TestHost();
        var app = SampleInputs.For(new AppOpenedTrigger().Descriptor, TriggerConfig.Of(("app", ParamValue.App("C:\\x\\spotify.exe", "Spotify"))), host.Services.Paths);
        Assert.Equal("Spotify", ((AppItem)app[0]).Name);

        var clip = new ClipboardChangedTrigger().Descriptor;
        Assert.IsType<UrlItem>(SampleInputs.For(clip, TriggerConfig.Of(("kind", ParamValue.Choice("link"))), host.Services.Paths)[0]);
        Assert.IsType<ImageItem>(SampleInputs.For(clip, TriggerConfig.Of(("kind", ParamValue.Choice("image"))), host.Services.Paths)[0]);
    }
}

public class ConditionLogicTests
{
    [Theory]
    [InlineData("09:00", "09:00", "17:00", true)]
    [InlineData("16:59", "09:00", "17:00", true)]
    [InlineData("17:00", "09:00", "17:00", false)]
    [InlineData("08:59", "09:00", "17:00", false)]
    [InlineData("23:30", "22:00", "06:00", true)]
    [InlineData("02:00", "22:00", "06:00", true)]
    [InlineData("12:00", "22:00", "06:00", false)]
    [InlineData("06:00", "22:00", "06:00", false)]
    [InlineData("12:00", "08:00", "08:00", true)]
    public void Time_between_including_overnight(string now, string from, string to, bool expected) =>
        Assert.Equal(expected, ConditionLogic.IsBetween(TimeOnly.Parse(now), TimeOnly.Parse(from), TimeOnly.Parse(to)));

    [Fact]
    public void Day_checks()
    {
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday };
        Assert.True(ConditionLogic.IsDayAllowed(DayOfWeek.Monday, weekdays));
        Assert.False(ConditionLogic.IsDayAllowed(DayOfWeek.Sunday, weekdays));
    }

    [Theory]
    [InlineData("below", 20, 15, true)]
    [InlineData("below", 20, 20, false)]
    [InlineData("above", 80, 81, true)]
    [InlineData("above", 80, 80, false)]
    [InlineData("below", 20, null, false)]
    [InlineData("above", 20, null, false)]
    public void Battery_checks(string direction, int level, int? battery, bool expected) =>
        Assert.Equal(expected, ConditionLogic.BatteryIs(direction, level, battery));

    [Theory]
    [InlineData("Home", "", true)]
    [InlineData("Home", "home", true)]
    [InlineData("Home", "Work", false)]
    [InlineData(null, "", false)]
    [InlineData(null, "Home", false)]
    public void Wifi_checks(string? current, string wanted, bool expected) =>
        Assert.Equal(expected, ConditionLogic.WifiMatches(current, wanted));

    [Fact]
    public void Full_screen_means_the_window_covers_the_whole_monitor()
    {
        var monitor = new Flowmate.Triggers.Native.Win32.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        Assert.True(ConditionLogic.CoversMonitor(new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }, monitor));
        Assert.True(ConditionLogic.CoversMonitor(new() { Left = -8, Top = -8, Right = 1928, Bottom = 1088 }, monitor));
        Assert.False(ConditionLogic.CoversMonitor(new() { Left = 0, Top = 0, Right = 1920, Bottom = 1040 }, monitor));
    }

    private static Task<bool> Check(ConditionProvider c, params (string, ParamValue)[] values) =>
        c.IsMetAsync(TriggerConfig.Of(values), CancellationToken.None);

    [Fact]
    public async Task Condition_providers_read_their_settings()
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        Assert.True(await Check(new TimeBetweenCondition(), ("from", ParamValue.Time(now.AddHours(-1))), ("to", ParamValue.Time(now.AddHours(1)))));
        Assert.False(await Check(new TimeBetweenCondition(), ("from", ParamValue.Time(now.AddHours(1))), ("to", ParamValue.Time(now.AddHours(2)))));

        var today = TP.AllDays;
        Assert.True(await Check(new DaysCondition(), ("days", today)));
        Assert.False(await Check(new DaysCondition(), ("days", ParamValue.List(Array.Empty<string>()))));

        Assert.True(await Check(new AppRunningCondition(), ("app", ParamValue.App("Definitely Not An App 12345")), ("state", ParamValue.Choice("notRunning"))));
        Assert.False(await Check(new AppRunningCondition(), ("app", ParamValue.App("Definitely Not An App 12345")), ("state", ParamValue.Choice("running"))));
        Assert.False(await Check(new AppFrontCondition(), ("app", ParamValue.App("Definitely Not An App 12345")), ("state", ParamValue.Choice("front"))));
    }
}

public class TriggerDetailTests
{
    [Fact]
    public void Monthly_rule_is_built_from_the_settings()
    {
        var r = MonthlyTrigger.RuleFrom(TriggerConfig.Of(("mode", ParamValue.Choice("nth")), ("nth", ParamValue.Choice("5")), ("weekday", ParamValue.Choice("fri"))));
        Assert.Equal(new MonthlyRule(MonthlyMode.NthWeekday, Nth: 5, Weekday: DayOfWeek.Friday), r);
        Assert.Equal(MonthlyMode.LastDay, MonthlyTrigger.RuleFrom(TriggerConfig.Of(("mode", ParamValue.Choice("last")))).Mode);
        Assert.Equal(15, MonthlyTrigger.RuleFrom(TriggerConfig.Of(("mode", ParamValue.Choice("day")), ("day", ParamValue.Number(15)))).Day);
    }

    [Theory]
    [InlineData("any", null, "hi", 0, false, true)]
    [InlineData("text", null, "hi", 0, false, true)]
    [InlineData("text", null, null, 0, true, false)]
    [InlineData("link", null, "https://example.com/a", 0, false, true)]
    [InlineData("link", null, "see https://example.com", 0, false, false)]
    [InlineData("image", null, null, 0, true, true)]
    [InlineData("files", null, null, 2, false, true)]
    [InlineData("files", null, "hi", 0, false, false)]
    [InlineData("contains", "invoice", "Your INVOICE is ready", 0, false, true)]
    [InlineData("contains", "invoice", "hello", 0, false, false)]
    [InlineData("contains", "", "hello", 0, false, false)]
    public void Clipboard_kinds(string kind, string? contains, string? text, int files, bool image, bool expected) =>
        Assert.Equal(expected, ClipboardChangedTrigger.Accepts(kind, contains, text, files, image));

    [Fact]
    public void Config_defaults_make_every_trigger_start_with_sensible_values()
    {
        var d = new DailyTrigger().Descriptor;
        var cfg = new TriggerConfig(null, d.Parameters);
        Assert.Equal(new TimeOnly(8, 0), cfg.GetTime("time"));
        Assert.Equal(7, TP.ParseDays(cfg.GetStrings("days")).Count);
    }

    [Fact]
    public void Days_are_parsed_from_ids_and_summarised()
    {
        var days = TP.ParseDays(new[] { "mon", "TUE", "Friday", "nonsense", "mon" });
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Friday }, days);
        Assert.Equal("every day", TP.DaysText(ScheduleCalculator.EveryDay.ToList()));
        Assert.Equal("weekdays", TP.DaysText(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }));
        Assert.Equal("weekends", TP.DaysText(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
    }

    [Fact]
    public void Daily_trigger_without_days_reports_a_problem_instead_of_running()
    {
        var sink = new RecordingSink();
        using var handle = new DailyTrigger().Start(TriggerConfig.Of(("time", ParamValue.Time("08:00")), ("days", ParamValue.List(Array.Empty<string>()))), sink);
        Assert.Contains("at least one day", sink.Problem);
    }

    [Fact]
    public void Once_trigger_in_the_past_reports_a_problem()
    {
        var sink = new RecordingSink();
        using var handle = new OnceTrigger().Start(TriggerConfig.Of(("when", ParamValue.Date(DateTimeOffset.Now.AddDays(-2)))), sink);
        Assert.Contains("already passed", sink.Problem);
        Assert.IsAssignableFrom<IOneShotTrigger>(new OnceTrigger());
    }

    [Fact]
    public void Once_trigger_that_is_due_fires_straight_away()
    {
        var sink = new RecordingSink();
        using var handle = new OnceTrigger().Start(TriggerConfig.Of(("when", ParamValue.Date(DateTimeOffset.Now.AddMinutes(-1)))), sink);
        SpinWait.SpinUntil(() => sink.Fired.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Single(sink.Fired);
    }

    [Fact]
    public void Interval_trigger_fires_nothing_until_the_interval_has_passed()
    {
        var sink = new RecordingSink();
        using var handle = new IntervalTrigger().Start(TriggerConfig.Of(("every", ParamValue.Number(30)), ("unit", ParamValue.Choice("minutes"))), sink);
        Thread.Sleep(200);
        Assert.Empty(sink.Fired);
    }

    private sealed class RecordingSink : ITriggerSink, ITriggerSinkExtras
    {
        public List<ContentValue> Fired { get; } = new();
        public string? Problem { get; private set; }
        public void Fire(ContentValue output, string? description = null) { lock (Fired) Fired.Add(output); }
        public void ReportProblem(string? message) => Problem = message;
        public Task<ContentValue?> FireAndWaitAsync(ContentValue output, string? description) { Fire(output, description); return Task.FromResult<ContentValue?>(output); }
    }
}
