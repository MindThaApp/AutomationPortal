# ap: the Automation Portal command line

`ap.exe` ships next to `AutomationPortal.exe` (zip build) and as the `ap` command in the Store build.

```
ap list [--json]
ap run "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--from-name <app>] [--ai] [--json] [--no-wait]
ap preview "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--json]
ap tools
ap new --file <path> [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--json]
ap import <file.autoportal> [--json]
ap export "Name" <file.autoportal> [--force]
ap capabilities [--json]
ap --help | --version
```

- **Name** is the shortcut's name (any capitals), its Quick Run alias, its id, its tool name (see `ap tools`), or the start of its name when only one shortcut starts that way.
- `--from-name <app>` gives the name shown on the request sheet ("AI Portal wants to run …"); `--ai` says the app asking is an AI assistant. Runs from an AI assistant are always asked first, and "Don't ask again" is never offered for them.
- `ap preview "Name"` says what the shortcut would do with that input, step by step: which steps only read, which save new files, which change files and which click and type in other apps. It never runs a step, never opens or reads the files and asks nothing. It works from the shortcut's steps alone, so it can't list file names the way "Show what it would do" in the app does. `--json` prints `{shortcut, id, toolName, effects, input, asksUser, asksFirst, steps: [{number, title, text, effect}], summary}`.
- `ap tools` prints every shortcut as a tool definition for apps with an AI assistant: a JSON array of `{type: "function", function: {name, description, parameters}}`, where `name` is the tool name, `description` the shortcut's description plus what it does to files, and `parameters` the input schema from `ap list --json`. Call a tool by running `ap run <tool name>` with `text` as `--input`, and `files` (or `folder`) as `--file`.
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

### Runs other apps ask for (`--from`), and the request sheet

Every run that arrives through `ap`, the pipe or an `automationportal://` link comes from outside Automation Portal's own window, tray, hotkeys and automations. Automation Portal shows its request sheet before such a run when any of these is true:

- the request asks for it (`--from`, or `ask` on the pipe);
- the caller says it is an AI assistant (`--ai`, or `"caller": {"kind": "ai"}`). This is asked every time, also for shortcuts that change nothing, and "Don't ask again" is not offered;
- the shortcut makes or changes files (its badge says "Makes copies" or "Changes files"), or clicks and types in other apps;
- the shortcut runs programs, downloads files or sends data off this PC.

Only a shortcut that does none of these runs straight away for an app that isn't an AI, as before. If Automation Portal isn't running, `ap` starts it in the background so that it can ask; `ap` never runs such a shortcut itself.

The sheet says "Files Portal wants to run "Tidy" on 3 files", shows the badge ("No file changes", "Makes copies" or "Changes files"), a line for an AI assistant, the full path of every item and the text it would get, and what it would do step by step (the same text as `ap preview`). The buttons are **Run** and **Don't run** (counting down the seconds left). **Don't run** is the default and has the focus: Enter and Esc both press it. For callers with a caller id that aren't an AI there is also a tick box "Don't ask again when Files Portal runs this shortcut". Known callers are shown by name (`filesportal` as Files Portal, `deskpal` as Desk Pal, `powerplanmanager` as Power Plan Manager); any other caller is shown by the name it gives (`--from-name`, `caller.app`), else its id, else "Another app". When the name it gives isn't the known name of its caller id, both are shown, as in "Files Portal (unverified), id x", and "Don't ask again" isn't offered.

Each app can have one request waiting for an answer. Another request from the same app while one is waiting is turned away at once: exit code 6 with `Not run: <App> already has a request waiting for your answer. Wait for it, then ask again.` Requests from different apps wait their turn, but at most 2 requests wait at once in all: a third is turned away at once with exit code 6, `Not run: 2 requests are already waiting for your answer. Try again in a moment.` and, for callers that send `caller` with `json`, status `busy`. These are logged as `rateLimited` and `busy`.

Every request is written to Automation Portal's log (`Logs\app.log` in its data folder) with the app, its kind, the shortcut, what it does to files and how it ended: `ran` (nothing to ask), `allowed`, `declined`, `timedOut`, `rateLimited`, `blocked` (refused before asking) or `notFound`. Link requests are logged the same way. A run that goes ahead is also in Run History as usual.

A link that may run a shortcut without asking (Settings > automationportal:// links) still asks when the shortcut makes or changes files, and "Don't ask again" isn't offered for such shortcuts.

- **Don't ask again** remembers that app with that shortcut: later pipe runs of it from that app start without a prompt, until the shortcut's steps change. Other shortcuts still ask. It is never offered, and never honoured, for requests that come through `ap` (`ap run`, `ap run --from`), because any program can start `ap` with any caller id; those are asked every time the rules above say so. Settings > Apps allowed to run shortcuts lists these, each with a Remove button. Apps that earlier versions allowed for every shortcut are turned into one entry per shortcut that existed when this version first started; shortcuts made later ask.
- **Don't run**, or no answer within 60 seconds: the shortcut does not run. `ap` exits with code 6 and prints `Not run: you chose Don't run` or `Not run: nobody answered in 60 seconds` on standard error; nothing goes to standard output (also with `--json`).
- Only one prompt shows at a time; other apps' requests wait their turn, and the 60 seconds count from when each request arrived. Enter presses Don't run, so a prompt that appears while you type can't be allowed by accident.
- `ap` always waits for your decision, also with `--no-wait` (which only skips waiting for the run itself), so allow at least 75 seconds. If Automation Portal isn't running, `ap` starts it in the background so that it can ask.
- `ap list`, `ap list --json`, `ap capabilities`, `ap preview`, `ap tools` and the pipe kinds `ping`, `capabilities`, `list`, `preview` and `tools` never ask and never run anything.
- Caller identity is not proven. The caller id, `caller.app` and `caller.kind` are whatever the other program says, and any program running as you can claim any of them (or leave out `kind: "ai"`). The request sheet is a consent step so that you know a run is coming and what it would do, not proof of who is asking. That is why "Don't ask again" covers one shortcut with its current steps only, is never offered for AI assistants or requests through `ap`, and an unknown name is marked "unverified".
- What that means in practice:
  - A program that talks to the pipe itself (not through `ap`) and sends `ask` with a caller id (the pipe's equivalent of `--from <id>`) and no `kind: "ai"` gets that id's stored consent. If you ticked "Don't ask again when Files Portal runs this shortcut", any such program that says it is `filesportal` can run that shortcut, with those steps, without a sheet.
  - Any program that can drive the screen, through UI Automation or synthetic keyboard and mouse input, can press Run once it is enabled, about a second after the sheet appears. This is a limit of consent given on screen. Run is deliberately left reachable through UI Automation, because people who use Voice Access, switch control and other assistive tools press it that way.
  - Only run programs you trust. The sheet tells you that a run is coming and what it would do; it doesn't prove who asked.

`ap import` with the app running shows the shortcut in Automation Portal and waits until you choose Add (exit code 0) or Cancel (exit code 4). Without the app it adds the shortcut straight away, up to the number of shortcuts your plan allows (the same limit the app uses; builds without a Store package have none). You can name several files; ones that don't fit are left alone and ap says "Imported 3 of 5. Open Automation Portal to add more." It exits with a non-zero code only when nothing could be imported. An import never replaces a shortcut: a name that is already used gets a number.

## Output

- Text: the shortcut's final output on standard output, problems on standard error.
- `--json`: `ap list` prints an array of `{id, name, folder, alias, hotkey, pinned, description, effects, inputs, toolName}` (see "list" under Kinds). `ap run` prints `{shortcut, id, status, output, error, durationMs, runId}`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | The shortcut ran but a step failed (the message says which) |
| 2 | Wrong or missing arguments |
| 3 | No shortcut with that name, alias or id |
| 4 | Cancelled or stopped before it finished |
| 5 | File, import or export problem (cannot read or save the file, not an Automation Portal shortcut) |
| 6 | Not run: the request sheet was shown and you chose Don't run, nobody answered in 60 seconds, or the same app already had a request waiting |

## Launch style (Explorer and shortcuts)

`ap` also accepts the app's own arguments (`--run`, `--import`, `--background`, `automationportal://...`, a `.autoportal` path). It then hands them to the running app or starts `AutomationPortal.exe` with them, closes its console at once and prints nothing. The Store build uses this for Send To, Desktop and Start menu links, because the `ap` alias is a path that survives updates. Only `ap.exe` and `AutomationPortal.exe` from Automation Portal's own folder can hand a command line to the running app; the pipe refuses it from any other program.

### `--run` and `--token`

Send To, Desktop, Start menu, Jump List and right-click entries that Automation Portal makes carry `--token <value>` after `--run <id>`. The token is worked out from the shortcut's id and the kind of entry (`sendto`, `verb`, `desktop`, `start` or `jumplist`) with a key kept for this install in Automation Portal's encrypted secret store, so other programs can't make one up for a shortcut that has no entry. Entries never carry `--input`.

A `--run` with a token runs as before only when all of these hold: the token is this install's token for that shortcut; the launch has no `--input`; it carries files (`--file`, `--files`) only if the token is a Send To or right-click one; and the program that started `ap.exe` or `AutomationPortal.exe` is `%WINDIR%\explorer.exe`. `ap.exe` tells the running app which program started it; when the app has to be started first, `ap.exe` starts it in the background and then hands the command line over, so the check still applies. If the check fails or can't be made, the token doesn't count.

Any other `--run` (a script, another program, a token that doesn't count, or an entry made by an earlier version) is handled like another app's request: the request sheet appears, with the caller shown as "Another program", whenever the shortcut makes or changes files, clicks and types in other apps, runs programs, downloads or sends data. It is never refused outright, and it is logged as `launch`. Automation Portal rewrites its own older entries at its next sync, and they ask until then. A right-click on several files is asked about once for the whole batch.

## Links, Send To, right-click

- `automationportal:` (with or without slashes) and `automationportal://open` open Automation Portal or bring its window forward. `automationportal://open?page=settings` also shows a page: `library`, `pinned`, `automations`, `snippets`, `gallery`, `history` or `settings` (an unknown page just opens the window). Nothing runs, so nothing is asked.
- `automationportal://run?name=Good%20Morning&input=hello` runs a shortcut (Settings > automationportal:// links: Never, Ask every time, or Per shortcut, where it asks first unless the shortcut's Details say "Links can run this without asking"). `automationportal://import?file=C%3A%5Cfile.autoportal` always asks.
- `automationportal://pick?path=C%3A%5Ca.txt&path=C%3A%5Cfolder` (repeat `path`, at most 100) opens a list of your shortcuts for those files and folders. It always asks, whatever Settings > automationportal:// links says; nothing runs until you pick a shortcut and click Run. Only full paths on this PC (`C:...`) are accepted; network, relative and `\?` paths are refused, and paths that do not exist are dropped.
- `automationportal://new?path=C%3A%5CPhotos%5Ca.jpg&path=%5C%5Cserver%5Cshare%5Cb.txt` (repeat `path`, URL-encoded, at most 100) opens the editor on a new, unsaved shortcut with those files to try it on, like `ap new`. It can't run anything, so it works whether or not Allow automationportal:// links is on and asks nothing. Full local paths and network paths are accepted; relative and device paths make the link fail with a message. Files that don't exist are allowed and noted (network paths are not checked, so opening the link never makes Windows sign in to a server).
- `AutomationPortal.exe --run <id|name> [--token <value>] [--input <text>] [--files <paths...>]` runs from a shortcut file or script; without the entry's token it is asked first as described under "`--run` and `--token`". `AutomationPortal.exe --unregister` removes everything the unpackaged build registered (run it before deleting the folder).

## Pipe contract v1 (for other apps)

Other apps can talk to a running Automation Portal over the named pipe `AutomationPortal-<user SID>` (local machine, current user only; byte mode). Connect, write one request as a single line of UTF-8 JSON ending in `\n`, then read one reply line. Closing the pipe before the reply cancels the request (a `run` with `wait` is cancelled). If nobody is listening, Automation Portal isn't running.

The frozen kinds are `ping`, `capabilities`, `list`, `run` and `import`. Later versions only add kinds, fields and features; they don't change or remove these. The kinds `event` and `new` were added later in the same way: they are part of contract 1, and apps check for them in the `kinds` list of `capabilities` before they use them.

Contract 1.1 (`contractVersion: "1.1"` in the hello; `contract` stays 1) adds, without changing anything a version 1 app relies on: the `list` fields `effects`, `inputs` and `toolName`, the kinds `preview` and `tools`, the request field `caller`, and the request sheet for runs that change files or come from an AI assistant (see "Runs other apps ask for" above). Apps that ignore unknown fields keep working. A version 1 `run` without `ask` of a shortcut that changes no files behaves exactly as before. The kinds `launch` and `export` also exist but are internal to Automation Portal and `ap`, and are not part of the contract.

### Request

| Field | Type | Default | Used by |
|---|---|---|---|
| `kind` | string | `""` | all |
| `name` | string | null | `run`: the shortcut's id, name, Quick Run alias, or the start of its name when only one shortcut starts that way. `event`: the event name |
| `input` | string | null | `run`: input text. `import`: the full text of the `.autoportal` file. `event`: the event's details, a JSON object as a string |
| `files` | string[] | `[]` | `run`: full paths of files and folders handed to the shortcut (after `input`, if both are given). A path with a control or format character (a line break, a direction mark such as U+202E) is refused with exit code 2 and nothing runs; format characters are also taken out of the text shown on the request sheet. `new`: the files to try the new shortcut on |
| `wait` | bool | `false` | `run`, `import`: reply when it has finished instead of when it has started |
| `json` | bool | `false` | `list`, `run`, `import`: put JSON in `output` instead of text |
| `source` | string | null | who is asking, for example `"cli"` from `ap`. Advisory only: it is shown to the user and grants nothing |
| `ask` | bool | `false` | `run`: ask the user before running, naming `source` as the app that wants it. `source` must then be a caller id (1 to 32 of `a`-`z`, `0`-`9`, `-`), or the reply is exit code 2. Without `ask` the user is still asked when the shortcut makes or changes files, or `caller.kind` is `ai` |
| `caller` | object | null | 1.1. `run`: `{"app": "AI Portal", "kind": "ai"}`. `app` is the name shown on the request sheet (control characters are dropped, at most 48 characters); `kind` is `app` or `ai`. `ai` is always asked. Advisory, like `source`: it can only make Automation Portal ask more, never less |

Field names are camelCase; unknown fields are ignored.

### Reply

`{ "exitCode": int, "output": string|null, "message": string|null }`. `output` is what `ap` prints to standard output, `message` a plain-English note or problem (what `ap` prints to standard error). Exit codes are the ones in the table above: 0 done, 1 a step failed, 2 wrong request, 3 shortcut not found, 4 cancelled or declined, 5 file, import or size problem, or Automation Portal has to be open to ask first, 6 not run because the user chose Don't run, didn't answer in time, or the same app already had a request waiting.

### Kinds

- `ping` and `capabilities`: exit code 0 and, in `output`, a hello: `{ "app": "AutomationPortal", "version": "1.0.0", "contract": 1, "edition": "store" | "unpackaged", "kinds": ["ping","capabilities","list","run","import","event","new"], "features": [...] }`. Features: `list.json`, `run.wait`, `run.input`, `run.files`, `run.json`, `import.json`, `cli.files-from` (``ap run --files-from`` exists), `caller-prompt` (`ask` on the pipe and `ap run --from` are understood), `new-shortcut` (the kind `new`, `ap new` and `automationportal://new` exist), and `import.review` when imports are shown to the user first (always, while the app is running). Contract 1.1 adds `contractVersion: "1.1"`, the kinds `preview` and `tools`, and the features `list.details` (the new `list` fields), `preview`, `tools`, `caller` (the request field `caller` is understood) and `run.request-sheet` (runs that change files or come from an AI assistant are asked first).
- `list`: with `json`, an array of `{id, name, folder, alias, hotkey, pinned, description, effects, inputs, toolName}`.
  - `effects`: `"none"` (no step changes files or controls other apps), `"makesCopies"` (the only steps that write save new files and leave the originals alone) or `"changesFiles"` (some step may create, change, move, rename, delete or replace files, or click and type in other apps). A step Automation Portal doesn't know counts as changing files. This is the same classification as the file-change check and the badges in the app.
  - `inputs`: a JSON Schema object for what the shortcut takes, from the input kinds in its Details: `text` (a string, for text, numbers, dates, links and the like), `files` (an array of full paths, for files and pictures, also folders when it takes them) or `folder` (one full path, when it takes folders but not files). For example `{"type":"object","properties":{"files":{"type":"array","items":{"type":"string"},"description":"Full paths of the files to work on."}},"additionalProperties":false}`. A shortcut that stops without input lists its one property in `required` (`minProperties: 1` when it has two). One that takes no input has no properties. The schema's `description` says when the shortcut also asks questions while it runs. Hand `text` over as `input` and `files` or `folder` as `files`.
  - `toolName`: a name for the shortcut made of `a`-`z`, `0`-`9` and `_`, at most 64 characters, unique among your shortcuts ("Tidy Downloads!" is `tidy_downloads`). It stays the same while the shortcut keeps its name: when two shortcuts give the same name, the older one keeps it and the newer one gets part of its id added (`tidy_downloads_1a2b3c4d`). `run` and `preview` accept it as `name`.
- `preview` (1.1): `name`, and optionally `input` and `files`, as for `run`. Replies at once with what the shortcut would do, worked out from its steps: it never runs a step, never opens or reads the files and never asks anything. Text, or with `json` `{shortcut, id, toolName, effects, input: {text, files}, asksUser, asksFirst, steps: [{number, title, text, effect}], summary}`, where a step's `effect` is `flow`, `reads`, `none`, `makesCopies`, `changesFiles` or `controlsApps`, and `asksFirst` says whether a `run` with the same request would show the request sheet. A shortcut that doesn't exist is exit code 3.
- `tools` (1.1): replies at once with every shortcut as a tool definition, `[{type: "function", function: {name, description, parameters}}]` (see `ap tools`). The output is always JSON.
- `run` that must be asked first (see "Runs other apps ask for" above): first the user is asked, unless the caller isn't an AI and has "Don't ask again" for this shortcut. Declined, no answer within 60 seconds, or another request from the same app still waiting: exit code 6, `output` null, and `message` `Not run: you chose Don't run`, `Not run: nobody answered in 60 seconds` or `Not run: <App> already has a request waiting for your answer. Wait for it, then ask again.`, with or without `json`. A request with `caller` and `json` also gets `output` `{shortcut, id, status: "declined" | "timedOut" | "rateLimited", error}`. Allowed: the run goes on as below. The reply never comes before the decision, also without `wait`, so give these requests a read timeout of at least 75 seconds. A shortcut that doesn't exist is reported (exit code 3) without asking. When the app that would ask isn't there (the in-process `ap`), the reply is exit code 5 and nothing runs.
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
- Each `source` may send a burst of up to 5 events, and after that one more for every second that passes (a token bucket). An event over that gets exit code 6 (`Not accepted: … sent too many events at once. After a burst of 5, send at most one a second.`) and is dropped. Automation Portal keeps track of up to 1000 sources; when it has to forget one, it forgets the one held back least, so a flood of new source names doesn't reset the limit of a source that is being held back. At most 2 events are handed to automations at once; another gets exit code 6 too. Nothing is shown for events, but every event request is written to Automation Portal's log with its source, name and whether it was accepted.
- The automation's own rules apply as for any trigger: paused automations, the pause for all automations, the minimum time between runs and the runaway guard. Every event that reaches an automation shows in its activity.
- Known events today are Desk Pal's (`deskpal.break.started`, `break.done`, `break.skipped`, `focus.on`, `focus.off`, `pause.on`, `pause.off`, `goal.reached`, `day.summary`); their fields are described in Desk Pal's pipe contract.

### Limits

- One request line may be at most 4 MB of UTF-8 (without the line break). A longer request gets exit code 5 and the pipe is closed; nothing is run or imported.
- The whole request line must arrive within 10 seconds of connecting. Otherwise the app replies with exit code 2 and closes the pipe.
- There is no limit on how long the reply takes: a `run` with `ask` waits up to 60 seconds for the user before anything else; a `run` with `wait` lasts as long as the shortcut, an `import` with `wait` as long as the user takes to decide.
