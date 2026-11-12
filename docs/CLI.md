# ap: the Automation Portal command line

`ap.exe` ships next to `AutomationPortal.exe` (zip build) and as the `ap` command in the Store build.

```
ap list [--json]
ap run "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--json] [--no-wait]
ap new --file <path> [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--json]
ap import <file.autoportal> [--json]
ap export "Name" <file.autoportal> [--force]
ap capabilities [--json]
ap --help | --version
```

- **Name** is the shortcut's name (any capitals), its Quick Run alias, its id, or the start of its name when only one shortcut starts that way.
- `--input -` reads the input text from standard input. `--file` can be repeated; relative paths are resolved from the current folder.
- `--files-from <list.txt>` reads file paths from a UTF-8 text file, one per line (blank lines are skipped, lines are trimmed, a BOM is fine). `--files-from -` reads them from standard input. It can be combined with `--file`. A list file that doesn't exist is a usage error (exit code 2), and `--input -` can't be combined with `--files-from -` because there is only one standard input. The list file is only read, never changed.
- `--from <caller>` is for other apps that run a shortcut through `ap` (for example Files Portal: `ap run "Tidy" --files-from - --from filesportal`). Automation Portal then asks you before the shortcut runs; see "Runs other apps ask for" below. The caller id is 1 to 32 characters, each a lower-case letter `a`-`z`, a digit or `-`; anything else is a usage error (exit code 2).
- `ap new` opens Automation Portal's editor on a new, unsaved shortcut with those files as what Run tries it on ("Create shortcut for these files"). It never runs anything, never saves anything until you change the shortcut, and never opens or changes the files. Give at least one file with `--file` or `--files-from` (there is no limit on the number of files beyond the 4 MB request size). Paths must be full paths on this PC (`C:\...`) or network paths (`\\server\share\...`); relative paths are resolved from the current folder, and device paths (`\\?\...`, `\\.\...`, `CON`, `NUL`...) are refused (exit code 2). Files that don't exist are allowed and marked "not found" in the editor. `--from <caller>` only names the app that sent the files; nothing is asked. Exit code 0 once the editor is showing; `--json` prints `{status: "opened", files, missing}`. If Automation Portal isn't running, `ap` starts it in the background first.
- `ap capabilities` prints the app's hello (the same as the pipe kind `capabilities`): version, pipe contract, edition, kinds and features. Add `--json` for the raw JSON. If Automation Portal isn't running, `ap` answers with its own version and kinds.
- `ap export` never replaces an existing file. If the file is already there it saves as `Name (2).autoportal` (or the next free number) and prints the name it used. Add `--force` to replace the file.
- `--data-root <folder>` (or the `AUTOMATIONPORTAL_DATA_ROOT` variable) works on a separate copy of the data. Use it for tests; it also keeps `ap` from talking to your real running app.

## Where it runs

1. If Automation Portal is running, `ap` sends the request over the pipe `AutomationPortal-<your user SID>` and the app runs it (tiles show progress, history is recorded, questions appear as Automation Portal windows). Closing `ap` or pressing Ctrl+C cancels the run.
2. If Automation Portal is not running, `ap` runs the shortcut itself. Questions (ask, choose, alert, pick a file) are asked in the terminal; questions go to standard error, so standard output is only the result. Steps that need the screen (select a screen area or point, toasts with buttons) say so and fail: open Automation Portal and run it again.

`ap run` waits for the shortcut and returns its result and exit code. `--no-wait` (app running only) returns as soon as the shortcut starts. Without the app `ap` always waits, because nothing could carry on after it exits.

### Runs other apps ask for (`--from`)

With `--from <caller>`, Automation Portal shows a prompt before the run: "Files Portal wants to run "Tidy" with 3 files", with **Allow once**, **Always allow** and **Don't run**. Known callers are shown by name (`filesportal` as Files Portal, `deskpal` as Desk Pal, `powerplanmanager` as Power Plan Manager); any other caller is shown as its id.

- **Always allow** remembers the caller id: later runs from it start without a prompt. Settings > Apps allowed to run shortcuts lists these apps, each with a Remove button.
- **Don't run**, or no answer within 60 seconds: the shortcut does not run. `ap` exits with code 6 and prints `Not run: you chose Don't run` or `Not run: nobody answered in 60 seconds` on standard error; nothing goes to standard output (also with `--json`).
- Only one prompt shows at a time; other requests wait their turn, and the 60 seconds count from when each request arrived. Enter doesn't press Allow, so a prompt that appears while you type can't be allowed by accident.
- `ap` always waits for your decision, also with `--no-wait` (which only skips waiting for the run itself), so allow at least 75 seconds. If Automation Portal isn't running, `ap` starts it in the background so that it can ask.
- `ap list`, `ap list --json`, `ap capabilities` and the pipe kinds `ping` and `capabilities` never ask.
- The caller id is whatever the other app says it is. This is a consent step so that you know a run is coming and from where, not a security boundary: any program running as you can claim any id.

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
| 6 | Not run: another app asked to run it (`--from`, or `ask` on the pipe) and you chose Don't run, or nobody answered in 60 seconds |

## Launch style (Explorer and shortcuts)

`ap` also accepts the app's own arguments (`--run`, `--import`, `--background`, `automationportal://...`, a `.autoportal` path). It then hands them to the running app or starts `AutomationPortal.exe` with them, closes its console at once and prints nothing. The Store build uses this for Send To, Desktop and Start menu links, because the `ap` alias is a path that survives updates.

## Links, Send To, right-click

- `automationportal:` (with or without slashes) and `automationportal://open` open Automation Portal or bring its window forward. `automationportal://open?page=settings` also shows a page: `library`, `pinned`, `automations`, `snippets`, `gallery`, `history` or `settings` (an unknown page just opens the window). Nothing runs, so nothing is asked.
- `automationportal://run?name=Good%20Morning&input=hello` runs a shortcut (asks first unless Settings > Allow automationportal:// links is on). `automationportal://import?file=C%3A%5Cfile.autoportal` always asks.
- `automationportal://pick?path=C%3A%5Ca.txt&path=C%3A%5Cfolder` (repeat `path`, at most 100) opens a list of your shortcuts for those files and folders. It always asks, even when Allow automationportal:// links is on; nothing runs until you pick a shortcut and click Run. Only full paths on this PC (`C:...`) are accepted; network, relative and `\?` paths are refused, and paths that do not exist are dropped.
- `automationportal://new?path=C%3A%5CPhotos%5Ca.jpg&path=%5C%5Cserver%5Cshare%5Cb.txt` (repeat `path`, URL-encoded, at most 100) opens the editor on a new, unsaved shortcut with those files to try it on, like `ap new`. It can't run anything, so it works whether or not Allow automationportal:// links is on and asks nothing. Full local paths and network paths are accepted; relative and device paths make the link fail with a message. Files that don't exist are allowed and noted (network paths are not checked, so opening the link never makes Windows sign in to a server).
- `AutomationPortal.exe --run <id|name> [--input <text>] [--files <paths...>]` runs from a shortcut file or script. `AutomationPortal.exe --unregister` removes everything the unpackaged build registered (run it before deleting the folder).

## Pipe contract v1 (for other apps)

Other apps can talk to a running Automation Portal over the named pipe `AutomationPortal-<user SID>` (local machine, current user only; byte mode). Connect, write one request as a single line of UTF-8 JSON ending in `\n`, then read one reply line. Closing the pipe before the reply cancels the request (a `run` with `wait` is cancelled). If nobody is listening, Automation Portal isn't running.

The frozen kinds are `ping`, `capabilities`, `list`, `run` and `import`. Later versions only add kinds, fields and features; they don't change or remove these. The kinds `event` and `new` were added later in the same way: they are part of contract 1, and apps check for them in the `kinds` list of `capabilities` before they use them. The kinds `launch` and `export` also exist but are internal to Automation Portal and `ap`, and are not part of the contract.

### Request

| Field | Type | Default | Used by |
|---|---|---|---|
| `kind` | string | `""` | all |
| `name` | string | null | `run`: the shortcut's id, name, Quick Run alias, or the start of its name when only one shortcut starts that way. `event`: the event name |
| `input` | string | null | `run`: input text. `import`: the full text of the `.autoportal` file. `event`: the event's details, a JSON object as a string |
| `files` | string[] | `[]` | `run`: full paths of files and folders handed to the shortcut (after `input`, if both are given). `new`: the files to try the new shortcut on |
| `wait` | bool | `false` | `run`, `import`: reply when it has finished instead of when it has started |
| `json` | bool | `false` | `list`, `run`, `import`: put JSON in `output` instead of text |
| `source` | string | null | who is asking, for example `"cli"` from `ap`. Advisory only: it is shown to the user and grants nothing |
| `ask` | bool | `false` | `run`: ask the user before running, naming `source` as the app that wants it. `source` must then be a caller id (1 to 32 of `a`-`z`, `0`-`9`, `-`), or the reply is exit code 2. Without `ask` a run behaves as before, whatever `source` says |

Field names are camelCase; unknown fields are ignored.

### Reply

`{ "exitCode": int, "output": string|null, "message": string|null }`. `output` is what `ap` prints to standard output, `message` a plain-English note or problem (what `ap` prints to standard error). Exit codes are the ones in the table above: 0 done, 1 a step failed, 2 wrong request, 3 shortcut not found, 4 cancelled or declined, 5 file, import or size problem, 6 not run because the user chose Don't run or didn't answer in time (`ask`).

### Kinds

- `ping` and `capabilities`: exit code 0 and, in `output`, a hello: `{ "app": "AutomationPortal", "version": "1.0.0", "contract": 1, "edition": "store" | "unpackaged", "kinds": ["ping","capabilities","list","run","import","event","new"], "features": [...] }`. Features: `list.json`, `run.wait`, `run.input`, `run.files`, `run.json`, `import.json`, `cli.files-from` (``ap run --files-from`` exists), `caller-prompt` (`ask` on the pipe and `ap run --from` are understood), `new-shortcut` (the kind `new`, `ap new` and `automationportal://new` exist), and `import.review` when imports are shown to the user first (always, while the app is running).
- `list`: with `json`, an array of `{id, name, folder, alias, hotkey, pinned, description}`.
- `run` with `ask`: first the user is asked (see "Runs other apps ask for" above), unless the caller is on the always-allowed list. Declined or no answer within 60 seconds: exit code 6, `output` null, and `message` `Not run: you chose Don't run` or `Not run: nobody answered in 60 seconds`, with or without `json`. Allowed: the run goes on as below. The reply never comes before the decision, also without `wait`, so give `ask` requests a read timeout of at least 75 seconds. A shortcut that doesn't exist is reported (exit code 3) without asking.
- `run`: without `wait`, replies at once (`Started "Name".`; with `json`, `{shortcut, status: "started"}`). With `wait`, replies when the shortcut ends; with `json`, `{shortcut, id, status, output, error, durationMs, runId}`.
- `import`: the app shows the shortcut and what it does, and adds it only if the user clicks Add. This happens for every pipe import, whatever `source` says. Without `wait` the reply comes at once (`{name, status: "review"}` with `json`); with `wait` it comes after the user decides: 0 with `{id, name, status: "added"}`, or 4 with `{name, status: "declined"}`. Closing the pipe while waiting leaves the review open for the user. An import never replaces a shortcut: it always gets a new id, and a name that is already used gets a number. Keyboard shortcut, Quick Run name and Send to entry stay off until the user turns them on.
- `new`: opens the editor on a new, unsaved shortcut with `files` as what Run tries it on. Nothing runs, nothing is saved until the user changes the shortcut, and the files are never opened or changed; nobody is asked, since nothing can happen without the user. Paths must be full local paths or network paths (no relative or device paths, no `..`); a bad path is exit code 2 and nothing opens. Files that don't exist are allowed and noted. `source`, when it is a caller id other than `cli`, is shown in the editor as where the files came from. The reply comes once the editor is showing: exit code 0, with `json` `{status: "opened", files, missing}`. Example: `{"kind":"new","files":["C:\\Photos\\a.jpg"],"source":"filesportal"}`.
- `event`: tells Automation Portal that something happened in another app. It only feeds "When another app sends an event" automations: it never runs a shortcut directly and never shows anything. The reply is always immediate (`wait` is ignored) and never waits for an automation: exit code 0 when the event is accepted, 2 when the name or details are invalid, 5 when the details are too large. See "Events" below.

### Events

```json
{"kind":"event","name":"deskpal.focus.on","input":"{\"event\":\"deskpal.focus.on\",\"at\":\"2026-11-12T10:00:00+01:00\",\"cause\":\"user\",\"minutes\":25}","wait":false,"source":"deskpal"}
```

- `name` is `<app>.<event>`: lower-case letters, digits, `.`, `_` and `-`, at most 100 characters, with no empty parts (`deskpal.break.started`).
- `input` is optional. When given, it must be a JSON object (as a string) of at most 64 KB. Its fields are handed to the automation as a dictionary, with the sender's field names; `at` and `until` become dates, and `event` and `at` are filled in when they are missing.
- `source` is advisory, as for the other kinds.
- Events that Automation Portal doesn't know are accepted (exit code 0), noted once in the trigger log and ignored.
- The automation's own rules apply as for any trigger: paused automations, the pause for all automations, the minimum time between runs and the runaway guard. Every event that reaches an automation shows in its activity.
- Known events today are Desk Pal's (`deskpal.break.started`, `break.done`, `break.skipped`, `focus.on`, `focus.off`, `pause.on`, `pause.off`, `goal.reached`, `day.summary`); their fields are described in Desk Pal's pipe contract.

### Limits

- One request line may be at most 4 MB of UTF-8 (without the line break). A longer request gets exit code 5 and the pipe is closed; nothing is run or imported.
- The whole request line must arrive within 10 seconds of connecting. Otherwise the app replies with exit code 2 and closes the pipe.
- There is no limit on how long the reply takes: a `run` with `ask` waits up to 60 seconds for the user before anything else; a `run` with `wait` lasts as long as the shortcut, an `import` with `wait` as long as the user takes to decide.
