

## From fix agent E3 (review-1)
- StoreDiagnostics.Error (Core): subscribe at startup, log to AppLog and show a one-time notification ("Flowmate couldn't read <file> and left it untouched"). Also show JsonShortcutLibrary.LoadErrors and JsonSettingsStore.LoadError somewhere (Settings or tray).
- H3 import: ImportResult now strips Hotkey/Alias/ShowInSendTo (see PendingHotkey/PendingAlias/PendingShowInSendTo) and exposes NeedsConfirmation + Warnings. Show a confirmation dialog listing Warnings (use ShortcutPackage.Inspect(shortcut, registry) BEFORE saving when possible; if the user declines, library.Delete(result.Shortcut.Id)). Offer to re-enable the pending hotkey/alias after the user confirms. Editor "Duplicate" also goes through Import, so duplicates now start without a hotkey.
- L2: call TempSweeper.Sweep(paths) once at startup (Core). ClipboardService writes to %LOCALAPPDATA%\Flowmate\temp\clipboard which Sweep cleans after a day; consider reusing one file name per image hash.
- L3: re-create the library store in-process after changing library location (or restart immediately) and reject targets inside the current library.
- L4: ShortcutRunner.SlotWaitTimeout (60 s) now fails the run with "Too many shortcuts are running at once"; hotkey/reminder callers should show that message.
- L6: ShortcutRunService.Cancel: catch ObjectDisposedException (or remove the CTS from the map under a lock before disposing).
- Editor runs: pass RunOptions.KeepTemp = true for Run-from-here capable runs (done in Editor RunController); nothing needed in App unless App runs shortcuts with seeds.

## Done by E2 (App side)
- StoreDiagnostics.Error: subscribed (log + one toast per message; queued until the app is up); library LoadErrors toast at start. DONE
- H3 import: AppHost.FinishImportAsync (warnings confirm, decline deletes, offer to turn on hotkey/alias/Send To) used by file, flowmate:// link and gallery; CLI import prints warnings and what was left off. DONE
- L2: TempSweeper.Sweep at startup. DONE
- L3: library move rejects targets inside the current library and restarts the app at once. DONE
- L4: failures already notify; message shown as-is. DONE
- L6: ShortcutRunService.Cancel catches ObjectDisposedException. DONE
