# Hosting the Flowmate engine in another app

Another app (for example a WinUI 3 file manager) can load `.flowmate` shortcuts and run them with Flowmate's actions,
without the Flowmate app and without WPF.

## 1. What to reference

| Project | Target | Use |
|---|---|---|
| `Flowmate.Core` | `net10.0` | Model, engine, built-in actions (variables, if/repeat/menu, text, dialogs), `.flowmate` files. Fine for plain `net10.0` glue code. |
| `Flowmate.Actions.Content` | `net10.0-windows10.0.22621.0`, min 10.0.19041 | Text, numbers, dates, lists, web, spreadsheets, QR. |
| `Flowmate.Actions.Documents` | same | Files and folders, pictures, PDF, mail, calendar, AI, share. |
| `Flowmate.Actions.Windows` | same | Apps and windows, keyboard and mouse, clipboard, screen, media, device settings. |
| `Flowmate.Triggers` (optional) | same | Automations (triggers and conditions). |

These libraries never reference WPF or Windows Forms, directly or through packages (`tests/Flowmate.Hosting.Tests` checks
this). An app targeting `net10.0-windows10.0.26100.0` with a minimum of 10.0.19041 can reference them.
Don't reference `Flowmate.Host.Wpf`, `Flowmate.Editor`, `Flowmate.App` or `Flowmate.Cli`: they are WPF.

## 2. Build an engine

```csharp
using Flowmate.Core;

var engine = FlowmateEngine.CreateBuilder()
    .UsePaths(FlowmatePaths.ForRoot(Path.Combine(myAppData, "Flows")))   // temp folders, saved values, history
    .AddActions(typeof(Flowmate.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
    .AddActions(typeof(Flowmate.Actions.Documents.AiService).Assembly)
    .AddActions(typeof(Flowmate.Actions.Windows.Apps.InstalledApps).Assembly)
    .AddActions(typeof(MyPortalActions.AddTagAction).Assembly)            // the host's own actions
    .ConfigureServices(s =>
    {
        s.UiThread = new MyUiThread(dispatcherQueue);
        s.UserInteraction = new MyDialogs();
        s.Clipboard = new MyClipboard();
        s.Notifications = new MyToasts();
    })
    .Build();
```

`engine.Actions` is the `ActionRegistry` (search, categories, descriptors for a picker UI), `engine.Runner` the
`ShortcutRunner`, `engine.Services` the `FlowmateServices`. Core's built-in actions are always included.
`engine.Actions.Diagnostics` lists duplicate ids or broken descriptors found while registering.

## 3. Services

Set the ones your app can provide. Anything left unset makes the actions that need it fail with
"This app can't … here. Try running the shortcut from the Flowmate app." The rest of the shortcut is unaffected.

| Service | Needed by | Notes |
|---|---|---|
| `UiThread` (`IUiThread`) | share sheet, anything that must run on the UI thread | Must be a thread with a message loop (WinUI `DispatcherQueue` is fine). |
| `UserInteraction` (`IUserInteraction`) | Ask, Show Alert, Choose from List/Menu, Show Result, file pickers, screen region | Cancel → throw `UserCancelledException`. |
| `Clipboard` (`IClipboardService`) | clip.* actions, Clipboard variable, Get Selected Text | |
| `Notifications` (`INotificationService`) | Show Notification, interactive notifications | |
| `Reminders`, `Secrets`, `Settings`, `KeyValues`, `History`, `Library` | reminders, AI keys, settings, Save/Load Value, history, Run Shortcut by name | Core has file-based stores: `ReminderStore`, `DpapiSecretStore`, `JsonSettingsStore`, `JsonKeyValueStore`, `JsonlRunHistory`, `JsonShortcutLibrary`. |
| `Http` | web actions | Defaults to a shared `HttpClient`. |

Two process-wide hooks are set without `FlowmateServices` because the code that uses them has no context at hand:

- `ImageEngine.Backend` (`IImageBackend`, in Actions.Documents): resize, convert, crop, rotate, add text, combine, photo
  details, PDF from pictures, pictures for AI. Without it those actions say "This app can't edit pictures here".
  Flowmate's own implementation (`WpfImageBackend`) uses WPF imaging, so a non-WPF host writes its own (e.g. on
  `Windows.Graphics.Imaging`) or leaves pictures out.
- `TriggerClipboard.Backend` (`ITriggerClipboard`, in Triggers): the "something is copied" trigger and pasting long text
  expansions. Without it the copy trigger stays quiet and expansions are typed.

## 4. Run a shortcut

Keep shortcuts in your own folder as `.flowmate` files:

```csharp
var flow = engine.LoadShortcut(Path.Combine(flowsFolder, "Tag selection.flowmate"));   // or LoadShortcut(stream)
engine.SaveShortcut(flow, path);                                                       // or SaveShortcut(flow, stream)
var problems = engine.Validate(flow);                                                  // unknown actions, missing values
```

The selected files are the shortcut input; the app's state goes in as named variables:

```csharp
var result = await engine.RunAsync(flow,
    ContentValue.Files(selectedPaths),
    new RunOptions
    {
        SeedVariables = new()
        {
            ["CurrentFolder"] = ContentValue.Files(new[] { currentFolder }),
            ["ActivePaneId"] = ContentValue.Text(activePaneId),
            ["ActiveTabId"] = ContentValue.Text(activeTabId),
            ["SelectedCount"] = ContentValue.Number(selectedPaths.Count),
        },
        Observer = progress,                // optional, see below
        CancellationToken = cancel.Token,
        RecordHistory = false,              // true writes to Services.History when one is set
    });

if (result.Status == RunStatus.Failed) ShowError(result.ErrorMessage);
else Show(result.Output);
```

In a shortcut the variables are used like any named variable (`ParamValue.Named("CurrentFolder")`, or
`VarRef.Named("SelectedCount")` inside text). Host actions can also read them with `ctx.GetVariable("ActivePaneId")`.

`RunResult` has `Status` (`Succeeded`, `Failed`, `Cancelled`, `Stopped`), `Output`, `ErrorMessage`, `FailedStepUid`,
`Duration` and `Steps` (per-step log).

## 5. Progress

Pass an `IRunObserver` in `RunOptions.Observer`:

```csharp
sealed class Progress : IRunObserver
{
    public void OnStepStarted(string uid) { }
    public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error) { }
    public void OnVariableSet(string name, ContentValue value) { }
    public void OnLog(string message) { }
    public void OnProgress(string uid, string message) { }   // "Renamed photo.jpg"
}
```

Callbacks arrive on worker threads; marshal to the UI thread before touching controls. The `uid` is the step's
`ActionInstance.Uid` in `flow.Actions`.

## 6. Host actions

Write a public, non-abstract `ShortcutAction` subclass with a public parameterless constructor and add its assembly with
`AddActions`. Use your own id prefix (for example `portal.addTag`) so ids never clash with Flowmate's; duplicates are
ignored and reported in `engine.Actions.Diagnostics`. See `docs/CORE-API.md` §3 for the action API.
