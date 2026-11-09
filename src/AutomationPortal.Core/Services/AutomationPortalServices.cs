namespace AutomationPortal.Core;

/// <summary>
/// Container for the host services, exposed to actions as <c>ctx.Services</c>. The host sets what it has;
/// reading a service that was never set throws a friendly <see cref="ActionException"/>.
/// </summary>
public sealed class AutomationPortalServices
{
    private IUserInteraction? _ui;
    private INotificationService? _notifications;
    private IReminderService? _reminders;
    private IShortcutLibrary? _library;
    private IShortcutRunner? _runner;
    private ISecretStore? _secrets;
    private ISettingsStore? _settings;
    private IUiThread? _uiThread;
    private IKeyValueStore? _keyValues;
    private IRunHistory? _history;
    private IClipboardService? _clipboard;
    private HttpClient? _http;

    public IUserInteraction UserInteraction { get => _ui ?? Missing<IUserInteraction>("show dialogs"); set => _ui = value; }
    public INotificationService Notifications { get => _notifications ?? Missing<INotificationService>("show notifications"); set => _notifications = value; }
    public IReminderService Reminders { get => _reminders ?? Missing<IReminderService>("schedule reminders"); set => _reminders = value; }
    public IShortcutLibrary Library { get => _library ?? Missing<IShortcutLibrary>("look up shortcuts"); set => _library = value; }
    public IShortcutRunner Runner { get => _runner ?? Missing<IShortcutRunner>("run other shortcuts"); set => _runner = value; }
    public ISecretStore Secrets { get => _secrets ?? Missing<ISecretStore>("read saved keys"); set => _secrets = value; }
    public ISettingsStore Settings { get => _settings ?? Missing<ISettingsStore>("read settings"); set => _settings = value; }
    public IUiThread UiThread { get => _uiThread ?? Missing<IUiThread>("use the clipboard or images"); set => _uiThread = value; }
    public IKeyValueStore KeyValues { get => _keyValues ?? Missing<IKeyValueStore>("keep saved values"); set => _keyValues = value; }
    public IRunHistory History { get => _history ?? Missing<IRunHistory>("keep run history"); set => _history = value; }
    public IClipboardService Clipboard { get => _clipboard ?? Missing<IClipboardService>("use the clipboard"); set => _clipboard = value; }

    public bool HasRunner => _runner is not null;
    public IUserInteraction? UserInteractionOrNull => _ui;
    public IClipboardService? ClipboardOrNull => _clipboard;
    public IRunHistory? HistoryOrNull => _history;
    public IShortcutLibrary? LibraryOrNull => _library;
    public IKeyValueStore? KeyValuesOrNull => _keyValues;

    /// <summary>Folders on disk (temp folders per run live under <c>Paths.TempRoot</c>).</summary>
    public AutomationPortalPaths Paths { get; set; } = AutomationPortalPaths.Default;

    /// <summary>Shared client, User-Agent "AutomationPortal/1.0", 100 s timeout. Don't dispose.</summary>
    public HttpClient Http
    {
        get => _http ??= CreateHttpClient();
        set => _http = value;
    }

    public static HttpClient CreateHttpClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, AllowAutoRedirect = true })
        { Timeout = TimeSpan.FromSeconds(100) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AutomationPortal/1.0");
        return c;
    }

    private static T Missing<T>(string what) => throw NotAvailable(what);

    /// <summary>The error for a host feature this app doesn't provide, e.g. <c>NotAvailable("edit pictures")</c>.</summary>
    public static ActionException NotAvailable(string what) =>
        new($"This app can't {what} here. Try running the shortcut from the Automation Portal app.");
}
