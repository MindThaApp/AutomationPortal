using System.Windows;
using System.Windows.Threading;
using Flowmate.Core;

namespace Flowmate.App;

public partial class App : Application
{
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write("Unhandled: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { AppLog.Write("Unobserved task error: " + ex.Exception); ex.SetObserved(); };

        // INTEGRATION(E): single instance + named pipe goes here, before AppHost.Create. A second launch forwards its
        // command line (--run, --import, --background, flowmate://) to the first instance and exits.
        try
        {
            _host = AppHost.Create(e.Args, Dispatcher);
            _host.Start();
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed: " + ex);
            MessageBox.Show("Flowmate couldn't start.\n\n" + ex.Message + "\n\nDetails were saved to the log file.", "Flowmate", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("UI error: " + e.Exception);
        e.Handled = true;
        try
        {
            if (_host?.MainWindow is { IsVisible: true } w)
                MessageBox.Show(w, "Something unexpected happened, but Flowmate is still running.\n\n" + e.Exception.Message, "Flowmate", MessageBoxButton.OK, MessageBoxImage.Warning);
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
