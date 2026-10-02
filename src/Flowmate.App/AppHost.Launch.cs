using System.Windows.Threading;
using Flowmate.App.Dialogs;
using Flowmate.App.Integration;
using Flowmate.App.Pages;
using Flowmate.Core;

namespace Flowmate.App;

// Command line, links, Send To, right-click menu, the pipe (second launches and the fm CLI) and shell sync.
public sealed partial class AppHost
{
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>The CLI's list / run / import / export, answered for fm.exe over the pipe.</summary>
    public ShortcutCommands Commands { get; private set; } = null!;

    private sealed class FileBatch
    {
        public readonly List<string> Files = new();
        public string? Input;
        public RunSource Source;
        public DispatcherTimer Timer = null!;
    }

    private readonly Dictionary<Guid, FileBatch> _batches = new();
    private Timer? _syncTimer;
    private LaunchCommand _firstLaunch = new();

    private void BuildIntegration(FlowmatePaths basePaths, bool customDataRoot, string[] args)
    {
        _firstLaunch = LaunchArguments.Parse(args);
        StartInBackground |= _firstLaunch.RunTarget is not null || _firstLaunch.ImportFile is not null;

        Commands = new ShortcutCommands(Library, (s, input, source, ct) => Runs.RunAsync(s, input, source, ct));

        var options = ShellOptionsFor(basePaths, customDataRoot);
        var shell = new WindowsShellIntegration(options, () => Library.All)
        {
            Notify = (title, body) => Notifications.Show(title, body),
            JumpListSink = entries => Dispatcher.BeginInvoke(() => JumpListApplier.Apply(entries, options.ExePath)),
        };
        Shell = shell;
        Library.Changed += (_, e) => { if ((e.What & LibraryChange.Shortcuts) != 0) ScheduleShellSync(TimeSpan.FromSeconds(1.5)); };
    }

    /// <summary>
    /// The real shell locations, or none for a throwaway data root (tests and demos must not register anything on the PC).
    /// FLOWMATE_SHELL_SANDBOX=&lt;folder&gt; exercises the whole thing against that folder and a throwaway HKCU key instead.
    /// </summary>
    private static ShellOptions ShellOptionsFor(FlowmatePaths basePaths, bool customDataRoot)
    {
        var sandbox = Environment.GetEnvironmentVariable("FLOWMATE_SHELL_SANDBOX");
        if (!string.IsNullOrWhiteSpace(sandbox))
            return ShellOptions.Sandbox(sandbox, @"Software\FlowmateSandbox-" + InstanceNames.For(sandbox)[^8..], Environment.ProcessPath ?? "Flowmate.exe", PackageIdentity.IsPackaged);
        return customDataRoot ? ShellOptions.Disabled(basePaths) : ShellOptions.ForCurrentUser(basePaths);
    }

    /// <summary>Runs after the window is up so it never slows start-up: registers links, then syncs Send To, the right-click menu and the Jump List.</summary>
    private void StartIntegration()
    {
        Task.Run(() =>
        {
            try { Shell.EnsureRegistered(); }
            catch (Exception ex) { AppLog.Write("Shell registration: " + ex.Message); }
        });
        ScheduleShellSync(TimeSpan.FromSeconds(2));
        ApplyLaunch(_firstLaunch, first: true);
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

    /// <summary>Everything that arrives over the pipe: a second launch's command line, or an fm.exe command.</summary>
    public Task<PipeResponse> HandlePipeAsync(PipeRequest request, CancellationToken ct)
    {
        if (request.Kind != "launch") return Commands.ExecuteAsync(request, ct);
        var launch = LaunchArguments.Parse(request.Args);
        Dispatcher.BeginInvoke(() => ApplyLaunch(launch, first: false));
        return Task.FromResult(PipeResponse.Done());
    }

    /// <summary>Acts on a command line (first or second launch). Runs on the UI thread.</summary>
    private void ApplyLaunch(LaunchCommand launch, bool first)
    {
        if (launch.Error is not null)
        {
            Notifications.Show("Flow Mate couldn't use that", launch.Error);
            return;
        }
        if (launch.ImportFile is { } file) _ = ImportFromFileAsync(file, launch.FromLink);
        else if (launch.RunTarget is { } target) _ = RunFromLaunchAsync(launch, target);
        else if (!first && !launch.Background) ShowMainWindow();   // opening Flow Mate again brings the window forward
    }

    private async Task RunFromLaunchAsync(LaunchCommand launch, string target)
    {
        try
        {
            var shortcut = Commands.Find(target);
            if (shortcut is null)
            {
                Notifications.Show("Couldn't find that shortcut", $"Flow Mate doesn't have a shortcut called \"{target}\". It may have been renamed or deleted.");
                return;
            }
            if (launch.FromLink && !Settings.Current.AllowFlowmateLinks)
            {
                var withInput = string.IsNullOrEmpty(launch.Input) ? "" : " with some text";
                var ok = await Prompts.ConfirmAsync(null, "Run this shortcut?",
                    $"A link or another app wants to run your shortcut \"{shortcut.Name}\"{withInput}. Only continue if you trust where it came from.\n\nTo stop being asked, turn on \"Allow flowmate:// links\" in Settings.", "Run");
                if (!ok) return;
            }

            if (launch.Files.Count > 0)
            {
                QueueFiles(shortcut, launch.Files, launch.Input, launch.FromLink ? RunSource.Link : RunSource.SendTo);
                return;
            }
            var input = string.IsNullOrEmpty(launch.Input) ? null : ContentValue.Text(launch.Input);
            if (await Runs.RunAsync(shortcut, input, launch.FromLink ? RunSource.Link : RunSource.Cli) is null)
                Notifications.Show(shortcut.Name, "This shortcut is already running.");
        }
        catch (Exception ex) { AppLog.Write("Run from launch failed: " + ex); }
    }

    /// <summary>Explorer starts one process per file for a right-click on several files; gather them into a single run.</summary>
    private void QueueFiles(Shortcut shortcut, IEnumerable<string> files, string? input, RunSource source)
    {
        if (!_batches.TryGetValue(shortcut.Id, out var batch))
        {
            batch = new FileBatch { Input = input, Source = source };
            var created = batch;
            created.Timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = BatchWindow };
            created.Timer.Tick += (_, _) =>
            {
                created.Timer.Stop();
                _batches.Remove(shortcut.Id);
                var content = ContentValue.Files(created.Files);
                if (!string.IsNullOrEmpty(created.Input)) content = ContentValue.Concat(ContentValue.Text(created.Input), content);
                _ = Runs.RunAsync(shortcut, content, created.Source);
            };
            _batches[shortcut.Id] = batch;
        }
        foreach (var f in files)
            if (!batch.Files.Contains(f, StringComparer.OrdinalIgnoreCase)) batch.Files.Add(f);
        batch.Timer.Stop();
        batch.Timer.Start();
    }

    private async Task ImportFromFileAsync(string path, bool fromLink)
    {
        try
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Prompts.MessageAsync(null, "Couldn't open the file", $"Flow Mate couldn't read \"{path}\". Check the file still exists.");
                return;
            }

            Shortcut preview;
            try { preview = ShortcutPackage.Parse(text); }
            catch (LibraryException ex) { await Prompts.MessageAsync(null, "Couldn't add the shortcut", ex.Message); return; }

            var warnings = ShortcutPackage.Inspect(preview, Actions).Select(w => w.Title).Distinct().ToList();
            var steps = preview.Actions.Count(a => a.Flow is null or { Role: FlowRole.Start });
            var message = $"\"{preview.Name}\" has {steps} step{(steps == 1 ? "" : "s")}. Add it to your shortcuts?";
            if (warnings.Count > 0) message += $"\n\nHeads up: it includes {string.Join(", ", warnings)}.";
            if (fromLink) message += "\n\nThis was opened from a link, so only add it if you trust where it came from.";
            if (!await Prompts.ConfirmAsync(null, "Add this shortcut?", message, "Add")) return;

            var result = ImportFile(path);
            Notifications.Show("Shortcut added", result.RenamedFrom is null
                ? $"\"{result.Shortcut.Name}\" is now in your shortcuts."
                : $"\"{result.Shortcut.Name}\" is now in your shortcuts. You already had one called \"{result.RenamedFrom}\", so this one got a new name.");
            ShowMainWindow(new NavTarget(AppPage.Library, Query: result.Shortcut.Name));
        }
        catch (LibraryException ex) { await Prompts.MessageAsync(null, "Couldn't add the shortcut", ex.Message); }
        catch (Exception ex) { AppLog.Write("Import from file failed: " + ex); }
    }

    /// <summary>Removes everything the app registered. Used by <c>Flowmate.exe --unregister</c> (uninstall) and works without starting the full app.</summary>
    public static void UnregisterShell(string? dataRoot)
    {
        var paths = string.IsNullOrWhiteSpace(dataRoot) ? FlowmatePaths.Default : FlowmatePaths.ForRoot(dataRoot);
        new WindowsShellIntegration(ShellOptionsFor(paths, !string.IsNullOrWhiteSpace(dataRoot)), () => Array.Empty<Shortcut>()).UnregisterAll();
    }
}
