using System.Windows;
using System.Windows.Threading;
using Flowmate.App.Integration;
using Flowmate.Core;

namespace Flowmate.App;

public partial class App : Application
{
    private AppHost? _host;
    private InstanceCoordinator? _instance;
    private readonly TaskCompletionSource<AppHost> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write("Unhandled: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { AppLog.Write("Unobserved task error: " + ex.Exception); ex.SetObserved(); };

        var launch = LaunchArguments.Parse(e.Args);
        var dataRoot = InstanceNames.DataRootFrom(e.Args);
        if (launch.Unregister)
        {
            try { AppHost.UnregisterShell(dataRoot); }
            catch (Exception ex) { AppLog.Write("Unregister failed: " + ex); }
            Shutdown(0);
            return;
        }

        // One copy per user: later launches hand their command line to the first one and leave.
        var instance = new InstanceCoordinator(InstanceNames.For(dataRoot));
        if (!instance.IsPrimary)
        {
            var delivered = instance.Forward(e.Args);
            if (!delivered)
                MessageBox.Show("Flow Mate is already running, but it didn't answer. Look for it in the notification area, or restart it from Task Manager.", "Flow Mate", MessageBoxButton.OK, MessageBoxImage.Information);
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
            _host = AppHost.Create(e.Args, Dispatcher);
            _host.Start();
            _ready.TrySetResult(_host);
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed: " + ex);
            _ready.TrySetException(ex);
            MessageBox.Show("Flow Mate couldn't start.\n\n" + ex.Message + "\n\nDetails were saved to the log file.", "Flow Mate", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(w, "Something unexpected happened, but Flow Mate is still running.\n\n" + e.Exception.Message, "Flow Mate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }
}

/// <summary>Tiny file logger. Path from FLOWMATE_LOG, else %LOCALAPPDATA%\Flowmate\logs\app.log.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; set; } =
        Environment.GetEnvironmentVariable("FLOWMATE_LOG") is { Length: > 0 } p ? p : Path.Combine(FlowmatePaths.Default.LogsFolder, "app.log");

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
