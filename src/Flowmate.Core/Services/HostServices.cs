namespace Flowmate.Core;

public enum InputKind { Text, Multiline, Number, Date, Time, Url }

/// <summary>One field of an "Ask for Several Things" form. Kind Choice uses <see cref="Options"/>.</summary>
public sealed record FormField(string Label, FormFieldKind Kind = FormFieldKind.Text, string? Default = null, IReadOnlyList<string>? Options = null);
public enum FormFieldKind { Text, Multiline, Number, Date, Time, Choice }

public sealed record NotificationResponse(string? ButtonLabel, string? ReplyText, bool Dismissed);

/// <summary>File-picker filter, e.g. <c>new FileFilter("Images", "*.png;*.jpg")</c>.</summary>
public sealed record FileFilter(string Name, string Pattern);

/// <summary>Rectangle in physical screen pixels (virtual-desktop coordinates).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>Point in physical screen pixels (virtual-desktop coordinates).</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// Everything that needs a window. Implemented by the App (dialogs) and the Cli (console prompts).
/// Cancelling any prompt must throw <see cref="UserCancelledException"/>.
/// </summary>
public interface IUserInteraction
{
    /// <summary>Returns true when OK was pressed, false when Cancel was pressed (only possible when showCancel is true).</summary>
    Task<bool> ShowAlertAsync(string title, string message, bool showCancel, CancellationToken ct = default);
    /// <summary>Shows one form with all fields. Returns a dictionary label to value in field order.</summary>
    Task<ContentValue> AskForFormAsync(string title, IReadOnlyList<FormField> fields, CancellationToken ct = default);
    Task<ContentValue> AskForInputAsync(string prompt, InputKind kind, string? defaultValue, CancellationToken ct = default);
    Task<ContentValue> ChooseFromListAsync(string prompt, IReadOnlyList<ContentItem> items, bool multiSelect, CancellationToken ct = default);
    /// <summary>Returns the zero-based index of the chosen label.</summary>
    Task<int> ChooseFromMenuAsync(string prompt, IReadOnlyList<string> labels, CancellationToken ct = default);
    Task ShowResultAsync(string title, ContentValue value, CancellationToken ct = default);
    Task PreviewAsync(ContentValue value, CancellationToken ct = default);
    Task<ContentValue> PickFilesAsync(string prompt, bool multiple, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default);
    Task<ContentValue> PickFolderAsync(string prompt, CancellationToken ct = default);
    /// <summary>Returns the chosen full path.</summary>
    Task<string> PickSaveFileAsync(string prompt, string? suggestedName = null, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default);
    /// <summary>Lets the user drag a rectangle on screen. Null when cancelled.</summary>
    Task<ScreenRect?> SelectScreenRegionAsync(CancellationToken ct = default);
    /// <summary>Lets the user click a spot on screen. Null when cancelled.</summary>
    Task<ScreenPoint?> SelectScreenPointAsync(CancellationToken ct = default);
}

public interface INotificationService
{
    /// <param name="silent">True for a toast without a sound (reminders with Sound = false).</param>
    void Show(string title, string body, string? imagePath = null, Action? openOnClick = null, bool silent = false);
    /// <summary>Toast with optional buttons and a reply box. Completes when the user responds, dismisses it, or the timeout passes (Dismissed = true).</summary>
    Task<NotificationResponse> ShowInteractiveAsync(string title, string body, IReadOnlyList<string> buttons, bool allowReply, string? imagePath = null, TimeSpan? timeout = null, CancellationToken ct = default);
    /// <summary>Toast with Run / Skip buttons. Returns true if the user chose Run (false on Skip or timeout).</summary>
    Task<bool> AskToRunAsync(string shortcutName, string reason, CancellationToken ct = default);
}

public sealed class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset When { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public Guid? ShortcutIdToRun { get; set; }
    public bool Sound { get; set; } = true;
}

public interface IReminderService
{
    /// <summary>Saves the reminder (persisted) and arms it. Returns it (Id filled in).</summary>
    Reminder Schedule(Reminder reminder);
    bool Cancel(Guid id);
    /// <summary>Upcoming reminders, soonest first.</summary>
    IReadOnlyList<Reminder> List();
    /// <summary>Raised (on a worker thread) when a reminder is due; the host shows a notification / runs the shortcut.</summary>
    event EventHandler<Reminder>? Due;
}

[Flags]
public enum LibraryChange { None = 0, Shortcuts = 1, Folders = 2, Automations = 4, Snippets = 8 }

public sealed class LibraryChangedEventArgs : EventArgs
{
    public LibraryChangedEventArgs(LibraryChange what, bool external) { What = what; External = external; }
    public LibraryChange What { get; }
    /// <summary>True when the change came from files modified outside this process (OneDrive sync, manual edit).</summary>
    public bool External { get; }
}

public sealed record ImportWarning(string ActionUid, string ActionId, string Title, ImportWarningKind Kind);
public enum ImportWarningKind { Destructive, Advanced, Unknown }

public sealed class ImportResult
{
    public required Shortcut Shortcut { get; init; }
    /// <summary>The original name when it was renamed because of a clash ("Resize Images" → "Resize Images 2").</summary>
    public string? RenamedFrom { get; init; }
    /// <summary>Destructive / advanced / unknown actions the shortcut contains (show a warning to the user).</summary>
    public IReadOnlyList<ImportWarning> Warnings { get; init; } = Array.Empty<ImportWarning>();
    /// <summary>True when the shortcut has destructive or advanced actions: ask the user before keeping it.</summary>
    public bool NeedsConfirmation => Warnings.Any(w => w.Kind is ImportWarningKind.Destructive or ImportWarningKind.Advanced or ImportWarningKind.Unknown);
    /// <summary>Settings stripped from the import (it is saved without them). The UI may offer to turn them on after the user confirms.</summary>
    public string? PendingHotkey { get; init; }
    public string? PendingAlias { get; init; }
    public bool PendingShowInSendTo { get; init; }
}

/// <summary>Friendly library error (name clash, unreadable file …). The message is safe to show.</summary>
public sealed class LibraryException : Exception
{
    public LibraryException(string message) : base(message) { }
    public LibraryException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The shortcut library. Returned <see cref="Shortcut"/> objects are shared: treat them as read-only,
/// <see cref="Shortcut.Clone"/> to edit, then <see cref="Save"/>. <see cref="Changed"/> may be raised on any thread.
/// </summary>
public interface IShortcutLibrary
{
    /// <summary>All shortcuts ordered by SortOrder then name.</summary>
    IReadOnlyList<Shortcut> All { get; }
    IReadOnlyList<ShortcutFolder> Folders { get; }
    IReadOnlyList<Automation> Automations { get; }
    Shortcut? GetById(Guid id);
    /// <summary>Case-insensitive exact name match.</summary>
    Shortcut? FindByName(string name);
    bool IsNameTaken(string name, Guid? exceptId = null);
    /// <summary>Stores a copy, stamps ModifiedAt. Throws <see cref="LibraryException"/> if the name belongs to another shortcut.</summary>
    void Save(Shortcut shortcut);
    /// <summary>Saves several shortcuts and raises <see cref="Changed"/> once (drag-to-reorder).</summary>
    void SaveMany(IEnumerable<Shortcut> shortcuts);
    void Delete(Guid id);
    void SaveFolder(ShortcutFolder folder);
    /// <summary>Deletes the folder; its shortcuts move back to the top level.</summary>
    void DeleteFolder(Guid id);
    IReadOnlyList<Snippet> Snippets { get; }
    void SaveSnippet(Snippet snippet);
    void DeleteSnippet(Guid id);
    void SaveAutomation(Automation automation);
    void DeleteAutomation(Guid id);
    /// <summary>Adds a copy with a new Id (uids kept), a unique name and warnings for risky actions.</summary>
    ImportResult Import(Shortcut shortcut);
    /// <summary>Imports <c>.flowmate</c> file text.</summary>
    ImportResult ImportJson(string flowmateJson);
    /// <summary>Returns <c>.flowmate</c> file text for the shortcut.</summary>
    string Export(Guid id);
    event EventHandler<LibraryChangedEventArgs>? Changed;
}

public interface IShortcutRunner
{
    Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null);
    /// <summary>Runs a shortcut from the library by Id. Returns a Failed result if it doesn't exist.</summary>
    Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null);
}

/// <summary>Per-user encrypted secrets (DPAPI), e.g. <c>ai.apiKey</c>.</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Remove(string name);
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    /// <summary>Applies the change, saves, and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> change);
    event EventHandler<AppSettings>? Changed;
}

/// <summary>Runs work on the single STA dispatcher thread (clipboard, WIC imaging, dialogs). The Cli supplies its own STA thread.</summary>
public interface IUiThread
{
    bool IsOnUiThread { get; }
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> func);
    /// <summary>Runs async work started on the UI thread.</summary>
    Task<T> InvokeTaskAsync<T>(Func<Task<T>> func);
}

/// <summary>Saved values that persist across runs (Save Value / Get Saved Value).</summary>
public interface IKeyValueStore
{
    ContentValue? Get(string key);
    void Set(string key, ContentValue value);
    bool Remove(string key);
    IReadOnlyList<string> Keys { get; }
}

/// <summary>Clipboard access for the Clipboard variable and Ask-for-input defaults. Implemented by the host on the UI thread.</summary>
public interface IClipboardService
{
    Task<ContentValue> GetAsync();
    Task SetAsync(ContentValue value);
    Task ClearAsync();
    /// <summary>Opaque copy of every format currently on the clipboard (null when it is empty).</summary>
    Task<object?> SnapshotAsync();
    /// <summary>Puts a snapshot from <see cref="SnapshotAsync"/> back exactly as it was (clears the clipboard when null).</summary>
    Task RestoreAsync(object? snapshot);
}

public enum RunStatus { Succeeded, Failed, Cancelled, Stopped }

public sealed class StepLog
{
    public string Uid { get; set; } = "";
    public string ActionId { get; set; } = "";
    public string Title { get; set; } = "";
    public double DurationMs { get; set; }
    /// <summary>Up to 200 characters of the step's output.</summary>
    public string OutputPreview { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class RunRecord
{
    public string RunId { get; set; } = "";
    public Guid ShortcutId { get; set; }
    public string ShortcutName { get; set; } = "";
    public RunSource Source { get; set; }
    public DateTimeOffset Started { get; set; }
    public double DurationMs { get; set; }
    public RunStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailedStepUid { get; set; }
    public string OutputPreview { get; set; } = "";
    public List<StepLog> Steps { get; set; } = new();
}

public sealed class RunHistoryFilter
{
    public Guid? ShortcutId { get; set; }
    public RunStatus? Status { get; set; }
    public DateTimeOffset? Since { get; set; }
    public string? Text { get; set; }
    public int Limit { get; set; } = 200;
}

/// <summary>Run history. <see cref="ShortcutRunner"/> adds a record after every top-level run, so hosts don't need to.</summary>
public interface IRunHistory
{
    void Add(RunRecord record);
    /// <summary>Newest first.</summary>
    IReadOnlyList<RunRecord> Query(RunHistoryFilter? filter = null);
    void Clear();
}
