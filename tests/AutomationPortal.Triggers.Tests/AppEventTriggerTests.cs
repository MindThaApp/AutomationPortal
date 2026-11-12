using AutomationPortal.Core;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

[Collection("AppEventStatics")]
public class AppEventTriggerTests : IDisposable
{
    private sealed class Sink : ITriggerSink
    {
        public List<(ContentValue Output, string? Description)> Fired { get; } = new();
        public void Fire(ContentValue output, string? description = null) => Fired.Add((output, description));
    }

    private readonly AppEventHub _hub = new();

    public AppEventTriggerTests() => AppEventTrigger.HubSource = () => _hub;

    public void Dispose() => AppEventTrigger.HubSource = () => TP.Env.AppEvents;

    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1));

    private static AppEvent E(string name, string? payload = null) => new() { Name = name, Payload = payload, Source = "deskpal", ReceivedAt = T0 };

    private static TriggerConfig Config(string ev, string breakKind = "any") =>
        new(new Dictionary<string, ParamValue>
        {
            ["app"] = ParamValue.Choice("deskpal"), ["event"] = ParamValue.Choice(ev), ["breakKind"] = ParamValue.Choice(breakKind),
        }, new AppEventTrigger().Descriptor.Parameters);

    private static DictionaryItem Dict(ContentValue v) => Assert.IsType<DictionaryItem>(v[0]);

    private static ContentValue Field(DictionaryItem d, string key) => d.Entries.Single(e => e.Key == key).Value;

    [Fact]
    public void Only_the_chosen_event_from_the_chosen_app_starts_the_automation()
    {
        var sink = new Sink();
        using (new AppEventTrigger().Start(Config("focus.on"), sink))
        {
            _hub.Publish(E("deskpal.focus.off", "{\"reason\":\"ended\"}"));
            _hub.Publish(E("deskpal.focus.on", "{\"minutes\":25}"));
            _hub.Publish(E("otherapp.focus.on"));
        }
        var fired = Assert.Single(sink.Fired);
        Assert.Equal("Desk Pal: focus turned on", fired.Description);
        Assert.Equal(25d, ((NumberItem)Field(Dict(fired.Output), "minutes")[0]).Value);
    }

    [Fact]
    public void Stopping_the_trigger_stops_listening()
    {
        var sink = new Sink();
        var handle = new AppEventTrigger().Start(Config("pause.off"), sink);
        Assert.Equal(1, _hub.ListenerCount);
        handle.Dispose();
        Assert.Equal(0, _hub.ListenerCount);
        _hub.Publish(E("deskpal.pause.off"));
        Assert.Empty(sink.Fired);
    }

    [Fact]
    public void A_break_kind_filter_only_lets_that_kind_through()
    {
        var sink = new Sink();
        using (new AppEventTrigger().Start(Config("break.done", "walk"), sink))
        {
            _hub.Publish(E("deskpal.break.done", "{\"kind\":\"stretch\"}"));
            _hub.Publish(E("deskpal.break.done", "{\"kind\":\"walk\",\"minutes\":4}"));
            _hub.Publish(E("deskpal.break.done"));
        }
        var fired = Assert.Single(sink.Fired);
        Assert.Equal("walk", Field(Dict(fired.Output), "kind").AsText());
    }

    [Fact]
    public void Any_break_kind_matches_every_break()
    {
        var m = new AppEventMatcher("deskpal", "break.started", "any");
        Assert.True(m.Matches(E("deskpal.break.started", "{\"kind\":\"eyes\"}")));
        Assert.True(m.Matches(E("deskpal.break.started")));
        Assert.False(m.Matches(E("deskpal.break.done", "{\"kind\":\"eyes\"}")));
    }

    [Fact]
    public void The_break_kind_filter_is_ignored_for_other_events()
    {
        var m = new AppEventMatcher("deskpal", "focus.on", "walk");
        Assert.True(m.Matches(E("deskpal.focus.on", "{\"minutes\":25}")));
    }

    [Fact]
    public void Details_become_trigger_variables_with_times_as_dates()
    {
        var c = AppEventMatcher.ToContent(E("deskpal.pause.on",
            "{\"event\":\"deskpal.pause.on\",\"at\":\"2026-11-12T10:00:00+01:00\",\"cause\":\"schedule\",\"until\":\"2026-11-12T11:30:00+01:00\"}"));
        var d = Dict(c);
        Assert.Equal(new[] { "event", "at", "cause", "until" }, d.Keys);
        Assert.Equal("deskpal.pause.on", Field(d, "event").AsText());
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1)), ((DateItem)Field(d, "at")[0]).Value);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 11, 30, 0, TimeSpan.FromHours(1)), ((DateItem)Field(d, "until")[0]).Value);
        Assert.Equal("schedule", Field(d, "cause").AsText());
        Assert.Equal("Desk Pal: reminders were paused", d.Display);
    }

    [Fact]
    public void Day_summary_counts_stay_a_dictionary()
    {
        var d = Dict(AppEventMatcher.ToContent(E("deskpal.day.summary", "{\"counts\":{\"stretches\":6,\"eyeBreaks\":11,\"walks\":2}}")));
        var counts = Dict(Field(d, "counts"));
        Assert.Equal(11d, ((NumberItem)Field(counts, "eyeBreaks")[0]).Value);
    }

    [Fact]
    public void Missing_event_and_time_are_filled_in()
    {
        var d = Dict(AppEventMatcher.ToContent(E("deskpal.pause.off")));
        Assert.Equal("deskpal.pause.off", Field(d, "event").AsText());
        Assert.Equal(T0, ((DateItem)Field(d, "at")[0]).Value);
    }

    [Fact]
    public void Unknown_events_are_dropped_without_reaching_triggers()
    {
        var seen = new List<AppEvent>();
        using var sub = _hub.Subscribe(seen.Add);
        _hub.Publish(E("deskpal.something.new", "{}"));
        _hub.Publish(E("otherapp.focus.on"));
        Assert.Empty(seen);
        _hub.Publish(E("deskpal.goal.reached", "{\"goal\":\"walks\"}"));
        Assert.Single(seen);
    }

    [Fact]
    public void A_failing_listener_does_not_stop_the_others()
    {
        var seen = 0;
        using var a = _hub.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var b = _hub.Subscribe(_ => seen++);
        _hub.Publish(E("deskpal.focus.off"));
        Assert.Equal(1, seen);
    }

    [Fact]
    public void Every_event_in_the_contract_is_offered()
    {
        var offered = new AppEventTrigger().Descriptor.Parameters.Single(p => p.Key == "event").Choices!.Select(c => c.Id);
        Assert.Equal(new[] { "break.started", "break.done", "break.skipped", "focus.on", "focus.off", "pause.on", "pause.off", "goal.reached", "day.summary" }, offered);
    }
}
