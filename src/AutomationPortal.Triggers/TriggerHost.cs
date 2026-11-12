using AutomationPortal.Core;
using AutomationPortal.Triggers.Platform;

namespace AutomationPortal.Triggers;

public sealed class TriggerHostOptions
{
    /// <summary>Where reminders are kept. Default: a new <see cref="ReminderStore"/> in the app data folder.</summary>
    public IReminderService? ReminderStore { get; set; }
    /// <summary>Expand snippets as they are typed (needs the keyboard hook while any snippet is enabled). Default true.</summary>
    public bool EnableTextExpansion { get; set; } = true;
}

/// <summary>
/// The single entry point for the app: owns keyboard shortcuts, typed phrases and text expansion, the macro recorder,
/// reminders and the automation engine. Create it with the host services, call <see cref="Start"/> once, <see cref="Dispose"/> on exit.
/// </summary>
public sealed class TriggerHost : IDisposable
{
    private readonly AutomationPortalServices _services;
    private readonly TriggerRegistry _triggers;
    private readonly ConditionRegistry _conditions;
    private readonly SystemEventHub _hub = new();
    private readonly TriggerEnvironment _env;
    private readonly TriggerHostOptions _options;
    private bool _started, _disposed;

    /// <param name="services">Needs Library, Runner, Notifications, Settings; UserInteraction for fill-in snippets.</param>
    /// <param name="triggers">Trigger registry; this assembly's triggers are added if they are not there yet.</param>
    /// <param name="conditions">Condition registry; this assembly's conditions are added if they are not there yet.</param>
    public TriggerHost(AutomationPortalServices services, TriggerRegistry triggers, ConditionRegistry conditions, TriggerHostOptions? options = null)
    {
        _services = services;
        _triggers = triggers;
        _conditions = conditions;
        _options = options ?? new TriggerHostOptions();
        RegisterProviders(triggers, conditions);

        _env = new TriggerEnvironment(services, _hub);
        Hotkeys = new HotkeyService(_hub);
        TypedPhrases = new TypedPhraseService(() => services.Settings.Current.AutomationsPaused);
        _env.Hotkeys = Hotkeys;
        _env.TypedPhrases = TypedPhrases;
        Recorder = new MacroRecorder();
        Reminders = new ReminderService(services, _options.ReminderStore ?? new ReminderStore(services.Paths));
        Automations = new AutomationEngine(services, triggers, conditions, _env);
        ShortcutHotkeys = new ShortcutHotkeyBinder(services, Hotkeys);
        Snippets = new SnippetExpansionService(services, TypedPhrases);
    }

    /// <summary>Global keyboard shortcuts. Register app-level ones (Quick Run …) after <see cref="Start"/>.</summary>
    public HotkeyService Hotkeys { get; }
    public TypedPhraseService TypedPhrases { get; }
    public MacroRecorder Recorder { get; }
    /// <summary>Set <c>services.Reminders</c> to this so the Remind Me actions use it.</summary>
    public ReminderService Reminders { get; }
    public AutomationEngine Automations { get; }
    public ShortcutHotkeyBinder ShortcutHotkeys { get; }
    public SnippetExpansionService Snippets { get; }

    /// <summary>Hands an event another app sent over the pipe to the "When another app sends an event" triggers. Returns at once.</summary>
    public void PublishAppEvent(AppEvent e) => _env.AppEvents.Publish(e);

    /// <summary>Adds this assembly's triggers and conditions to the registries (does nothing for ones already there).</summary>
    public static void RegisterProviders(TriggerRegistry triggers, ConditionRegistry conditions)
    {
        var assembly = typeof(TriggerHost).Assembly;
        if (triggers.Get("time.daily") is null) triggers.RegisterAssembly(assembly);
        if (conditions.Get("cond.timeBetween") is null) conditions.RegisterAssembly(assembly);
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        TriggerLog.FilePath = Path.Combine(_services.Paths.LogsFolder, "triggers.log");
        TriggerEnvironment.Current = _env;
        try { _hub.Start(); }
        catch (Exception ex) { TriggerLog.Write("System event window didn't start: " + ex.Message); }

        Step("reminders", Reminders.Start);
        Step("keyboard shortcuts", ShortcutHotkeys.Start);
        if (_options.EnableTextExpansion) Step("text expansion", Snippets.Start);
        Step("automations", Automations.Start);
    }

    private static void Step(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { TriggerLog.Write($"Couldn't start {what}: {ex}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Step("stop automations", Automations.Dispose);
        Step("stop text expansion", Snippets.Dispose);
        Step("stop keyboard shortcuts", ShortcutHotkeys.Dispose);
        Step("stop recorder", Recorder.Dispose);
        Step("stop typed phrases", TypedPhrases.Dispose);
        Step("stop reminders", Reminders.Dispose);
        Step("stop watchers", _env.Stop);
        Step("stop shortcuts", Hotkeys.Dispose);
        Step("stop event window", _hub.Dispose);
        TriggerEnvironment.Clear(_env);
    }
}
