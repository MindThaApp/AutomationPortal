# Engine packages changelog

Applies to `AutomationPortal.Core`, `AutomationPortal.Actions.Content`, `AutomationPortal.Actions.Documents`,
`AutomationPortal.Actions.Windows` and `AutomationPortal.Triggers` (earlier entries use the former Flowmate.* names). See `docs/HOSTING.md` for versioning.

## 1.0.0-preview.4

- Renamed to Automation Portal: package ids, namespaces, link scheme and file extension.

## 1.0.0-preview.3

Security hardening release. Host apps should read "Behaviour changes" below.

- `PathSafety` (Core): protected folders (Windows, Program Files, profile roots, and folders that hold protected ones)
  are refused for writes and deletes, including through symbolic links, device paths and admin shares. A variable that
  ends up empty (also behind quotes, spaces or dots) can no longer empty a path segment, and a variable may not bring
  in a `..` segment.
- `WriteViaTemp`: files are rewritten through a temporary name and put in place afterwards, writing through symbolic
  links; a failed new file leaves no temp file behind.
- Run Command, cmd and PowerShell:
  - Values are inserted as they are, with no quote guessing.
  - cmd refuses values holding `& | < > ^ % ! "`, parentheses or a line break.
  - A space in a value is only accepted inside `"{v}"`.
  - A `/` in a cmd value is refused.
  - Private and very long values are refused where they would have to travel as an environment variable into a
    PowerShell command that runs on another computer, session or process.
- Import-risk flags: actions declare `ImportRisk`, and importing a shortcut warns about steps that run programs, download
  files or send data.
- Downloads and extracted zip files carry the Mark of the Web.
- "Test now" runs on a temporary sample folder, never on real folders, asks first, and names steps that can delete.
- Folder triggers: events are held while a run is busy, deferred through a cooldown (minimum 5 s), never dropped
  (batches of 1,000 past 1,000 waiting paths), and ignore their own output and work files.
- Runaway breaker: every trigger except hotkeys and typed text is paused with a lasting "ran too often" and a Resume
  button after more than 60 runs in 10 minutes. New setting "Don't pause this automation when it runs often"
  (`Automation.AllowFrequentRuns`, `PausedReason`), off by default.
- Safer Move, Copy, Save, Zip, Note and Spreadsheet actions: copies land through a unique temporary name, folder moves
  remove only originals that were copied and checked, zip extraction refuses colon names, and workbooks, CSVs and notes
  are rewritten via a temp file with a one-time backup.
- Links: new `flowmate://pick` verb opens a shortcut chooser; for any `flowmate:` argument only the link counts and it
  overrides other arguments; network paths are refused and a linked file asks before opening.
- New action "Show in Files Portal".
- Pinned dependencies (no floating versions); Pkcs package patched to 10.0.14; native Windows DLLs load from System32
  only.
- `PublicAPI.Shipped.txt` updated with the new public API.

Behaviour changes hosts should know about:

- File actions now refuse protected folders and paths that leave the intended folder; empty path variables stop the step.
- Run Command refuses more values than before (see above); existing shortcuts may stop at those steps.
- Overwriting actions keep a backup or use a unique name rather than replacing silently.
- Folder triggers wait for the running automation and honour a 5 s minimum cooldown.
- Automations can now be paused by the runaway breaker; hosts that show automation state should show `PausedReason`.
- Importing shortcuts shows risk warnings; links no longer pass extra arguments past the link.

## 1.0.0-preview.2

- File actions: `file.renamePattern`, `file.compare` and `file.hash`.
- Power plans: `dev.powerPlan.*` actions, the `power.plan` trigger and the `cond.powerPlan` condition, backed by
  `Flowmate.Core.Platform.PowerPlans` (`IPowerPlanApi`, `WindowsPowerPlanApi`).
- Power Plan Manager actions (`ppm.*`) with a status-pipe client.
- Default WinRT picture backend (`WinRtImageBackend`), used when the host sets no picture backend. Gaps: JPEG to JPEG
  keeps the common photo tags rather than every EXIF entry, resampling is a triangle filter (not WPF's scaler), text is
  drawn with GDI+ so glyph edges differ slightly, and GIF keeps only the first frame (no animation).
- `PublicAPI.Shipped.txt` updated with the power plan types.
## 1.0.0-preview.1

- First packaged release of the engine and action libraries for hosting in other apps.
- No WPF or Windows Forms dependency; `FlowmateEngine` builder in Core.
- `Flowmate.Core` public API frozen in `PublicAPI.Shipped.txt`.
