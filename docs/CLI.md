# ap: the Automation Portal command line

`ap.exe` ships next to `AutomationPortal.exe` (zip build) and as the `ap` command in the Store build.

```
ap list [--json]
ap run "Name" [--input <text>] [--file <path>]... [--json] [--no-wait]
ap import <file.autoportal> [--json]
ap export "Name" <file.autoportal> [--force]
ap --help | --version
```

- **Name** is the shortcut's name (any capitals), its Quick Run alias, its id, or the start of its name when only one shortcut starts that way.
- `--input -` reads the input text from standard input. `--file` can be repeated; relative paths are resolved from the current folder.
- `ap export` never replaces an existing file. If the file is already there it saves as `Name (2).autoportal` (or the next free number) and prints the name it used. Add `--force` to replace the file.
- `--data-root <folder>` (or the `AUTOMATIONPORTAL_DATA_ROOT` variable) works on a separate copy of the data. Use it for tests; it also keeps `ap` from talking to your real running app.

## Where it runs

1. If Automation Portal is running, `ap` sends the request over the pipe `AutomationPortal-<your user SID>` and the app runs it (tiles show progress, history is recorded, questions appear as Automation Portal windows). Closing `ap` or pressing Ctrl+C cancels the run.
2. If Automation Portal is not running, `ap` runs the shortcut itself. Questions (ask, choose, alert, pick a file) are asked in the terminal; questions go to standard error, so standard output is only the result. Steps that need the screen (select a screen area or point, toasts with buttons) say so and fail: open Automation Portal and run it again.

`ap run` waits for the shortcut and returns its result and exit code. `--no-wait` (app running only) returns as soon as the shortcut starts. Without the app `ap` always waits, because nothing could carry on after it exits.

## Output

- Text: the shortcut's final output on standard output, problems on standard error.
- `--json`: `ap list` prints an array of `{id, name, folder, alias, hotkey, pinned, description}`. `ap run` prints `{shortcut, id, status, output, error, durationMs, runId}`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | The shortcut ran but a step failed (the message says which) |
| 2 | Wrong or missing arguments |
| 3 | No shortcut with that name, alias or id |
| 4 | Cancelled or stopped before it finished |
| 5 | File, import or export problem (cannot read or save the file, not an Automation Portal shortcut) |

## Launch style (Explorer and shortcuts)

`ap` also accepts the app's own arguments (`--run`, `--import`, `--background`, `automationportal://...`, a `.autoportal` path). It then hands them to the running app or starts `AutomationPortal.exe` with them, closes its console at once and prints nothing. The Store build uses this for Send To, Desktop and Start menu links, because the `ap` alias is a path that survives updates.

## Links, Send To, right-click

- `automationportal://run?name=Good%20Morning&input=hello` runs a shortcut (asks first unless Settings > Allow automationportal:// links is on). `automationportal://import?file=C%3A%5Cfile.autoportal` always asks.
- `automationportal://pick?path=C%3A%5Ca.txt&path=C%3A%5Cfolder` (repeat `path`, at most 100) opens a list of your shortcuts for those files and folders. It always asks, even when Allow automationportal:// links is on; nothing runs until you pick a shortcut and click Run. Only full paths on this PC (`C:...`) are accepted; network, relative and `\?` paths are refused, and paths that do not exist are dropped.
- `AutomationPortal.exe --run <id|name> [--input <text>] [--files <paths...>]` runs from a shortcut file or script. `AutomationPortal.exe --unregister` removes everything the unpackaged build registered (run it before deleting the folder).
