# Core requests from D1 (App shell)

## 1. AppSettings: hotkeys for "Run on selection" and "Quick Panel at pointer"
**What:** add `string RunOnSelectionHotkey` (default `"Ctrl+Alt+R"`) and `string QuickPanelHotkey` (default `"Ctrl+Alt+P"`) to `AppSettings`.
**Why:** SPEC section 6.7 and 6.8 describe both shortcuts as settings, but `AppSettings` only has `QuickRunHotkey`.
**Workaround now:** the App stores them in `app-ui.json` next to `settings.json` (`LocalUiSettings`). Integration binds the hotkeys from there. When Core gets the fields, delete `LocalUiSettings` and read them from `ISettingsStore`.

## 2. IShortcutLibrary: save many shortcuts in one change
**What:** `void SaveMany(IEnumerable<Shortcut> shortcuts)` that writes all files and raises `Changed` once.
**Why:** drag-to-reorder rewrites `SortOrder` for every shortcut that moved, so the grid reloads once per saved shortcut.
**Workaround now:** call `Save` in a loop (the page debounces reloads).

## 3. Automation.LastRun is written by the engine (note, no API change)
The Automations page shows "Ran today at ..." from `Automation.LastRun`, falling back to the newest Automation run in `IRunHistory`. Triggers/engine should set `LastRun` and call `SaveAutomation` after each run.

## Status (E1)
1. DONE. `AppSettings.RunOnSelectionHotkey` and `QuickPanelHotkey` added; `LocalUiSettings.MigrateHotkeys` moves the old app-ui.json values once at start and clears them.
2. DONE. `IShortcutLibrary.SaveMany` (one `Changed` event). Note for D: the library page can now switch its reorder loop to it.
3. DONE. The engine already sets `Automation.LastRun` (`AutomationEngine.RecordRun`); the Automations page also refreshes on `AutomationRan` and shows `GetStatus` errors.
