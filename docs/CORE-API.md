# Flowmate.Core API guide

For the library owners (B1/B2/B3/C/D/E). Everything below lives in `Flowmate.Core` (namespace `Flowmate.Core`; fakes in `Flowmate.Core.Testing`;
built-in actions in `Flowmate.Core.Actions`). Don't edit Core: write requests to `docs/core-requests/<area>.md`.

## 0. Rules (SPEC §0)
- Build only your project: `dotnet build <your.csproj> -p:BuildProjectReferences=false` (Core is prebuilt). Run your own tests.
- Commit only your paths: `git add <paths>` then `git commit -m "<area>: <msg>" -- <paths>`; if `.git/index.lock` exists wait 5 s and retry.
- Plain-English user text ("Find pattern", not "regex"). Errors say what happened and what to do.
- Actions/triggers/conditions are found by assembly scanning: **public, non-abstract, public parameterless ctor**. No registration list. Keep `Descriptor` in a `static readonly` field. Ids are unique across the solution (`text.combine`, `time.daily` ...); duplicates are ignored and reported in `registry.Diagnostics`.

## 1. Content
`ContentValue` = immutable list of `ContentItem` (single value = list of 1; nothing = `ContentValue.Empty`).

```csharp
ContentValue.Text("hi"); .Number(5); .Bool(true); .Date(dto); .Url("https://x.org"); .File(path); .Folder(path);
ContentValue.FileOrFolder(path); .Files(paths); .Image(path, w, h); .Images(paths);
ContentValue.Dict(("name", ContentValue.Text("Sam")), ("age", ContentValue.Number(3)));
ContentValue.Concat(a, b); ContentValue.FromItems(items); ContentValue.Of(anyDotNetValue);
ContentValue.FromJson(text) / v.ToJson(indented)        // JSON object -> Dictionary, array -> list
v.AsText(sep="\n") AsTexts() AsNumber() AsNumbers() AsBool() AsDate() AsDates() AsUrls() AsDictionary()
await v.AsFilesAsync(ctx, includeFolders: false)        // text -> temp .txt, URL -> download
await v.AsImagesAsync(ctx)                              // non-images throw a friendly ActionException
v.Property("Word Count")  v.Pick(VarPick.First)  v.CoerceTo(ContentKind.Number)
```
Items: `TextItem NumberItem BooleanItem DateItem UrlItem FileItem FolderItem ImageItem(Path,Width?,Height?) DictionaryItem LocationItem AppItem WindowItem`.
`PathItem` is the base of File/Folder/Image. `DictionaryItem`: `Get(key)`, `GetPath("a.b.0")`, `With(key,value)`, `Without`, `Keys`, `Entries`.
`item.PropertyNames` / `item.GetProperty(name)` follow SPEC §3.5. `FileKinds.Of(path)` -> "Image", "PDF", "Document", ... (use it for `{kind}` folders).
`ContentFormat.Size(bytes)` ("1.2 MB"), `.Number(d)`, `.Date(d)`. Images are always files: write generated images into `ctx.TempFolder` (`ctx.NewTempPath("name.png")`).

## 2. ParamValue / VarRef factories (shortcut data)
```csharp
ParamValue.Text("Hi ", VarRef.Output(uid), "!")        // template with inline variables
ParamValue.Number(5)  .Bool(true)  .Choice("spaces")  .Date(dto)  .Time("07:30")  .Duration(90)  .Hotkey("Ctrl+K")  .File(path)  .App(id, name)
ParamValue.Output(uid)  .Named("Total")  .ShortcutInput()  .Var(VarRef.Clipboard)      // whole value is a variable
ParamValue.List("a", "b")  .List(p1, p2)  .Dict(("k", ParamValue.Text("v")))  .Ask()
ParamValue.AllOf(ConditionRow.Of(left, ConditionOperators.Contains, "x"))  .AnyOf(...)
// implicit: string, int, double, bool, VarRef -> ParamValue
VarRef.Output(uid) .Named(name) .ShortcutInput .Clipboard .CurrentDate .RepeatItem .RepeatIndex .MenuResult .Device
VarRef.Output(uid).WithProperty("Name").As(ContentKind.Number).Picking(VarPick.First)
ActionInstance.Create("text.combine", ("text", ...)).WithUid("a1").OnErrorRetry(2, 1)
var b = new ShortcutBuilder("Name"); var u = b.Add("id", (k, v)); b.If(...) .Menu(...) .Repeat(...) .RepeatEach(...); b.Build();
```
JSON shapes are exactly SPEC §3.3 (`FlowmateJson.Options`/`Indented`; `FlowmateJson.Clone(x)`).

## 3. Writing an action
```csharp
public sealed class RenameFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.rename", Title = "Rename Files", Category = ActionCategories.FilesAndFolders, Icon = "Rename24",
        Keywords = new[] { "name", "batch" },
        Summary = "Rename {files} using {mode}",             // tokens render inline; others go under "Show More"
        Description = "Gives files new names by replacing text or adding a prefix.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Renamed Files",
        IsDestructive = false,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },   // empty -> previous output
            ParameterDescriptor.ChoiceOf("mode", "Rename by", "replace",
                new Choice("replace", "Replacing text"), new Choice("prefix", "Adding a prefix")),
            ParameterDescriptor.Text("find", "Find") with { VisibleWhen = new VisibleWhen("mode", "replace") },
            ParameterDescriptor.Text("with", "Replace with") with { VisibleWhen = new VisibleWhen("mode", "replace") },
            ParameterDescriptor.Text("prefix", "Prefix") with { VisibleWhen = new VisibleWhen("mode", "prefix") },
            ParameterDescriptor.DynamicChoice("style", "Numbering", async (services, ct) =>
                new Choice[] { "none", "1, 2, 3" }),                                   // ChoicesProvider(FlowmateServices, CancellationToken)
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await ctx.GetFilesAsync("files");
        var mode = await ctx.GetChoiceAsync("mode", "replace");
        if (files.Count == 0) throw new ActionException("There are no files to rename. Choose some files first.");
        var result = new List<string>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!f.Exists) throw new ActionException($"Couldn't find the file '{f.FullName}'. Check it still exists.");
            var newName = mode == "prefix" ? await ctx.GetTextAsync("prefix") + f.FullName
                                           : f.FullName.Replace(await ctx.GetTextAsync("find"), await ctx.GetTextAsync("with"));
            var target = Path.Combine(Path.GetDirectoryName(f.Path)!, newName);
            File.Move(f.Path, target);
            result.Add(target);
            ctx.ReportProgress($"Renamed {f.FullName}");
        }
        return ContentValue.Files(result);
    }
}
```
Context getters (all resolve variables, Ask Each Time, descriptor defaults, implicit input, then convert):
`GetContentAsync GetTextAsync(key, fallback) GetNumberAsync(key, fallback) GetNumberOrNullAsync GetBoolAsync GetChoiceAsync GetDateAsync GetTimeAsync GetDurationAsync GetFilesAsync(key, includeFolders) GetImagesAsync GetPathAsync GetUrlsAsync GetListAsync GetDictionaryAsync GetConditionsAsync` (returns `ConditionSet` with `.Evaluate()` / `.Evaluate(leftSelector)` for Filter List).
Also: `Input` (previous output), `ShortcutInput`, `ShortcutName`, `RunId`, `Source`, `Depth`, `TempFolder`, `NewTempPath`, `WriteTempFile`, `DownloadAsync(uri)`, `CancellationToken`, `Log`, `ReportProgress`, `GetVariable/SetVariable`, `Services`, `HasParameter`, `PeekLiteral`.
Rules: throw `ActionException("friendly message")` for expected failures; `UserCancelledException` ends quietly; other exceptions get a generic friendly message. Honour `ctx.CancellationToken`. Return `ContentValue.Empty` for "no output" (set `HasOutput = false`). Parameter keys not in `Summary` appear under Show More. `IsInput` params default to the previous output. Descriptor `IsAdvanced` hides it unless Settings > Show advanced actions; `IsDestructive` shows a warning on import.
Summary tokens must name real parameter keys (a Core test enforces this for built-ins; do the same in yours with `descriptor.SummarySegments`).

Run in a shortcut: Choice parameter values arrive as the choice **id** (`GetChoiceAsync`). Control-flow actions (`flow.*` with `FlowKind`) are engine-run (Core owns them).

## 4. Triggers and conditions (agent C)
```csharp
public sealed class AppOpenedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.opened", Title = "An app opens", Category = "Apps", Icon = "Apps24",
        Summary = "When {app} opens", Description = "Runs when the app you choose starts.",
        OutputKind = ContentKind.App, OutputDescription = "The app that opened",
        Parameters = new[] { new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true } },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var name = config.GetApp("app")?.Name ?? config.GetString("app");
        var timer = new Timer(_ => { if (/* process started */ false) sink.Fire(ContentValue.From(new AppItem(name, name)), $"{name} opened"); }, null, 0, 2000);
        return timer;                                                 // Dispose() stops it (release hooks/watchers/timers)
    }
}

public sealed class OnlyOnBatteryCondition : ConditionProvider
{
    private static readonly ConditionDescriptor D = new()
    {
        Id = "cond.power", Title = "Power source", Category = "Power", Summary = "I'm {state}", Description = "Only when on battery or plugged in.",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", "State", "battery", new Choice("battery", "on battery"), new Choice("plugged", "plugged in")) },
    };
    public override ConditionDescriptor Descriptor => D;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Task.FromResult(config.GetChoice("state") == "battery" /* && on battery */);
}
```
`TriggerConfig`: literal values only: `GetString GetNumber GetInt GetBool GetChoice GetTime GetDate GetDuration GetStrings GetApp GetFolder`, with descriptor defaults. Build one in tests with `TriggerConfig.Of(("app", ParamValue.Text("Spotify")))` or `TriggerConfig.From(automation.Trigger, descriptor.Parameters)`. Registries: `TriggerRegistry.RegisterAssembly`, `ConditionRegistry.RegisterAssembly`. Automation model: `Automation { Trigger, Conditions, ShortcutId, RunMode, NotifyWhenRun, CooldownSeconds, LastRun }`; persisted by `IShortcutLibrary.SaveAutomation`. Run automations with `IShortcutRunner.RunAsync(shortcutId, trigger output, new RunOptions { Source = RunSource.Automation })`.

## 5. Host services (`ctx.Services`, a `FlowmateServices`)
Reading an unset service throws a friendly `ActionException`.
- `UserInteraction` (`IUserInteraction`): `ShowAlertAsync AskForInputAsync AskForFormAsync ChooseFromListAsync ChooseFromMenuAsync ShowResultAsync PreviewAsync PickFilesAsync PickFolderAsync PickSaveFileAsync SelectScreenRegionAsync SelectScreenPointAsync` (physical pixels; null = cancelled). Cancelling a prompt throws `UserCancelledException`.
- `Notifications`: `Show(title, body, imagePath?, openOnClick?)`, `ShowInteractiveAsync(title, body, buttons, allowReply, imagePath, timeout, ct)` -> `NotificationResponse { ButtonLabel, ReplyText, Dismissed }`, `AskToRunAsync(shortcutName, reason)`.
- `Reminders`: `Schedule(new Reminder { When, Title, Body, ShortcutIdToRun, Sound })`, `Cancel(id)`, `List()`, `Due` event. Core's `ReminderStore` persists and fires `Due`; the host calls `Start()` and shows the toast.
- `Secrets`: `Get/Set/Remove` (DPAPI), e.g. `ai.apiKey`. `Settings`: `Current` (`AppSettings`) + `Update(s => ...)`.
- `UiThread`: `await ctx.Services.UiThread.InvokeAsync(() => Clipboard.GetText())` (STA: clipboard, WIC imaging, dialogs). `InvokeTaskAsync` for async work.
- `Clipboard` (`IClipboardService`, an addition to SPEC; the host implements it; used by the Clipboard variable and the clip.* actions).
- **No WPF in libraries.** Action libraries and Triggers must not use WPF or Windows Forms (`tests/Flowmate.Hosting.Tests` checks). Use Win32/WinRT, or a host hook: `FlowmateServices.NotAvailable("edit pictures")` makes the standard "This app can't … here" error for a hook the host didn't set. Existing hooks: `ImageEngine.Backend` (`IImageBackend`), `TriggerClipboard.Backend` (`ITriggerClipboard`); WPF implementations live in `Flowmate.Host.Wpf` (`WpfHost.InstallBackends()`, `UseWpf(...)`, `WpfAppIcons`).
- `KeyValues` (saved values), `History`, `Library` (`IShortcutLibrary`), `Runner` (`IShortcutRunner`), `Http` (shared `HttpClient`, UA `Flowmate/1.0`), `Paths` (`FlowmatePaths`).
- Library: `All Folders Automations Snippets GetById FindByName IsNameTaken Save Delete SaveFolder DeleteFolder SaveAutomation DeleteAutomation SaveSnippet DeleteSnippet Import ImportJson Export Changed`. Returned objects are shared: `Clone()` to edit, then `Save`. `Changed` can fire on any thread (`LibraryChange` flags, `External` = file watcher).
- Concrete stores for hosts: `JsonShortcutLibrary(paths, registry)`, `JsonSettingsStore`, `DpapiSecretStore`, `JsonKeyValueStore`, `JsonlRunHistory`, `ReminderStore`. `FlowmatePaths.Default`, `FlowmatePaths.ForRoot(dir)`, `paths.WithLibrary(folder)`.
- Engine: `FlowmateEngine.CreateBuilder().UsePaths(p).AddActions(assembly)….ConfigureServices(s => …).Build()` → `engine.Actions`, `.Services`, `.Runner`, `.RunAsync(shortcut, input, options)`, `.Validate`, `.LoadShortcut/SaveShortcut(path or stream)`. Same as `new ShortcutRunner(ActionRegistry.CreateWithBuiltIns() + RegisterAssembly(...), services)` (also sets `services.Runner`). **The runner writes run history itself** for every top-level run (`RunOptions.RecordHistory`), hosts must not. Other apps: `docs/HOSTING.md`.
- `.flowmate` files: `ShortcutPackage.Load(path|stream)`, `Save(shortcut, path|stream)` (path save is write-then-replace), `Serialize`, `Parse`, `Inspect`.

## 6. Engine notes
- `RunOptions { Source, Depth, CancellationToken, Observer, RecordHistory, StartAtUid, SeedOutputs, SeedVariables, RunId, SharedTempFolder, KeepTemp }`; `RunResult { Status, Output, ErrorMessage, FailedStepUid, Started, Duration, Steps }`.
- `StartAtUid` ("Run from this step"): top-level action -> starts there; inside an If/Menu/Repeat -> restarts at the beginning of that whole top-level block. Earlier steps are skipped; the step before the start gets its output from `SeedOutputs[uid]` (else shortcut input). `SeedOutputs` also feeds magic variables. Named variables from the previous run are *not* restored.
- `IRunObserver`: `OnStepStarted/OnStepFinished/OnVariableSet/OnLog` (+ default `OnProgress`). Callbacks arrive on worker threads.
- `ShortcutValidator.Validate(shortcut, registry)` -> `ValidationIssue(Severity, Code, Message, ActionUid, ParameterKey)`. `FlowTree.Parse(actions)` gives the block tree (throws `FlowStructureException`).
- Menu: the labels used at run time are the **Middle** actions' `Flow.Label`; the Start's `items` list mirrors them.
- Built-in action ids: `flow.comment if menu repeat repeatEach wait stop output runShortcut nothing`, `var.set add get saveValue loadValue`, `content.text number list dict`, `ui.showResult alert ask askMany chooseList notify preview`. `ui.askMany` takes a `fields` list of dictionaries (`label`, `kind`, `default`, `options`). `ui.notify` takes `buttons`, `reply`, `wait`, `timeout`.
- Snippets: `Snippet { Id, Abbreviation, Text, Enabled, MatchCase, Folder }`; `SnippetExpander.Expand(text, now, clipboardText, fillIns)` -> `(Result, CursorOffsetFromEnd)`; `FillInLabels(text)`; `ApplyTypedCase(typed, expansion)`. `Shortcut.Alias` is the Quick Run short code.

## 7. Testing an action with the fakes
```csharp
using Flowmate.Core.Testing;

[Fact]
public async Task Renames()
{
    using var host = new TestHost();                         // all services faked, temp paths, ShortcutRunner + built-ins
    host.UI.Enqueue("answer");                               // scripted replies; host.UI.EnqueueCancel(); host.UI.Calls/Prompts
    host.Clipboard.Value = ContentValue.Text("clip");
    var output = await ActionTestHarness.RunAsync(host, new RenameFilesAction(),
        ContentValue.Files(new[] { path }),                  // optional implicit input
        ("mode", ParamValue.Choice("prefix")), ("prefix", "new_"));
    Assert.Equal("new_a.txt", output.Property("Full Name").AsText());

    // or through the real engine (errors, retry, variables):
    var result = await ActionTestHarness.RunInShortcutAsync(host, "file.rename", null, ("mode", ParamValue.Choice("prefix")));
    Assert.Equal(RunStatus.Failed, result.Status);
}
```
Fakes: `FakeUserInteraction FakeNotificationService FakeReminderService InMemorySecretStore InMemorySettingsStore InlineUiThread InMemoryKeyValueStore InMemoryRunHistory FakeClipboard InMemoryShortcutLibrary TestHost ActionTestHarness`. `new TestHost().With(typeof(MyAction).Assembly)` registers your actions in the registry. For file work use `host.Root` or your own temp dir.
