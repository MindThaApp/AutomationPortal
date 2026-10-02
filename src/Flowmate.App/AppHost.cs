using System.Windows;
using System.Windows.Threading;
using Flowmate.App.Controls;
using Flowmate.App.DesignTime;
using Flowmate.App.Integration;
using Flowmate.App.Pages;
using Flowmate.App.Services;
using Flowmate.App.Shell;
using Flowmate.Core;

namespace Flowmate.App;

/// <summary>
/// Composition root: the one place that creates the services, registries, runner, library, history and settings,
/// and the one place integration (phase E) plugs the other projects in. Search for INTEGRATION(E).
/// </summary>
public sealed class AppHost : IDisposable
{
    public static AppHost Current { get; private set; } = null!;

    // ---- core services ---------------------------------------------------------------------
    public FlowmatePaths Paths { get; private set; } = null!;
    public FlowmateServices Services { get; private set; } = null!;
    public ActionRegistry Actions { get; private set; } = null!;
    public TriggerRegistry Triggers { get; private set; } = null!;
    public ConditionRegistry Conditions { get; private set; } = null!;
    public ShortcutRunner Runner { get; private set; } = null!;
    public JsonShortcutLibrary LibraryStore { get; private set; } = null!;
    public IShortcutLibrary Library => LibraryStore;
    public IRunHistory History { get; private set; } = null!;
    public ISettingsStore Settings { get; private set; } = null!;
    public ISecretStore Secrets { get; private set; } = null!;
    public ReminderStore Reminders { get; private set; } = null!;
    public IUiThread UiThread { get; private set; } = null!;

    // ---- app services ----------------------------------------------------------------------
    public LocalUiSettings UiSettings { get; private set; } = null!;
    public ThemeService Theme { get; private set; } = null!;
    public ShortcutRunService Runs { get; private set; } = null!;
    public UserInteraction Interaction { get; private set; } = null!;
    public ToastNotificationService Notifications { get; private set; } = null!;
    public GalleryService Gallery { get; private set; } = null!;

    // ---- pieces integration swaps ----------------------------------------------------------
    public IShortcutEditorLauncher EditorLauncher { get; set; } = null!;
    public IShellIntegration Shell { get; set; } = new StubShellIntegration();
    public IAiKeyTester AiKeyTester { get; set; } = new StubAiKeyTester();
    public IParameterFormFactory ParameterForms { get; set; } = null!;
    /// <summary>Quick Run results sources. Index 0 is the built-in shortcut matcher.</summary>
    public List<IQuickRunProvider> QuickRunProviders { get; } = new();

    // ---- windows ---------------------------------------------------------------------------
    public MainWindow? MainWindow { get; private set; }
    public TrayService? Tray { get; private set; }
    private QuickRunWindow? _quickRun;
    private QuickPanelWindow? _quickPanel;
    private IReadOnlyList<GallerySection>? _gallerySections;

    public bool IsExiting { get; private set; }
    public bool StartInBackground { get; private set; }
    public Dispatcher Dispatcher { get; private set; } = null!;

    private AppHost() { }

    public static AppHost Create(string[] args, Dispatcher dispatcher)
    {
        var host = new AppHost { Dispatcher = dispatcher };
        Current = host;
        host.Build(args);
        return host;
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private string[] _args = Array.Empty<string>();

    private void Build(string[] args)
    {
        _args = args;
        StartInBackground = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        // A throwaway data root (tests, demos) keeps everything away from the user's real %APPDATA%.
        var dataRoot = ArgValue(args, "--data-root") ?? Environment.GetEnvironmentVariable("FLOWMATE_DATA_ROOT");
        var basePaths = string.IsNullOrWhiteSpace(dataRoot) ? FlowmatePaths.Default : FlowmatePaths.ForRoot(dataRoot);
        if (Environment.GetEnvironmentVariable("FLOWMATE_LOG") is not { Length: > 0 })
            AppLog.FilePath = Path.Combine(basePaths.LogsFolder, "app.log");

        UiThread = new DispatcherUiThread(Dispatcher);
        Settings = new JsonSettingsStore(basePaths);
        Paths = string.IsNullOrWhiteSpace(Settings.Current.LibraryFolder) ? basePaths : basePaths.WithLibrary(Settings.Current.LibraryFolder);
        Secrets = new DpapiSecretStore(Paths);
        UiSettings = new LocalUiSettings(Paths);

        // Registries. Core's built-in actions come with CreateWithBuiltIns.
        Actions = ActionRegistry.CreateWithBuiltIns();
        Triggers = new TriggerRegistry();
        Conditions = new ConditionRegistry();
        Triggers.RegisterAssembly(typeof(ActionRegistry).Assembly);
        Conditions.RegisterAssembly(typeof(ActionRegistry).Assembly);
        // INTEGRATION(E): register the other action / trigger / condition assemblies here, for example
        //   Actions.RegisterAssembly(typeof(Flowmate.Actions.Content.<AnyPublicType>).Assembly);   // Content, Windows, Documents
        //   Triggers.RegisterAssembly(typeof(Flowmate.Triggers.<AnyPublicType>).Assembly);
        //   Conditions.RegisterAssembly(typeof(Flowmate.Triggers.<AnyPublicType>).Assembly);
        // Until then the Automations UI uses sample providers (only registered while the registries are empty).
        SampleProviders.RegisterIfEmpty(Triggers, Conditions);

        LibraryStore = new JsonShortcutLibrary(Paths, Actions);
        foreach (var problem in LibraryStore.LoadErrors) AppLog.Write("Library: " + problem);
        History = new JsonlRunHistory(Paths);
        Reminders = new ReminderStore(Paths);

        Services = new FlowmateServices
        {
            Paths = Paths, Library = Library, History = History, Settings = Settings, Secrets = Secrets, Reminders = Reminders,
            UiThread = UiThread, KeyValues = new JsonKeyValueStore(Paths),
        };
        Interaction = new UserInteraction(UiThread);
        Services.UserInteraction = Interaction;
        Services.Clipboard = new ClipboardService(UiThread, Paths);
        Notifications = new ToastNotificationService(UiThread, () => Services.UserInteraction, (t, b) => Tray?.ShowBalloon(t, b));
        Services.Notifications = Notifications;
        Runner = new ShortcutRunner(Actions, Services);   // also registers itself as Services.Runner

        Runs = new ShortcutRunService(Runner, Library, Settings, () => Services.Notifications, UiThread);
        Runs.StateChanged += OnRunStateChanged;
        Gallery = new GalleryService();
        Theme = new ThemeService();

        MainWindowOwner = () => MainWindow is { IsVisible: true } w ? w : null;
        EditorLauncher = new FallbackEditorLauncher(Library, MainWindowOwner);
        // INTEGRATION(E): EditorLauncher = new <EditorLauncher using Flowmate.Editor ShortcutEditor.Open(...) and IconPickerDialog>(...)
        ParameterForms = new LiteralParameterFormFactory();
        // INTEGRATION(E): ParameterForms = <editor's parameter form factory> to get variable-aware parameter editors.
        // INTEGRATION(E): Shell = new <registry / shell-link IShellIntegration> (start with Windows, Send to, desktop links).
        // INTEGRATION(E): AiKeyTester = new <real AI key tester>.

        QuickRunProviders.Add(new ShortcutQuickRunProvider(Library, UiSettings));
        // INTEGRATION(E): QuickRunProviders.Add(calculator / unit and currency conversion / web search / Ask AI providers).

        Settings.Changed += (_, s) => Dispatcher.BeginInvoke(() => Theme.Apply(s.Theme));
        Reminders.Due += OnReminderDue;
    }

    public Func<Window?> MainWindowOwner { get; private set; } = () => null;

    // ---- start / stop ----------------------------------------------------------------------

    public void Start()
    {
        Theme.Apply(Settings.Current.Theme);
        Tray = new TrayService(this);
        Tray.Show();
        if (!StartInBackground) ShowMainWindow();
        Reminders.Start();
        ShowRequestedWindow();

        // INTEGRATION(E): start the TriggerHost / AutomationEngine here (it reads Settings.Current.AutomationsPaused).
        // INTEGRATION(E): bind global hotkeys with HotkeyService:
        //   Settings.Current.QuickRunHotkey          -> ShowQuickRun()
        //   UiSettings.Current.RunOnSelectionHotkey  -> grab the selected text, then ShowQuickRun(selectedText)
        //   UiSettings.Current.QuickPanelHotkey      -> ShowQuickPanel(atPointer: true)
        //   every Shortcut.Hotkey                    -> Runs.RunAsync(shortcut, null, RunSource.Hotkey)
        //   (re-bind when Settings.Changed / UiSettings.Changed / Library.Changed fire)
        // INTEGRATION(E): handle command-line actions here and from the named pipe: --run, --import, --background, flowmate://run.
        //   Helpers: RunByNameOrId(...), ImportFile(...), ShowMainWindow(), ShowQuickRun().
    }

    /// <summary>Developer aid: <c>--show quickrun|quickrun-selection|panel|panel-pointer|wizard|history|settings</c> opens that window at start.</summary>
    private void ShowRequestedWindow()
    {
        var what = ArgValue(_args, "--show");
        if (what is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            switch (what.ToLowerInvariant())
            {
                case "quickrun": ShowQuickRun(); break;
                case "quickrun-selection": ShowQuickRun("Some selected text"); break;
                case "panel": ShowQuickPanel(false); break;
                case "panel-pointer": ShowQuickPanel(true); break;
                case "wizard": _ = Dialogs.AutomationWizardWindow.ShowAsync(this, MainWindow); break;
                case "history": OpenRunHistory(); break;
                case "settings": ShowMainWindow(new NavTarget(AppPage.Settings)); break;
            }
        });
    }

    public void Exit()
    {
        if (IsExiting) return;
        IsExiting = true;
        Dispatcher.BeginInvoke(() =>
        {
            try { _quickRun?.Close(); _quickPanel?.Close(); MainWindow?.Close(); } catch { }
            Tray?.Dispose();
            Application.Current.Shutdown();
        });
    }

    public void Dispose()
    {
        try { Tray?.Dispose(); } catch { }
        try { Notifications.Dispose(); } catch { }
        try { Reminders.Dispose(); } catch { }
        try { LibraryStore.Dispose(); } catch { }
    }

    // ---- windows ---------------------------------------------------------------------------

    public void ShowMainWindow(NavTarget? target = null)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is null)
            {
                MainWindow = new MainWindow(this);
                Theme.Refresh(MainWindow);
            }
            if (target is not null && MainWindow.IsLoaded) MainWindow.NavigateTo(target);
            else if (target is not null) MainWindow.Loaded += (_, _) => MainWindow.NavigateTo(target);
            if (!MainWindow.IsVisible) MainWindow.Show();
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
            MainWindow.Topmost = true;      // reliable way to get in front when another app is active
            MainWindow.Topmost = false;
            MainWindow.Focus();
        });
    }

    /// <summary>Opens Quick Run. <paramref name="presetInput"/> is the selected text for "Run on selection".</summary>
    public void ShowQuickRun(string? presetInput = null)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _quickRun ??= new QuickRunWindow(this);
            _quickRun.Open(presetInput);
        });
    }

    /// <summary>Opens the Quick Panel near the tray, or at the mouse pointer.</summary>
    public void ShowQuickPanel(bool atPointer)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _quickPanel ??= new QuickPanelWindow(this);
            if (_quickPanel.IsVisible && !atPointer) { _quickPanel.Hide(); return; }
            _quickPanel.Open(atPointer);
        });
    }

    public void ToggleAutomationsPaused() =>
        Settings.Update(s => s.AutomationsPaused = !s.AutomationsPaused);

    public void OpenRunHistory(string? runId = null) => ShowMainWindow(new NavTarget(AppPage.History, Item: runId));

    public void Reload() => LibraryStore.Reload();

    // ---- gallery ---------------------------------------------------------------------------

    public IReadOnlyList<GallerySection> GallerySections(bool reload = false)
    {
        if (reload || _gallerySections is null) _gallerySections = Gallery.Load();
        return _gallerySections;
    }

    // ---- helpers integration can use -------------------------------------------------------

    /// <summary>For --run and flowmate://run: finds a shortcut by id or (case-insensitive) name and runs it.</summary>
    public Task<RunResult?> RunByNameOrId(string nameOrId, string? inputText = null, RunSource source = RunSource.Cli)
    {
        var s = Guid.TryParse(nameOrId, out var id) ? Library.GetById(id) : Library.FindByName(nameOrId);
        if (s is null) return Task.FromResult<RunResult?>(null);
        return Runs.RunAsync(s, string.IsNullOrEmpty(inputText) ? null : ContentValue.Text(inputText), source);
    }

    /// <summary>For --import and the .flowmate file association.</summary>
    public ImportResult ImportFile(string path) => Library.ImportJson(File.ReadAllText(path));

    // ---- reactions -------------------------------------------------------------------------

    private void OnRunStateChanged(object? sender, RunStateChangedEventArgs e)
    {
        if (e.State != TileRunState.Failed || e.Result is null) return;
        var watching = MainWindow is { IsVisible: true } w && w.Current.Page is AppPage.Library or AppPage.Pinned or AppPage.Folder;
        if (e.Source == RunSource.Library && watching) return;   // the library page shows its own message
        Notifications.Show($"\"{e.ShortcutName}\" didn't finish", e.Result.ErrorMessage ?? "Something went wrong.", null, () => OpenRunHistory(e.Result.RunId));
    }

    private void OnReminderDue(object? sender, Reminder r)
    {
        Notifications.Show(r.Title, r.Body ?? "", null, null);
        if (r.ShortcutIdToRun is { } id)
            Dispatcher.BeginInvoke(() => _ = Runs.RunAsync(id, null, RunSource.Reminder));
    }
}
