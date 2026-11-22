using System.Reflection;
using AutomationPortal.Core;
using Sentry;
using Sentry.Protocol;

namespace AutomationPortal.App.Startup;

/// <summary>
/// Crash reports through Sentry (EU region), on unless the user turned them off (<see cref="AppSettings.SendCrashReports"/>)
/// and only when <see cref="Dsn"/> is set. Every event is redacted in <see cref="BeforeSend"/>; breadcrumbs are never kept, so no step values,
/// file names, clipboard text or typed text can ride along. Turning it on or off in Settings takes effect straight away.
/// Never started in test runs or with a throwaway data root (AUTOMATIONPORTAL_NO_CRASH_REPORTS=1, --data-root, AUTOMATIONPORTAL_DATA_ROOT).
/// </summary>
internal sealed class CrashReporting
{
    // The Sentry project's public client key (DSN). It can only send events; paste it here. Empty = crash reporting is off.
    internal const string Dsn = "https://d2e46e7dda882f96f82f75d8a2983e22@o4512289248706560.ingest.de.sentry.io/4512449904640080";

    private readonly object _gate = new();
    private readonly CrashReportRedactor _redactor = CrashReportRedactor.ForThisPc(AppContext.BaseDirectory);
    private readonly bool _blocked;
    private IDisposable? _sdk;
    private volatile bool _wanted;

    private CrashReporting(bool blocked) => _blocked = blocked;

    public static CrashReporting? Current { get; private set; }

    public static CrashReporting Create(string? dataRoot, bool sandbox) =>
        Current = new CrashReporting(!string.IsNullOrWhiteSpace(dataRoot) || sandbox
            || Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_NO_CRASH_REPORTS") == "1");

    /// <summary>Whether this build can send reports at all (a DSN is set). The first-run choice and the Settings toggle show only then.</summary>
    public static bool IsAvailable => Dsn.Length > 0;

    public bool IsRunning => _sdk is not null;

    public static string EnvironmentName(bool packaged, bool debug) => packaged ? "store" : debug ? "dev" : "beta";

#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    /// <summary>The user's choice. Takes effect at once: nothing is sent after this returns false, even while the SDK is still shutting down.</summary>
    public void SetWanted(bool wanted) => _wanted = wanted;

    /// <summary>Sets the choice, then starts or stops the SDK on the pool (stopping can take a moment).</summary>
    public void Update(bool wanted)
    {
        SetWanted(wanted);
        _ = Task.Run(Apply);
    }

    /// <summary>Starts or stops the SDK to match <see cref="SetWanted"/>. Call off the UI thread.</summary>
    public void Apply()
    {
        lock (_gate)
        {
            if (!(_wanted && !_blocked && IsAvailable))
            {
                _sdk?.Dispose();
                _sdk = null;
                return;
            }

            if (_sdk is not null) return;
            try { _sdk = Start(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Crash reporting couldn't start: " + ex.Message);
                _sdk = null;
            }

            // Turned off while it was starting.
            if (!_wanted) { _sdk?.Dispose(); _sdk = null; }
        }
    }
    // Kept apart so that a run with reporting off never loads the SDK.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private IDisposable Start() => SentrySdk.Init(Configure);

    private void Configure(SentryOptions options)
    {
        options.Dsn = Dsn;
        options.Debug = false;
        options.SendDefaultPii = false;
        options.IsEnvironmentUser = false;
        options.ServerName = string.Empty;
        options.AutoSessionTracking = false;
        options.IsGlobalModeEnabled = true;
        options.Release = "automation-portal@" + (typeof(CrashReporting).Assembly.GetName().Version?.ToString(3) ?? "0");
        options.Environment = EnvironmentName(PackageIdentity.IsPackaged, DebugBuild);
        options.AttachStacktrace = false;
        options.SendClientReports = false;
        options.MaxBreadcrumbs = 0;
        options.TracesSampleRate = 0;
        options.ShutdownTimeout = TimeSpan.FromSeconds(2);
        // Unobserved task exceptions are captured by our own handler, as handled errors.
        options.DisableUnobservedTaskExceptionCapture();
        options.SetBeforeSend(BeforeSend);
        options.SetBeforeBreadcrumb(static _ => null);
        options.SetBeforeSendTransaction(static _ => null);
        options.SetBeforeSendFeedback(static _ => null);
    }

    public void CaptureCrash(Exception exception, string source, bool handled)
    {
        if (!_wanted || !IsRunning || exception is null) return;
        try
        {
            exception.Data[Mechanism.HandledKey] = handled;
            exception.Data[Mechanism.MechanismKey] = source;
            SentrySdk.CaptureException(exception, scope => scope.SetTag("source", source));
            if (!handled) SentrySdk.Flush(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Reporting a crash must never be the next crash.
        }
    }

    private SentryEvent? BeforeSend(SentryEvent evt, SentryHint hint)
    {
        if (!_wanted) return null;
        try
        {
            hint.Attachments.Clear();
            Redact(evt);
            return evt;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Unsure what is in it: it does not go.
            return null;
        }
    }

    // Contexts that say nothing about the person or their PC.
    private static readonly string[] KeptContexts = ["os", "runtime"];

    internal void Redact(SentryEvent evt)
    {
        evt.ServerName = null;
        evt.User.Username = null;
        evt.User.Email = null;
        evt.User.IpAddress = null;
        evt.User.Id = null;
        foreach (var key in evt.Contexts.Keys.ToList())
            if (!KeptContexts.Contains(key)) evt.Contexts.Remove(key);
        evt.Request = new SentryRequest();
        evt.Fingerprint = [];
        evt.Modules.Clear();
        (evt.Breadcrumbs as ICollection<Breadcrumb>)?.Clear();
        evt.TransactionName = _redactor.Redact(evt.TransactionName);
        evt.Logger = _redactor.Redact(evt.Logger);
        if (evt.Message is { } message)
        {
            message.Message = _redactor.Redact(message.Message);
            message.Formatted = _redactor.Redact(message.Formatted);
            message.Params = null;
        }

        foreach (var exception in evt.SentryExceptions ?? [])
        {
            // The app's own exceptions (ActionException and friends) carry shortcut names and typed text in their messages: type name only.
            exception.Value = IsFrameworkType(exception.Type) ? _redactor.Redact(exception.Value) : null;
            exception.Module = null;
            if (exception.Mechanism is { } mechanism)
            {
                mechanism.Data.Clear();
                mechanism.Description = null;
            }

            RedactFrames(exception.Stacktrace);
        }

        foreach (var thread in evt.SentryThreads ?? [])
        {
            thread.Name = null;
            RedactFrames(thread.Stacktrace);
        }

        foreach (var image in evt.DebugImages ?? [])
        {
            image.CodeFile = _redactor.Redact(image.CodeFile);
            image.DebugFile = _redactor.Redact(image.DebugFile);
        }

        // Extra data and tags can hold free text (names, values), so only the tag we set ourselves keeps its value.
        foreach (var key in evt.Extra.Keys.ToList())
            evt.SetExtra(key, "<removed>");
        foreach (var (key, value) in evt.Tags.ToList())
            evt.SetTag(key, key == "source" ? _redactor.Redact(value) ?? string.Empty : "<removed>");
    }

    /// <summary>True for .NET and Windows exception types, whose messages are fixed text (plus paths, which are redacted). Anything else is the app's own.</summary>
    internal static bool IsFrameworkType(string? type) =>
        type is not null && (type.StartsWith("System.", StringComparison.Ordinal) || type.StartsWith("Microsoft.", StringComparison.Ordinal)
            || type.StartsWith("Windows.", StringComparison.Ordinal) || type.StartsWith("WinRT.", StringComparison.Ordinal));
    private void RedactFrames(SentryStackTrace? stack)
    {
        foreach (var frame in stack?.Frames ?? [])
        {
            frame.FileName = _redactor.Redact(frame.FileName);
            frame.AbsolutePath = _redactor.Redact(frame.AbsolutePath);
            frame.ContextLine = _redactor.Redact(frame.ContextLine);
            frame.Package = _redactor.Redact(frame.Package);
            frame.Vars.Clear();
            frame.PreContext.Clear();
            frame.PostContext.Clear();
        }
    }
}

