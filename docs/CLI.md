# ap: the Automation Portal command line

`ap.exe` ships next to `AutomationPortal.exe` (zip build) and as the `ap` command in the Store build.

```
ap list [--json]
ap run "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--json] [--no-wait]
ap import <file.autoportal> [--json]
ap export "Name" <file.autoportal> [--force]
ap capabilities [--json]
ap --help | --version
```

- **Name** is the shortcut's name (any capitals), its Quick Run alias, its id, or the start of its name when only one shortcut starts that way.
- `--input -` reads the input text from standard input. `--file` can be repeated; relative paths are resolved from the current folder.
- `--files-from <list.txt>` reads file paths from a UTF-8 text file, one per line (blank lines are skipped, lines are trimmed, a BOM is fine). `--files-from -` reads them from standard input. It can be combined with `--file`. A list file that doesn't exist is a usage error (exit code 2), and `--input -` can't be combined with `--files-from -` because there is only one standard input. The list file is only read, never changed.
- `ap capabilities` prints the app's hello (the same as the pipe kind `capabilities`): version, pipe contract, edition, kinds and features. Add `--json` for the raw JSON. If Automation Portal isn't running, `ap` answers with its own version and kinds.
- `ap export` never replaces an existing file. If the file is already there it saves as `Name (2).autoportal` (or the next free number) and prints the name it used. Add `--force` to replace the file.
- `--data-root <folder>` (or the `AUTOMATIONPORTAL_DATA_ROOT` variable) works on a separate copy of the data. Use it for tests; it also keeps `ap` from talking to your real running app.

## Where it runs

1. If Automation Portal is running, `ap` sends the request over the pipe `AutomationPortal-<your user SID>` and the app runs it (tiles show progress, history is recorded, questions appear as Automation Portal windows). Closing `ap` or pressing Ctrl+C cancels the run.
2. If Automation Portal is not running, `ap` runs the shortcut itself. Questions (ask, choose, alert, pick a file) are asked in the terminal; questions go to standard error, so standard output is only the result. Steps that need the screen (select a screen area or point, toasts with buttons) say so and fail: open Automation Portal and run it again.

`ap run` waits for the shortcut and returns its result and exit code. `--no-wait` (app running only) returns as soon as the shortcut starts. Without the app `ap` always waits, because nothing could carry on after it exits.

`ap import` with the app running shows the shortcut in Automation Portal and waits until you choose Add (exit code 0) or Cancel (exit code 4). Without the app it adds the shortcut straight away. An import never replaces a shortcut: a name that is already used gets a number.

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

- `automationportal:` (with or without slashes) and `automationportal://open` open Automation Portal or bring its window forward. `automationportal://open?page=settings` also shows a page: `library`, `pinned`, `automations`, `snippets`, `gallery`, `history` or `settings` (an unknown page just opens the window). Nothing runs, so nothing is asked.
- `automationportal://run?name=Good%20Morning&input=hello` runs a shortcut (asks first unless Settings > Allow automationportal:// links is on). `automationportal://import?file=C%3A%5Cfile.autoportal` always asks.
- `automationportal://pick?path=C%3A%5Ca.txt&path=C%3A%5Cfolder` (repeat `path`, at most 100) opens a list of your shortcuts for those files and folders. It always asks, even when Allow automationportal:// links is on; nothing runs until you pick a shortcut and click Run. Only full paths on this PC (`C:...`) are accepted; network, relative and `\?` paths are refused, and paths that do not exist are dropped.
- `AutomationPortal.exe --run <id|name> [--input <text>] [--files <paths...>]` runs from a shortcut file or script. `AutomationPortal.exe --unregister` removes everything the unpackaged build registered (run it before deleting the folder).

## Pipe contract v1 (for other apps)

Other apps can talk to a running Automation Portal over the named pipe `AutomationPortal-<user SID>` (local machine, current user only; byte mode). Connect, write one request as a single line of UTF-8 JSON ending in `\n`, then read one reply line. Closing the pipe before the reply cancels the request (a `run` with `wait` is cancelled). If nobody is listening, Automation Portal isn't running.

The frozen kinds are `ping`, `capabilities`, `list`, `run` and `import`. Later versions only add kinds, fields and features; they don't change or remove these. The kinds `launch` and `export` also exist but are internal to Automation Portal and `ap`, and are not part of the contract.

### Request

| Field | Type | Default | Used by |
|---|---|---|---|
| `kind` | string | `""` | all |
| `name` | string | null | `run`: the shortcut's id, name, Quick Run alias, or the start of its name when only one shortcut starts that way |
| `input` | string | null | `run`: input text. `import`: the full text of the `.autoportal` file |
| `files` | string[] | `[]` | `run`: full paths of files and folders handed to the shortcut (after `input`, if both are given) |
| `wait` | bool | `false` | `run`, `import`: reply when it has finished instead of when it has started |
| `json` | bool | `false` | `list`, `run`, `import`: put JSON in `output` instead of text |
| `source` | string | null | who is asking, for example `"cli"` from `ap`. Advisory only: it is shown to the user and grants nothing |

Field names are camelCase; unknown fields are ignored.

### Reply

`{ "exitCode": int, "output": string|null, "message": string|null }`. `output` is what `ap` prints to standard output, `message` a plain-English note or problem (what `ap` prints to standard error). Exit codes are the ones in the table above: 0 done, 1 a step failed, 2 wrong request, 3 shortcut not found, 4 cancelled or declined, 5 file, import or size problem.

### Kinds

- `ping` and `capabilities`: exit code 0 and, in `output`, a hello: `{ "app": "AutomationPortal", "version": "1.0.0", "contract": 1, "edition": "store" | "unpackaged", "kinds": ["ping","capabilities","list","run","import"], "features": [...] }`. Features: `list.json`, `run.wait`, `run.input`, `run.files`, `run.json`, `import.json`, `cli.files-from` (``ap run --files-from`` exists), and `import.review` when imports are shown to the user first (always, while the app is running).
- `list`: with `json`, an array of `{id, name, folder, alias, hotkey, pinned, description}`.
- `run`: without `wait`, replies at once (`Started "Name".`; with `json`, `{shortcut, status: "started"}`). With `wait`, replies when the shortcut ends; with `json`, `{shortcut, id, status, output, error, durationMs, runId}`.
- `import`: the app shows the shortcut and what it does, and adds it only if the user clicks Add. This happens for every pipe import, whatever `source` says. Without `wait` the reply comes at once (`{name, status: "review"}` with `json`); with `wait` it comes after the user decides: 0 with `{id, name, status: "added"}`, or 4 with `{name, status: "declined"}`. Closing the pipe while waiting leaves the review open for the user. An import never replaces a shortcut: it always gets a new id, and a name that is already used gets a number. Keyboard shortcut, Quick Run name and Send to entry stay off until the user turns them on.

### Limits

- One request line may be at most 4 MB of UTF-8 (without the line break). A longer request gets exit code 5 and the pipe is closed; nothing is run or imported.
- The whole request line must arrive within 10 seconds of connecting. Otherwise the app replies with exit code 2 and closes the pipe.
- There is no limit on how long the reply takes: a `run` with `wait` lasts as long as the shortcut, an `import` with `wait` as long as the user takes to decide.
