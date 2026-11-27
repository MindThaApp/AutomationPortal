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
        new("break.started", Strings.Get("AppEvent_DeskPal_BreakStarted_Label"), Strings.Get("AppEvent_DeskPal_BreakStarted_Happened")),
        new("break.done", Strings.Get("AppEvent_DeskPal_BreakDone_Label"), Strings.Get("AppEvent_DeskPal_BreakDone_Happened")),
        new("break.skipped", Strings.Get("AppEvent_DeskPal_BreakSkipped_Label"), Strings.Get("AppEvent_DeskPal_BreakSkipped_Happened")),
        new("focus.on", Strings.Get("AppEvent_DeskPal_FocusOn_Label"), Strings.Get("AppEvent_DeskPal_FocusOn_Happened")),
        new("focus.off", Strings.Get("AppEvent_DeskPal_FocusOff_Label"), Strings.Get("AppEvent_DeskPal_FocusOff_Happened")),
        new("pause.on", Strings.Get("AppEvent_DeskPal_PauseOn_Label"), Strings.Get("AppEvent_DeskPal_PauseOn_Happened")),
        new("pause.off", Strings.Get("AppEvent_DeskPal_PauseOff_Label"), Strings.Get("AppEvent_DeskPal_PauseOff_Happened")),
        new("goal.reached", Strings.Get("AppEvent_DeskPal_GoalReached_Label"), Strings.Get("AppEvent_DeskPal_GoalReached_Happened")),
        new("day.summary", Strings.Get("AppEvent_DeskPal_DaySummary_Label"), Strings.Get("AppEvent_DeskPal_DaySummary_Happened")),
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
        return Strings.Format("AppEvent_Describe", app?.Name ?? e.App, known?.Happened ?? e.Event);
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
        Id = "app.event", Title = Strings.Get("Trigger_AppEvent_Title"), Category = TP.CatApps, Icon = "PlugConnected24",
        Summary = Strings.Get("Trigger_AppEvent_Summary"),
        Description = Strings.Get("Trigger_AppEvent_Description"),
        OutputKind = ContentKind.Dictionary,
        OutputDescription = Strings.Get("Trigger_AppEvent_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("app", Strings.Get("Trigger_AppEvent_Param_App_Label"), AppEventCatalog.DeskPal.Id,
                AppEventCatalog.Apps.Select(a => new Choice(a.Id, a.Name)).ToArray()) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_AppEvent_Param_Event_Label"), AppEventCatalog.DeskPal.Events[0].Id,
                AppEventCatalog.DeskPal.Events.Select(e => new Choice(e.Id, e.Label)).ToArray()) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("breakKind", Strings.Get("Trigger_AppEvent_Param_BreakKind_Label"), "any",
                new Choice("any", Strings.Get("Trigger_AppEvent_Param_BreakKind_Choice_Any")), new Choice("stretch", Strings.Get("Trigger_AppEvent_Param_BreakKind_Choice_Stretch")), new Choice("water", Strings.Get("Trigger_AppEvent_Param_BreakKind_Choice_Water")), new Choice("eyes", Strings.Get("Trigger_AppEvent_Param_BreakKind_Choice_Eyes")), new Choice("walk", Strings.Get("Trigger_AppEvent_Param_BreakKind_Choice_Walk")))
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
