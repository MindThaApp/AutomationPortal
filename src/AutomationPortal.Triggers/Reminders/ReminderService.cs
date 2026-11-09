using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>
/// Reminders: stores them with Core's persistence, shows a notification when one is due and optionally runs a shortcut.
/// A reminder that came due while the PC was off or asleep fires once when Automation Portal starts, marked "(missed)".
/// Give this to <c>AutomationPortalServices.Reminders</c> so the Remind Me actions use it.
/// </summary>
public sealed class ReminderService : IReminderService, IDisposable
{
    /// <summary>A reminder this much later than planned counts as missed.</summary>
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(2);

    private readonly AutomationPortalServices _services;
    private readonly IReminderService _store;
    private readonly Func<DateTimeOffset> _clock;
    private bool _started, _disposed;

    /// <param name="store">Where reminders live. Pass <c>new ReminderStore(paths)</c> (Core's file-backed store).</param>
    public ReminderService(AutomationPortalServices services, IReminderService store, Func<DateTimeOffset>? clock = null)
    {
        _services = services;
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Raised after a due reminder was shown (and its shortcut started). For refreshing lists; don't show another notification.</summary>
    public event EventHandler<Reminder>? Due;
    /// <summary>Raised when the notification for a reminder was shown. The flag says whether it was missed.</summary>
    public event EventHandler<(Reminder Reminder, bool Missed)>? Fired;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _store.Due += OnStoreDue;
        (_store as ReminderStore)?.Start();
    }

    public Reminder Schedule(Reminder reminder) => _store.Schedule(reminder);
    public bool Cancel(Guid id) => _store.Cancel(id);
    public IReadOnlyList<Reminder> List() => _store.List();

    private void OnStoreDue(object? sender, Reminder reminder)
    {
        try { Handle(reminder); }
        catch (Exception ex) { TriggerLog.Write("Reminder failed: " + ex.Message); }
    }

    internal void Handle(Reminder reminder)
    {
        var missed = _clock() - reminder.When > MissedAfter;
        var title = string.IsNullOrWhiteSpace(reminder.Title) ? "Reminder" : reminder.Title;
        var body = reminder.Body ?? "";
        if (missed)
        {
            title += " (missed)";
            body = (body.Length > 0 ? body + "\n" : "") + $"This was due {reminder.When.LocalDateTime:f}.";
        }

        Guid? shortcutId = reminder.ShortcutIdToRun;
        if (shortcutId is { } id && _services.Library.GetById(id) is null)
        {
            body = (body.Length > 0 ? body + "\n" : "") + "The shortcut for this reminder was deleted, so it didn't run.";
            shortcutId = null;
        }

        try { _services.Notifications.Show(title, body, null, null, silent: !reminder.Sound); }
        catch (Exception ex) { TriggerLog.Write("Couldn't show a reminder: " + ex.Message); }

        if (shortcutId is { } run)
        {
            _ = Task.Run(async () =>
            {
                try { await _services.Runner.RunAsync(run, ContentValue.Text(reminder.Title ?? ""), new RunOptions { Source = RunSource.Reminder }); }
                catch (Exception ex) { TriggerLog.Write("Reminder shortcut failed: " + ex.Message); }
            });
        }

        Fired?.Invoke(this, (reminder, missed));
        Due?.Invoke(this, reminder);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Due -= OnStoreDue;
        (_store as IDisposable)?.Dispose();
    }
}
