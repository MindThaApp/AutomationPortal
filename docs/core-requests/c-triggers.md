# Core requests from C (Flowmate.Triggers)

None of these block anything. Each has a local workaround that already ships.

## 1. Home location in `AppSettings`
**What:** `double? HomeLatitude`, `double? HomeLongitude`, `string? HomeCity` (Settings > General, "Where I am").
**Why:** Sunrise/sunset (`time.sun`) and weather alerts (`weather.alert`) need a location. Today Flowmate asks Windows location, then falls back to an
approximate position from the internet address, and caches the answer for 7 days in `%LOCALAPPDATA%\Flowmate\trigger-state\location.json`.
**Workaround in C:** if `%LOCALAPPDATA%\Flowmate\trigger-state\home-location.json` exists (`{"latitude":51.5,"longitude":-0.12,"city":"London"}`) it wins over everything.
Integration can write that file from a Settings field until the setting exists, or change `LocationProvider` to read the setting.

## 2. Multi-pick list parameters (`ParamType.List` with `Choices`)
**What:** The editor should render a `ParamType.List` descriptor that has `Choices` as a multi-select (checkbox list or chips), stored as `ParamValue.List("mon","tue",…)`.
**Why:** `time.daily` / `cond.days` ("On these days") and `file.changed` ("Files that are added / changed / removed") use it. `TriggerConfig.GetStrings` already reads it.

## 3. Reminder sound
**What:** `INotificationService.Show` has no way to ask for a silent toast, so `Reminder.Sound = false` is ignored.
**Suggested API:** `void Show(string title, string body, string? imagePath = null, Action? openOnClick = null, bool silent = false)`.

## 4. App pick-list ids
**What:** Please document what `AppValue.Id` holds for apps chosen in the App parameter picker (exe path? process name? `Package!App` id?).
**Why:** `app.opened`, `app.closed`, `app.focused`, `key.shortcut` ("only in app") and `cond.appRunning` / `cond.appFront` match a running process against it.
`AppMatcher` accepts an .exe path, a file/process name (with or without `.exe`), the Start-menu display name, and Store ids (`Family!App`), so any of those works today.
The macro recorder writes `AppValue.Id` = full .exe path and `Name` = the app's display name into `app.switch` and `input.click`.

## 5. Optional: trigger sink extras
**What:** `ITriggerSink` could carry `ReportProblem(string? message)` (shown on the automation row) and `Task<ContentValue?> FireAndWaitAsync(...)` (text expansion with a shortcut).
**Why:** C defines these as an internal `ITriggerSinkExtras` implemented by the engine's sinks. Third-party providers can't use them without a Core change. Not needed for v1.

## 6. DPI awareness (note for D/E)
The macro recorder stores physical screen pixels. `Flowmate.exe` must stay PerMonitorV2 aware (as the spec says) or click coordinates will be scaled.

## Status (E1)
1. DONE. `AppSettings.HomeLatitude/HomeLongitude/HomeCity` added; Settings has a 'Where I am' card; `LocationProvider` reads the setting first (the home-location.json file still works as a fallback).
2. DONE. `ParamType.List` with `Choices` renders as tick boxes in the editor's ParameterForm (`MultiChoiceEditor`); summaries show the chosen labels.
3. DONE. `INotificationService.Show(..., bool silent = false)`; `ReminderService` passes `!Reminder.Sound`; the toast service adds a silent audio element.
4. DONE. `AppValue.Id` is documented on the type: the .exe path or the AUMID (Family!App). The picker, `AppLauncher` (matches id or path), Triggers' `AppMatcher` and the recorder all accept both.
5. DECLINED for v1. `ITriggerSinkExtras` stays internal to Flowmate.Triggers; there are no third-party trigger providers yet, so a public contract would only freeze an API nobody uses.
6. NOTED. Flowmate.exe keeps PerMonitorV2 (app.manifest).
