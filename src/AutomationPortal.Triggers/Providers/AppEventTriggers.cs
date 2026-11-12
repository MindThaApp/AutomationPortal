using System.Globalization;
using System.Text.Json;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>One event a known app can send, with the words the trigger picker shows.</summary>
internal sealed record KnownAppEvent(string Id, string Label, string Happened);

/// <summary>An app that sends events to Automation Portal (see "Pipe contract v1" in docs/CLI.md).</summary>
internal sealed record KnownEventApp(string Id, string Name, IReadOnlyList<KnownAppEvent> Events);

/// <summary>The apps and events the "When another app sends an event" trigger offers. Other event names are accepted and ignored.</summary>
internal static class AppEventCatalog
{
    public static readonly KnownEventApp DeskPal = new("deskpal", "Desk Pal", new KnownAppEvent[]
    {
        new("break.started", "A break starts", "a break started"),
        new("break.done", "A break is done", "a break is done"),
        new("break.skipped", "A break is skipped", "a break was skipped"),
        new("focus.on", "Focus turns on", "focus turned on"),
        new("focus.off", "Focus turns off", "focus turned off"),
        new("pause.on", "Reminders are paused", "reminders were paused"),
        new("pause.off", "Reminders resume", "reminders resumed"),
        new("goal.reached", "A daily goal is reached", "a daily goal was reached"),
        new("day.summary", "The day's summary is ready", "the day's summary is ready"),
    });

    public static IReadOnlyList<KnownEventApp> Apps { get; } = new[] { DeskPal };

    public static KnownEventApp? FindApp(string id) => Apps.FirstOrDefault(a => a.Id == id);

    public static KnownAppEvent? Find(string appId, string eventId) => FindApp(appId)?.Events.FirstOrDefault(e => e.Id == eventId);

    public static bool IsKnown(AppEvent e) => Find(e.App, e.Event) is not null;
}

/// <summary>
/// Hands events that arrive over the pipe to the triggers listening for them. Publishing never waits for a shortcut:
/// triggers only call <see cref="ITriggerSink.Fire"/>, which queues the run.
/// </summary>
public sealed class AppEventHub
{
    private const int MaxLoggedUnknown = 50;
    private readonly object _gate = new();
    private readonly List<Action<AppEvent>> _handlers = new();
    private readonly HashSet<string> _loggedUnknown = new();

    /// <summary>Delivers <paramref name="e"/> to every listening trigger. Events Automation Portal doesn't know are noted in the log once and dropped.</summary>
    public void Publish(AppEvent e)
    {
        if (!AppEventCatalog.IsKnown(e))
        {
            bool log;
            lock (_gate) log = _loggedUnknown.Count < MaxLoggedUnknown && _loggedUnknown.Add(e.Name);
            if (log) TriggerLog.Write($"Ignored the event \"{e.Name}\": Automation Portal doesn't know it.");
            return;
        }
        Action<AppEvent>[] copy;
        lock (_gate) copy = _handlers.ToArray();
        foreach (var h in copy) TriggerLog.Guard(() => h(e), "App event " + e.Name);
    }

    internal IDisposable Subscribe(Action<AppEvent> handler)
    {
        lock (_gate) _handlers.Add(handler);
        return new DisposableAction(() => { lock (_gate) _handlers.Remove(handler); });
    }

    internal int ListenerCount { get { lock (_gate) return _handlers.Count; } }
}

/// <summary>Decides whether an event is the one an automation waits for, and what the shortcut gets. Pure, for tests.</summary>
internal sealed class AppEventMatcher
{
    private readonly string _app, _event, _breakKind;

    public AppEventMatcher(string app, string eventId, string breakKind)
    {
        _app = app.Trim().ToLowerInvariant();
        _event = eventId.Trim().ToLowerInvariant();
        _breakKind = string.IsNullOrWhiteSpace(breakKind) ? "any" : breakKind.Trim().ToLowerInvariant();
    }

    public bool Matches(AppEvent e)
    {
        if (e.App != _app || e.Event != _event) return false;
        if (_breakKind == "any" || !_event.StartsWith("break.", StringComparison.Ordinal)) return true;
        return string.Equals(ReadString(e.Payload, "kind"), _breakKind, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"Desk Pal: a break started".</summary>
    public static string Describe(AppEvent e)
    {
        var app = AppEventCatalog.FindApp(e.App);
        var known = AppEventCatalog.Find(e.App, e.Event);
        return $"{app?.Name ?? e.App}: {known?.Happened ?? e.Event}";
    }

    /// <summary>
    /// The event's details as a dictionary with the sender's field names (event, at, cause and the event's own fields).
    /// Times (at, until) become dates; event and at are filled in when the sender left them out.
    /// </summary>
    public static ContentValue ToContent(AppEvent e)
    {
        var entries = new List<KeyValuePair<string, ContentValue>>();
        if (e.Payload is { } payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (entries.Any(x => x.Key == p.Name)) continue;
                    entries.Add(new(p.Name, IsTimeField(p.Name) && p.Value.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(p.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                            ? ContentValue.Date(t)
                            : ContentValue.FromJsonElement(p.Value)));
                }
            }
            catch (JsonException) { entries.Clear(); }
        }
        if (!entries.Any(x => x.Key == "event")) entries.Insert(0, new("event", ContentValue.Text(e.Name)));
        if (!entries.Any(x => x.Key == "at")) entries.Insert(1, new("at", ContentValue.Date(e.ReceivedAt)));
        return ContentValue.Dict(entries, Describe(e));
    }

    private static bool IsTimeField(string name) => name is "at" or "until";

    private static string? ReadString(string? json, string field)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed class AppEventTrigger : TriggerProvider
{
    private static readonly string[] BreakEvents = { "break.started", "break.done", "break.skipped" };

    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.event", Title = "When another app sends an event", Category = TP.CatApps, Icon = "PlugConnected24",
        Summary = "When {app} says: {event}",
        Description = "Runs when another app on this PC tells Automation Portal that something happened, for example when Desk Pal starts a break or focus turns on. " +
                      "The app must be running and have sharing events with Automation Portal turned on in its settings. Nothing is sent to the internet.",
        OutputKind = ContentKind.Dictionary,
        OutputDescription = "The event's details: event, at (when it happened), cause (user or schedule), and fields such as kind, minutes, reason, until, goal or counts",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("app", "App", AppEventCatalog.DeskPal.Id,
                AppEventCatalog.Apps.Select(a => new Choice(a.Id, a.Name)).ToArray()) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("event", "Event", AppEventCatalog.DeskPal.Events[0].Id,
                AppEventCatalog.DeskPal.Events.Select(e => new Choice(e.Id, e.Label)).ToArray()) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("breakKind", "Kind of break", "any",
                new Choice("any", "Any"), new Choice("stretch", "Stretch"), new Choice("water", "Water"), new Choice("eyes", "Eye break"), new Choice("walk", "Walk"))
                with { AllowVariables = false, AllowAskEachTime = false, VisibleWhen = new VisibleWhen("event", BreakEvents) },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    /// <summary>Where events come from. Tests swap in their own hub.</summary>
    internal static Func<AppEventHub> HubSource { get; set; } = () => TP.Env.AppEvents;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var matcher = new AppEventMatcher(config.GetChoice("app", AppEventCatalog.DeskPal.Id), config.GetChoice("event", AppEventCatalog.DeskPal.Events[0].Id),
            config.GetChoice("breakKind", "any"));
        return HubSource().Subscribe(e =>
        {
            if (matcher.Matches(e)) sink.Fire(AppEventMatcher.ToContent(e), AppEventMatcher.Describe(e));
        });
    }
}
