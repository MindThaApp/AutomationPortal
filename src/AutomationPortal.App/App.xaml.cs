using System.Windows;
using System.Windows.Threading;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Startup;
using AutomationPortal.Core;

namespace AutomationPortal.App;

public partial class App : Application
{
    private AppHost? _host;
    private InstanceCoordinator? _instance;
    private readonly TaskCompletionSource<AppHost> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AutomationPortal.Editor.AccessibleNames.Register();
        AutomationPortal.Editor.DesignTokens.Attach(this);
        AutomationPortal.Editor.HighContrast.Attach(this);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            AppLog.Write("Unhandled: " + ex.ExceptionObject);
            if (ex.ExceptionObject is Exception crash) CrashReporting.Current?.CaptureCrash(crash, "AppDomain.UnhandledException", handled: false);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write("Unobserved task error: " + ex.Exception);
            CrashReporting.Current?.CaptureCrash(ex.Exception, "TaskScheduler.UnobservedTaskException", handled: true);
            ex.SetObserved();
        };

        // An automationportal:// link launch acts on the link only; extra arguments could have been smuggled in through the URL.
        var args = LaunchArguments.Trusted(e.Args).ToArray();
        var launch = LaunchArguments.Parse(args);
        var dataRoot = InstanceNames.DataRootFrom(args);
        if (launch.Unregister)
        {
            try { AppHost.UnregisterShell(dataRoot, AppHost.ArgValue(args, "--shell-sandbox")); }
            catch (Exception ex) { AppLog.Write("Unregister failed: " + ex); }
            Shutdown(0);
            return;
        }

        // One copy per user: later launches hand their command line to the first one and leave.
        var instance = new InstanceCoordinator(InstanceNames.For(dataRoot));
        if (!instance.IsPrimary)
        {
            var delivered = instance.Forward(args);
            if (!delivered)
                MessageBox.Show(Strings.Get("App_AutomationPortalIsAlready"), Strings.Get("App_AutomationPortal"), MessageBoxButton.OK, MessageBoxImage.Information);
            instance.Dispose();
            Shutdown(delivered ? 0 : 1);
            return;
        }

        _instance = instance;
        // The pipe is open before the app is ready; requests that arrive in between wait for it.
        instance.StartServer(async (request, ct) =>
        {
            var host = await _ready.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            return await host.HandlePipeAsync(request, ct).ConfigureAwait(false);
        });

        try
        {
            _host = AppHost.Create(args, Dispatcher);
            var reporter = CrashReporting.Create(dataRoot, AppHost.ArgValue(args, "--shell-sandbox") is not null);
            reporter.Update(_host.Settings.Current.SendCrashReports);
            RenderHealth.ApplyRememberedMode(_host.Paths);
            _host.Start();
            _ready.TrySetResult(_host);
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed: " + ex);
            _ready.TrySetException(ex);
            MessageBox.Show("Automation Portal couldn't start.\n\n" + ex.Message + "\n\nDetails were saved to the log file.", Strings.Get("App_AutomationPortal"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("UI error: " + e.Exception);
        e.Handled = true;
        try
        {
            if (_host?.MainWindow is { IsVisible: true } w)
                MessageBox.Show(w, "Something unexpected happened, but Automation Portal is still running.\n\n" + e.Exception.Message, Strings.Get("App_AutomationPortal"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }
}

/// <summary>Tiny file logger. Path from AUTOMATIONPORTAL_LOG, else %LOCALAPPDATA%\Automation Portal\logs\app.log.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; set; } =
        Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_LOG") is { Length: > 0 } p ? p : Path.Combine(AutomationPortalPaths.Default.LogsFolder, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

