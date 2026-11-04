using System.Windows.Threading;
using Flowmate.Core;
using Flowmate.Host.Wpf;

namespace Flowmate.Cli;

/// <summary>What fm.exe builds when the app isn't running: the library, the actions and a runner, with console prompts.</summary>
internal sealed class CliHost : IDisposable
{
    private readonly JsonShortcutLibrary _library;
    private readonly ShortcutRunner _runner;
    private readonly Dispatcher _dispatcher;

    public ShortcutCommands Commands { get; }
    public ConsoleInteraction Interaction { get; } = new();

    public CliHost(string? dataRoot)
    {
        var basePaths = string.IsNullOrWhiteSpace(dataRoot) ? FlowmatePaths.Default : FlowmatePaths.ForRoot(dataRoot);
        var settings = new JsonSettingsStore(basePaths);
        var paths = string.IsNullOrWhiteSpace(settings.Current.LibraryFolder) ? basePaths : basePaths.WithLibrary(settings.Current.LibraryFolder);

        // The clipboard and imaging actions need a single STA thread with a message loop, like the app's UI thread.
        _dispatcher = WpfHost.StartStaDispatcher();
        var engine = FlowmateEngine.CreateBuilder()
            .UsePaths(paths)
            .AddActions(typeof(Flowmate.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .AddActions(typeof(Flowmate.Actions.Windows.Apps.InstalledApps).Assembly)
            .AddActions(typeof(Flowmate.Actions.Documents.AiService).Assembly)
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
            await _runner.RunAsync(shortcut, input, new RunOptions { Source = source, CancellationToken = ct }).ConfigureAwait(false));
    }

    public void Dispose()
    {
        try { _library.Dispose(); } catch { }
        try { _dispatcher.InvokeShutdown(); } catch { }
    }
}
