using System.Windows.Threading;
using AutomationPortal.Core;
using AutomationPortal.Host.Wpf;

namespace AutomationPortal.Cli;

/// <summary>What ap.exe builds when the app isn't running: the library, the actions and a runner, with console prompts.</summary>
internal sealed class CliHost : IDisposable
{
    private readonly JsonShortcutLibrary _library;
    private readonly ShortcutRunner _runner;
    private readonly Dispatcher _dispatcher;

    public ShortcutCommands Commands { get; }
    /// <summary>Where the app keeps its settings (the plan cache lives beside them).</summary>
    public AutomationPortalPaths BasePaths { get; }
    public int ShortcutCount => _library.All.Count;
    public ConsoleInteraction Interaction { get; } = new();

    public CliHost(string? dataRoot)
    {
        UserFolders.UseRoot(dataRoot);
        var basePaths = string.IsNullOrWhiteSpace(dataRoot) ? AutomationPortalPaths.Default : AutomationPortalPaths.ForRoot(dataRoot);
        BasePaths = basePaths;
        var settings = new JsonSettingsStore(basePaths);
        var paths = string.IsNullOrWhiteSpace(settings.Current.LibraryFolder) ? basePaths : basePaths.WithLibrary(settings.Current.LibraryFolder);

        // The clipboard and imaging actions need a single STA thread with a message loop, like the app's UI thread.
        _dispatcher = WpfHost.StartStaDispatcher();
        var engine = AutomationPortalEngine.CreateBuilder()
            .UsePaths(paths)
            .AddActions(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .AddActions(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly)
            .AddActions(typeof(AutomationPortal.Actions.Documents.AiService).Assembly)
            .UseWpf(_dispatcher)
            .Build();

        _library = new JsonShortcutLibrary(paths, engine.Actions, watch: false);
        var services = engine.Services;
        services.Library = _library;
        services.History = new JsonlRunHistory(paths);
        services.Settings = settings;
        services.Secrets = new DpapiSecretStore(paths);
        services.KeyValues = new JsonKeyValueStore(paths);
        services.UserInteraction = Interaction;
        services.Notifications = new ConsoleNotifications();
        _runner = engine.Runner;
        Commands = new ShortcutCommands(_library, async (shortcut, input, source, ct) =>
            await _runner.RunAsync(shortcut, input, new RunOptions { Source = source, CancellationToken = ct }).ConfigureAwait(false))
        {
            Actions = engine.Actions,
        };
    }

    public void Dispose()
    {
        try { _library.Dispose(); } catch { }
        try { _dispatcher.InvokeShutdown(); } catch { }
    }
}
