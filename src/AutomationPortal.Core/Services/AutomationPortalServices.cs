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

    public IUserInteraction UserInteraction { get => _ui ?? Missing<IUserInteraction>(Strings.Get("Services_Need_Dialogs")); set => _ui = value; }
    public INotificationService Notifications { get => _notifications ?? Missing<INotificationService>(Strings.Get("Services_Need_Notifications")); set => _notifications = value; }
    public IReminderService Reminders { get => _reminders ?? Missing<IReminderService>(Strings.Get("Services_Need_Reminders")); set => _reminders = value; }
    public IShortcutLibrary Library { get => _library ?? Missing<IShortcutLibrary>(Strings.Get("Services_Need_Library")); set => _library = value; }
    public IShortcutRunner Runner { get => _runner ?? Missing<IShortcutRunner>(Strings.Get("Services_Need_Runner")); set => _runner = value; }
    public ISecretStore Secrets { get => _secrets ?? Missing<ISecretStore>(Strings.Get("Services_Need_Secrets")); set => _secrets = value; }
    public ISettingsStore Settings { get => _settings ?? Missing<ISettingsStore>(Strings.Get("Services_Need_Settings")); set => _settings = value; }
    public IUiThread UiThread { get => _uiThread ?? Missing<IUiThread>(Strings.Get("Services_Need_UiThread")); set => _uiThread = value; }
    public IKeyValueStore KeyValues { get => _keyValues ?? Missing<IKeyValueStore>(Strings.Get("Services_Need_KeyValues")); set => _keyValues = value; }
    public IRunHistory History { get => _history ?? Missing<IRunHistory>(Strings.Get("Services_Need_History")); set => _history = value; }
    public IClipboardService Clipboard { get => _clipboard ?? Missing<IClipboardService>(Strings.Get("Services_Need_Clipboard")); set => _clipboard = value; }

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
        new(Strings.Format("Services_NotAvailable", what));
}
