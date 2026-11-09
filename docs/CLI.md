# fm: the Flow Mate command line

`fm.exe` ships next to `Flowmate.exe` (zip build) and as the `fm` command in the Store build.

```
fm list [--json]
fm run "Name" [--input <text>] [--file <path>]... [--json] [--no-wait]
fm import <file.flowmate> [--json]
fm export "Name" <file.flowmate> [--force]
fm --help | --version
```

- **Name** is the shortcut's name (any capitals), its Quick Run alias, its id, or the start of its name when only one shortcut starts that way.
- `--input -` reads the input text from standard input. `--file` can be repeated; relative paths are resolved from the current folder.
- `fm export` never replaces an existing file. If the file is already there it saves as `Name (2).flowmate` (or the next free number) and prints the name it used. Add `--force` to replace the file.
- `--data-root <folder>` (or the `FLOWMATE_DATA_ROOT` variable) works on a separate copy of the data. Use it for tests; it also keeps `fm` from talking to your real running app.

## Where it runs

1. If Flow Mate is running, `fm` sends the request over the pipe `Flowmate-<your user SID>` and the app runs it (tiles show progress, history is recorded, questions appear as Flow Mate windows). Closing `fm` or pressing Ctrl+C cancels the run.
2. If Flow Mate is not running, `fm` runs the shortcut itself. Questions (ask, choose, alert, pick a file) are asked in the terminal; questions go to standard error, so standard output is only the result. Steps that need the screen (select a screen area or point, toasts with buttons) say so and fail: open Flow Mate and run it again.

`fm run` waits for the shortcut and returns its result and exit code. `--no-wait` (app running only) returns as soon as the shortcut starts. Without the app `fm` always waits, because nothing could carry on after it exits.

## Output

- Text: the shortcut's final output on standard output, problems on standard error.
- `--json`: `fm list` prints an array of `{id, name, folder, alias, hotkey, pinned, description}`. `fm run` prints `{shortcut, id, status, output, error, durationMs, runId}`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | The shortcut ran but a step failed (the message says which) |
| 2 | Wrong or missing arguments |
| 3 | No shortcut with that name, alias or id |
| 4 | Cancelled or stopped before it finished |
| 5 | File, import or export problem (cannot read or save the file, not a Flow Mate shortcut) |

## Launch style (Explorer and shortcuts)

`fm` also accepts the app's own arguments (`--run`, `--import`, `--background`, `flowmate://...`, a `.flowmate` path). It then hands them to the running app or starts `Flowmate.exe` with them, closes its console at once and prints nothing. The Store build uses this for Send To, Desktop and Start menu links, because the `fm` alias is a path that survives updates.

## Links, Send To, right-click

- `flowmate://run?name=Good%20Morning&input=hello` runs a shortcut (asks first unless Settings > Allow flowmate:// links is on). `flowmate://import?file=C%3A%5Cfile.flowmate` always asks.
- `Flowmate.exe --run <id|name> [--input <text>] [--files <paths...>]` runs from a shortcut file or script. `Flowmate.exe --unregister` removes everything the unpackaged build registered (run it before deleting the folder).
