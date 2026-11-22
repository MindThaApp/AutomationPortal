using System.Diagnostics;
using System.Windows.Threading;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Pages;
using AutomationPortal.Core;

namespace AutomationPortal.App;

// Command line, links, Send To, right-click menu, the pipe (second launches and the ap CLI) and shell sync.
public sealed partial class AppHost
{
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>The CLI's list / run / import / export, answered for ap.exe over the pipe.</summary>
    public ShortcutCommands Commands { get; private set; } = null!;

    private sealed class FileBatch
    {
        public readonly List<string> Files = new();
        public string? Input;
        public RunSource Source;
        public bool Gate;
        public bool AllTokens;
        public DispatcherTimer Timer = null!;
    }

    /// <summary>The tokens entries made by this app carry (<see cref="LaunchTokens"/>).</summary>
    private LaunchTokens _tokens = null!;

    /// <summary>The program that started this AutomationPortal.exe, read at start-up (for the first launch's --run).</summary>
    private string? _ownParent;

    private bool TokenAccepted(Guid shortcutId, LaunchCommand launch, string? launcherParent)
    {
        try { return _tokens.Accepts(shortcutId, launch.Token, !string.IsNullOrEmpty(launch.Input), launch.Files.Count > 0, launcherParent); }
        catch (Exception ex) { AppLog.Write("Launch token check failed: " + ex.Message); return false; }
    }

    private string? TokenFor(Guid shortcutId, string entryKind)
    {
        try { return _tokens.For(shortcutId, entryKind); }
        catch (Exception ex) { AppLog.Write("Launch token failed: " + ex.Message); return null; }
    }

    private readonly Dictionary<Guid, FileBatch> _batches = new();
    private Timer? _syncTimer;
    private LaunchCommand _firstLaunch = new();

    // ---- problems reading or writing the user's files (Core reports them; never block the user with a dialog) ----

    private readonly List<string> _pendingDiagnostics = new();
    private readonly HashSet<string> _shownDiagnostics = new();
    private bool _diagnosticsLive;

    private void SubscribeDiagnostics()
    {
        StoreDiagnostics.Error += (message, ex) =>
        {
            AppLog.Write("Storage: " + message + (ex is null ? "" : " " + ex.Message));
            lock (_pendingDiagnostics)
            {
                if (!_shownDiagnostics.Add(message)) return;
                if (!_diagnosticsLive) { _pendingDiagnostics.Add(message); return; }
            }
            ShowDiagnostic(message);
        };
    }

    private void ShowDiagnostic(string message) =>
        Dispatcher.BeginInvoke(() => Notifications.Show(Strings.Get("AppHost_AutomationPortalHadA"), message));

    private void FlushDiagnostics()
    {
        List<string> toShow;
        lock (_pendingDiagnostics)
        {
            _diagnosticsLive = true;
            toShow = _pendingDiagnostics.ToList();
            _pendingDiagnostics.Clear();
        }
        foreach (var m in toShow) ShowDiagnostic(m);
        if (LibraryStore.LoadErrors.Count > 0 && _shownDiagnostics.Add("library-load"))
            ShowDiagnostic($"{LibraryStore.LoadErrors.Count} shortcut file{(LibraryStore.LoadErrors.Count == 1 ? " was" : "s were")} left untouched because Automation Portal couldn't read {(LibraryStore.LoadErrors.Count == 1 ? "it" : "them")}. Details are in the log.");
    }

    private void BuildIntegration(AutomationPortalPaths basePaths, bool customDataRoot, string[] args)
    {
        _firstLaunch = LaunchArguments.Parse(args);
        if (_firstLaunch.Token is not null) _ownParent = PipeClients.ParentImageOf(Environment.ProcessId);
        StartInBackground |= _firstLaunch.RunTarget is not null || _firstLaunch.ImportFile is not null || _firstLaunch.PickFiles.Count > 0
                             || _firstLaunch.NewShortcutFiles.Count > 0;

        Commands = new ShortcutCommands(Library, (s, input, source, ct) => Runs.RunAsync(s, input, source, ct))
        {
            ReviewImport = ReviewPipeImportAsync, PublishEvent = e => TriggerHost?.PublishAppEvent(e),
            Consent = new CallerConsent(Settings, AskCallerAsync),
            ChangesFiles = s => Logic.FileChangeCheck.FileChangingSteps(s, Actions).Count > 0,
            ControlsApps = s => Logic.FileChangeCheck.ControlsApps(s, Actions),
            Actions = Actions,
            DescribeStep = x => Actions.Describe(x.ActionId) is { } d ? Logic.AutomationSentence.RenderSummary(d.SummarySegments, d.Parameters, x.Parameters) : null,
            LogRequest = entry => AppLog.Write(entry.Describe()),
            LogEvent = AppLog.Write,
            OpenNewShortcut = (request, _) => Dispatcher.InvokeAsync(() => OpenNewShortcutForFiles(request)).Task,
        };

        var options = ShellOptionsFor(basePaths, customDataRoot, ArgValue(args, "--shell-sandbox"));
        try { if (CallerConsent.MigrateWholeCallers(Settings, Library.All) is var n and > 0) AppLog.Write($"Apps allowed to run shortcuts: {n} app(s) allowed for every shortcut now cover only the shortcuts there are."); }
        catch (Exception ex) { AppLog.Write("Allowed apps migration: " + ex.Message); }
        _tokens = new LaunchTokens(Secrets);
        var shell = new WindowsShellIntegration(options, () => Library.All)
        {
            TokenFor = TokenFor,
            Notify = (title, body) => Notifications.Show(title, body),
            JumpListSink = entries => Dispatcher.BeginInvoke(() => JumpListApplier.Apply(entries, options.ExePath)),
        };
        Shell = shell;
        Library.Changed += (_, e) => { if ((e.What & LibraryChange.Shortcuts) != 0) ScheduleShellSync(TimeSpan.FromSeconds(1.5)); };
        // A shortcut that appears while the app runs gets the current "Links" setting as its own; shortcuts that were already here keep following the global one.
        var knownShortcuts = Library.All.Select(s => s.Id).ToHashSet();
        Library.Changed += (_, e) =>
        {
            if ((e.What & LibraryChange.Shortcuts) == 0) return;
            foreach (var s in Library.All)
                if (knownShortcuts.Add(s.Id) && !e.External) LinkPolicy.ApplyDefaultToNew(Settings, s.Id);
        };
    }

    /// <summary>
    /// The real shell locations, or none for a throwaway data root (tests and demos must not register anything on the PC).
    /// <c>--shell-sandbox &lt;folder&gt;</c> (or AUTOMATIONPORTAL_SHELL_SANDBOX) exercises the whole thing against that folder and a throwaway HKCU key instead.
    /// </summary>
    private static ShellOptions ShellOptionsFor(AutomationPortalPaths basePaths, bool customDataRoot, string? sandboxArg = null)
    {
        var sandbox = sandboxArg ?? Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_SHELL_SANDBOX");
        if (!string.IsNullOrWhiteSpace(sandbox))
            return ShellOptions.Sandbox(sandbox, @"Software\AutomationPortalSandbox-" + InstanceNames.For(sandbox)[^8..], Environment.ProcessPath ?? "AutomationPortal.exe", PackageIdentity.IsPackaged);
        return customDataRoot ? ShellOptions.Disabled(basePaths) : ShellOptions.ForCurrentUser(basePaths);
    }

    /// <summary>Runs after the window is up so it never slows start-up: registers links, then syncs Send To, the right-click menu and the Jump List.</summary>
    private void StartIntegration()
    {
        FlushDiagnostics();
        Task.Run(() => { try { TempSweeper.Sweep(Paths); } catch (Exception ex) { AppLog.Write("Temp sweep: " + ex.Message); } });
        Task.Run(() =>
        {
            try { var n = LibraryStore.RecentlyDeleted.Purge(DateTimeOffset.Now); if (n > 0) AppLog.Write($"Recently deleted: removed {n} item(s) older than {RecentlyDeleted.KeepFor.Days} days."); }
            catch (Exception ex) { AppLog.Write("Recently deleted cleanup: " + ex.Message); }
            try { var n = LibraryStore.Versions.Purge(DateTimeOffset.Now); if (n > 0) AppLog.Write($"Version history: removed {n} old version(s)."); }
            catch (Exception ex) { AppLog.Write("Version history cleanup: " + ex.Message); }
        });
        Task.Run(() =>
        {
            try
            {
                Shell.EnsureRegistered();
                // Start with Windows can be changed outside the app (Task Manager, Settings > Apps > Startup): show what is true.
                var actual = Shell.IsStartWithWindowsEnabled;
                var real = Shell is WindowsShellIntegration { IsEnabled: true };
                if (real && PackageIdentity.IsPackaged && !actual && Settings.Current.StartWithWindows)
                {
                    // The packaged build's startup entry begins switched off. Keep the user's choice and ask once.
                    if (!Settings.Current.StartWithWindowsNoticeShown)
                    {
                        Settings.Update(s => s.StartWithWindowsNoticeShown = true);
                        Notifications.Show(Strings.Get("AppHost_StartWithWindowsIs"), Strings.Get("AppHost_TurnStartWithWindows"));
                    }
                }
                else if (real && actual != Settings.Current.StartWithWindows) Settings.Update(s => s.StartWithWindows = actual);
                AppLog.Write($"Started {typeof(AppHost).Assembly.GetName().Version?.ToString(3)} ({(PackageIdentity.IsPackaged ? "packaged " + PackageIdentity.FamilyName : "unpackaged")}); " +
                             $"shell integration {(real ? "on" : "off")}; start with Windows {(actual ? "on" : "off")}");
            }
            catch (Exception ex) { AppLog.Write("Shell registration: " + ex.Message); }
        });
        ScheduleShellSync(TimeSpan.FromSeconds(2));
        ApplyLaunch(_firstLaunch, first: true, _ownParent);
    }

    private readonly object _syncGate = new();

    private void ScheduleShellSync(TimeSpan delay)
    {
        lock (_syncGate)
        {
            _syncTimer ??= new Timer(_ =>
            {
                try { Shell.Sync(Library.All); }
                catch (Exception ex) { AppLog.Write("Shell sync: " + ex.Message); }
            });
            _syncTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Everything that arrives over the pipe: a second launch's command line, or an ap.exe command.</summary>
    public Task<PipeResponse> HandlePipeAsync(PipeRequest request, CancellationToken ct)
    {
        if (request.Kind != "launch") return Commands.ExecuteAsync(request, ct);
        // "launch" hands over a whole command line: only a second AutomationPortal.exe or ap.exe from this app's own folder may send it.
        var image = PipeClients.ImagePathOf(request.ClientProcessId);
        if (!PipeClients.IsOwnLauncher(image, AppContext.BaseDirectory))
        {
            AppLog.Write($"Pipe launch refused: the sender ({ShortcutCommands.SafeLogText(image) ?? "unknown program"}) isn't Automation Portal.");
            return Task.FromResult(PipeResponse.Error(CliExit.Usage, "Only Automation Portal and ap.exe can send a launch request."));
        }
        var launch = LaunchArguments.Parse(request.Args);
        Dispatcher.BeginInvoke(() => ApplyLaunch(launch, first: false, request.ParentImage));
        return Task.FromResult(PipeResponse.Done());
    }

    /// <summary>
    /// Acts on a command line (first or second launch). Runs on the UI thread. <paramref name="launcherParent"/> is the program that
    /// started the AutomationPortal.exe or ap.exe that brought this command line, or null; a --run token only counts when it is Explorer.
    /// </summary>
    private void ApplyLaunch(LaunchCommand launch, bool first, string? launcherParent)
    {
        if (launch.Error is not null)
        {
            Notifications.Show(Strings.Get("AppHost_AutomationPortalCouldntUse"), launch.Error);
            return;
        }
        if (launch.ImportFile is { } file) _ = ImportFromFileAsync(file, launch.FromLink);
        else if (launch.RunTarget is { } target) _ = RunFromLaunchAsync(launch, target, launcherParent);
        else if (launch.PickFiles.Count > 0) _ = PickFromLaunchAsync(launch.PickFiles);
        else if (launch.NewShortcutFiles.Count > 0)
        {
            // Can't run anything, so it works whatever Settings › Links says.
            try { OpenNewShortcutForFiles(new NewShortcutRequest(launch.NewShortcutFiles, LaunchArguments.MissingNewShortcutFiles(launch.NewShortcutFiles), null)); }
            catch (Exception ex) { AppLog.Write("New shortcut from link failed: " + ex); }
        }
        else if (launch.Open) ShowMainWindow(Enum.TryParse<AppPage>(launch.OpenPage, ignoreCase: true, out var page) ? new NavTarget(page) : null);
        else if (!first && !launch.Background) ShowMainWindow();   // opening Automation Portal again brings the window forward
    }

    /// <summary>
    /// automationportal://pick: shows the items and a list of shortcuts, and runs the one the user chooses with the items as input.
    /// Always asks, whatever Settings › Links says: nothing runs without the user's click.
    /// </summary>
    private async Task PickFromLaunchAsync(IReadOnlyList<string> items)
    {
        try
        {
            var shortcut = await PickShortcutDialog.ShowAsync(null, items, Library.All);
            if (shortcut is null) return;
            if (await Runs.RunAsync(shortcut, ContentValue.Files(items), RunSource.Link) is null)
                Notifications.Show(shortcut.Name, Strings.Get("AppHost_ThisShortcutIsAlready"));
        }
        catch (Exception ex)
        {
            AppLog.Write("Pick from link failed: " + ex);
            Notifications.Show("Couldn't run that shortcut", ex.Message);
        }
    }

    /// <summary>How often links ran each shortcut lately (kept in memory only).</summary>
    private readonly LinkRunLimiter _linkRuns = new();

    private async Task RunFromLaunchAsync(LaunchCommand launch, string target, string? launcherParent)
    {
        try
        {
            var shortcut = Commands.Find(target);
            if (shortcut is null)
            {
                Notifications.Show("Couldn't find that shortcut", $"Automation Portal doesn't have a shortcut called \"{target}\". It may have been renamed or deleted.");
                return;
            }
            // Text that names a file or folder would be used as that file or folder by steps that take files, so a link carrying
            // one is always shown first, even when links are allowed.
            var pathInput = launch.FromLink && LaunchArguments.InputLooksLikePath(launch.Input);
            if (launch.FromLink)
            {
                var carriesPaths = pathInput || launch.Files.Count > 0;
                // A shortcut that clicks and types in other apps always asks first, even when it is trusted for links.
                var controlsApps = Logic.FileChangeCheck.ControlsApps(shortcut, Actions);
                // Trust given to other steps doesn't count: after an edit the link asks again.
                var stepsHash = LinkPolicy.StepsHash(shortcut);
                // Links that run the same shortcut again and again ask first, and say why.
                var tooOften = _linkRuns.Check(shortcut.Id);
                var effect = ShortcutEffects.Of(shortcut, Actions);
                var decision = LinkPolicy.Decide(Settings.Current, shortcut.Id, carriesPaths, controlsApps, stepsHash, rateLimited: tooOften is not null);
                // A link never runs a shortcut that makes or changes files without asking, even when links may run it.
                var mustAsk = decision == LinkDecision.Run && effect != ShortcutEffect.None;
                if (mustAsk) decision = LinkDecision.Ask;
                void LogLink(string outcome) => AppLog.Write($"Run request from a link: \"{shortcut.Name}\" effects {ShortcutEffects.WireName(effect)}, {launch.Files.Count} file(s){(string.IsNullOrEmpty(launch.Input) ? "" : ", text")}: {outcome}");
                switch (decision)
                {
                    case LinkDecision.Block:
                        LogLink("blocked");
                        Notifications.Show(Strings.Get("AppHost_LinksCantRunShortcuts"), $"A link asked to run \"{shortcut.Name}\". Links are turned off in Settings › Links, so it didn't run.");
                        return;
                    case LinkDecision.Ask:
                        var preview = Commands.DryRun(shortcut, launch.Input, launch.Files).Describe();
                        var (run, remember) = await Prompts.AskLinkAsync(
                            $"A link or another app wants to run your shortcut \"{shortcut.Name}\"{DescribeLinkInput(launch)}. Windows doesn't tell Automation Portal which app or web page sent this, so only continue if you trust where it came from." +
                            (tooOften is not null ? $"\n\n{tooOften} So this time you're asked first." : "") +
                            (pathInput ? "\n\nThe text looks like a file or folder on this PC. Steps that work on files would use it." : "") +
                            (controlsApps ? "\n\nThis shortcut clicks and types in other apps." : "") +
                            $"\n\n{ShortcutEffects.Badge(effect)}. {preview}",
                            effect == ShortcutEffect.None && LinkPolicy.CanRemember(Settings.Current, carriesPaths, controlsApps, shortcut.Id));
                        LogLink(run ? "allowed" : "declined");
                        if (!run)
                        {
                            if (tooOften is not null) Notifications.Show($"\"{shortcut.Name}\" didn't run", $"{tooOften} You chose not to run it again.");
                            return;
                        }
                        if (remember) LinkPolicy.SetTrusted(Settings, shortcut.Id, true, stepsHash);
                        break;
                    default:
                        LogLink("ran");
                        break;
                }
                _linkRuns.Ran(shortcut.Id);
            }

            // A command line (links were checked above) runs as before only with the token of an entry this app made.
            var gate = !launch.FromLink;
            var validToken = gate && TokenAccepted(shortcut.Id, launch, launcherParent);
            if (launch.Files.Count > 0)
            {
                QueueFiles(shortcut, launch.Files, launch.Input, launch.FromLink ? RunSource.Link : RunSource.SendTo, gate, validToken);
                return;
            }
            if (gate && await Commands.GateLaunchAsync(shortcut, launch.Input, Array.Empty<string>(), validToken, CancellationToken.None) is not null) return;
            var input = string.IsNullOrEmpty(launch.Input) ? null : ContentValue.Text(launch.Input);
            if (await Runs.RunAsync(shortcut, input, launch.FromLink ? RunSource.Link : RunSource.Cli) is null)
                Notifications.Show(shortcut.Name, Strings.Get("AppHost_ThisShortcutIsAlready"));
        }
        catch (Exception ex)
        {
            AppLog.Write("Run from launch failed: " + ex);
            Notifications.Show("Couldn't run that shortcut", $"\"{target}\" didn't start: {ex.Message}");
        }
    }

    /// <summary>What a link hands to the shortcut, word for word (shortened), for the run prompt.</summary>
    private static string DescribeLinkInput(LaunchCommand launch)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(launch.Input))
        {
            var text = launch.Input.Length > 400 ? launch.Input[..400] + "…" : launch.Input;
            parts.Add($" with this text:\n\n{text}\n");
        }
        if (launch.Files.Count > 0)
        {
            var shown = string.Join("\n", launch.Files.Take(8));
            if (launch.Files.Count > 8) shown += $"\n…and {launch.Files.Count - 8} more";
            parts.Add($"{(parts.Count > 0 ? "\nand" : "")} with {(launch.Files.Count == 1 ? "this file" : "these files")}:\n\n{shown}\n");
        }
        return string.Concat(parts);
    }

    /// <summary>Explorer starts one process per file for a right-click on several files; gather them into a single run.</summary>
    private void QueueFiles(Shortcut shortcut, IEnumerable<string> files, string? input, RunSource source, bool gate, bool validToken)
    {
        if (!_batches.TryGetValue(shortcut.Id, out var batch))
        {
            batch = new FileBatch { Input = input, Source = source, Gate = gate, AllTokens = validToken };
            var created = batch;
            created.Timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = BatchWindow };
            created.Timer.Tick += async (_, _) =>
            {
                created.Timer.Stop();
                _batches.Remove(shortcut.Id);
                try
                {
                    // Asked once for the whole batch, so a right-click on many files shows one sheet.
                    if (created.Gate && await Commands.GateLaunchAsync(shortcut, created.Input, created.Files, created.AllTokens, CancellationToken.None) is not null) return;
                    var content = ContentValue.Files(created.Files);
                    if (!string.IsNullOrEmpty(created.Input)) content = ContentValue.Concat(ContentValue.Text(created.Input), content);
                    await Runs.RunAsync(shortcut, content, created.Source);
                }
                catch (Exception ex) { AppLog.Write("Run from launch failed: " + ex); }
            };
            _batches[shortcut.Id] = batch;
        }
        batch.Gate |= gate;
        batch.AllTokens &= validToken;
        foreach (var f in files)
            if (!batch.Files.Contains(f, StringComparer.OrdinalIgnoreCase)) batch.Files.Add(f);
        batch.Timer.Stop();
        batch.Timer.Start();
    }

    internal async Task ImportFromFileAsync(string path, bool fromLink)
    {
        try
        {
            if (fromLink)
            {
                // Nothing is opened before the user agrees: even looking at a file can reach out to another computer.
                if (LaunchArguments.LinkImportProblem(path) is { } problem)
                {
                    await Prompts.MessageAsync(null, "Couldn't add the shortcut", problem);
                    return;
                }
                var open = await Prompts.ConfirmAsync(null, Strings.Get("AppHost_OpenThisShortcutFile"),
                    $"A link or another app wants to add a shortcut from this file:\n\n{path}\n\nAutomation Portal will open it and show you what the shortcut does before anything is added. Only continue if you trust where it came from.", "Open it");
                if (!open) return;
            }

            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Prompts.MessageAsync(null, "Couldn't open the file", $"Automation Portal couldn't read \"{path}\". Check the file still exists.");
                return;
            }

            var result = await ReviewImportAsync(text, fromLink ? Strings.Get("AppHost_ThisWasOpenedFrom") : null);
            if (result is null) return;
            await FinishImportAsync(result, null, warningsShown: true);
            ShowImported(result);
        }
        catch (LibraryException ex) { await Prompts.MessageAsync(null, "Couldn't add the shortcut", ex.Message); }
        catch (Exception ex) { AppLog.Write("Import from file failed: " + ex); }
    }

    /// <summary>
    /// An import that arrived over the pipe. Always shown for review, also when the request says it came from ap.exe: any program
    /// running as this user can claim that. Returns once the shortcut is added (or the user said no); the extras question follows.
    /// </summary>
    private Task<ImportResult?> ReviewPipeImportAsync(string text, PipeRequest request) =>
        Dispatcher.InvokeAsync(async () =>
        {
            var origin = string.Equals(request.Source, "cli", StringComparison.OrdinalIgnoreCase)
                ? Strings.Get("AppHost_ThisWasSentWith")
                : Strings.Get("AppHost_AnotherProgramOnThis");
            ImportResult? result;
            try { result = await ReviewImportAsync(text, origin); }
            catch (LibraryException ex) { await Prompts.MessageAsync(null, "Couldn't add the shortcut", ex.Message); throw; }
            if (result is null) return null;
            _ = FinishAndShowAsync(result);
            return result;
        }).Task.Unwrap();

    /// <summary>
    /// automationportal://new, ap new and the pipe kind "new": opens the editor on a new, unsaved shortcut with the files as what Run
    /// tries it on, and brings it forward. Runs nothing and never opens or changes the files. Runs on the UI thread.
    /// </summary>
    private void OpenNewShortcutForFiles(NewShortcutRequest request)
    {
        var options = new AutomationPortal.Editor.EditorOpenOptions(SampleFiles: request.Files, SampleMissing: request.Missing,
            SampleFrom: request.CallerId is { } caller ? Callers.FriendlyName(caller) : null);
        if (EditorLauncher is Services.EditorLauncher real) { if (real.OpenNewWindow(null, options) is { } editor) Infrastructure.WindowPlacement.BringToFront(editor); }
        else EditorLauncher.OpenNew(null, options);
    }

    /// <summary>Another app (ap run --from, or a pipe request with ask) wants to run a shortcut. CallerConsent shows one of these at a time.</summary>
    private Task<CallerDecision> AskCallerAsync(CallerPrompt prompt, CancellationToken ct) =>
        Dispatcher.InvokeAsync(() => Prompts.AskCallerAsync(prompt, CallerConsent.DefaultTimeout, ct,
            () => ShowMainWindow(new NavTarget(AppPage.Settings)))).Task.Unwrap();

    private async Task FinishAndShowAsync(ImportResult result)
    {
        try
        {
            await FinishImportAsync(result, null, warningsShown: true);
            ShowImported(result);
        }
        catch (Exception ex) { AppLog.Write("Finishing an import failed: " + ex); }
    }

    /// <summary>Shows what the shortcut does and adds it only if the user says so. Null when it wasn't added. Runs on the UI thread.</summary>
    private async Task<ImportResult?> ReviewImportAsync(string text, string? originNote)
    {
        Shortcut preview;
        try { preview = ShortcutPackage.Parse(text); }
        catch (LibraryException ex) { await Prompts.MessageAsync(null, "Couldn't add the shortcut", ex.Message); return null; }
        if (!AllowNewShortcut(null)) return null;   // at the plan's limit: nothing is added, the upgrade window says why

        var warnings = ShortcutPackage.Inspect(preview, Actions).Select(w => w.Describe()).Distinct().ToList();
        var steps = preview.Actions.Count(a => a.Flow is null or { Role: FlowRole.Start });
        var message = $"\"{preview.Name}\" has {steps} step{(steps == 1 ? "" : "s")}. Add it to your shortcuts?";
        if (warnings.Count > 0) message += $"\n\nHeads up: it includes {string.Join(", ", warnings)}.";
        if (originNote is not null) message += "\n\n" + originNote;
        if (!await Prompts.ConfirmAsync(null, Strings.Get("AppHost_AddThisShortcut"), message, "Add")) return null;

        return Library.ImportJson(text);   // exactly what was shown, even if the file changed since; never replaces a shortcut
    }

    private void ShowImported(ImportResult result)
    {
        Notifications.Show(Strings.Get("AppHost_ShortcutAdded"), result.RenamedFrom is null
            ? $"\"{result.Shortcut.Name}\" is now in your shortcuts."
            : $"\"{result.Shortcut.Name}\" is now in your shortcuts. You already had one called \"{result.RenamedFrom}\", so this one got a new name.");
        ShowMainWindow(new NavTarget(AppPage.Library, Query: result.Shortcut.Name));
    }

    /// <summary>
    /// After an import (file, link or gallery): imported shortcuts never arrive with a hotkey, alias or Send To entry
    /// switched on. Asks about risky steps (unless the caller already showed them), then offers to turn those extras on.
    /// </summary>
    public async Task FinishImportAsync(ImportResult result, System.Windows.Window? owner, bool warningsShown)
    {
        if (!warningsShown && result.NeedsConfirmation)
        {
            var what = string.Join(", ", result.Warnings.Select(w => w.Describe()).Distinct());
            var keep = await Prompts.ConfirmAsync(owner, Strings.Get("AppHost_KeepThisShortcut"), $"\"{result.Shortcut.Name}\" includes {what}. Only keep it if you trust where it came from.", Strings.Get("AppHost_KeepIt"), cancelText: Strings.Get("AppHost_RemoveIt"));
            if (!keep) { Library.Delete(result.Shortcut.Id); return; }
        }

        var extras = new List<string>();
        if (result.PendingHotkey is { Length: > 0 } hk) extras.Add($"the hotkey {hk}");
        if (result.PendingAlias is { Length: > 0 } al) extras.Add($"the Quick Run name \"{al}\"");
        if (result.PendingShowInSendTo) extras.Add("an entry in the Send to and right-click menus");
        if (extras.Count == 0) return;

        var on = await Prompts.ConfirmAsync(owner, "Turn on the extras?", $"\"{result.Shortcut.Name}\" came with {string.Join(", ", extras)}. These are off for now. Turn them on?", "Turn on", cancelText: "Leave off");
        if (!on || Library.GetById(result.Shortcut.Id)?.Clone() is not { } s) return;

        var notes = new List<string>();
        if (result.PendingHotkey is { Length: > 0 } key)
        {
            if (HotkeyProblemFor(key, s.Id) is { } problem) notes.Add(problem); else s.Hotkey = key;
        }
        if (result.PendingAlias is { Length: > 0 } alias)
        {
            if (Library.All.Any(x => x.Id != s.Id && string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase))) notes.Add($"The Quick Run name \"{alias}\" is already used by another shortcut.");
            else s.Alias = alias;
        }
        if (result.PendingShowInSendTo) s.ShowInSendTo = true;
        try { Library.Save(s); }
        catch (LibraryException ex) { notes.Add(ex.Message); }
        if (notes.Count > 0) await Prompts.MessageAsync(owner, "Some extras stayed off", string.Join("\n", notes));
    }

    /// <summary>Starts a fresh copy of the app once this one has closed (used after the library location changes).</summary>
    public void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            var extra = _args.Where(a => a != "--unregister").Select(a => " \"" + a.Replace("\"", "") + "\"");
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & \"{exe}\"{string.Concat(extra)}") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { AppLog.Write("Restart: " + ex.Message); }
        }
        Exit();
    }

    /// <summary>Removes everything the app registered. Used by <c>AutomationPortal.exe --unregister</c> (uninstall) and works without starting the full app.</summary>
    public static void UnregisterShell(string? dataRoot, string? sandbox = null)
    {
        var paths = string.IsNullOrWhiteSpace(dataRoot) ? AutomationPortalPaths.Default : AutomationPortalPaths.ForRoot(dataRoot);
        new WindowsShellIntegration(ShellOptionsFor(paths, !string.IsNullOrWhiteSpace(dataRoot), sandbox), () => Array.Empty<Shortcut>()).UnregisterAll();
    }
}
