using System.Windows;
using System.Windows.Threading;
using AutomationPortal.App.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Pages;
using AutomationPortal.App.Services;
using AutomationPortal.App.Shell;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using AutomationPortal.Host.Wpf;
using AutomationPortal.Triggers;

namespace AutomationPortal.App;

/// <summary>
/// Composition root: the one place that creates the services, registries, runner, library, history and settings,
/// and the one place integration (phase E) plugs the other projects in. Search for INTEGRATION(E).
/// </summary>
public sealed partial class AppHost : IDisposable
{
    public static AppHost Current { get; private set; } = null!;

    // ---- core services ---------------------------------------------------------------------
    public AutomationPortalPaths Paths { get; private set; } = null!;
    public AutomationPortalServices Services { get; private set; } = null!;
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
    public TriggerHost TriggerHost { get; private set; } = null!;
    public IUiThread UiThread { get; private set; } = null!;

    // ---- app services ----------------------------------------------------------------------
    public LocalUiSettings UiSettings { get; private set; } = null!;
    public ThemeService Theme { get; private set; } = null!;
    public ShortcutRunService Runs { get; private set; } = null!;
    public UserInteraction Interaction { get; private set; } = null!;
    public ToastNotificationService Notifications { get; private set; } = null!;
    public GalleryService Gallery { get; private set; } = null!;
    /// <summary>"Related by meaning" search; does nothing unless Portal AI Host is ready and can embed.</summary>
    public SemanticSearchService Semantic { get; private set; } = null!;

    // ---- pieces integration swaps ----------------------------------------------------------
    public IShortcutEditorLauncher EditorLauncher { get; set; } = null!;
    public IShellIntegration Shell { get; set; } = null!;
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

    internal static string? ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private string[] _args = Array.Empty<string>();

    private void Build(string[] args)
    {
        _args = args;
        SubscribeDiagnostics();
        StartInBackground = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        // A throwaway data root (tests, demos) keeps everything away from the user's real %APPDATA%.
        var dataRoot = ArgValue(args, "--data-root") ?? Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_DATA_ROOT");
        _customDataRoot = !string.IsNullOrWhiteSpace(dataRoot);
        // Test harnesses: no global hotkeys or low-level hooks, so a test run never touches the real keyboard.
        _noGlobalInput = args.Any(a => a.Equals("--no-global-input", StringComparison.OrdinalIgnoreCase))
            || Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_NO_GLOBAL_INPUT") == "1";
        UserFolders.UseRoot(dataRoot);
        var basePaths = string.IsNullOrWhiteSpace(dataRoot) ? AutomationPortalPaths.Default : AutomationPortalPaths.ForRoot(dataRoot);
        if (Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_LOG") is not { Length: > 0 })
            AppLog.FilePath = Path.Combine(basePaths.LogsFolder, "app.log");

        UiThread = new DispatcherUiThread(Dispatcher);
        Settings = new JsonSettingsStore(basePaths);
        Paths = string.IsNullOrWhiteSpace(Settings.Current.LibraryFolder) ? basePaths : basePaths.WithLibrary(Settings.Current.LibraryFolder);
        Secrets = new DpapiSecretStore(Paths);
        UiSettings = new LocalUiSettings(Paths);

        // Engine: Core's built-in actions plus the action libraries, the runner, and the WPF UI thread, clipboard and imaging.
        var engine = AutomationPortalEngine.CreateBuilder()
            .UsePaths(Paths)
            .AddActions(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .AddActions(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly)
            .AddActions(typeof(AutomationPortal.Actions.Documents.AiService).Assembly)
            .UseWpf(UiThread)
            .Build();
        Actions = engine.Actions;
        Services = engine.Services;
        Runner = engine.Runner;   // registered as Services.Runner
        Triggers = new TriggerRegistry();
        Conditions = new ConditionRegistry();
        AutomationPortal.Ai.Windows.WindowsAiProvider.Register();
        TriggerHost.RegisterProviders(Triggers, Conditions);
        foreach (var d in Actions.Diagnostics.Concat(Triggers.Diagnostics).Concat(Conditions.Diagnostics)) AppLog.Write("Registry: " + d);
        UiSettings.MigrateHotkeys(Settings);

        LibraryStore = new JsonShortcutLibrary(Paths, Actions);
        foreach (var problem in LibraryStore.LoadErrors) AppLog.Write("Library: " + problem);
        History = new JsonlRunHistory(Paths);
        Reminders = new ReminderStore(Paths);

        Services.Library = Library;
        Services.History = History;
        Services.Settings = Settings;
        Services.Secrets = Secrets;
        Services.Reminders = Reminders;
        Services.KeyValues = new JsonKeyValueStore(Paths);
        Interaction = new UserInteraction(UiThread);
        Services.UserInteraction = Interaction;
        Notifications = new ToastNotificationService(UiThread, () => Services.UserInteraction, (t, b) => Tray?.ShowBalloon(t, b));
        Services.Notifications = Notifications;

        TriggerHost = new TriggerHost(Services, Triggers, Conditions, new TriggerHostOptions { ReminderStore = Reminders, GlobalInputDisabled = _noGlobalInput });
        Services.Reminders = TriggerHost.Reminders;
        var fakeRecorder = args.Any(a => a.Equals("--fake-recorder", StringComparison.OrdinalIgnoreCase));
        RecorderSourceFactory = target => fakeRecorder ? new FakeRecorderSource() : new AppRecorderSource(target, _noGlobalInput);

        Runs = new ShortcutRunService(Runner, Library, Settings, () => Services.Notifications, UiThread, id => Actions.Describe(id)?.Title);
        Runs.StateChanged += OnRunStateChanged;
        TriggerHost.Automations.AutomationRan += OnAutomationRan;
        Runs.ReportsOwnResult = r => DescribeRun(r) is not null;
        Gallery = new GalleryService();
        Theme = new ThemeService();

        MainWindowOwner = () => MainWindow is { IsVisible: true } w ? w : null;
        EditorLauncher = new EditorLauncher(this);
        ParameterForms = new EditorParameterFormFactory();
        AppPickerHooks.Provider = InstalledApps.ChoicesProvider;
        AppPickerHooks.IconLoader = id =>
        {
            var app = InstalledApps.FindAsync(id).GetAwaiter().GetResult();
            return app is null ? null : WpfAppIcons.Load(app, 40);
        };

        BuildIntegration(basePaths, !string.IsNullOrWhiteSpace(dataRoot), args);

        Semantic = new SemanticSearchService(Library, Actions, () => GallerySections().SelectMany(sec => sec.Items).ToList(), AutomationPortal.Actions.Documents.PortalAiHostEmbedder.For(AutomationPortal.Actions.Documents.AiProviderStore.For(Services)));
        QuickRunProviders.Add(new ShortcutQuickRunProvider(Library, UiSettings));
        QuickRunProviders.Add(new CalculatorQuickRunProvider(this));
        QuickRunProviders.Add(new UnitQuickRunProvider(this));
        QuickRunProviders.Add(new CurrencyQuickRunProvider(this));
        QuickRunProviders.Add(new DateQuickRunProvider(this));
        QuickRunProviders.Add(new WebSearchQuickRunProvider());

        Settings.Changed += (_, s) =>
        {
            Dispatcher.BeginInvoke(() => Theme.Apply(s));
            if (_started) Task.Run(ApplyAppHotkeys);
        };
    }

    private bool _customDataRoot, _noGlobalInput;

    /// <summary>Opens the welcome (first start, and Settings › Show welcome again).</summary>
    public void ShowWelcome() => _ = Dialogs.WelcomeDialog.ShowAsync(this, MainWindow);

    public Func<Window?> MainWindowOwner { get; private set; } = () => null;

    /// <summary>
    /// Creates the recorder behind the recording bar for the app the user picked (Record and repeat: UI Automation steps).
    /// <c>--fake-recorder</c> swaps in one that records fixed steps and watches nothing.
    /// </summary>
    public Func<AutomationPortal.Triggers.RecordTarget, Logic.IMacroRecorderSource> RecorderSourceFactory { get; set; } = null!;

    /// <summary>When Portal AI Host was allowed before, checks whether it is ready so Describe it can use it. Only reads; never asks for access.</summary>
    private async Task ProbeAiHostAsync()
    {
        try
        {
            var store = AutomationPortal.Actions.Documents.AiProviderStore.For(Services);
            if (store.HasHostToken) await store.Host.GetStateAsync();
        }
        catch (Exception ex) { AppLog.Write("Portal AI Host check: " + ex.Message); }
    }

    public void OpenAiSettings() => ShowMainWindow(new NavTarget(AppPage.Settings, Item: "ai"));

    /// <summary>Tray / Quick Panel: a new shortcut opens in the editor and recording starts right away.</summary>
    public void RecordNewShortcut(Guid? folderId = null) =>
        Dispatcher.BeginInvoke(() => EditorLauncher.OpenNew(folderId, new EditorOpenOptions(Record: true)));

    // ---- start / stop ----------------------------------------------------------------------

    public void Start()
    {
        Theme.Apply(Settings.Current);
        Tray = new TrayService(this);
        Tray.Show();
        TriggerHost.Start();   // reminders, per-shortcut hotkeys, text expansion, automations
        _started = true;
        StartPlan();
        ApplyAppHotkeys();
        TriggerHost.ShortcutHotkeys.ConflictsChanged += (_, _) => HotkeyProblemsChanged?.Invoke(this, EventArgs.Empty);
        if (!StartInBackground) ShowMainWindow();
        ShowRequestedWindow();
        if (Logic.FirstRun.ShouldShowWelcome(Settings.Current, StartInBackground, _customDataRoot, ArgValue(_args, "--show") is not null, Library.All.Count()))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ShowWelcome);
        StartIntegration();
    }

    // ---- global hotkeys -----------------------------------------------------------

    private bool _started;
    private readonly object _hotkeyGate = new();
    private readonly Dictionary<string, string> _hotkeyProblems = new();

    /// <summary>Raised (any thread) when a Quick Run / Run on selection / Quick Panel / shortcut hotkey starts or stops working.</summary>
    public event EventHandler? HotkeyProblemsChanged;

    /// <summary>Why an app-level hotkey ("quickrun", "selection", "panel") couldn't be registered, or null.</summary>
    public string? HotkeyProblem(string which) { lock (_hotkeyGate) return _hotkeyProblems.GetValueOrDefault(which); }

    private void ApplyAppHotkeys()
    {
        var s = Settings.Current;
        lock (_hotkeyGate)
        {
            var before = string.Join("|", _hotkeyProblems.OrderBy(p => p.Key).Select(p => p.Key + p.Value));
            Bind("quickrun", s.QuickRunHotkey, () => ShowQuickRun(), Strings.Get("AppHost_QuickRun"));
            Bind("selection", s.RunOnSelectionHotkey, () => _ = RunOnSelectionAsync(), Strings.Get("AppHost_RunOnSelectedText"));
            Bind("panel", s.QuickPanelHotkey, () => ShowQuickPanel(true), "the Quick Panel");
            if (before != string.Join("|", _hotkeyProblems.OrderBy(p => p.Key).Select(p => p.Key + p.Value)))
                HotkeyProblemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Bind(string which, string? keys, Action callback, string label)
    {
        var owner = "app:" + which;
        var hk = TriggerHost.Hotkeys;
        if (string.IsNullOrWhiteSpace(keys)) { hk.Unregister(owner); _hotkeyProblems.Remove(which); return; }
        var wanted = HotkeyParser.Normalize(keys);
        var current = hk.Registrations.FirstOrDefault(r => r.OwnerId == owner);
        if (current is not null && string.Equals(current.Hotkey, wanted, StringComparison.OrdinalIgnoreCase)) { _hotkeyProblems.Remove(which); return; }
        var result = hk.Register(owner, keys, () => Dispatcher.BeginInvoke(callback), label);
        if (result.Success) _hotkeyProblems.Remove(which);
        else _hotkeyProblems[which] = result.Reason ?? Strings.Get("AppHost_ThisHotkeyCantBe");
    }

    /// <summary>Plain-English reason a hotkey can't work for this shortcut, or null.</summary>
    public string? HotkeyProblemFor(string hotkey, Guid shortcutId)
    {
        var chord = HotkeyParser.Normalize(hotkey);
        if (chord is null) return $"\"{hotkey}\" isn't a hotkey Automation Portal understands.";
        var s = Settings.Current;
        foreach (var (name, keys) in new[] { (Strings.Get("AppHost_QuickRun"), s.QuickRunHotkey), (Strings.Get("AppHost_RunOnSelectedText"), s.RunOnSelectionHotkey), ("the Quick Panel", s.QuickPanelHotkey) })
            if (HotkeyParser.Normalize(keys) == chord) return $"{chord} is already used by {name}. Choose a different hotkey.";
        var other = Library.All.FirstOrDefault(x => x.Id != shortcutId && HotkeyParser.Normalize(x.Hotkey) == chord);
        if (other is not null) return $"{chord} is already used by the shortcut \"{other.Name}\". Choose a different hotkey.";
        return TriggerHost.ShortcutHotkeys.ConflictFor(shortcutId);
    }

    private async Task RunOnSelectionAsync()
    {
        string? text = null;
        try
        {
            var probe = new ShortcutBuilder(Strings.Get("AppHost_SelectedText"));
            probe.Add("text.getSelected");
            var result = await Runner.RunAsync(probe.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.QuickRun, RecordHistory = false });
            if (result.Status == RunStatus.Succeeded) text = result.Output.AsText();
            else if (result.Status == RunStatus.Failed) Notifications.Show("Couldn't read the selected text", result.ErrorMessage ?? "Select some text first.", null, null, silent: true);
        }
        catch (Exception ex) { AppLog.Write("Run on selection failed: " + ex.Message); }
        ShowQuickRun(text);
    }

    /// <summary>Quick Run's "Ask AI": asks the default AI provider and shows the answer.</summary>
    public async Task AskAiAsync(string question, string? input)
    {
        var b = new ShortcutBuilder(Strings.Get("AppHost_AskAI"));
        b.Add("ai.ask", ("prompt", question));
        b.Add("ui.showResult");
        var result = await Runner.RunAsync(b.Build(), string.IsNullOrEmpty(input) ? ContentValue.Empty : ContentValue.Text(input),
            new RunOptions { Source = RunSource.QuickRun, RecordHistory = false });
        if (result.Status == RunStatus.Failed) Notifications.Show("Ask AI didn't work", result.ErrorMessage ?? "Something went wrong.", null, null);
    }

    /// <summary>Saves a shortcut as an .autoportal file the user chooses.</summary>
    public void ExportShortcut(Shortcut s, Window? owner)
    {
        var name = Path.GetInvalidFileNameChars().Aggregate(s.Name, (n, c) => n.Replace(c, '_'));
        if (PickerHost.Current.PickPath(new PickRequest(PickKind.SaveFile, Strings.Get("LibraryPage_ExportShortcut"), "Automation Portal shortcut (*.autoportal)|*.autoportal", SuggestedName: name + ".autoportal", Owner: owner)) is not { } file) return;
        try { TestRootGuard.Check(file); File.WriteAllText(file, Library.Export(s.Id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
        { _ = Dialogs.Prompts.MessageAsync(owner, "Couldn't export", $"Automation Portal couldn't save the file. {ex.Message}"); }
    }

    /// <summary>Developer aid: <c>--show quickrun|quickrun-selection|panel|panel-pointer|wizard|history|settings|ai-settings|automations|snippets|gallery|welcome</c> opens that window at start (welcome only with a custom data root).</summary>
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
                case "ai-settings": ShowMainWindow(new NavTarget(AppPage.Settings, Item: "ai")); break;
                case "automations": ShowMainWindow(new NavTarget(AppPage.Automations)); break;
                case "snippets": ShowMainWindow(new NavTarget(AppPage.Snippets)); break;
                case "gallery": ShowMainWindow(new NavTarget(AppPage.Gallery)); break;
                // Only with a throwaway data root, where the first-run welcome never shows by itself.
                case "welcome" when _customDataRoot: ShowMainWindow(); ShowWelcome(); break;
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
        try { _syncTimer?.Dispose(); } catch { }
        try { Notifications.Dispose(); } catch { }
        try { TriggerHost.Dispose(); } catch { }
        try { LibraryStore.Dispose(); } catch { }
    }

    // ---- windows ---------------------------------------------------------------------------

    public void ShowMainWindow(NavTarget? target = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is null)
            {
                MainWindow = new MainWindow(this);
                RenderHealth.Watch(MainWindow, Paths, RecreateMainWindow);
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
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                () =>
                {
                    Infrastructure.SpeedBudget.Log("main window shown", watch, Infrastructure.SpeedBudget.WindowShownMs);
                    Infrastructure.SpeedBudget.LogColdStart();
                    BeginUpdateCheck();
                });
        });
    }

    // ---- update notice (Store build only) ------------------------------------------------------

    private Logic.UpdateNudge? _nudge;
    private int _updateCheckStarted;

    /// <summary>The version the "update ready" notice shows for, or null when it is hidden.</summary>
    public string? UpdateNoticeVersion => _nudge?.Visible;

    /// <summary>Raised on the UI thread when the notice appears or is hidden.</summary>
    public event EventHandler? UpdateNoticeChanged;

    /// <summary>After the first window is up: a short wait, then one Store check off the UI thread. Packaged builds only, once per session.</summary>
    private void BeginUpdateCheck()
    {
        if (!PackageIdentity.IsPackaged || Interlocked.Exchange(ref _updateCheckStarted, 1) == 1) return;
        var nudge = new Logic.UpdateNudge(true, new Services.StoreUpdateProbe(),
            () => Settings.Current.DismissedUpdateVersion, v => Settings.Update(s => s.DismissedUpdateVersion = v));
        _nudge = nudge;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (await nudge.CheckAsync().ConfigureAwait(false) is not null)
                    await Dispatcher.BeginInvoke(() => UpdateNoticeChanged?.Invoke(this, EventArgs.Empty));
            }
            catch (Exception ex) { AppLog.Write("Update notice: " + ex.Message); }
        });
    }

    /// <summary>"Update": opens this app's Microsoft Store page.</summary>
    public void OpenStorePage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://pdp/?productid=9PB9C8FHG105") { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Write("Store page: " + ex.Message); }
    }

    /// <summary>"Later": hides the notice and remembers the version.</summary>
    public void DismissUpdateNotice()
    {
        _nudge?.Dismiss();
        UpdateNoticeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecreateMainWindow()
    {
        var old = MainWindow;
        if (old is null) return;
        var target = old.Current;
        MainWindow = null;
        old.Recycling = true;
        old.Close();
        ShowMainWindow(target);
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
        Settings.Update(s => { s.AutomationsPaused = !s.AutomationsPaused; s.AutomationsPausedUntil = null; });

    /// <summary>Pauses automations until the given time, or until resumed when null.</summary>
    public void PauseAutomations(DateTimeOffset? until) =>
        Settings.Update(s => { s.AutomationsPaused = true; s.AutomationsPausedUntil = until; });

    public void ResumeAutomations() =>
        Settings.Update(s => { s.AutomationsPaused = false; s.AutomationsPausedUntil = null; });

    public void OpenRunHistory(string? runId = null) => ShowMainWindow(new NavTarget(AppPage.History, Item: runId));

    public void Reload() => LibraryStore.Reload();

    // ---- gallery ---------------------------------------------------------------------------

    public IReadOnlyList<GallerySection> GallerySections(bool reload = false)
    {
        if (reload || _gallerySections is null) _gallerySections = Gallery.Load();
        return _gallerySections;
    }

    // ---- helpers integration can use -------------------------------------------------------

    /// <summary>For --run and automationportal://run: finds a shortcut by id or (case-insensitive) name and runs it.</summary>
    public Task<RunResult?> RunByNameOrId(string nameOrId, string? inputText = null, RunSource source = RunSource.Cli)
    {
        var s = Guid.TryParse(nameOrId, out var id) ? Library.GetById(id) : Library.FindByName(nameOrId);
        if (s is null) return Task.FromResult<RunResult?>(null);
        return Runs.RunAsync(s, string.IsNullOrEmpty(inputText) ? null : ContentValue.Text(inputText), source);
    }

    /// <summary>For --import and the .autoportal file association.</summary>
    public ImportResult ImportFile(string path) => Library.ImportJson(File.ReadAllText(path));

    // ---- reactions -------------------------------------------------------------------------

    /// <summary>What a finished run changed, from its own record of file changes, with Undo when that record can be undone; null when it changed no files.</summary>
    public RunResultBarModel? DescribeRun(RunResult r)
    {
        if (string.IsNullOrEmpty(r.RunId)) return null;
        try { return RunResultText.ForRun(r.ShortcutName, r.RunId, new FileJournal(Paths).Read(r.RunId)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The same sentence as the result bar, as a notification with Undo and See details, for when the library isn't on screen.</summary>
    private void ShowRunToast(RunResultBarModel bar)
    {
        var actions = new List<(string, Action)>();
        if (bar.CanUndo) actions.Add(("Undo", () => _ = UndoFromToastAsync(bar.RunId)));
        actions.Add((Strings.Get("AppHost_SeeDetails"), () => OpenRunHistory(bar.RunId)));
        var body = char.ToUpperInvariant(bar.Summary[0]) + bar.Summary[1..] + "." + (bar.Message is null ? "" : " " + bar.Message);
        Notifications.ShowWithActions($"\"{bar.ShortcutName}\" finished", body, actions, () => OpenRunHistory(bar.RunId));
    }

    private async Task UndoFromToastAsync(string runId)
    {
        // A toast can be clicked long after the run (records are kept a week): past a few minutes, show what would be undone and ask.
        if (RunUndo.AskFirst(RunUndo.Read(Paths, runId), DateTimeOffset.Now))
        {
            await Dispatcher.InvokeAsync(() => RunUndo.RunAsync(null, Paths, runId)).Task.Unwrap();
            return;
        }
        var (title, message) = await RunUndo.UndoWithoutAskingAsync(Paths, runId);
        Notifications.Show(title, message, null, () => OpenRunHistory(runId));
    }

    /// <summary>The first time an automation runs by itself (not a test): one notification saying what it did. The automation's own "tell me when it runs" toast already says it, so that counts as told.</summary>
    private void OnAutomationRan(object? sender, AutomationRanEventArgs e)
    {
        if (e.IsTest || e.Result is not { Status: RunStatus.Succeeded or RunStatus.Stopped } done || UiSettings.Current.FirstAutomationRunShown) return;
        var take = false;
        UiSettings.Update(d => take = FirstAutomationRun.TryTake(d));
        if (!take || Library.Automations.FirstOrDefault(a => a.Id == e.AutomationId)?.NotifyWhenRun == true) return;
        try
        {
            var bar = DescribeRun(done);
            var actions = new List<(string, Action)>();
            if (bar?.CanUndo == true) actions.Add(("Undo", () => _ = UndoFromToastAsync(bar.RunId)));
            actions.Add((Strings.Get("AppHost_SeeWhatItDid"), () => OpenRunHistory(done.RunId)));
            Notifications.ShowWithActions(FirstAutomationRun.Title(e.ShortcutName), FirstAutomationRun.Body(bar), actions, () => OpenRunHistory(done.RunId));
        }
        catch (Exception ex) { AppLog.Write("First automation notification failed: " + ex.Message); }
    }

    private void OnRunStateChanged(object? sender, RunStateChangedEventArgs e)
    {
        if (e.Result is not null && e.State != TileRunState.Running)
            RunSounds.Play(RunSounds.For(UiSettings.Current.PlayRunSounds, e.Source, e.State));
        var watching = MainWindow is { IsVisible: true } w && w.WindowState != WindowState.Minimized && w.Current.Page is AppPage.Library or AppPage.Pinned or AppPage.Folder;
        if (e.State == TileRunState.Succeeded && e.Result is { } done && !watching && RunResultText.Reports(e.Source))
        {
            if (DescribeRun(done) is { } bar) ShowRunToast(bar);
            return;
        }
        if (e.State != TileRunState.Failed || e.Result is null) return;
        if (e.Source == RunSource.Library && watching) return;   // the library page shows its own message
        var (failTitle, failBody) = UnattendedFailure.Describe(e.ShortcutName, Library.GetById(e.ShortcutId), e.Source, e.Result.ErrorMessage);
        Notifications.Show(failTitle, failBody, null, () => OpenRunHistory(e.Result.RunId));
    }
}
