# Spec gaps (docs/SPEC.md against the code)

Checked on 2026-11-28 against branch `feat/spec-audit` (base `e8fc78b`). Evidence is a file (with line where useful) or a
test file. "Built" means the code and a test or caller exist; it does not mean it was checked by hand in the app.

## Summary

| Status | Items |
|---|---|
| Built | 112 |
| Partly built | 7 |
| Not built | 8 |
| **Total** | **127** |

Every action, trigger and condition id named in SPEC §4 and §5 exists in the code (256 ids found, none missing).
All 30 gallery starters in §8 exist. The gaps are almost all about the Microsoft Store package, the
"deferred to v2" list, and three queued items.

## Gaps, most useful first

| # | Gap | Status | Evidence | Plan | Size |
|---|---|---|---|---|---|
| 1 | Explorer right-click menu in the Store package | Not built | `App/Integration/WindowsShellIntegration.cs:350` (cascade only when not packaged); no `IExplorerCommand` or `desktop4/desktop5` entry in `packaging/AppxManifest.store.template.xml` | Add a small COM `IExplorerCommand` server (submenu of the shortcuts that show in Send to), register it with `desktop4:FileExplorerContextMenus` + `com:ComServer` in both manifests | L |
| 2 | Send to and Start menu links in the Store package | Partly | `WindowsShellIntegration.cs:325-330` skips them when `PackageCapabilities.UnvirtualizedWritesAllowed` is false; Store manifest has no `unvirtualizedResources` | Either file the reconsideration request (checklist in `docs/store/packaging.md:180-184`) and ship the "full" manifest, or hide the toggles in Details for the Store build so nothing promises them | M |
| 3 | Set Light/Dark Mode in the Store package | Partly | `Actions.Windows/Devices/SystemActions.cs:31` refuses when writes are virtualized | Same capability decision as #2; until then keep the Store note and offer "Open Settings page" as the fallback step | M |
| 4 | Pipe `run` reply: structured `files:[{path, kind}]` for AI Portal | Not built | `Core/Integration/ShortcutCommands.cs:368-372` returns `shortcut, id, status, output, error, durationMs, runId` only; branch `feat/run-files` has no commits | Collect File / Folder / Image items from `RunResult.Output`, add `files` (path + kind) as an additive contract-1 field, document it in `docs/CLI.md`, test in `PipeContractTests` | M |
| 5 | "Remove all Automation Portal links and data" action | Not built | `docs/store/packaging.md:184`; only `--unregister` exists for the unpackaged build (`App/AppHost.Launch.cs:545`) | Settings > About action: remove only files recorded in `shell-state.json` and the app's own data folders, show the list first, confirm. **Deletes user data: needs an independent review before any build** | M |
| 6 | English words passed into translated messages | Partly | `Actions.Documents/Files/Transfer.cs:60,168-169` ("move"/"copy"); `ProtectedPaths.Ensure(path, "move")` in `FileUndo.cs:117-118` and `Transfer.cs:67`; `MailTo.ParseRecipients` default `"recipient"` (`Mail/MailActions.cs:10`; callers already pass resource nouns). BulkRenamer "control character" is fixed (`BulkRename.cs:187` uses `BulkRenamer_Noun_ControlCharacter`) | Use one resource string per verb (or whole sentences per verb) instead of a verb argument; make the MailTo default a resource noun; add a test that no `Strings.Format` argument is an English literal | S |
| 7 | Show in the Windows Share sheet as a target | Not built | no `windows.shareTarget` in either manifest | Add the share target extension, take the shared files/text/link as Shortcut Input, ask which shortcut to run | M |
| 8 | On-device Windows AI imaging (describe image, remove background, better OCR) | Not built | `Ai.Windows/PhiSilicaBackend.cs` covers text only; no `ImageDescription` or background removal anywhere | Add the Windows AI imaging APIs behind the same "only when packaged on a Copilot+ PC" check; new `img.removeBackground` action | L |
| 9 | Copilot-key launch | Not built | nothing in manifests or `LaunchArguments.cs` | Make the packaged app launchable from the Copilot key setting; on that launch open Quick Run | S |
| 10 | Click to Do integration | Not built | nothing in manifests or code | Register shortcuts that accept text or images as App Actions so Click to Do can offer them | M |
| 11 | Spec text no longer matches the code | Partly | §0 still says `C:\AI\Projects\Flowmate\`; §1 says Windows 10 19041 but the package needs 22000 (`docs/store/packaging.md:114`); §3.9 paths differ in the Store package (`Core/Storage/AutomationPortalPaths.cs:49`); §7.1 names `desktop6` but packaging uses flexible virtualization | Update SPEC.md §0, §1, §3.9 and §7.1 | S |
| 12 | Phi Silica answer not checked on real hardware | Partly | SPEC §10 F status; `tests/AutomationPortal.Ai.Windows.Tests/WindowsAiProviderTests.cs` uses fakes | One run on a Copilot+ PC with the packaged build | S |
| 13 | Video/audio compression, Do Not Disturb toggle, website blocking | Not built | deferred by SPEC §9 (needs ffmpeg / no public API / browser extension) | Leave deferred | - |

Not in the spec but found while checking: string resources are still missing in `Actions.Content` (in flight on
`loc/content`, `e0c02fd`), `Triggers` (52 inline `Title = "…"`), `Host.Wpf` and `Cli`; `Actions.Documents`,
`Actions.Windows` and `Ai.Windows` have English `Strings.resx` only. Some App/Editor text is still inline, for example
`Shell/QuickRunWindow.xaml.cs:177` ("Run with input: "), `Pages/GalleryPage.xaml.cs:255,309` ("Close", "Cancel",
"Set up "), `Editor/DetailsSheet.cs:98` (input kind names).

## Full checklist

### §1 Tech stack

| Item | Status | Evidence |
|---|---|---|
| Shared engine never uses WPF or Windows Forms | Built | `tests/AutomationPortal.Hosting.Tests/ArchitectureTests.cs` |
| Image backend behind `IImageBackend` (WinRT default, WPF in hosts) | Built | `ImageBackendParityTests.cs` |
| Icons stored as SymbolRegular names, fallback `Flash24` | Built | `XamlSymbolNameTests.cs`, `IconTests.cs` |

### §3 Core

| Item | Status | Evidence |
|---|---|---|
| Shortcut model and folders | Built | `Core/Model/Shortcut.cs` |
| Flat action list with control-flow groups | Built | `Core/Engine/FlowTree.cs:6-15` |
| Parameter values and VarRef kinds | Built | `Core/Values/VarRef.cs:51` |
| Named, magic, repeat and menu variables; saved values | Built | `Core/Actions/VariableAndUiActions.cs:34`, `Core.Tests/EngineTests.cs` |
| Content kinds, properties and forgiving conversion | Built | `Core.Tests/ModelAndContentTests.cs` |
| Action SDK (summary tokens, Show More, advanced, destructive) | Built | `Actions.Content.Tests/DescriptorTests.cs`, `Actions.Windows.Tests/DescriptorTests.cs` |
| If operators by type, match all/any | Built | `Core/Values/ConditionOperators.cs:13-32` |
| Repeat, Repeat with Each, Choose from Menu, Stop, Output, Wait, Run Shortcut | Built | `Core/Actions/FlowActions.cs`, `Core.Tests/EngineTests.cs` |
| Per-action On error (Stop / Continue / Retry) | Built | `Core.Tests/RunnerSafetyTests.cs` |
| Safety: depth 16, 8 runs, 100,000 loop cap | Built | `Core/Engine/ShortcutRunner.cs:8-10` |
| Validator issues | Built | `Core.Tests/StorageAndValidatorTests.cs` |
| Run from this step (`StartAtUid`, `SeedOutputs`) | Built | `Core/Engine/RunTypes.cs`, `Editor/Logic/RunController.cs` |
| Host service interfaces | Built | `Core/Services/HostServices.cs`, `Hosting.Tests/HeadlessHostTests.cs` |
| Library folder, configurable, external-change reload | Built | `Core/Storage/JsonShortcutLibrary.cs`, `App.Tests/LibraryLocationTests.cs` |
| Recently deleted (copy, verify, `.info`, 30 days) | Built | `Core/Storage/RecentlyDeleted.cs`, `Core.Tests/RecentlyDeletedTests.cs` |
| Back up library now (new zip, never overwrites) | Built | `Core/Storage/LibraryBackup.cs` |
| Run history keeps 1,000 runs | Built | `Core/Storage/Stores.cs:188` |
| Atomic writes | Built | `Core.Tests/StorageSafetyTests.cs` |
| `.autoportal` export and import (new id, rename on clash, warnings) | Built | `Core.Tests/ImportRiskTests.cs`, `Actions.Windows.Tests/ImportRiskTests.cs` |
| Storage paths as written in §3.9 | Partly | Store package uses package folders, `Core/Storage/AutomationPortalPaths.cs:49` (gap 11) |
| Automations model, trigger and condition SDK | Built | `Core/Sdk/TriggerSdk.cs`, `Triggers.Tests/AutomationEngineTests.cs` |
| Snippets and placeholders | Built | `Triggers/Typing/SnippetExpansionService.cs`, `Triggers.Tests/TypingTests.cs` |

### §4 Action catalog (every id present)

| Category | Status | Evidence |
|---|---|---|
| A: Core built-ins (flow, variables, content, Show & Ask) | Built | `Core/Engine/FlowTree.cs`, `Core/Actions/VariableAndUiActions.cs:34,174,213` |
| Text (14) | Built | `Actions.Content/Actions/TextActions.cs:130`, `PasswordAndQrActions.cs:74`, `TextTests.cs`, `QrTests.cs` |
| Numbers (8) | Built | `NumberActions.cs:11`, `NumberTests.cs` |
| Dates (6) | Built | `DateActions.cs:113`, `DateTests.cs` |
| Lists & Dictionaries | Built | `ListActions.cs:189,325`, `ListAndDictionaryTests.cs` |
| Web (13 actions) | Built | `WebActions.cs:527`, `WebTests.cs` |
| Weather & Location (web) | Built | `WeatherActions.cs:41,106`, `WeatherTests.cs` |
| Spreadsheets | Built | `SpreadsheetActions.cs:61`, `SpreadsheetTests.cs` |
| Device & Settings (incl. power plans) | Built | `Actions.Windows/Devices/AudioActions.cs`, `DisplayActions.cs`, `PowerActions.cs`, `PowerPlanActions.cs:49` |
| Set Light/Dark Mode in the Store package | Partly | `SystemActions.cs:31` (gap 3) |
| Power Plan Manager actions (`ppm.*`) | Built | `PowerPlanActions.cs:189-287`, `PowerPlanManagerStatus.cs:201`, `PowerPlanTests.cs` |
| Apps & Windows (incl. Wait Until, Files Portal) | Built | `AppActions.cs:157`, `WindowActions.cs:53`, `WaitUntilAction.cs:70`, `WaitUntilTests.cs`, `FilesPortalLinkTests.cs` |
| Desk Pal actions (`deskpal.*`) | Built | `Apps/DeskPalActions.cs:10-91`, `DeskPalTests.cs` |
| Run Command (Advanced) | Built | `Advanced/RunCommandAction.cs:9`, `RunCommandInjectionTests.cs` |
| Keyboard & Mouse | Built | `InputActions.cs:115` |
| Clipboard | Built | `ClipboardActions.cs:79` |
| Screen (capture, OCR, grab text, pick colour) | Built | `ScreenActions.cs:11` |
| Sound & Media (incl. speak, dictate, record) | Built | `MediaActions.cs:131`, `SpeechActions.cs:84` |
| Location | Built | `LocationActions.cs:9` |
| Files & Folders (incl. rename by pattern, compare, hash) | Built | `ChangeActions.cs`, `FindActions.cs:138`, `BulkRename.cs:201`, `CompareHash.cs:126`, `FileTests.cs`, `BulkRenameTests.cs` |
| Images (incl. Compress Media) | Built | `ImageActions.cs:384`, `CompressImagesAction.cs:7`, `ImageTests.cs`, `CompressMediaTests.cs` |
| PDF | Built | `PdfActions.cs:243`, `DocumentTests.cs` |
| Notes & Reminders, calendar | Built | `NoteActions.cs:90`, `ReminderActions.cs:19`, `CalendarActions.cs:9` |
| Email & Sharing | Built | `MailActions.cs:88`, `ShareActions.cs:78` |
| AI providers (Claude, OpenAI, Gemini, DeepSeek, Kimi, Azure, Ollama, LM Studio, custom) | Built | `Actions.Documents/Ai/AiProviders.cs:8-9`, `AiTests.cs` |
| AI actions (ask, summarize, rewrite, translate, extract, describe image) | Built | `AiActions.cs:63,223`, `AiAnswerTypesTests.cs` |

### §5 Triggers and conditions (every id present)

| Item | Status | Evidence |
|---|---|---|
| Time (daily, monthly, interval, once, sunrise/sunset, calendar .ics) | Built | `Triggers/Providers/TimeTriggers.cs:76,200`, `ScheduleTests.cs` |
| Calls & Screen | Built | `CallAndScreenTriggers.cs:55,70` |
| Web & Weather | Built | `WebTriggers.cs:70,141`, `Triggers.Tests/WebTests.cs` |
| System (sign in, wake, lock, unlock, idle, back) | Built | `SystemAppTriggers.cs:146` |
| Apps (opened, closed, focused, app event) | Built | `SystemAppTriggers.cs:232`, `AppEventTriggers.cs:148`, `AppEventTriggerTests.cs` |
| Files & Drives | Built | `FileNetworkPowerTriggers.cs:9,179`, `FileTriggerTests.cs` |
| Network | Built | `FileNetworkPowerTriggers.cs:296` |
| Power (incl. power plan changes) | Built | `FileNetworkPowerTriggers.cs:395`, `PowerPlanTriggerTests.cs` |
| Devices | Built | `DeviceKeyboardClipboardTriggers.cs:100` |
| Keyboard (shortcut, typed phrase) | Built | `DeviceKeyboardClipboardTriggers.cs:134`, `HotkeyTests.cs` |
| Clipboard | Built | `DeviceKeyboardClipboardTriggers.cs:211` |
| Notification arrives (Store) | Built | `NotificationTriggers.cs:131`, `NotificationTriggerTests.cs` |
| Conditions (11) | Built | `Conditions.cs:175` |
| Folder triggers skip partial downloads, wait, debounce | Built | `Triggers/Files/FolderChangeBatcher.cs:7`, `FolderLoopTests.cs` |
| Text expansion: undo, case, password fields, paused | Built | `Typing/TypedPhraseService.cs:260,334`, `TypingTests.cs` |
| Macro recorder: window-relative clicks, `app.switch` steps | Built | `Recorder/RecorderStepBuilder.cs:144`, `RecorderTests.cs` |
| Automation options (ask first, notify, cooldown, pause all) | Built | `Automations/AutomationEngine.cs`, `AutomationEngineTests.cs` |
| Hotkey service reports conflicts | Built | `Hotkeys/ShortcutHotkeyBinder.cs:26` |

### §6 App UI

| Item | Status | Evidence |
|---|---|---|
| Fluent look, theme, design tokens | Built | `XamlDesignTokenTests.cs` |
| Palette gradients | Built | `PaletteCacheTests.cs`, `TileAppearanceTests.cs` |
| Shell navigation and search | Built | `App/MainWindow.xaml.cs`, `PaletteSearchTests.cs` |
| Library tiles, menu, drag, empty state | Built | `Pages/LibraryPage.xaml.cs`, `Pages/TileFileDrop.cs`, `NeutralTileTests.cs` |
| Editor header and icon picker | Built | `Editor/IconNames.cs` |
| Sentence cards with inline tokens, Show More | Built | `Editor/UI/ActionRowView.cs` |
| Token text box and variable picker (`{`, right-click) | Built | `Editor/UI/TokenTextBox.cs:11,165` |
| Control-flow display | Built | `Editor.Tests/ActionOrderTests.cs` |
| Drag from panel, reorder, hover "+" between cards | Built | `ActionPanelView.xaml.cs:65`, `ShortcutEditorWindow.xaml.cs:475`, `UI/QuickInsert.cs:10` |
| Copy/paste/duplicate/delete, undo and redo | Built | `Editor/Logic/EditorDocument.cs`, `Editor.Tests/DocumentTests.cs` |
| Right panel: search, suggestions, favourites | Built | `Editor/ViewModels/ActionPanelViewModel.cs` |
| Run feedback and Variables inspector | Built | `Editor/UI/ResultsDrawer.cs:14,88`, `RunFeedbackTests.cs` |
| Inline validation | Built | `Editor.Tests/LogicTests.cs` |
| Details sheet (alias, hotkey, Send to, input, triggers, links) | Built | `Editor/DetailsSheet.cs:98,151-153` |
| Run from here | Built | `Editor/UI/ActionRowView.cs` |
| Describe it / Describe a change | Built | `Editor/ShortcutEditorWindow.Ai.cs`, `ShortcutAiTests.cs`, `AiAndRecordingTests.cs` |
| Record bar | Built | `App/Shell/RecordingBarWindow.cs`, `RecordingTests.cs` |
| Automations page, wizard, Test now, Pause all | Built | `Pages/AutomationsPage.xaml.cs`, `TestNowPromptTests.cs` |
| Snippets page | Built | `Pages/SnippetsPage.xaml` |
| Gallery sections, details, Add with automation, import questions | Built | `Pages/GalleryPage.xaml.cs:256,266` |
| Run History with details and Clear | Built | `Pages/RunHistoryPage.xaml.cs`, `RunHistoryActionsTests.cs` |
| Quick Run (fuzzy, Tab = run with input, calculator, conversions, web/AI fallbacks) | Built | `Shell/QuickRunWindow.xaml.cs:177,232`, `Services/QuickRunExtras.cs`, `QuickRunFallbacksTests.cs` |
| Run on selection shortcut | Built | `Shell/QuickRunHints.cs`, `App/AppHost.cs` |
| Tray, Quick Panel, panel at the pointer | Built | `Shell/QuickPanelWindow.xaml.cs`, `Core/Services/AppSettings.cs:79`, `TrayMenuTests.cs` |
| Settings: general, keyboard, library + OneDrive helper | Built | `Pages/SettingsPage.xaml.cs`, `AppSettings.cs:14,23` |
| Settings: crash reports (opt-in, redacted) | Built | `App/Startup/CrashReporting.cs`, `CrashReportsSettingTests.cs`, `CrashReportRedactorTests.cs` |
| Settings: AI model Fast / Smart / Best | Built | `SettingsPage.xaml.cs:145-147` |
| Settings: notify after keyboard-shortcut runs | Built | `AppSettings.cs:94` |
| Settings: link policy and per-shortcut setting | Built | `Core/Integration/LinkPolicy.cs`, `DetailsSheet.cs:151-153`, `LinkSafetyTests.cs` |
| Dialogs and screen selection overlays | Built | `App/Dialogs/ScreenSelection.cs` |

### §7 Run methods and Windows integration

| Item | Status | Evidence |
|---|---|---|
| Single instance with named pipe | Built | `App/Integration/InstanceCoordinator.cs`, `PipeContractTests.cs` |
| Arguments (`--background`, `--run`, `--import`) | Built | `Core/Integration/LaunchArguments.cs` |
| `automationportal://` links with confirmation | Built | `App/AppHost.Launch.cs`, `LinkSafetyTests.cs` |
| `.autoportal` file association | Built | `AppxManifest.store.template.xml:85` |
| Send to and classic right-click cascade (unpackaged) | Built | `WindowsShellIntegration.cs:386-424`, `ShellIntegrationTests.cs` |
| Desktop / Start menu links with generated icon | Built | `WindowsShellIntegration.cs:343-347`, `AppIconFileTests.cs` |
| Jump List of pinned shortcuts | Built | `App/Integration/JumpListApplier.cs`, `WindowsShellIntegration.cs:356` |
| Start with Windows (Run key / startupTask) | Built | `AppxManifest.store.template.xml:73` |
| CLI `ap` (list, run, `--json`, in-process fallback) | Built | `docs/CLI.md`, `Cli.Tests/*` |
| Package: protocol, file type, `ap` alias | Built | `AppxManifest.store.template.xml:79-96` |
| Package: Explorer right-click (`IExplorerCommand`) | Not built | gap 1 |
| Package: Send to still works | Partly | gap 2 |
| Package: real settings writes (`unvirtualizedResources`) | Partly | only `AppxManifest.full.template.xml:143`; Store template drops it (gaps 2-3) |
| Store identity and privacy policy | Built | `packaging/identity.json`, `docs/store/` |

### §8 Gallery starters

| Item | Status | Evidence |
|---|---|---|
| All 30 starters in §8 | Built | `gallery/*.autoportal` (48 files incl. extras) |

### §9 Deferred to v2

| Item | Status | Evidence |
|---|---|---|
| Notification trigger | Built | `NotificationTriggers.cs:131` |
| On-device Windows AI (text) | Built | `Ai.Windows/PhiSilicaBackend.cs`, `WindowsAiProviderTests.cs` |
| Share sheet target | Not built | gap 7 |
| Copilot-key launch | Not built | gap 9 |
| Click to Do | Not built | gap 10 |
| Windows AI imaging (remove background, describe image, OCR) | Not built | gap 8 |
| Video/audio compression, Do Not Disturb, website blocking | Not built | deferred by design (gap 13) |

### §10 Phase F

| Item | Status | Evidence |
|---|---|---|
| Describe it, Describe a change, recorder bar, Windows AI provider | Built | see §6 rows |
| Phi Silica checked on real hardware | Partly | gap 12 |

### Queued items

| Item | Status | Evidence |
|---|---|---|
| Remove links and data action (needs independent review before any build) | Not built | gap 5 |
| `files:[{path, kind}]` in the pipe `run` reply | Not built | gap 4 |
| English words as format arguments in translated messages | Partly | gap 6 |
| Spec text matches the code | Partly | gap 11 |
