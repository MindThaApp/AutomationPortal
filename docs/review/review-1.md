# Flowmate code review 1 (read-only)

Date: 2026-10-02. Scope: Core engine and storage, Documents file actions, Windows power actions, Triggers (hooks, hotkeys, engine), plus the App and Editor threading and data paths.
No source files were changed. Every finding was checked against the code twice. Line numbers are from the tree as it was read. App files are being edited at the same time, so lines in `Flowmate.App` may have moved.

Counts: **Critical 1 · High 4 · Medium 8 · Low 9**

## Fix status (E3)

| # | Status | Notes |
|---|---|---|
| C1 | Fixed (c6a0737) | Refuses src inside target; old item parked aside, recycled only after the new one is in place, restored on failure. Test: TransferSafetyTests |
| H1 | Fixed (b203d08) | Unreadable != empty: retry with back-off, keep previous in-memory data, refuse writes to unreadable files, `.bak` before overwrite, corrupt file quarantined at startup. Tests: StorageSafetyTests |
| H2 | Fixed (b203d08, e638df7) | Library reload, ReminderStore.Tick and every Triggers timer callback guarded (TriggerLog.Guard / StoreDiagnostics). Surfacing in UI: see app-followups.md |
| H3 | Fixed in Core/Triggers (b203d08, e638df7); UI part Deferred to App | Import strips Hotkey/Alias/SendTo (Pending* on ImportResult), NeedsConfirmation; Ctrl+C/V/X/Z/Y/A/S/P blocked as global hotkeys. Confirmation dialog: app-followups.md |
| H4 | Fixed (0cea8e6) | Nested runs share the parent temp folder; Run-from-here keeps temp (RunOptions.KeepTemp). Test: RunnerSafetyTests |
| M1 | Fixed (d841f3f) | WH_MOUSE_LL + EVENT_OBJECT_FOCUS reset buffer and undo. Caveat: a paste into an app that ignores Ctrl+V still arms Backspace-undo for 5 s |
| M2 | Fixed (c6a0737) | SHFileOperation with FOF_ALLOWUNDO + FOF_WANTNUKEWARNING: the shell asks before permanent delete |
| M3 | Fixed (b203d08) | Settings, secrets, saved values, reminders: same missing/unreadable/corrupt handling |
| M4 | Fixed (b203d08) | Revision counter discards a racing reload; self-write detection by content hash. Not done: moving LastRun out of automations.json (a failed write is now harmless and logged) |
| M5 | Fixed (de0c022) | Editor merges folder/order from the latest library copy on save; Same/ReloadFrom compare them |
| M6 | Fixed (b9bdb3e) | PowerShell: variables escaped for their quote context; cmd: env vars + delayed expansion. Tests: RunCommandInjectionTests |
| M7 | Fixed (d841f3f) | Keys in password boxes are never stored; recorded as Ask for Input + Type Text. UIA timeout counts as password |
| M8 | Fixed (c6a0737) | ActionDescriptor.DestructiveWhenChoice + IsDestructiveFor, used by Inspect and the editor badge |
| L1 | Fixed (0cea8e6) | ActionDescriptor.SensitiveOutput (password, ask, ask-many, get clipboard) shows "(hidden)" in history |
| L2 | Fixed in Core (0cea8e6); startup call Deferred to App | TempSweeper.Sweep(paths) |
| L3 | Deferred to App | SettingsPage is E2's file: see app-followups.md |
| L4 | Fixed (0cea8e6) | 60 s bounded wait, then "Too many shortcuts" failure (callers already notify on failure) |
| L5 | Fixed (de0c022) | Close is cancelled (with a choice) when the final save fails |
| L6 | Deferred to App | ShortcutRunService is in App: see app-followups.md |
| L7 | Fixed (b203d08) | Duplicates reported in LoadErrors, deleted shortcut's copies set aside as .dup |
| L8 | Fixed (d841f3f) | Per-thread HookRunner with its own thread id and handles |
| L9 | Fixed (de0c022) | RunOptions.SeedVariables, per-step snapshots in RunController |
---

## Critical

### C1. "Move/Copy Files" with **Replace** can permanently delete the source folder (and replaced folders are never recoverable)
`src/Flowmate.Actions.Documents/Files/Transfer.cs:51-77` (delete at `:73-75`, guard at `:55`)

- **Scenario:** A user moves `C:\X\Proj\Proj` to `C:\X` with "If a file with that name is already there" = *Replace it*. This nested same-name folder is very common after Extract Zip. In that case `target` = `C:\X\Proj`, which already exists and is the **parent of the source**. The guard `IsInside(destDir, src)` only checks for the opposite nesting. The Replace branch runs `Directory.Delete(target, true)`, which permanently deletes `C:\X\Proj` and the source inside it, with no Recycle Bin. `Directory.Move` then throws `DirectoryNotFoundException`. The user sees "Couldn't move 'Proj'. It may be open in another program", but the folder tree is already gone. The same happens for a file `C:\A\Report\Report` moved to `C:\A`.
- **More generally:** in Replace mode, any existing destination folder is hard-deleted *before* the move or copy. If the move then fails (a locked file in the source, a cross-volume copy error), the replaced folder is lost.
- **Fix:**
  - Refuse when `src` is inside `target` (`IsInside(src, target)`).
  - Move the existing target aside: rename it to a temp name in the same folder, or send it to the Recycle Bin with `FileSystem.DeleteDirectory(..., SendToRecycleBin)`.
  - Do the transfer, and delete the aside copy only after it succeeds. Roll back on failure.
  - Consider marking Move Files `IsDestructive` when `ifExists=replace`.

---

## High

### H1. Library reload treats an unreadable `automations.json` / `snippets.json` / `folders.json` as empty, and the next save wipes the user's data
`src/Flowmate.Core/Storage/JsonShortcutLibrary.cs:40-56`, persisted at `:71-73`

- **Scenario:** The library sits in OneDrive (a supported setup). OneDrive or an antivirus holds `automations.json` open while the watcher-triggered `Load()` runs. `ReadList` catches the `IOException` and returns `new()`, so `AutomationList` becomes empty. The AutomationEngine reloads and stops every trigger. The next `SaveAutomation` (the user creates or toggles one, or `RecordRun` after any fire) writes a one-item list over the real file, and all other automations are permanently lost.
- The same happens to snippets and folders. Shortcuts whose individual file is locked silently drop out of the map, losing their hotkeys until the next reload.
- It also happens at startup with a corrupt file. The class comment says "corrupt files are skipped, never deleted", but the first save overwrites them.
- **Fix:**
  - On a read error, keep the previous in-memory list (on reload) and mark that list "unsafe to persist".
  - Retry with a short back-off on sharing violations.
  - Never write a list file whose last load failed. Copy the bad file to `*.corrupt-<date>.json` before any overwrite.

### H2. Exceptions on timer threads crash the tray app
- `src/Flowmate.Core/Storage/JsonShortcutLibrary.cs:88`: the debounce timer calls `Load()` with no try/catch. Inside `Load`, `Directory.EnumerateFiles` (`:31`) sits outside the per-file try, and the per-file catch filter (`:38`) does not include `UnauthorizedAccessException`.
- `src/Flowmate.Core/Storage/Stores.cs:256-264`: `ReminderStore.Tick` calls `Save()` (AtomicFile) on a timer thread with no catch.
- **Scenario:** The user deletes or renames the library's `shortcuts` folder, OneDrive unlinks it, or one shortcut file gets a deny ACL. The watcher fires and `Load()` throws `DirectoryNotFoundException` or `UnauthorizedAccessException` on a ThreadPool timer callback, which terminates the process. `App.xaml.cs:15` only logs `AppDomain.UnhandledException`, so all automations, hotkeys and text expansion stop silently. A reminder coming due while `reminders.json` is briefly locked does the same.
  - `ReminderStore` has a second problem: `Arm()` is never reached, so later reminders also stop.
- **Fix:** Wrap every `System.Threading.Timer` callback body in try/catch and log. In `Load`, call `Directory.CreateDirectory` first and catch `UnauthorizedAccessException` per file. In `Tick`, save after raising, or catch the save error and re-arm.

### H3. Imported shortcuts keep their keyboard shortcut and bind it globally at once; import warnings are never shown
- `src/Flowmate.Core/Storage/ShortcutLibraryBase.cs:194-211`: `Import` resets Id, name and folder but keeps `Hotkey`, `Alias` and `ShowInSendTo`.
- `src/Flowmate.Triggers/Hotkeys/ShortcutHotkeyBinder.cs:65-80`: `Sync` registers any non-empty `Hotkey` when the library changes.
- `src/Flowmate.Triggers/Hotkeys/HotkeyParser.cs:127-129`: any chord with Ctrl counts as "safe".
- **Scenario:** A shared `.flowmate` file sets `"hotkey": "Ctrl+C"` (or `Ctrl+V`) and contains Run Command or Delete Files. On import (`ImportJson`, which is what `--import` and the `.flowmate` file association will use), `RegisterHotKey(Ctrl+C)` succeeds because nothing else registers it. From then on, every copy the user makes runs the imported shortcut with no prompt.
  - `ImportResult.Warnings` (destructive or advanced actions, required by SPEC §3.9) is computed, but no caller in App or Editor reads it.
  - The editor's "Duplicate" (`ShortcutEditorWindow.xaml.cs:159`) also uses `Import`, so the copy carries the same hotkey and causes a conflict.
- **Fix:**
  - In `Import`, clear `Hotkey`, `Alias` and `ShowInSendTo`, or keep them only after the user confirms them in the import dialog.
  - Show `Warnings` before saving, and require confirmation when Destructive or Advanced actions are present.
  - Disallow binding standard edit chords (Ctrl+C/V/X/Z/A/S) globally.

### H4. A nested "Run Shortcut" deletes its temp folder before the caller can use its output
`src/Flowmate.Core/Actions/FlowActions.cs:161-164`, `src/Flowmate.Core/Engine/ShortcutRunner.cs:58,107-111`, `src/Flowmate.Core/Engine/RunScope.cs:18,62-68`

- **Scenario:** Shortcut A runs shortcut B. B's last step is Take Screenshot, Download URL, Make QR or any action that writes to `ctx.NewTempPath`. The nested `RunAsync` builds its own `RunScope` with a new random `RunId`, and its `finally` calls `CleanupTemp()`. A receives a `FileItem` or `ImageItem` whose file was just deleted, so A's next step (Save File, Share, Resize) fails with "Couldn't find the file".
  - Related: Run-from-here seeds (`RunController.BuildSeed`) point into the previous run's deleted temp folder.
- **Fix:** Nested runs (`Depth > 0`) should reuse the parent's temp folder (pass the parent `RunId` or `TempFolder` through `RunOptions`) and skip cleanup. Only the depth-0 scope should delete it.

---

## Medium

### M1. Text expansion deletes the wrong characters after a mouse click; Backspace "undo" can erase unrelated text
`src/Flowmate.Triggers/Typing/TypingEngine.cs:32-75`, `src/Flowmate.Triggers/Typing/TypedPhraseService.cs:161-186, 219-232, 249`

- The rolling buffer and the undo state reset only on key events or a change of the top-level foreground window. Mouse clicks and focus moves inside the same window are invisible to them.
- **Scenario 1:** The user types `;si`, clicks into another paragraph or another field of the same browser window, then types `g`. `;sig` matches, and `ReplaceTypedAsync` sends 4 Backspaces at the new caret, deleting 3 characters of unrelated text.
- **Scenario 2:** Within 5 s of an expansion, the user clicks elsewhere in the same document and presses Backspace. The key is swallowed and `UndoAsync` presses Right ×CursorBack and then Backspace ×ExpandedLength at the new location, deleting user text. This also applies when the paste path did nothing (an app that ignores Ctrl+V): `NoteExpansion` is still recorded.
- **Fix:** Add a WH_MOUSE_LL hook (or listen for EVENT_OBJECT_FOCUS / caret WinEvents) to clear the buffer and undo state on any click or focus change. Before undo, also check that the focused element is unchanged.

### M2. "Delete Files" silently deletes permanently on drives without a Recycle Bin
`src/Flowmate.Actions.Documents/Files/ChangeActions.cs:202,207`

- `UIOption.OnlyErrorDialogs` maps to `FOF_SILENT|FOF_NOCONFIRMATION`, and `FOF_WANTNUKEWARNING` is not set. For network shares, many USB or SD volumes, and files larger than the bin quota, the shell permanently destroys the file without asking.
- The action description promises "Sends files … to the Recycle Bin, where you can still get them back."
- **Fix:** Call `SHFileOperation` / `IFileOperation` with `FOF_ALLOWUNDO|FOF_WANTNUKEWARNING` (or `FOFX_RECYCLEONDELETE`). Alternatively, check `SHQueryRecycleBin` for the drive first and fail or ask when the drive has no bin.

### M3. Secret, saved-value and settings stores treat a read failure as empty, then overwrite
`src/Flowmate.Core/Storage/Stores.cs:57-66,76` (DPAPI), `:87-98` (saved values), `:23-32,34-44` (settings)

- **DPAPI:** `Read()` returns an empty dictionary on any `IOException` (file briefly locked, or a redirected roaming profile). `Set()` then writes a dictionary with one key, wiping every other API key.
- **Settings:** a transient read failure of `settings.json` at startup yields defaults, so `LibraryFolder` becomes null. The app opens an empty default library (the user's OneDrive library "vanishes"), and the first `Settings.Update` (pause toggle, theme, recent actions) persists the defaults, losing the pointer for good.
- **Saved values:** a corrupt `saved-values.json` is replaced on the next Save Value.
- **Fix:** Separate "file missing" from "file unreadable". On unreadable, retry, then refuse to write and surface an error. Keep a `.bak` copy before overwriting.

### M4. Watcher reload can revert a concurrent in-app save; external edits within 3 s of a self-write are ignored
`src/Flowmate.Core/Storage/JsonShortcutLibrary.cs:27-49, 84-89`

- `Load()` reads every file outside `Gate` and then swaps `ShortcutMap` and the lists wholesale.
- **Scenario:** `SaveAutomation` (for example `AutomationEngine.RecordRun` on every trigger fire, `AutomationEngine.cs:304-314`) commits between the read and the swap. Memory then holds the old list, and the next persist writes old+new, so the earlier change is lost.
- `_selfWrites` suppresses *every* event for that path for 3 s. A OneDrive download of another PC's edit in that window is never loaded, and the next local save overwrites it.
- **Fix:**
  - Hold a library revision counter and discard a reload whose snapshot started before the last local write, or reload per changed file under the lock.
  - Compare file contents or hashes instead of time-based suppression.
  - Avoid rewriting `automations.json` on every fire: keep `LastRun` in local state such as `trigger-state`.

### M5. An open editor reverts "Move to folder" and reordering done in the main window
`src/Flowmate.Editor/ViewModels/EditorViewModel.cs:417-438, 387`

- **Scenario:** The editor is open on shortcut S. The user drags S into folder "Work" (or reorders tiles) in the library. `OnLibraryChanged` calls `Same(lib)`, which compares neither `FolderId` nor `SortOrder`, so it returns true and the editor keeps its stale copy. The next edit in the editor saves `Doc.Shortcut` and S jumps back out of "Work". `ReloadFrom` also never copies `SortOrder`.
- **Fix:** Include `FolderId`, `SortOrder` and the other metadata in `Same` and `ReloadFrom`. Better, have the editor save only the fields it owns by merging onto the latest library copy.

### M6. Run Command splices variables raw into shell text (command injection from trigger input)
`src/Flowmate.Actions.Windows/Advanced/RunCommandAction.cs:34, 46-48`

- **Scenario:** An automation "When files are added to Downloads → Run Command `Remove-Item "<Shortcut Input>"`". A downloaded file named `a$(Start-Process calc).pdf` is valid on Windows, and PowerShell expands `$(...)` inside double quotes. With cmd, `x & calc & .pdf` works the same way. File names, clipboard text and RSS or web content then become code.
- **Fix:** Pass variable values out-of-band. For PowerShell, prepend `$fm_input = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('…'))` and render tokens as `$fm_varN`, or use environment variables. For cmd, escape `^&|<>%"`. Show a warning in the editor when a variable is placed inside the command text.

### M7. The macro recorder captures passwords in plain text into the (possibly OneDrive-synced) library
`src/Flowmate.Triggers/Recorder/MacroRecorder.cs:150-160`

- Typed-phrase triggers skip password fields, but the recorder does not. Typing a password while recording produces a "Type Text" step with the password, saved to `shortcuts\<id>.json`.
- **Fix:** Use the UIA focused-element `IsPassword` check (as in `TypedPhraseService.IsPasswordFieldFocused`, run off the hook thread in the worker) and record a placeholder ("Ask for password") instead of the keystrokes.

### M8. Delete or replace semantics of Move and Copy are not flagged destructive on import or in the editor
`src/Flowmate.Actions.Documents/Files/ChangeActions.cs:139-167`, `TransferParams` at `:120-121`

- Move and Copy with `ifExists=replace` overwrite or delete existing files and folders (see C1), but `IsDestructive` is false. `ShortcutPackage.Inspect` and the editor's warning badge therefore never flag them. Rename Files is non-destructive by design and is fine.
- **Fix:** Add a per-instance destructive check (descriptor callback or `IsDestructiveWhen(param, value)`) and use it in `Inspect` and `ActionRowView`.

---

## Low

### L1. The run history stores step outputs in plain text, including generated passwords
`src/Flowmate.Core/Engine/ShortcutRunner.cs:343-347`, `src/Flowmate.Core/Engine/RunTypes.cs:58`, `src/Flowmate.Core/Storage/Stores.cs:146`

- "Generate Password" (`text.password`), Ask for Input answers and clipboard text land in `%LOCALAPPDATA%\Flowmate\history\runs-*.jsonl` as `OutputPreview`.
- **Fix:** Add `ActionDescriptor.SensitiveOutput` and redact previews for those actions, and for Ask/clipboard-derived values.

### L2. Temp files are never swept
- `src/Flowmate.App/Services/ClipboardService.cs:46-52` writes a new full PNG to `%LOCALAPPDATA%\Flowmate\temp\clipboard` every time the Clipboard variable or `WhenNoInput.UseClipboard` reads an image. Nothing ever deletes these files.
- Run folders whose `CleanupTemp` failed (`RunScope.cs:67`) and `temp\trigger-samples` also stay forever.
- **Scenario:** An interval automation using the Clipboard variable while a screenshot is on the clipboard writes about 1,440 PNGs a day. They also keep possibly sensitive images around.
- **Fix:** At startup, sweep `TempRoot` entries older than a day, skipping run folders that are still active.

### L3. Changing the library location keeps writing to the old library until restart
`src/Flowmate.App/Pages/SettingsPage.xaml.cs:158-169` (file under active edit)

- After the copy, the running app still uses the old `Paths`. Any edits, automation `LastRun` writes or snippet changes made before the user restarts go to the old folder and are missing after restart.
- If the new folder is inside the current one, `CopyDirectory` enumerates into its own output.
- **Fix:** Re-create the library store in-process (or force a restart right away), and reject targets inside the current library.

### L4. The concurrency limit can block hotkey and reminder runs forever
`src/Flowmate.Core/Engine/ShortcutRunner.cs:70-74`, `ShortcutHotkeyBinder.cs:104`, `ReminderService.cs:76`

- These callers pass no token, so `_slots.WaitAsync` has no timeout. If 8 runs sit on dialogs (for example automations showing results while the user is away), every later hotkey press queues silently.
- **Fix:** Use a bounded wait, then fail with "Too many shortcuts are running" and notify.

### L5. Closing the editor discards edits when the final save fails
`src/Flowmate.Editor/ShortcutEditorWindow.xaml.cs:499-505`

- The return value of `SaveNow()` is ignored, so the window closes on an IO or LibraryException.
- **Fix:** Cancel the close and show the error when `SaveNow()` returns false.

### L6. `ShortcutRunService.Cancel` can throw `ObjectDisposedException`
`src/Flowmate.App/Services/ShortcutRunService.cs:43-46, 75-79`

- The tile click handler gets the CTS, the run finishes and disposes it, and then `Cancel()` throws on the UI thread. The global handler shows a "Something unexpected happened" box.
- **Fix:** Catch `ObjectDisposedException`, or remove before dispose under a lock.

### L7. Duplicate shortcut Ids across files make deleted shortcuts come back
`src/Flowmate.Core/Storage/JsonShortcutLibrary.cs:36, 65-70`

- OneDrive conflict copies (`<id>-PCNAME.json`) or an Explorer "Copy" carry the same `Id`. `Load` keeps an arbitrary one, and `Delete` removes only `<id>.json`, so the shortcut reappears on the next reload.
- **Fix:** Track the source file per Id and delete or warn about duplicates (show them in `LoadErrors`).

### L8. Keyboard hook stop/start race can leave an orphaned hook thread
`src/Flowmate.Triggers/Typing/TypedPhraseService.cs:97-120`

- `StopHook` reads `_threadId` outside the lock. If `EnsureHook` starts a new thread in between, WM_QUIT goes to the new thread and the old hook keeps running forever. That breaks the "hook only exists while phrases are registered" guarantee, and `IsHookInstalled` reports false.
- **Fix:** Capture the thread id together with the thread under `_gate` (store it in a per-thread object) and post to that id.

### L9. Run-from-here does not restore named variables
`src/Flowmate.Editor/Logic/RunController.cs:41-46, 80`

- Only magic-variable outputs are seeded, and `_named` is cleared. Steps below that use Get Variable or a named variable set above the start point silently receive Nothing. With implicit input this can feed the wrong value into the next action.
- **Fix:** Snapshot named variables per step in the observer and seed them through a new `RunOptions.SeedVariables`.

---

## Checked and found OK (no finding)

- **FlowTree:** parsing and nesting errors.
- **Engine:** retry, continue and stop policies, cancellation propagation, the loop cap and the depth cap.
- **Zip:** zip-slip protection in `ZipTools.Extract`. Make Zip cleans up on failure.
- **Rename planner:** collision and swap handling, temp parking with rollback, and case-only renames.
- **Power actions:** Ask-first is honoured (`Confirm.AskFirstAsync`), and the display change uses `CDS_TEST`.
- **Typed phrases:** they ignore injected input, so there is no feedback loop.
- **Clipboard watcher:** skips Flowmate's own copies and excluded or password-manager formats.
- **HotkeyService:** the snapshot design avoids a hub-thread deadlock.
- **Threading:** `UserInteraction` marshals to the UI thread through `InvokeTaskAsync`, and no sync-over-async was found on the UI thread (`AppPickerHooks.IconLoader` runs off-thread with `ConfigureAwait(false)`).
- **`flowmate://`:** handling is not implemented yet (`AppHost.cs:178` TODO). When it is wired, `RunByNameOrId` itself does not enforce `AllowFlowmateLinks` or the confirmation, so the E2 code must.
