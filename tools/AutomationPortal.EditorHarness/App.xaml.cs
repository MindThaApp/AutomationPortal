using System.IO;
using System.Windows;
using System.Windows.Threading;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Editor;
using Wpf.Ui.Appearance;

namespace AutomationPortal.EditorHarness;

/// <summary>
/// Opens the shortcut editor standalone. Options: --dark, --stress (300 actions), --new (new shortcut flow with "Describe it"),
/// --selftest (drives the editor and exits), --shots &lt;folder&gt; (self test saves screenshots), --log &lt;file&gt;.
/// Uses a throwaway library under %TEMP%, never the real %APPDATA%\Automation Portal.
/// </summary>
public partial class App : Application
{
    public static string LogPath { get; private set; } = Path.Combine(Path.GetTempPath(), "automationportal-editor-harness.log");
    public static bool Failed { get; set; }

    public static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--log") is { } lp) LogPath = lp;
        try { File.WriteAllText(LogPath, ""); } catch { }
        Log("Harness starting");

        DispatcherUnhandledException += (_, ex) => { Failed = true; Log("UNHANDLED UI EXCEPTION: " + ex.Exception); ex.Handled = args.Contains("--selftest"); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Failed = true; Log("UNHANDLED EXCEPTION: " + ex.ExceptionObject); };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log("Unobserved task exception: " + ex.Exception); ex.SetObserved(); };

        if (args.Contains("--dark")) ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        var quiet = args.Contains("--selftest");
        HarnessUi.Quiet = quiet;

        var root = Path.Combine(Path.GetTempPath(), "automationportal-editor-harness", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Log("Throwaway library: " + root);
        var paths = AutomationPortalPaths.ForRoot(root);
        var registry = ActionRegistry.CreateWithBuiltIns();
        registry.RegisterAssembly(typeof(App).Assembly);
        foreach (var d in registry.Diagnostics) Log("Registry: " + d);
        var library = new JsonShortcutLibrary(paths, registry);
        var settings = new JsonSettingsStore(paths);
        settings.Update(s => { s.FavoriteActionIds.AddRange(new[] { "ui.ask", "content.text" }); s.RecentActionIds.Add("ui.showResult"); });
        var services = new AutomationPortalServices
        {
            UserInteraction = new HarnessUi(), Notifications = new FakeNotificationService(), Reminders = new FakeReminderService(),
            Library = library, Secrets = new InMemorySecretStore(), Settings = settings, UiThread = new InlineUiThread(),
            KeyValues = new InMemoryKeyValueStore(), History = new InMemoryRunHistory(), Clipboard = new FakeClipboard(), Paths = paths,
        };
        _ = new ShortcutRunner(registry, services);
        var host = new HarnessHost(args.Contains("--new") || args.Contains("--selftest"));
        var context = new EditorContext(services, registry, host);

        var showcase = SampleShortcuts.Showcase();
        library.Save(showcase);
        library.Save(SampleShortcuts.Small());
        var stress = SampleShortcuts.Stress();
        library.Save(stress);
        foreach (var s in registry.All) { }

        Window window;
        if (args.Contains("--new")) window = ShortcutEditor.Open(new Shortcut { Name = "New Shortcut", Icon = "Flash24", Color = "teal" }, context);
        else if (args.Contains("--stress")) window = ShortcutEditor.Open(stress.Id, context)!;
        else window = ShortcutEditor.Open(showcase.Id, context)!;
        Log("Editor window opened");

        if (args.Contains("--selftest"))
        {
            var shots = Arg("--shots");
            window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await SelfTest.RunAsync(window, context, library, showcase, stress, shots); }
                catch (Exception ex) { Failed = true; Log("SELFTEST EXCEPTION: " + ex); }
                Log(Failed ? "SELFTEST FAILED" : "SELFTEST OK");
                Shutdown(Failed ? 1 : 0);
            }), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); Log("Still running after 3 s"); };
            timer.Start();
        }
    }
}

internal sealed class HarnessHost : IEditorHost
{
    private readonly bool _ai;
    public HarnessHost(bool ai) { _ai = ai; }

    public IReadOnlyList<AutomationSummary> GetAutomationsFor(Guid shortcutId) => new[]
    {
        new AutomationSummary(Guid.NewGuid(), "Every day at 07:30", true),
        new AutomationSummary(Guid.NewGuid(), "When Spotify opens", false),
    };
    public void AddTrigger(Guid shortcutId) => App.Log("Host: AddTrigger " + shortcutId);
    public void EditAutomation(Guid id) => App.Log("Host: EditAutomation " + id);
    public void AddToDesktop(Shortcut shortcut) => App.Log("Host: AddToDesktop " + shortcut.Name);
    public void AddToStartMenu(Shortcut shortcut) => App.Log("Host: AddToStartMenu " + shortcut.Name);
    public void Export(Shortcut shortcut) => App.Log("Host: Export " + shortcut.Name);
    public bool CanDescribeWithAi => _ai;
    public void OpenAiSettings() => App.Log("Host: OpenAiSettings");
    public bool CanRecord => true;
    public SuggestedAutomation? LastSuggestion { get; private set; }

    public async Task<AiDraftResult> DescribeWithAiAsync(string description, Shortcut? current, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        if (description.Contains("garbage")) return AiDraftResult.Failed("The AI's draft wasn't usable, even after a second try. Try describing it with a bit more detail, or build it step by step.");
        if (current is not null)
        {
            var revised = current.Clone();
            revised.Actions.Add(ActionInstance.Create("ui.showResult").WithUid("ai-show"));
            revised.Actions[0].OutputName = "Changed by AI";
            return new AiDraftResult { Shortcut = revised, Summary = "Shows the result at the end and renames the first output." };
        }
        var s = new Shortcut { Name = "Morning briefing", Icon = "WeatherSunny24", Color = "orange" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", description)).WithUid("ai1"));
        s.Actions.Add(ActionInstance.Create("sample.danger").WithUid("ai2"));
        s.Actions.Add(ActionInstance.Create("ui.showResult").WithUid("ai3"));
        return new AiDraftResult
        {
            Shortcut = s, Summary = "Writes your sentence, runs the sample danger step, then shows the result.",
            Automations = new[] { new SuggestedAutomation("time.daily", "Every day at 8:00", new Dictionary<string, ParamValue> { ["time"] = ParamValue.Time("08:00") }) },
        };
    }

    public void AddSuggestedAutomation(Guid shortcutId, SuggestedAutomation suggestion)
    {
        LastSuggestion = suggestion;
        App.Log("Host: AddSuggestedAutomation " + suggestion.TriggerId + " for " + shortcutId);
    }

    public async Task<IReadOnlyList<ActionInstance>?> RecordMacroAsync(Window editor)
    {
        App.Log("Host: RecordMacro start (editor minimised: " + (editor.WindowState == WindowState.Minimized) + ")");
        await Task.Delay(400);
        return new[]
        {
            ActionInstance.Create("content.text", ("text", "recorded 1")).WithUid("rec1"),
            ActionInstance.Create("content.text", ("text", "recorded 2")).WithUid("rec2"),
        };
    }
    public void ShowRunDetails(string runId) => App.Log("Host: ShowRunDetails " + runId);
}
