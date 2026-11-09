# Automation Portal — Product & Technical Spec (v1)

Automation Portal is an everyday automation app for Windows 11, modelled on **Apple Shortcuts**, plus the best
*easy* features of Power Automate (recorder, run history, retry-on-error, variable inspector),
Keyboard Maestro / AutoHotkey (keyboard shortcuts, typed-phrase triggers, text expansion),
Raycast / PowerToys Run (quick-run palette), Tasker (automation conditions), Stream Deck (button panel)
and IFTTT (plain-language "When this → do that").

**Audience:** non-technical everyday users. **No coding, no IT/enterprise features** (no cloud flows,
databases, UI-element selectors, Active Directory, terminal emulation …). Power-user bits (run command)
live in a hidden "Advanced" category.

---

## 0. Ground rules for every agent

- Project root `C:\AI\Projects\Flowmate\`. **Never create files in `C:\AI\` root.** Temp files → `%TEMP%`.
  Build outputs → `builds\`, helper scripts → `scripts\`, docs → `docs\`.
- .NET 10 SDK (10.0.401 installed). `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `<LangVersion>latest</LangVersion>`, file-scoped namespaces. Keep warnings low.
- **Stay inside the folders you own (§2).** Never edit another area's folder or `AutomationPortal.sln`.
  Need something from Core? Write it in `docs/core-requests/<area>.md` and work around it locally.
- During parallel phases build only your own project:
  `dotnet build <your.csproj> -p:BuildProjectReferences=false` (Core is prebuilt). Run your own tests.
- Git: WIP commit after each meaningful step, **only your own paths**:
  `git add <paths>` then `git commit -m "<area>: <msg>" -- <paths>`. If `.git/index.lock` exists, wait 5 s and retry.
- **User-facing words are plain English**: "Find pattern" not "regex", "Keyboard shortcut" not "hotkey",
  "Folder" not "directory". Errors say what happened and what to do ("Couldn't find the file
  'report.pdf'. Check the folder still exists.").
- Implement directly; do not re-delegate.

## 1. Tech stack

- WPF on .NET 10 + **WPF-UI** (NuGet `WPF-UI`, 4.x) for Fluent/Mica look, `CommunityToolkit.Mvvm`.
- TFMs: `AutomationPortal.Core` = `net10.0` (platform-neutral, unit-testable). Everything else =
  `net10.0-windows10.0.22621.0` with `<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>`
  (gives WinRT: OCR, Radios, Geolocation, media sessions, speech, Windows.Data.Pdf).
- **Shared engine:** Core, the three action libraries, Triggers and Ai.Windows never use WPF or Windows Forms
  (no `UseWPF`/`UseWindowsForms`, no package that brings either), so other apps (e.g. a WinUI 3 app) can host them.
  Only `AutomationPortal.Host.Wpf`, Editor, App and Cli use WPF. `tests/AutomationPortal.Hosting.Tests` enforces this. See `docs/HOSTING.md`.
- System.Text.Json, xUnit.
- Allowed NuGet (permissive licences only — MIT/Apache/BSD/MS-PL): WPF-UI, CommunityToolkit.Mvvm,
  Microsoft.Toolkit.Uwp.Notifications (toasts), QRCoder, ZXing.Net, ClosedXML, UglyToad.PdfPig,
  PDFsharp 6.x, HtmlAgilityPack, NAudio (`NAudio.Core`/`.Wasapi`/`.WinMM` only; the `NAudio` meta-package brings
  Windows Forms). **Not** ImageSharp (licence). Images behind `IImageBackend`: WPF imaging in the WPF hosts, `WinRtImageBackend` (Windows.Graphics.Imaging, no WPF) by default elsewhere
  (the WPF implementation is in `AutomationPortal.Host.Wpf`).
- Icons: WPF-UI `SymbolRegular` enum **names** stored as strings (e.g. `"Globe24"`). Core never references WPF-UI.

## 2. Solution layout & ownership

```
AutomationPortal.sln
src/AutomationPortal.Core/               A   model, content, variables, engine, SDK, built-in actions, storage, history
src/AutomationPortal.Actions.Content/    B1  text, numbers, dates, lists & dictionaries, web, weather, spreadsheets, QR
src/AutomationPortal.Actions.Windows/    B2  device & settings, apps & windows, keyboard & mouse, clipboard, screen & OCR, media & speech, location
src/AutomationPortal.Actions.Documents/  B3  files & folders, images, PDF, notes & reminders, calendar, email, AI, phone push
src/AutomationPortal.Triggers/           C   trigger + condition providers, automation engine, keyboard-shortcut service, typed-phrase hook, macro recorder engine
src/AutomationPortal.Ai.Windows/         E   on-device AI provider (Windows AI APIs)
src/AutomationPortal.Host.Wpf/           E   WPF host pieces: dispatcher UI thread, clipboard service, picture backend, copy-trigger clipboard, app icons
src/AutomationPortal.Editor/             D   shortcut editor (WPF)
src/AutomationPortal.App/                D   WPF app: shell, library, editor, automations UI, gallery UI, history, settings, tray, quick run, dialogs
src/AutomationPortal.Cli/                E   `automationportal` console CLI
tests/Automation Portal.<X>.Tests/        same owner as <X>
gallery/                         E   starter shortcuts (*.autoportal)
```

Libraries reference **Core only** (Ai.Windows also Actions.Documents; Host.Wpf references the libraries it implements hooks for).
App and Cli reference everything they host and build their engine with `AutomationPortalEngine.CreateBuilder()` + `UseWpf(...)`.
Actions, triggers and conditions are discovered by **assembly scanning** (non-abstract subclasses with a
public parameterless constructor) — no central registration list to edit.

---

## 3. Core

### 3.1 Shortcut

```
Shortcut
  Id               Guid
  Name             string           unique (case-insensitive) in the library
  Alias            string?          short code for Quick Run ("gm" → Good Morning)
  Description      string?
  Icon             string           SymbolRegular name, default "Flash24"
  Color            string           palette key: red, orange, yellow, green, mint, teal, cyan, blue, indigo,
                                    purple, pink, brown, gray, graphite, slate
  FolderId         Guid?
  SortOrder        int
  Pinned           bool             tray menu + Quick Panel
  Hotkey           string?          "Ctrl+Alt+M"
  ShowInQuickRun   bool = true
  ShowInSendTo     bool             Explorer "Send to" + right-click menu
  AcceptsInput     List<ContentKind>   empty = takes no input
  WhenNoInput      ContinueWithNothing | AskForFiles | UseClipboard | StopAndSay
  Actions          List<ActionInstance>
  ImportQuestions  List<ImportQuestion { ActionUid, ParameterKey, Question, DefaultValue? }>
  CreatedAt, ModifiedAt   DateTimeOffset
  SchemaVersion    int = 1
ShortcutFolder { Id, Name, Icon, SortOrder }
```

### 3.2 Actions in a shortcut — flat list with control-flow groups (like Apple Shortcuts)

```
ActionInstance
  Uid          string     short unique id (8–12 chars); target of magic variables
  ActionId     string     e.g. "text.combine"
  Parameters   Dictionary<string, ParamValue>
  OutputName   string?    user rename of the magic variable (default = descriptor OutputName)
  Disabled     bool
  OnError      { Mode: Stop | Continue | Retry, Retries = 2, RetryDelaySeconds = 1 }
  Flow         { GroupId, Role: Start | Middle | End, Label? }   only for control-flow actions
  UiCollapsed  bool
```

- **If**: Start (holds conditions) → optional Middle ("Otherwise") → End ("End If").
- **Choose from Menu**: Start (prompt + item labels) → one Middle per item (Label = item text, same order) → End.
- **Repeat / Repeat with Each**: Start → End.
- All parts of a group share the same ActionId; Role tells them apart. The engine parses the flat list into a
  tree; broken nesting gives a friendly validation error. The editor moves/deletes whole groups together.

### 3.3 Parameter values (JSON tagged union, discriminator `"t"`)

```
{"t":"text","parts":["Hello ", {"var":VarRef}, "!"]}     text template with inline variables
{"t":"num","v":12.5}   {"t":"bool","v":true}   {"t":"choice","v":"spaces"}
{"t":"var","ref":VarRef}                                 whole value is a variable
{"t":"list","items":[ParamValue…]}
{"t":"dict","items":[{"k":ParamValue,"v":ParamValue}…]}
{"t":"ask"}                                              Ask Each Time
{"t":"conds","match":"all|any","items":[{"left":ParamValue,"op":"contains","right":ParamValue,"right2":ParamValue?}]}
{"t":"date","v":"2026-10-02T09:00:00+01:00"}  {"t":"time","v":"07:30"}  {"t":"duration","seconds":90}
{"t":"hotkey","v":"Ctrl+Alt+K"}  {"t":"file","path":"…"}  {"t":"app","id":"…","name":"…"}
```

`VarRef`:
```
{"kind":"output","uid":"<action uid>"}   magic variable (output of an earlier action)
{"kind":"named","name":"Total"}
{"kind":"input"}                          Shortcut Input
{"kind":"clipboard"} {"kind":"currentDate"} {"kind":"repeatItem"} {"kind":"repeatIndex"} {"kind":"menuResult"} {"kind":"device"}
optional: "prop":"Name"   property of each item (e.g. File → Name; Dictionary → "key:city")
          "as":"Number"   coerce to a ContentKind
          "pick":"first|last|random|all" (default all)
```

### 3.4 Variables

- Named variables are per run. **Set Variable** replaces; **Add to Variable** appends.
- Magic variables = each action's output, by Uid; only actions *above* can be referenced (validator enforces).
- Repeat Item / Repeat Index inside repeat blocks; Menu Result inside menu branches.
- **Saved values** persist across runs (`var.saveValue` / `var.loadValue`) — e.g. counters, "last run" dates.

### 3.5 Content and automatic conversion

`ContentKind`: Text, Number, Boolean, Date, Url, File, Folder, Image, Dictionary, Location, App, Window, Any.

`ContentItem` (immutable) — `Kind`, `DisplayText`, `GetProperty(name)`, `PropertyNames`:

| Kind | Payload | Properties (shown in the variable picker) |
|---|---|---|
| Text | string | Length, Word Count, Line Count |
| Number | double | — |
| Boolean | bool | — |
| Date | DateTimeOffset | Year, Month, Day, Hour, Minute, Weekday, Week of Year, Unix Time, Short Date, Long Date, Time |
| Url | Uri | Host, Path, Query, Scheme |
| File / Folder | full path | Name (no extension), Full Name, Extension, Size, Size Text ("1.2 MB"), Created, Modified, Parent Folder, Path, Kind (Image/Document/…) |
| Image | file path (+ width/height) | Width, Height + all File properties |
| Dictionary | ordered string → ContentValue | Keys, Values, any key via `key:<name>` |
| Location | lat, lon, name?, city?, country? | Latitude, Longitude, Name, City, Country |
| App | id, name, path | Name, Path |
| Window | handle, title, app name | Title, App |

`ContentValue` = immutable list of `ContentItem` (single value = list of one). Helpers:
`AsText()` (items joined by new lines), `AsNumber()`, `AsBool()`, `AsDates()`, `AsUrls()`, `AsDictionary()`
(parses JSON text), `AsFilesAsync(ctx)` (text → temp .txt, URL → download), `AsImagesAsync`.
Conversion must be forgiving ("12.5 kg" → 12.5; "yes"/"true"/"1" → true; JSON text ↔ Dictionary).
Images are always backed by a file (generated images go into the run's temp folder).

### 3.6 Action SDK

```csharp
public abstract class ShortcutAction
{
    public abstract ActionDescriptor Descriptor { get; }
    public abstract Task<ContentValue> RunAsync(ActionContext ctx);
}
```

`ActionDescriptor`: `Id`, `Title`, `Category` (§4 names), `Keywords[]`, `Icon`,
`Summary` — sentence with `{paramKey}` tokens rendered inline in the editor, e.g. `"Resize {images} to {width} wide"`;
parameters not in the Summary appear under "Show More" —, `Description` (1–2 plain sentences),
`InputKinds`, `OutputKind`, `OutputName` ("Resized Images"), `Parameters`,
`IsAdvanced` (hidden unless Settings › Show advanced actions), `IsDestructive` (⚠ on import / when added),
`NeedsInternet`, `FlowKind` (None | If | Menu | Repeat | RepeatEach).

`ParameterDescriptor`: `Key`, `Label`, `Type`, `Default`, `Placeholder`, `Choices` (id + label) or
`ChoicesProvider` (async, for dynamic lists: audio devices, voices, installed apps, Wi-Fi networks,
Bluetooth devices, monitors, shortcuts), `Min/Max/Step`, `Unit`, `VisibleWhen (key, values[])`,
`AllowVariables = true`, `AllowAskEachTime = true`, `Multiline`, `Required`,
`IsInput` (when empty, receives the previous action's output — Shortcuts' implicit input), `Hint`.

`ParamType`: Text, MultilineText, Number, Boolean, Choice, Date, Time, DateTime, Duration, File, Folder,
SaveFile, App, Hotkey, Color, Url, Content (any variable/input), List, Dictionary, Conditions,
VariableName, Shortcut.

`ActionContext`: `GetContentAsync(key)`, `GetTextAsync`, `GetNumberAsync(key, fallback)`, `GetBoolAsync`,
`GetChoiceAsync`, `GetDateAsync`, `GetDurationAsync`, `GetFilesAsync`, `GetListAsync`, `GetDictionaryAsync`,
`GetConditionsAsync` — all resolve variables, Ask Each Time (via UI) and conversion.
Plus `Input` (previous output), `ShortcutInput`, `ShortcutName`, `RunId`, `TempFolder` (per run, deleted
afterwards), `CancellationToken`, `Log(string)`, `ReportProgress(string)`, and `Services`
(typed accessors for the host services in §3.8, plus a shared `HttpClient` with UA `AutomationPortal/1.0`).
Throw `ActionException("friendly message")` for expected failures; `UserCancelledException` ends quietly.

### 3.7 Engine (`ShortcutRunner`)

- `RunAsync(Shortcut, ContentValue input, RunOptions)`; `RunOptions`: `Source` (Editor, Library, Hotkey,
  Automation, Cli, SendTo, QuickRun, Link, Nested, Reminder), `Depth`, `CancellationToken`,
  `IRunObserver` (OnStepStarted(uid), OnStepFinished(uid, output, duration, error), OnVariableSet, OnLog).
- Implicit input; Comment and disabled actions pass the previous output through.
- **If** operators by type — Text: is, is not, contains, does not contain, begins with, ends with,
  matches pattern (advanced), has any value, has no value. Number: =, ≠, >, ≥, <, ≤, is between.
  Date: is today, is before, is after, is between. Boolean: is true, is false. Match all / any.
  Output of an If block = output of the last action in the branch that ran.
- **Repeat N** (Repeat Index, 1-based), **Repeat with Each** (Repeat Item + Index). Output = list of each
  pass's last output ("Repeat Results").
- **Choose from Menu** → UI menu → runs that branch; Menu Result = chosen label.
- Stop This Shortcut, Stop and Output, Wait (cancellable), Run Shortcut (by Id, passes input, returns
  output; max depth 16), Nothing.
- Per-action OnError: Stop (default) / Continue (output = nothing) / Retry N times.
- Safety: loop cap 100,000 iterations; cancellation everywhere; max 8 concurrent runs.
- `RunResult`: Status (Succeeded, Failed, Cancelled, Stopped), Output, ErrorMessage, FailedStepUid,
  Started, Duration, Steps (uid, actionId, title, durationMs, outputPreview ≤ 200 chars, error).
- `ShortcutValidator` → issues for the editor: unknown action, variable points to a later/removed action,
  broken groups, missing required parameters.
- **Run from this step**: `RunOptions.StartAtUid` + `RunOptions.SeedOutputs` (outputs of earlier steps from the
  previous editor run).

### 3.8 Host services (interfaces in Core; WPF implementations in Host.Wpf and App/Cli; fakes in Core.Tests)

- `IUserInteraction`: ShowAlertAsync(title, message, showCancel) → bool; AskForInputAsync(prompt, kind
  [text, multiline, number, date, time, url], default) → ContentValue; AskForFormAsync(title, fields) →
  Dictionary (Ask for Several Things); ChooseFromListAsync(prompt, items,
  multiSelect); ChooseFromMenuAsync(prompt, labels) → index; ShowResultAsync(title, ContentValue);
  PreviewAsync(ContentValue); PickFilesAsync(prompt, multi, filters); PickFolderAsync; PickSaveFileAsync;
  SelectScreenRegionAsync() → rect?; SelectScreenPointAsync() → point?. Cancel → `UserCancelledException`.
- `INotificationService`: Show(title, body, imagePath?, openOnClick?); AskToRunAsync(shortcutName, reason) → bool;
  ShowInteractiveAsync(title, body, buttons, allowReply, imagePath?, timeout, ct) → NotificationResponse
  { ButtonLabel?, ReplyText?, Dismissed }.
- `IReminderService`: Schedule(Reminder { Id, When, Title, Body, ShortcutIdToRun?, Sound }), Cancel, List — persisted.
- `IShortcutLibrary`: All, Folders, GetById, FindByName, Save, Delete, Import, Export, `Changed` event.
- `IShortcutRunner` (nested runs and hosts).
- `ISecretStore` (DPAPI, current user) — e.g. `ai.apiKey`. `ISettingsStore` (`AppSettings`, §6.9).
- `IUiThread`: InvokeAsync on an STA dispatcher (clipboard, WIC imaging, dialogs). Cli supplies its own STA thread.
- Library-level hooks (process-wide, set once by the host): `ImageEngine.Backend` (`IImageBackend`, Actions.Documents; defaults to `WinRtImageBackend`) and
  `TriggerClipboard.Backend` (`ITriggerClipboard`, Triggers). `WpfHost.InstallBackends()` sets both (the WPF image backend explicitly).
- `IKeyValueStore` (saved values). `IRunHistory`: Add(RunRecord), Query(filter).

### 3.9 Storage

- Library folder (configurable so it can live in OneDrive for sync; default `%APPDATA%\Automation Portal\Library\`):
  `shortcuts\<id>.json`, `folders.json`, `automations.json`. Watch for external changes → reload + `Changed`.
- `%APPDATA%\Automation Portal\`: `settings.json`, `secrets.dat`, `reminders.json`, `saved-values.json`.
- `%LOCALAPPDATA%\Automation Portal\`: `history\runs-YYYY-MM.jsonl` (keep last 1,000 runs), `logs\`, `temp\<runId>\`, `icons\`.
- Atomic writes (temp file + replace).
- Export `.autoportal` = `{"format":"autoportal.shortcut","version":1,"shortcut":{…}}`. Import gives a new Id,
  keeps Uids, renames on clash ("Name 2"), lists any ⚠ destructive/advanced actions it contains.

### 3.10 Automations (model + SDK in Core; providers and engine in Triggers)

```
Automation { Id, Enabled, Trigger: TriggerInstance { TriggerId, Parameters },
             Conditions: List<ConditionInstance { ConditionId, Parameters }>,
             ShortcutId, RunMode: Immediately | AskFirst, NotifyWhenRun, CooldownSeconds, LastRun?, CreatedAt }

abstract TriggerProvider   : Descriptor (Id, Title, Category, Icon, Summary "When {app} opens",
                             Description, Parameters, OutputKind, OutputDescription)
                             IDisposable Start(TriggerConfig config, ITriggerSink sink)   // sink.Fire(ContentValue, description)
abstract ConditionProvider : Descriptor + Task<bool> IsMetAsync(TriggerConfig config, CancellationToken)
```
Trigger/condition parameters use literal values only (no variables). Trigger output becomes Shortcut Input.
A shortcut can have several automations; the editor's Details sheet lists and adds them ("Triggers").

### 3.11 Snippets (text expansion — a simple list, not shortcuts)

`Snippet { Id, Abbreviation, Text, Enabled, MatchCase, Folder? }` in `snippets.json` (library folder).
Placeholders: `{date}`, `{time}`, `{date:<format>}`, `{clipboard}`, `{cursor}` (caret lands here),
`{input:Label}` (fill-in asked at expansion). `SnippetExpander` (pure, tested) in Core; typing hook in C; page in D.

---

## 4. Action catalog

Category names (editor colour in brackets): Scripting (gray), Variables (orange), Show & Ask (blue),
Text (yellow), Numbers (slate), Dates (red), Lists & Dictionaries (orange), Web (blue), Weather & Location (cyan),
Spreadsheets (green), Files & Folders (cyan), Images (purple), PDF (red), Notes & Reminders (yellow),
Email & Sharing (blue), AI (indigo), Device & Settings (gray), Apps & Windows (indigo),
Keyboard & Mouse (graphite), Clipboard (teal), Screen (pink), Sound & Media (pink), Advanced (graphite, hidden).

### A — Core built-ins (`AutomationPortal.Core`)
`flow.comment` Comment · `flow.if` If · `flow.menu` Choose from Menu · `flow.repeat` Repeat ·
`flow.repeatEach` Repeat with Each · `flow.wait` Wait · `flow.stop` Stop This Shortcut ·
`flow.output` Stop and Output · `flow.runShortcut` Run Shortcut · `flow.nothing` Nothing ·
`var.set` Set Variable · `var.add` Add to Variable · `var.get` Get Variable · `var.saveValue` Save Value ·
`var.loadValue` Get Saved Value · `content.text` Text · `content.number` Number · `content.list` List ·
`content.dict` Dictionary · `ui.showResult` Show Result · `ui.alert` Show Alert · `ui.ask` Ask for Input ·
`ui.askMany` Ask for Several Things (form → dictionary) · `ui.chooseList` Choose from List ·
`ui.notify` Show Notification (optional buttons / reply box → output = choice or reply) · `ui.preview` Preview

### B1 — `AutomationPortal.Actions.Content`
- **Text**: `text.combine` Combine Text · `text.split` Split Text · `text.replace` Replace Text (Find-pattern option) ·
  `text.case` Change Case · `text.cleanup` Clean Up Text (trim, extra spaces, empty lines, line breaks) ·
  `text.getPart` Get Part of Text (first/last N characters/words/lines, between X and Y, before/after) ·
  `text.find` Find in Text (presets: email addresses, phone numbers, links, numbers, dates; or custom pattern) ·
  `content.count` Count (items, characters, words, lines, sentences) · `text.fromInput` Get Text from Input ·
  `text.encode` Encode / Decode (Base64, URL, HTML) · `text.hash` Make Hash (advanced) ·
  `text.password` Generate Password (length, symbols, digits, or word passphrase) ·
  `text.qrMake` Make QR Code · `text.qrRead` Read QR Code / Barcode
- **Numbers**: `num.calculate` Calculate · `num.expression` Calculate Expression ("2*(3+4)") · `num.round` Round Number ·
  `num.random` Random Number · `num.format` Format Number · `num.convertUnits` Convert Measurement (length, weight,
  temperature, volume, speed, area, data size, time) · `num.stats` Calculate Statistics (sum, average, min, max, median) ·
  `num.currency` Convert Currency (frankfurter.app)
- **Dates**: `date.date` Date · `date.current` Current Date · `date.format` Format Date (short/medium/long, ISO,
  relative "in 3 days", custom) · `date.adjust` Adjust Date (add/subtract; start/end of day/week/month/year) ·
  `date.between` Get Time Between Dates · `date.parse` Get Dates from Input ("tomorrow 3pm", "next Friday", "in 3 days")
- **Lists & Dictionaries**: `list.getItem` Get Item from List (first/last/random/index/range) · `list.sort` Sort List
  (A–Z, Z–A, numbers, dates, by property, shuffle, reverse) · `list.unique` Remove Duplicates ·
  `list.filter` Filter List (conditions, limit) · `dict.getValue` Get Dictionary Value (key or path "a.b.0", all keys, all values) ·
  `dict.setValue` Set Dictionary Value · `dict.fromInput` Get Dictionary from Input (JSON) · `dict.toJson` Make JSON Text
- **Web**: `web.fetch` Get Contents of URL (method, headers, JSON/form/text/file body) · `web.download` Download File ·
  `web.pageText` Get Web Page Text · `web.pageLinks` Get Links from Web Page · `web.pageImages` Get Images from Web Page ·
  `web.open` Open URLs · `web.search` Search the Web (Google, Bing, DuckDuckGo, YouTube, Wikipedia, Amazon, Maps) ·
  `web.urlPart` Get Part of URL · `web.expand` Expand Short Link · `web.cleanUrl` Remove Tracking from Link ·
  `web.rss` Get Items from RSS Feed · `web.publicIp` Get Public IP Address · `web.isUp` Check if Website Is Up
- **Weather & Location (web parts)**: `weather.current` Get Current Weather · `weather.forecast` Get Weather Forecast ·
  `loc.find` Find Location by Name (Open-Meteo, no key needed)
- **Spreadsheets**: `sheet.read` Read Spreadsheet (CSV/XLSX → list of dictionaries) · `sheet.addRow` Add Row to Spreadsheet ·
  `sheet.makeCsv` Make CSV from List

### B2 — `AutomationPortal.Actions.Windows`
- **Device & Settings**: `dev.setVolume` Set Volume · `dev.changeVolume` Turn Volume Up/Down · `dev.mute` Mute Sound ·
  `dev.getVolume` Get Volume · `dev.audioOutput` Set Sound Output Device · `dev.audioInput` Set Microphone ·
  `dev.appVolume` Set App Volume (per app, mute app) · `dev.micMute` Mute Microphone ·
  `dev.displayMode` Set Display Mode (PC screen only / duplicate / extend / second screen only) ·
  `dev.resolution` Set Screen Resolution & Refresh Rate · `dev.hdr` Turn HDR On/Off ·
  `dev.monitorInput` Switch Monitor Input (DDC/CI) ·
  `dev.brightness` Set Brightness (laptop via WMI, external monitors via DDC/CI) · `dev.theme` Set Light/Dark Mode ·
  `dev.wallpaper` Set Wallpaper · `dev.wifi` Turn Wi-Fi On/Off · `dev.bluetooth` Turn Bluetooth On/Off ·
  `dev.joinWifi` Connect to Wi-Fi · `dev.network` Get Network Details · `dev.battery` Get Battery Level ·
  `dev.details` Get Device Details · `dev.powerMode` Set Power Mode · `dev.keepAwake` Keep PC Awake · `dev.powerPlan.switch` Switch Power Plan · `dev.powerPlan.get` Get Current Power Plan · `dev.powerPlan.list` List Power Plans · Power Plan Manager (separate app, driven by its `powerplanmanager:` links and read-only status pipe): `ppm.plan` · `ppm.preset` · `ppm.limit` · `ppm.topup` · `ppm.pause` · `ppm.resume` · `ppm.calibrate` · `ppm.keepawake` · `ppm.show` · `ppm.status` · `ppm.presets` ·
  `dev.lock` Lock PC · `dev.sleep` Sleep · `dev.power` Shut Down / Restart / Sign Out / Hibernate (⚠, asks first by default) ·
  `dev.screenOff` Turn Off Screen · `dev.settings` Open Settings Page · `dev.recycleBin` Empty Recycle Bin (⚠)
- **Apps & Windows**: `app.open` Open App (installed + Store apps) · `app.quit` Quit App (gentle, then force option) ·
  `app.isRunning` Is App Running · `app.list` Get Running Apps · `app.front` Get Front App · `app.switch` Switch to App ·
  `app.waitFor` Wait for App to Open/Close · `app.openFile` Open Files (default app or chosen app) ·
  `app.reveal` Show in File Explorer · `app.revealInFilesPortal` Show in Files Portal (falls back to Explorer) · `win.arrange` Arrange Window (maximize, minimize, restore, snap halves/quarters,
  center, next monitor, size & position) · `win.onTop` Keep Window on Top · `win.showDesktop` Show Desktop ·
  `win.close` Close Window · `win.list` Get Windows · `win.saveLayout` Save Window Layout (named) ·
  `win.restoreLayout` Restore Window Layout (reopens missing apps option) ·
  `text.getSelected` Get Selected Text (from the front app; restores the clipboard) ·
  `web.currentPage` Get Current Web Page (URL + title from Edge/Chrome/Firefox via UI Automation) ·
  `adv.runCommand` Run Command (Advanced)
- **Keyboard & Mouse**: `input.type` Type Text · `input.keys` Press Keys ("Ctrl+Shift+T") · `input.click` Click ·
  `input.moveMouse` Move Mouse · `input.scroll` Scroll
- **Clipboard**: `clip.get` Get Clipboard · `clip.set` Copy to Clipboard (optional clear after N seconds) · `clip.clear` Clear Clipboard
- **Screen**: `screen.capture` Take Screenshot (screen, all screens, front window, select area) ·
  `screen.ocr` Get Text from Image · `screen.grabText` Grab Text from Screen (select area → text) ·
  `screen.pickColor` Pick Color from Screen
- **Sound & Media**: `media.playPause` Play/Pause · `media.next` Skip Forward · `media.previous` Skip Back ·
  `media.nowPlaying` Get What's Playing · `media.playSound` Play Sound · `media.speak` Speak Text ·
  `media.dictate` Dictate Text · `media.record` Record Audio
- **Location**: `loc.current` Get Current Location · `loc.distance` Get Distance · `loc.maps` Show in Maps

### B3 — `AutomationPortal.Actions.Documents`
- **Files & Folders**: `file.get` Get File · `file.getFolder` Get Folder (Desktop, Documents, Downloads, Pictures,
  Music, Videos, Home, OneDrive, or chosen) · `file.contents` Get Contents of Folder · `file.find` Find Files
  (name, extension, kind, size, date; sort; limit) · `file.filter` Filter Files · `file.details` Get Details of Files ·
  `file.rename` Rename Files (tokens {name} {ext} {n} {date:yyyy-MM-dd} {taken}; replace in name; prefix/suffix;
  numbering) · `file.renamePattern` Rename Files by Pattern (advanced; {name} {ext} {n} with start/padding, {date} modified, {parent};
  find/replace plain or regex; change case; collisions skip or " (2)"; planned first, swaps safe, rollback on failure; preview only) ·
  `file.compare` Compare Files (same? boolean, or text line diff; second may be a folder) · `file.hash` Get File Hash (MD5/SHA-1/SHA-256/SHA-512, lowercase hex) · `file.move` Move Files (into subfolders by {kind}/{year}/{month}; if exists: keep both/replace/skip) ·
  `file.copy` Copy Files · `file.delete` Delete Files (Recycle Bin; ⚠) · `file.newFolder` Create Folder ·
  `file.save` Save File · `file.append` Append to Text File · `file.readText` Get Text from Document
  (.txt .md .csv .json .html .docx .pdf) · `file.zip` Make Zip · `file.unzip` Extract Zip ·
  `file.selected` Get Selected Files in File Explorer
- **Images**: `img.resize` Resize Images · `img.convert` Convert Images (JPEG, PNG, BMP, GIF, TIFF) · `img.crop` Crop Images
  (incl. 1:1, 16:9, 4:3) · `img.rotate` Rotate / Flip · `img.combine` Combine Images · `img.details` Get Details of Images
  (size, date taken, camera, GPS → Location) · `img.stripInfo` Remove Photo Info · `img.addText` Add Text to Image
- **PDF**: `pdf.make` Make PDF (from images/text/files) · `pdf.merge` Combine PDFs · `pdf.split` Split PDF ·
  `pdf.text` Get Text from PDF · `pdf.details` Get Details of PDF · `pdf.toImages` Make Images from PDF (Windows.Data.Pdf)
- **Notes & Reminders**: `note.add` Add to Note (Documents\Automation Portal Notes\<name>.md) · `note.get` Get Note ·
  `note.open` Open Note · `rem.add` Remind Me (at a time or in N minutes; optionally run a shortcut) ·
  `rem.timer` Start Timer · `rem.list` Get Upcoming Reminders · `cal.addEvent` Add Calendar Event
  (classic Outlook if present, else .ics opened in the default calendar app) · `cal.getEvents` Get Calendar Events (classic Outlook)
- **Email & Sharing**: `mail.compose` Compose Email (Outlook draft with attachments, else mailto) ·
  `mail.send` Send Email via Outlook (⚠, "show before sending" on by default) · `share.phone` Send to My Phone (ntfy) ·
  `share.share` Share… (Windows share sheet)
- **AI** — multi-provider (bring your own key): Claude (official Anthropic SDK), ChatGPT/OpenAI, Google Gemini,
  DeepSeek, Kimi (Moonshot), Microsoft Azure OpenAI / Foundry (the "Copilot" option — no public consumer Copilot
  API exists), local AI (Ollama, LM Studio), and any OpenAI-compatible endpoint. Default provider + per-action
  override; config in `ai-providers.json` + keys in ISecretStore `ai.<providerId>.apiKey`. Settings › AI lists
  providers with key fields, model pickers (from each provider's model list) and "Test". Actions: `ai.ask` Ask AI (answer as: Text, Yes/No → Boolean for If, Number, List,
  Dictionary) · `ai.summarize` Summarize · `ai.rewrite` Rewrite Text (proofread, more professional, friendlier,
  shorter, longer, simpler, make a table, make a list) · `ai.translate` Translate Text ·
  `ai.extract` Extract Information (→ dictionary) · `ai.describeImage` Describe Image

## 5. Triggers & conditions (C — `AutomationPortal.Triggers`)

| Category | Triggers |
|---|---|
| Time | `time.daily` At a time of day (pick days) · `time.monthly` Monthly (day N / first Monday / last day) · `time.interval` Every N minutes/hours (optional active hours) · `time.once` On a date & time · `time.sun` At sunrise / sunset (± offset) · `cal.eventSoon` N minutes before a calendar event (from a calendar's .ics link — no sign-in) |
| Calls & Screen | `call.started` When I'm in a call (mic or camera starts being used — CapabilityAccessManager ConsentStore) · `call.ended` When a call ends · `screen.screenshotTaken` When I take a screenshot (Screenshots folder + Snipping Tool clipboard) |
| Web & Weather | `web.rssNew` New item in an RSS feed · `web.pageChanged` A web page changes (optional "contains text") · `weather.alert` Weather: rain/snow expected, temperature above/below N (Open-Meteo) |
| System | `system.signIn` When I sign in / Automation Portal starts · `system.wake` When PC wakes · `system.lock` When PC is locked · `system.unlock` When PC is unlocked · `system.idle` When I've been away N minutes · `system.back` When I come back |
| Apps | `app.opened` When an app opens · `app.closed` When an app closes · `app.focused` When an app comes to the front |
| Files & Drives | `file.changed` When files are added/changed/removed in a folder (kind/extension filter) · `drive.connected` When a USB drive is plugged in / removed |
| Network | `net.wifiJoined` When I join Wi-Fi (any / named) · `net.wifiLeft` When I leave Wi-Fi · `net.online` Internet comes back · `net.offline` Internet drops |
| Power | `power.pluggedIn` · `power.unplugged` · `power.battery` Battery drops below / rises above N% · `power.saver` Battery saver turns on/off · `power.plan` Power plan changes (optionally to one plan) |
| Devices | `device.bluetooth` Bluetooth device connects/disconnects · `device.usb` Any USB device connects/disconnects (pick from list) · `device.display` Monitor connected/disconnected · `device.audio` Sound output changes / headphones plugged in |
| Keyboard | `key.shortcut` Keyboard shortcut (optionally only when app X is in front) · `key.typed` When I type a phrase (option: replace the phrase with the shortcut's text output = **text expansion**) |
| Clipboard | `clip.changed` When I copy something (any / text / link / image / files / contains …) |

Conditions ("Only if…"): `cond.timeBetween` · `cond.days` · `cond.power` (on battery / plugged in) · `cond.battery` · `cond.powerPlan`
(below/above N%) · `cond.wifi` (connected to …) · `cond.appRunning` · `cond.appFront` ·
`cond.notFullscreen` (not in a full-screen app/game/presentation) · `cond.online` · `cond.inCall` (in a call / not in a call).

Quality rules: folder triggers ignore partial downloads (`.crdownload`, `.part`, `.tmp`, `.download`), wait until a
file's size is stable and it can be opened, and group bursts (debounce, default 3 s) into one run with the list
of files. Text expansion: Backspace right after an expansion undoes it; case follows what was typed unless
MatchCase; never expands in password fields (UI Automation IsPassword) or when Automation Portal is paused; supports
`{cursor}` and `{input:…}`. Macro recorder: record clicks relative to the target window and insert
`app.switch` steps so playback survives windows moving.

Automation options: Run immediately / Ask before running (toast with Run · Skip) · Notify when it runs ·
Don't run more than once every N minutes · global "Pause all automations".

C also owns: `HotkeyService` (RegisterHotKey on a message-only window thread; used by Quick Run, shortcut
keyboard shortcuts and `key.shortcut`; reports conflicts), `TypedPhraseService` (low-level keyboard hook,
used by `key.typed` + text expansion), `MacroRecorder` (records clicks/keys/typing/pauses → `input.*` and
`flow.wait` ActionInstances), `AutomationEngine` (loads automations, starts providers, checks conditions,
cooldown and pause, AskFirst via INotificationService, runs via IShortcutRunner with Source = Automation).

## 6. App UI (D — `AutomationPortal.App`)

Fluent, Windows 11 native: WPF-UI `FluentWindow` with Mica, light/dark/system theme, PerMonitorV2 DPI,
keyboard navigable, subtle animations.

**Palette** (shortcut tiles = vertical gradient top → bottom; action category icons use the bottom colour):
red `#FF5F57→#D92D20` · orange `#FFA94D→#E8590C` · yellow `#FFD43B→#F08C00` · green `#8CE99A→#2B8A3E` ·
mint `#63E6BE→#099268` · teal `#66D9E8→#0C8599` · cyan `#74C0FC→#1971C2` · blue `#5C9DFF→#2B5DD8` ·
indigo `#8C8CFF→#4C3FD1` · purple `#C08CFF→#8132D6` · pink `#FF8FC7→#D6336C` · brown `#D2A679→#8B5E34` ·
gray `#ADB5BD→#6C757D` · graphite `#6C757D→#343A40` · slate `#94A3B8→#475569`.
Category → colour key as listed in §4. Unknown icon names fall back to `Flash24`.

1. **Shell** — NavigationView: All Shortcuts · Pinned · folders (+ New Folder) · — · Automations · Gallery ·
   Run History; footer Settings. Search box in the title bar (shortcuts + gallery).
2. **Library** — responsive grid of tiles (~170×105, 12 px corners, vertical gradient of the shortcut colour,
   white icon top-left, name bottom-left, "⋯" top-right on hover). Click = run (spinner → ✓ / ✕ flash; failure
   shows message + "See details"). ⋯ / right-click: Edit, Duplicate, Rename, Change Icon, Pin, Move to Folder,
   Keyboard Shortcut, Add to Desktop, Export, Delete. Drag to reorder / onto folders. "+" tile = new shortcut.
   Friendly empty state with "Browse the Gallery".
3. **Editor** (own window per shortcut)
   - Header: icon chip (→ picker: 15 colours + searchable grid of ~150 curated icons), editable name,
     ▶ Run / ■ Stop, ⓘ Details, ⋯ (Export, Duplicate, Add to Desktop).
   - Action cards: category-coloured icon, **sentence-style summary with inline tokens** (literal values as
     accent-coloured text buttons, variables as coloured pills). Click a token → popup editor for that
     parameter type. "Show More ▾" for remaining parameters. Card menu: Duplicate, Disable, Rename output,
     On error ▸ Stop / Continue / Retry, Copy, Help, Delete.
   - **Token text box**: normal typing with inline variable pills; a Variables button, typing `{`, or
     right-click opens the **variable picker** (outputs of actions above with their icons, Shortcut Input,
     Clipboard, Current Date, Ask Each Time, Repeat Item/Index, Menu Result, named variables). Clicking a pill →
     choose property (Name, Size…), convert-as, or remove.
   - Control flow: Start cards normal; Otherwise / End If / menu items / End Repeat as slim bars; children
     indented with a vertical guide line.
   - Drag & drop from the action panel and to reorder (groups move together); hover "+" between cards;
     Ctrl+C/V/D, Delete, Ctrl+Z/Y (undo stack).
   - Right panel: search, **Suggestions** (based on previous output kind + recently used), Favourites (☆),
     categories with colour icons. Click = append, drag = insert. Hover = help (description, input, output).
   - **Run feedback**: current card highlighted + spinner; finished cards get ✓/✕ + time; failed card shows the
     error inline. Bottom drawer: final output (rich view) + **Variables inspector** (live values).
   - Inline validation banners on cards.
   - Details sheet: name, alias, description, keyboard shortcut recorder, Pin, Show in Quick Run, Show in
     "Send to" & right-click (+ accepted input: Files, Images, Text, Links, Folders), If there's no input (…),
     **Triggers** (this shortcut's automations: list + "Add trigger"), import questions, Add to Desktop /
     Start menu, Export.
   - Card menu also has **Run from here** (reuses earlier outputs from the last run).
   - New shortcut → "Start blank", **"Describe it"** (the default AI provider drafts the steps from a plain sentence; hidden with a link to Settings › AI when no AI is set up) or "Record a new shortcut". The draft opens unsaved with a "Drafted by AI — check each step before running" note (Undo / Keep), destructive steps marked, and "Add this automation?" buttons when the sentence says when. Editor ⋯ → "Describe a change" revises the steps as one undo step. AI-made shortcuts never run by themselves.
   - Toolbar **Record**: a small always-on-top bar (red dot, time, Pause, Stop, "Ignore waits", stop shortcut Ctrl+Alt+Esc) while the editor is minimised; the recorded steps are inserted at the selection as one undo step.
4. **Automations** — rows read as sentences ("When Spotify opens → Run *Focus Mode*") with toggle and last run.
   **New Automation** wizard: (1) pick trigger from category tabs as big cards; (2) set its options +
   optional "Only if…" conditions; (3) run an existing shortcut or create a new one; (4) Run immediately /
   Ask first, Notify, cooldown. "Pause all automations" at the top. Each automation has **Test now** (runs the
   shortcut with sample trigger input).
4b. **Snippets** page — simple list (abbreviation → text) with folders, search, enable toggles, placeholder
   buttons ({date}, {clipboard}, {cursor}, fill-in…), and a live preview.
5. **Gallery** — sections (Get Started, Files & Photos, Productivity, Device, AI, Automation Ideas); tile →
   details sheet (description, actions list, "Add Shortcut", "Add with Automation"); asks import questions.
6. **Run History** — time, shortcut, source icon, status, duration; details pane with each step, its output or
   error. Clear history.
7. **Quick Run** — global keyboard shortcut (default Win+Alt+Space): borderless centred search popup
   (PowerToys Run / Raycast style); fuzzy match on name + alias; Enter runs; Tab → "Run with input: <typed text>";
   Esc closes; recent first. Extras: inline calculator ("12*7.5") and conversions ("5 km in miles",
   "20 usd to eur"); fallbacks "Search the web for …" and "Ask AI …". A second shortcut (default Ctrl+Alt+R)
   grabs the **selected text** first and opens Quick Run with it as input ("Run on selection").
8. **Tray & Quick Panel** — tray left-click opens a flyout grid of pinned shortcut tiles (Stream Deck feel) +
   Open Automation Portal + Pause automations. Right-click menu: pinned shortcuts, Quick Run, Open, Pause, Exit.
   The same panel can open **at the mouse pointer** with a keyboard shortcut (setting). Closing the main window
   keeps Automation Portal running in the tray (setting).
9. **Settings** — General (start with Windows, keep running in tray, theme); Keyboard (Quick Run shortcut);
   Library (location, "Move to OneDrive" helper); Actions (show advanced actions; AI key (masked, DPAPI) and
   model: Fast / Smart / Best); Notifications (notify when keyboard-shortcut runs finish); Links (allow
   automationportal:// links); About (version, open data folder).
10. **Dialogs** implementing `IUserInteraction`: modern topmost windows centred on the active monitor, work
    even when the main window is hidden; screen-region and point selection overlays.

## 7. Run methods & Windows integration (E)

Single instance (`AutomationPortal.exe`) with a named pipe; second launches forward their arguments and exit.
Arguments: `--background`, `--run <id|name> [--input <text>] [--files <paths…>]`, `--import <file.autoportal>`,
`automationportal://run?name=…&input=…` (registered under HKCU; confirmation unless allowed in Settings).
`.autoportal` file association → import. Explorer: per-shortcut "Send to" entries + classic right-click
cascade "Automation Portal ▸ <shortcuts>" for files/folders (HKCU only). Desktop / Start-menu `.lnk` with a generated
`.ico` (glyph + colour). Taskbar **Jump List** of pinned shortcuts. Start with Windows via HKCU Run (`--background`).
CLI `ap.exe` (AssemblyName `ap` — `automationportal.exe` would clash with `AutomationPortal.exe` on a case-insensitive
file system): `ap list | run "Name" [--input …] [--file …] [--json]` — forwards to the running app via the
pipe, otherwise runs in-process with console prompts.

### 7.1 Microsoft Store target (decided 2026-10-02)

Automation Portal will ship in the Microsoft Store as an **MSIX** (full-trust desktop app, `runFullTrust`), while an
unpackaged build stays available for development. All integration code must work in **both** modes
(detect package identity at runtime):
- Startup → `windows.startupTask` manifest extension when packaged (HKCU Run key only unpackaged).
- `automationportal://` → `windows.protocol`; `.autoportal` → `windows.fileTypeAssociation`; `ap` CLI → `windows.appExecutionAlias`.
- Explorer right-click → packaged `IExplorerCommand` (Windows 11 modern menu) via `desktop4/desktop5` extensions; Send To shortcuts still work.
- Toasts use package identity when present.
- Registry/AppData write virtualization: actions that change real user settings (dark mode, wallpaper…) must
  not be virtualized → `desktop6:RegistryWriteVirtualization` / `FileSystemWriteVirtualization` disabled
  (restricted capability `unvirtualizedResources`, needs justification at submission).
- Package identity also unlocks the §9 "deferred to v2" items (Share target, notification trigger, etc.).
- Store needs: Partner Center account, reserved app name (check "Automation Portal" is available early), privacy policy
  URL (app uses internet, location, microphone, clipboard, keyboard hooks), age rating, screenshots, and clear
  justification for keyboard hooks (text expansion/recorder) and restricted capabilities.
- Packaging via Windows SDK `makeappx`/`signtool` (no Visual Studio needed); scripts in `scripts\`.
- **Reserved in Partner Center (2026-10-02):** Store ID `9PB9C8FHG105`; name "Automation Portal" (display
  name — use it in the UI and manifest), package family name `34458MindtheApp.AutomationPortal_24e29cj9741rt`. Package identity name/publisher come from Partner Center › Product identity at packaging time.
  Privacy policy source: `docs/store/privacy-policy.md`, published at
  https://mindthaapp.github.io/flow-mate/privacy.html (repo MindThaApp/flow-mate — update both together).

## 8. Gallery starters (E)

Good Morning (weather + speak + open apps) · Organize Downloads · Resize Images · Images to PDF · Combine PDFs ·
Grab Text from Screen → clipboard · Copy as Plain Text · Quick Note · Focus Mode (mute, quit distractions,
25-min timer) · Meeting Mode · Battery Saver (automation idea) · Clipboard → QR Code · Rename Photos by Date ·
Clean Link · Weather Now · Translate Clipboard (AI) · Summarize Web Page (AI) · Daily Bing Wallpaper ·
Zip & Email · Headphones → volume 40% (automation idea) · Dark Mode at Sunset (automation idea) ·
Keep PC Awake 1 h · Unit Converter · Password → Clipboard · Text expansion ";date" / ";addr" · What's Playing ·
Pomodoro · Open Work Apps · Tidy Desktop · Remove Photo Location Before Sharing.

## 9. Out of scope / Windows equivalents

Siri → Quick Run, keyboard shortcuts, typed phrases (voice later). iCloud sync → library folder in OneDrive.
HomeKit, Health, CarPlay, NFC, SMS/iMessage, third-party App Intents, Focus modes (we open the setting) —
not available on Windows. No enterprise/IT features.

Deferred to v2 — need Windows package identity (sparse package / MSIX): appearing in the Windows Share sheet
as a target, "when a notification arrives" trigger, Copilot-key launch, on-device Windows AI, Click to Do
integration, Windows AI imaging (remove background). Also deferred: video/audio compression (needs ffmpeg),
Windows Do Not Disturb toggle (no public API), website blocking (needs a browser extension).
Research notes: `docs/research/feature-survey.md`.

## 10. Phases

1. **A** — scaffold the solution + all project skeletons (building), Core, Core tests, git init.
2. **Parallel** — B1, B2, B3, C, D (each in its own folders; App references Core only in this phase and uses
   design-time sample descriptors for triggers).
3. **E** — integration: wire projects, host services, run methods, CLI, gallery, apply `docs/core-requests`,
   publish to `builds\`. Then **F** — "Describe it" AI shortcut builder + "describe a change", macro-recorder UI,
   and the **Windows AI (on-device)** provider: Windows App SDK `Microsoft.Windows.AI` (Phi Silica text generation,
   summarize/rewrite; plus Windows AI imaging — describe image, remove background, better OCR) registered as an
   extra IAiProvider; only when packaged (MSIX) on a Copilot+ PC, otherwise shown greyed with the reason.
   Free, private, offline — make it the suggested default when available.
4. Review & fix.

**F status (2026-10-02):** done: Describe it / Describe a change (ShortcutAiBuilder, catalog + gold examples + validator repair loop), recorder bar, Windows AI provider (src/AutomationPortal.Ai.Windows, see docs/store/packaging.md), polish. Not verified on hardware: a real on-device Phi Silica answer (no NPU on the dev PC).
