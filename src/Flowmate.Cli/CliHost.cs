using System.Windows.Threading;
using Flowmate.App.Services;
using Flowmate.Core;

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

        var actions = ActionRegistry.CreateWithBuiltIns();
        actions.RegisterAssembly(typeof(Flowmate.Actions.Content.Utilities.ExpressionEvaluator).Assembly);
        actions.RegisterAssembly(typeof(Flowmate.Actions.Windows.Apps.InstalledApps).Assembly);
        actions.RegisterAssembly(typeof(Flowmate.Actions.Documents.AiService).Assembly);

        _dispatcher = StartStaDispatcher();
        var ui = new DispatcherUiThread(_dispatcher);
        _library = new JsonShortcutLibrary(paths, actions, watch: false);
        var services = new FlowmateServices
        {
            Paths = paths, Library = _library, History = new JsonlRunHistory(paths), Settings = settings, Secrets = new DpapiSecretStore(paths),
            KeyValues = new JsonKeyValueStore(paths), UiThread = ui, UserInteraction = Interaction, Notifications = new ConsoleNotifications(),
            Clipboard = new ClipboardService(ui, paths),
        };
        _runner = new ShortcutRunner(actions, services);
        Commands = new ShortcutCommands(_library, async (shortcut, input, source, ct) =>
            await _runner.RunAsync(shortcut, input, new RunOptions { Source = source, CancellationToken = ct }).ConfigureAwait(false));
    }

    /// <summary>The clipboard and imaging actions need a single STA thread with a message loop, like the app's UI thread.</summary>
    private static Dispatcher StartStaDispatcher()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Flowmate STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public void Dispose()
    {
        try { _library.Dispose(); } catch { }
        try { _dispatcher.InvokeShutdown(); } catch { }
    }
}
