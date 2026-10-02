namespace Flowmate.Core.Testing;

/// <summary>
/// Scripted user interaction for tests. Queue answers with the Enqueue… methods (or set the handlers);
/// every call is also recorded in <see cref="Calls"/>. Cancel a prompt with <see cref="EnqueueCancel"/>.
/// </summary>
public sealed class FakeUserInteraction : IUserInteraction
{
    private readonly Queue<object> _answers = new();
    public List<string> Calls { get; } = new();
    public List<string> Prompts { get; } = new();
    public List<ContentValue> Shown { get; } = new();

    /// <summary>Queue the next answer: string/ContentValue/bool/int/ScreenRect/ScreenPoint, or <see cref="UserCancelledException"/>.</summary>
    public FakeUserInteraction Enqueue(object answer) { _answers.Enqueue(answer); return this; }
    public FakeUserInteraction EnqueueCancel() => Enqueue(new UserCancelledException());

    private T Next<T>(string call, string prompt, Func<object, T> convert, T fallback)
    {
        Calls.Add(call); Prompts.Add(prompt);
        if (_answers.Count == 0) return fallback;
        var a = _answers.Dequeue();
        if (a is UserCancelledException c) throw c;
        return convert(a);
    }

    public Task<bool> ShowAlertAsync(string title, string message, bool showCancel, CancellationToken ct = default) =>
        Task.FromResult(Next("alert", message, a => a is bool b ? b : true, true));

    public Task<ContentValue> AskForInputAsync(string prompt, InputKind kind, string? defaultValue, CancellationToken ct = default) =>
        Task.FromResult(Next("ask", prompt, a => ContentValue.Of(a), ContentValue.Text(defaultValue ?? "")));

    public Task<ContentValue> AskForFormAsync(string title, IReadOnlyList<FormField> fields, CancellationToken ct = default) =>
        Task.FromResult(Next("form", title, a => ContentValue.Of(a),
            ContentValue.Dict(fields.Select(f => new KeyValuePair<string, ContentValue>(f.Label, ContentValue.Text(f.Default ?? ""))))));

    public Task<ContentValue> ChooseFromListAsync(string prompt, IReadOnlyList<ContentItem> items, bool multiSelect, CancellationToken ct = default) =>
        Task.FromResult(Next("chooseList", prompt, a => a is int i ? ContentValue.From(items[i]) : ContentValue.Of(a),
            items.Count > 0 ? ContentValue.From(items[0]) : ContentValue.Empty));

    public Task<int> ChooseFromMenuAsync(string prompt, IReadOnlyList<string> labels, CancellationToken ct = default) =>
        Task.FromResult(Next("menu", prompt, a => a is int i ? i : a is string s ? Math.Max(0, labels.ToList().IndexOf(s)) : 0, 0));

    public Task ShowResultAsync(string title, ContentValue value, CancellationToken ct = default)
    {
        Calls.Add("showResult"); Prompts.Add(title); Shown.Add(value);
        return Task.CompletedTask;
    }

    public Task PreviewAsync(ContentValue value, CancellationToken ct = default)
    {
        Calls.Add("preview"); Prompts.Add(""); Shown.Add(value);
        return Task.CompletedTask;
    }

    public Task<ContentValue> PickFilesAsync(string prompt, bool multiple, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        Task.FromResult(Next("pickFiles", prompt, a => a is string s ? ContentValue.FileOrFolder(s) : ContentValue.Of(a), ContentValue.Empty));

    public Task<ContentValue> PickFolderAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult(Next("pickFolder", prompt, a => a is string s ? ContentValue.Folder(s) : ContentValue.Of(a), ContentValue.Empty));

    public Task<string> PickSaveFileAsync(string prompt, string? suggestedName = null, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        Task.FromResult(Next("pickSave", prompt, a => a.ToString() ?? "", suggestedName ?? "file.txt"));

    public Task<ScreenRect?> SelectScreenRegionAsync(CancellationToken ct = default) =>
        Task.FromResult(Next<ScreenRect?>("region", "", a => a is ScreenRect r ? r : null, new ScreenRect(0, 0, 100, 100)));

    public Task<ScreenPoint?> SelectScreenPointAsync(CancellationToken ct = default) =>
        Task.FromResult(Next<ScreenPoint?>("point", "", a => a is ScreenPoint p ? p : null, new ScreenPoint(0, 0)));
}

public sealed class FakeNotificationService : INotificationService
{
    public List<(string Title, string Body)> Shown { get; } = new();
    public bool AskToRunAnswer { get; set; } = true;
    public NotificationResponse InteractiveAnswer { get; set; } = new("OK", null, false);
    public List<(string Title, IReadOnlyList<string> Buttons, bool Reply)> InteractiveCalls { get; } = new();

    public void Show(string title, string body, string? imagePath = null, Action? openOnClick = null) => Shown.Add((title, body));
    public Task<bool> AskToRunAsync(string shortcutName, string reason, CancellationToken ct = default) => Task.FromResult(AskToRunAnswer);

    public Task<NotificationResponse> ShowInteractiveAsync(string title, string body, IReadOnlyList<string> buttons, bool allowReply,
        string? imagePath = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        InteractiveCalls.Add((title, buttons, allowReply));
        return Task.FromResult(InteractiveAnswer);
    }
}

public sealed class FakeReminderService : IReminderService
{
    private readonly List<Reminder> _items = new();
    public event EventHandler<Reminder>? Due;
    public Reminder Schedule(Reminder reminder) { _items.Add(reminder); return reminder; }
    public bool Cancel(Guid id) => _items.RemoveAll(r => r.Id == id) > 0;
    public IReadOnlyList<Reminder> List() => _items.OrderBy(r => r.When).ToList();
    public void RaiseDue(Reminder r) => Due?.Invoke(this, r);
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _d = new();
    public string? Get(string name) => _d.TryGetValue(name, out var v) ? v : null;
    public void Set(string name, string value) => _d[name] = value;
    public void Remove(string name) => _d.Remove(name);
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; } = new();
    public event EventHandler<AppSettings>? Changed;
    public void Update(Action<AppSettings> change) { change(Current); Changed?.Invoke(this, Current); }
}

/// <summary>Runs everything inline on the calling thread.</summary>
public sealed class InlineUiThread : IUiThread
{
    public bool IsOnUiThread => true;
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    public Task<T> InvokeTaskAsync<T>(Func<Task<T>> func) => func();
}

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, ContentValue> _d = new(StringComparer.OrdinalIgnoreCase);
    public ContentValue? Get(string key) => _d.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, ContentValue value) => _d[key] = value;
    public bool Remove(string key) => _d.Remove(key);
    public IReadOnlyList<string> Keys => _d.Keys.ToList();
}

public sealed class InMemoryRunHistory : IRunHistory
{
    public List<RunRecord> Records { get; } = new();
    public void Add(RunRecord record) => Records.Add(record);
    public IReadOnlyList<RunRecord> Query(RunHistoryFilter? filter = null) =>
        Records.OrderByDescending(r => r.Started).Where(r => filter?.ShortcutId is null || r.ShortcutId == filter.ShortcutId).ToList();
    public void Clear() => Records.Clear();
}

public sealed class FakeClipboard : IClipboardService
{
    public ContentValue Value { get; set; } = ContentValue.Empty;
    public Task<ContentValue> GetAsync() => Task.FromResult(Value);
    public Task SetAsync(ContentValue value) { Value = value; return Task.CompletedTask; }
    public Task ClearAsync() { Value = ContentValue.Empty; return Task.CompletedTask; }
}

/// <summary>Library that lives only in memory.</summary>
public sealed class InMemoryShortcutLibrary : ShortcutLibraryBase
{
    public InMemoryShortcutLibrary(ActionRegistry? registry = null) : base(registry) { }
}

/// <summary>A fully faked set of services plus the fakes themselves for assertions.</summary>
public sealed class TestHost : IDisposable
{
    public FlowmateServices Services { get; }
    public FakeUserInteraction UI { get; } = new();
    public FakeNotificationService Notifications { get; } = new();
    public FakeReminderService Reminders { get; } = new();
    public InMemoryShortcutLibrary Library { get; }
    public InMemorySecretStore Secrets { get; } = new();
    public InMemorySettingsStore Settings { get; } = new();
    public InMemoryKeyValueStore KeyValues { get; } = new();
    public InMemoryRunHistory History { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public ActionRegistry Registry { get; }
    public ShortcutRunner Runner { get; }
    /// <summary>Temp root for this host (deleted on dispose).</summary>
    public string Root { get; }

    public TestHost(ActionRegistry? registry = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
        Registry = registry ?? ActionRegistry.CreateWithBuiltIns();
        Library = new InMemoryShortcutLibrary(Registry);
        Services = new FlowmateServices
        {
            UserInteraction = UI, Notifications = Notifications, Reminders = Reminders, Library = Library, Secrets = Secrets,
            Settings = Settings, UiThread = new InlineUiThread(), KeyValues = KeyValues, History = History, Clipboard = Clipboard,
            Paths = FlowmatePaths.ForRoot(Root),
        };
        Runner = new ShortcutRunner(Registry, Services);
    }

    /// <summary>Registers more actions (e.g. the assembly under test).</summary>
    public TestHost With(System.Reflection.Assembly assembly) { Registry.RegisterAssembly(assembly); return this; }

    public void Dispose() { try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { } }
}

/// <summary>Runs a single action in isolation: <c>var output = await ActionTestHarness.RunAsync(host, new MyAction(), ("text", "hi"))</c>.</summary>
public static class ActionTestHarness
{
    public static ActionContext CreateContext(TestHost host, ShortcutAction action, ContentValue? input = null, params (string Key, ParamValue Value)[] parameters)
    {
        var step = ActionInstance.Create(action.Descriptor.Id, parameters);
        var scope = new RunScope(host.Services, "Test Shortcut", input ?? ContentValue.Empty);
        return new ActionContext(scope, step, action.Descriptor, input ?? ContentValue.Empty);
    }

    public static Task<ContentValue> RunAsync(TestHost host, ShortcutAction action, params (string Key, ParamValue Value)[] parameters) =>
        action.RunAsync(CreateContext(host, action, null, parameters));

    public static Task<ContentValue> RunAsync(TestHost host, ShortcutAction action, ContentValue input, params (string Key, ParamValue Value)[] parameters) =>
        action.RunAsync(CreateContext(host, action, input, parameters));

    /// <summary>Runs a one-action shortcut through the real engine (variables, errors, retry policies).</summary>
    public static Task<RunResult> RunInShortcutAsync(TestHost host, string actionId, ContentValue? input = null, params (string Key, ParamValue Value)[] parameters)
    {
        var s = new Shortcut { Name = "Test" };
        s.Actions.Add(ActionInstance.Create(actionId, parameters));
        return host.Runner.RunAsync(s, input ?? ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
    }
}
