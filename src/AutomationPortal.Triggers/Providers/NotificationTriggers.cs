using AutomationPortal.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AutomationPortal.Triggers;

/// <summary>One notification from the Windows notification list.</summary>
public sealed record NotificationInfo(uint Id, string AppName, string AppId, string Title, string Text);

/// <summary>Where notifications come from. The real one is the Windows notification listener; tests use a fake.</summary>
internal interface INotificationSource
{
    /// <summary>Null when this build can read notifications, otherwise the reason it can't.</summary>
    string? UnavailableReason { get; }
    /// <summary>True when the user has allowed notification access (asks the first time).</summary>
    Task<bool> RequestAccessAsync();
    Task<IReadOnlyList<NotificationInfo>> GetCurrentAsync();
}

internal sealed class WindowsNotificationSource : INotificationSource
{
    public const string UnpackagedReason = "Needs the Microsoft Store version of Automation Portal";

    public string? UnavailableReason => PackageIdentity.IsPackaged ? null : UnpackagedReason;

    public async Task<bool> RequestAccessAsync() =>
        await UserNotificationListener.Current.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed;

    public async Task<IReadOnlyList<NotificationInfo>> GetCurrentAsync()
    {
        var list = new List<NotificationInfo>();
        foreach (var n in await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast))
        {
            try
            {
                var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                var texts = binding?.GetTextElements().Select(t => t.Text).ToList() ?? new List<string>();
                list.Add(new NotificationInfo(n.Id, n.AppInfo?.DisplayInfo?.DisplayName ?? "", n.AppInfo?.AppUserModelId ?? "",
                    texts.FirstOrDefault() ?? "", string.Join("\n", texts.Skip(1))));
            }
            catch (Exception ex) { TriggerLog.Write("Couldn't read one notification: " + ex.Message); }
        }
        return list;
    }
}

/// <summary>Decides which new notifications should start the shortcut. Pure (time is passed in), for tests.</summary>
internal sealed class NotificationMatcher
{
    private readonly string _app, _title, _text;
    private readonly bool _includeOwn;
    private readonly TimeSpan _cooldown;
    private readonly Func<NotificationInfo, bool> _isOwn;
    private readonly HashSet<uint> _seen = new();
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new();
    private bool _seeded;

    public NotificationMatcher(string app, string title, string text, bool includeOwn, TimeSpan cooldown, Func<NotificationInfo, bool> isOwn)
    {
        _app = app.Trim(); _title = title.Trim(); _text = text.Trim();
        _includeOwn = includeOwn; _cooldown = cooldown; _isOwn = isOwn;
    }

    private static bool Has(string value, string needle) => needle.Length == 0 || value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public bool Matches(NotificationInfo n) => Has(n.AppName, _app) && Has(n.Title, _title) && Has(n.Text, _text) && (_includeOwn || !_isOwn(n));

    /// <summary>Given everything currently in the list, returns the new notifications that match and aren't repeats. The first call only notes what was already there.</summary>
    public IReadOnlyList<NotificationInfo> Process(IReadOnlyList<NotificationInfo> current, DateTimeOffset now)
    {
        var fresh = current.Where(n => !_seen.Contains(n.Id)).ToList();
        _seen.Clear();
        foreach (var n in current) _seen.Add(n.Id);
        if (!_seeded) { _seeded = true; return Array.Empty<NotificationInfo>(); }

        foreach (var key in _lastFired.Where(kv => now - kv.Value > _cooldown).Select(kv => kv.Key).ToList()) _lastFired.Remove(key);

        var result = new List<NotificationInfo>();
        foreach (var n in fresh)
        {
            if (!Matches(n)) continue;
            var key = n.Title + "\n" + n.Text;
            if (_cooldown > TimeSpan.Zero && _lastFired.TryGetValue(key, out var last) && now - last < _cooldown) continue;
            _lastFired[key] = now;
            result.Add(n);
        }
        return result;
    }
}

internal sealed class NotificationWatcher
{
    private readonly INotificationSource _source;
    private readonly NotificationMatcher _matcher;
    private readonly ITriggerSink _sink;
    private readonly Func<DateTimeOffset> _clock;
    private bool _granted;

    public NotificationWatcher(INotificationSource source, NotificationMatcher matcher, ITriggerSink sink, Func<DateTimeOffset>? clock = null)
    {
        _source = source; _matcher = matcher; _sink = sink; _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public bool HasAccess => _granted;

    public async Task PollOnceAsync()
    {
        if (!_granted)
        {
            _granted = await _source.RequestAccessAsync();
            if (!_granted) { _sink.ReportProblem("Windows hasn't allowed Automation Portal to read notifications. Turn it on in Windows Settings, under Privacy & security, then Notifications."); return; }
            _sink.ReportProblem(null);
        }
        foreach (var n in _matcher.Process(await _source.GetCurrentAsync(), _clock()))
        {
            var content = ContentValue.Dict(new[]
            {
                new KeyValuePair<string, ContentValue>("Title", ContentValue.Text(n.Title)),
                new KeyValuePair<string, ContentValue>("Text", ContentValue.Text(n.Text)),
                new KeyValuePair<string, ContentValue>("App", ContentValue.Text(n.AppName)),
            }, string.IsNullOrEmpty(n.Text) ? n.Title : n.Title + ": " + n.Text);
            _sink.Fire(content, string.IsNullOrEmpty(n.AppName) ? "A notification arrived" : $"A notification arrived from {n.AppName}");
        }
    }
}

public sealed class NotificationArrivesTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "notif.arrived", Title = "When a notification arrives", Category = TP.CatSystem, Icon = "Alert24",
        Summary = "When a notification arrives",
        Description = "Runs when Windows shows a notification. You can limit it to one app, or to words in the title or the text. Notification text stays on this PC and is only given to your shortcut. " +
                      "Needs the Microsoft Store version of Automation Portal, and you must allow it in Windows Settings. Notifications from Automation Portal itself are ignored unless you turn that on.",
        OutputKind = ContentKind.Any, OutputDescription = "The notification's title, text and app name",
        Parameters = new[]
        {
            ParameterDescriptor.Text("app", "App name contains", null, "Any app") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Text("title", "Title contains", null, "Anything") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Text("text", "Text contains", null, "Anything") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Number("cooldown", "Ignore the same notification for", 10, 0, 3600, "seconds") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Bool("includeOwn", "Also react to Automation Portal's own notifications", false) with { AllowVariables = false, AllowAskEachTime = false },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    internal static Func<INotificationSource> SourceFactory { get; set; } = () => new WindowsNotificationSource();

    internal static bool IsOwn(NotificationInfo n)
    {
        if (string.Equals(n.AppName, "Automation Portal", StringComparison.OrdinalIgnoreCase)) return true;
        var family = PackageIdentity.FamilyName;
        return family is not null && n.AppId.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase);
    }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var source = SourceFactory();
        if (source.UnavailableReason is { } reason)
        {
            sink.ReportProblem(reason + ".");
            return new DisposableAction(() => { });
        }
        var matcher = new NotificationMatcher(config.GetString("app"), config.GetString("title"), config.GetString("text"),
            config.GetBool("includeOwn"), TimeSpan.FromSeconds(Math.Max(0, config.GetNumber("cooldown", 10))), IsOwn);
        var watcher = new NotificationWatcher(source, matcher, sink);
        return TP.Poll(_ => watcher.PollOnceAsync(), TimeSpan.FromSeconds(2), () => TimeSpan.FromSeconds(watcher.HasAccess ? 2 : 15));
    }
}
