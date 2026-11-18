# Suite review: Automation Portal, Files Portal, Power Plan Manager, Desk Pal (2026-11-11)

Read-only product and UX review. Nothing was launched or built; the UI was judged from XAML and code, and the
integration was checked in code on both sides of every link. Paths are relative to each project folder:

- **AP** = `C:\AI\Projects\Flowmate` (only `src\AutomationPortal.*`; the `src\Flowmate.*` folders were ignored)
- **FP** = `C:\AI\Projects\FilesPortal`
- **PPM** = `C:\AI\Projects\PowerPlanTray\source\repos\PowerPlanTray`
- **DP** = `C:\AI\Projects\DeskPal`

Effort: S = up to 2 days, M = up to 2 weeks, L = more. Risk: Low / Med / High (chance of regressions or of
harming user data or hardware).

Power Plan Manager already has a detailed review dated today
(`C:\AI\Projects\PowerPlanTray\docs\reviews\2026-11-11-ui-ux-automation-review.md`). Its main claims were
re-checked in code and hold. This report does not repeat it; section 2.3 lists what was verified, what it
missed and where this report disagrees.

---

## 1. Executive summary: top 10, ranked by user value against effort

| # | Recommendation | Apps | Value | Effort | Risk |
|---|---|---|---|---|---|
| 1 | **One written suite contract plus a `hello` reply on every pipe** (app, version, contract number, edition, entitlement, capabilities, whether outside control is allowed). Today there are three message shapes, three trust models and no version anywhere (section 3.1). Everything below depends on this. | All | High | S per app | Low |
| 2 | **PPM: per-edition pipe names, retry, and `proAccess` in status.** With Lite and Pro both installed, the second one to start never gets the pipe and never retries, so AP can read one edition and send links to the other (`src\PowerPlanTray.Core\Services\StatusPipeServer.cs:40`, `:87-88`, `:120`). | PPM, AP | High | S | Low |
| 3 | **AP: an "App event" trigger** fed by two transports: siblings that push (Desk Pal, Files Portal) send `event` on AP's pipe; siblings that stay read-only (PPM) are subscribed to. One place to map events to shortcuts, with AP's conditions, pause-all and run history, instead of a mapping screen in every sibling. | AP first | High | M | Med |
| 4 | **FP: "Run shortcut ▸" submenu filled from AP's library** (pipe `list`, then `run` with the selected paths). It replaces today's two competing entry points: "Open in Automation Portal…" (a link, a prompt, then a picker) and "Run a flow…" (a file picker every time, in-process, no history). | FP, AP | High | M | Med |
| 5 | **AP: "Recently deleted" for shortcuts, and "Back up library now".** Deleting a shortcut is a permanent `File.Delete` behind a "can't be undone" prompt (`src\AutomationPortal.App\Pages\LibraryPage.xaml.cs:326-333`, `src\AutomationPortal.Core\Storage\JsonShortcutLibrary.cs:221`). | AP | High | S | Low |
| 6 | **PPM: request ids on links plus read-only `GET RESULT`;** AP then reports the real outcome instead of "it shows the result as a notification". Keep the pipe read-only (agreeing with PPM's own review, against AP's `SET` request). | PPM, AP | High | M | Low |
| 7 | **AP pipe hardening before it is frozen as a sibling contract:** cap the request size and read time, make `ping` return the `hello`, and show the in-app import review when `import` does not come from `ap.exe` (section 3.2, items 1-3). | AP | Med-High | S | Low |
| 8 | **AP: first-run welcome and a "Connected apps" card in Settings** showing each sibling as Not installed / Installed / Running / Control off, with the one fix needed. Today sibling actions fail at run time with a sentence; there is no first-run flow at all. | AP | High | M | Low |
| 9 | **Desk Pal: build the hold (lease) scheduler, a real tray menu and single-instance guard now;** leave the pipe to step 5b as planned, but adopt the suite `hello` and caller check from day one. The current scaffold fires a test nudge at every start and has no instance guard (`src\DeskPal\App.xaml.cs:46`). | DP | High | M | Low |
| 10 | **One naming and accessibility pass across the suite:** the setting that lets other apps in ("Allow automationportal:// links" / "Allow links from other apps" / no setting), "shortcut" versus "flow", theme wording, spelling, automation names on unnamed controls, stale "Flow Mate" references. | All | Med | S-M | Low |

Close runners-up: tell the user when Files Portal sends only the first 100 selected items (S); AP library export/import of everything in one file (S); timed keep-awake in PPM (S).

---

## 2. Per-app review

### 2.1 Automation Portal

#### UI/UX findings

| # | Finding | Where |
|---|---|---|
| A1 | **No first-run experience.** The first screen is the empty library with two buttons ("Browse the Gallery", "Create a shortcut"). Nothing explains the tray icon, Quick Run keys, automations or that the app keeps running. No welcome or onboarding type exists in the App or Editor projects. | `src\AutomationPortal.App\Pages\LibraryPage.xaml:77-88`, `LibraryPage.xaml.cs:144-145` |
| A2 | **The "Describe it" builder is not on the front door.** The empty state and "New Shortcut" lead to the blank editor; the builder is reached from inside it. The project's own survey (item 2 of section 3) already calls for the description box to be the first thing a new user sees. | `docs\research\feature-survey.md:155-157`, `src\AutomationPortal.App\Pages\LibraryPage.xaml:22-23` |
| A3 | **Deleting a shortcut is permanent.** The prompt says "This can't be undone" and the store deletes the file. Every other destructive path in the app was made recoverable in review 1; this one was not in scope there. | `LibraryPage.xaml.cs:326-333`, `src\AutomationPortal.Core\Storage\JsonShortcutLibrary.cs:221` |
| A4 | **Tile "More options" button is invisible until hover** (`Opacity="0"`). It has an automation name, so screen readers find it, but a sighted keyboard user tabbing to it sees nothing unless a focus trigger raises the opacity; check on screen. The context-menu key should also open the same menu. | `src\AutomationPortal.App\Controls\ShortcutTile.xaml:53` |
| A5 | **Tray menu is thin for a tray-first app.** Pinned shortcuts (max 10, silently cut), Quick Run, Open, Record, Pause, Exit. Missing: "Stop running shortcuts" (a runaway shortcut can only be stopped from a tile), "Last run failed: see details", Settings, and a paused state that is visible on the icon itself, not only in the tooltip. | `src\AutomationPortal.App\Shell\TrayService.cs:42-46`, `:73`, `:82-91` |
| A6 | **Settings, "Where I am":** three text boxes (city, latitude, longitude) with placeholder text only and no automation names; no validation message and no "Use my current location" button. | `src\AutomationPortal.App\Pages\SettingsPage.xaml:61-68` |
| A7 | **Settings grouping.** "Phone notifications topic" sits under the AI heading; it belongs under Notifications. The model picker offers "Fast / Smart / Best" with no hint of cost or speed. | `SettingsPage.xaml:138-162` |
| A8 | **The links switch undersells its reach.** "Allow automationportal:// links" lets any web page or app run any shortcut by name with text input and no prompt (file inputs and path-like text still prompt, which is good). An all-or-nothing switch is the wrong shape: users want "always allow this shortcut from links", not "all shortcuts". | `SettingsPage.xaml:182-186`, `src\AutomationPortal.App\AppHost.Launch.cs:193-203` |
| A9 | **Link prompt is good, but dead-ends.** "Run this shortcut?" shows the text and up to 8 files. It offers no "Always allow for this shortcut" and no way to see which app asked (not knowable for a link; say so). | `AppHost.Launch.cs:197-201`, `:219-236` |
| A10 | **Failures of link and launch runs go to the log only** when something throws; "shortcut not found" does notify. A link that fails silently looks like the app ignored the user. | `AppHost.Launch.cs:180`, `:215` |
| A11 | **Quick Run placeholder** says only "Type a shortcut name", although the box also does calculations, fallbacks and aliases. A rotating hint ("Try: 12*7, or a shortcut's short code") teaches the feature. Empty result text "Nothing found. Try another word." offers no action; add "Create a shortcut called '…'" and "Search the Gallery". | `src\AutomationPortal.App\Shell\QuickRunWindow.xaml:38`, `:89` |
| A12 | **Run History** filters by name and status only. No filter by source (hotkey, automation, link, CLI, sibling app), which is the first question when something ran unexpectedly. | `src\AutomationPortal.App\Pages\RunHistoryPage.xaml:18-20` |
| A13 | **No localisation layer and no explicit high-contrast handling** in App or Editor (strings are inline in XAML and code; no resource files). PPM ships 50 languages and FP has an 8-language track; AP will be the odd one out in the Store. | whole `src\AutomationPortal.App`, `src\AutomationPortal.Editor` |
| A14 | **Sibling actions fail late.** "Show in Files Portal" quietly falls back to Explorer (fine), but PPM actions throw "isn't installed / isn't running / turn on Allow links" only when the shortcut runs. The editor does not badge the step, and the Gallery does not say "Needs Power Plan Manager" up front. | `src\AutomationPortal.Actions.Windows\Devices\PowerPlanActions.cs:121-122`, `:160-168`; `PowerPlanManagerStatus.cs:23-24` |
| A15 | **Good and worth keeping:** friendly, specific empty states on every page; the red failure bar with "See details" (`LibraryPage.xaml:28-47`); "Pause all automations" with a banner; confirmation text that names the consequence ("2 automations use it and will stop working"); `ap export` never replacing a file (`docs\CLI.md`). |

#### Feature gaps against peers

| Gap | Peer | Note |
|---|---|---|
| Webhook or local HTTP trigger; any "another app told me" trigger | Power Automate, Tasker, Keyboard Maestro | No trigger id for outside events among the ~48 registered (`src\AutomationPortal.Triggers`); the pipe can only *run by name*. See recommendation 3. |
| Recently deleted, version history, "revert to yesterday" | Apple Shortcuts (iCloud), Keyboard Maestro | Only `.bak` before overwrite exists (review 1, H1). |
| Whole-library export/import in one file | Keyboard Maestro, Espanso | Only per-shortcut "Export…" (`LibraryPage.xaml.cs:246`) and "Move to OneDrive". |
| Per-shortcut link permission | Apple Shortcuts ("Allow running from…"), Stream Deck | See A8. |
| Step-level breakpoints and "run next step" | Power Automate for desktop | "Run from here" exists; pause-and-step does not. |
| Image or text anchors for recorded clicks | Power Automate, Keyboard Maestro | Survey item 7; recorder still replays positions. |
| Share-sheet style "accepts" declaration (which file types a shortcut wants) | Apple Shortcuts | Needed so Files Portal's submenu can show only shortcuts that fit the selection. |
| Plugin or action SDK for third parties | Stream Deck, PowerToys Run | Out of scope for now; the sibling contract in section 3 is the cheaper first step. |

#### New or improved features

| Feature | Effort | Risk | Notes |
|---|---|---|---|
| Recently deleted (30 days) and "Back up library now" | S | Low | Safety design in section 4. |
| First-run welcome: three cards (what a shortcut is with the description box, where the tray icon and Quick Run keys are, pick three starters) | M | Low | Changes nothing until Finish; never imports with hotkeys on. |
| Connected apps card (Settings) | M | Low | Reads each sibling's `hello`; buttons only open the sibling or the Store on click. |
| App event trigger with pipe `event` kind and PPM subscription adapter | M | Med | Caller identity check required (section 3.1). Rate cap per source. |
| "Accepts" on a shortcut (files, folders, images, text, anything) plus `list` returning it | S | Low | Additive field; old `.autoportal` files default to "anything". |
| Per-shortcut "Allow from links" replacing the global switch (keep the global one as "ask every time / never") | S | Med | Migration: global On becomes per-shortcut On only for shortcuts that have already been run from a link (history knows the source), otherwise Off. |
| Tray: Stop all running, last failure, paused icon overlay, Settings | S | Low | |
| Run History source filter and "started by: Desk Pal / Files Portal / link" | S | Low | Needs the caller name from the pipe (section 3.1). |
| Gallery and editor badge "Needs: Power Plan Manager (Pro)" with a Get button | S | Low | Survey item 11. |
| `automationportal://new?path=…` (already proposed): open the editor with a new shortcut whose input is those items | S | Low | Must only open the editor, unsaved; never run. Count as `pick` for prompting. |
| Localisation infrastructure (string resources, pseudo-locale run) | L | Med | Do before more UI is added. |

### 2.2 Files Portal

Files Portal is the most complete app of the four; findings concentrate on the edges that touch other apps
and on a few items its own docs still list as open.

#### UI/UX findings

| # | Finding | Where |
|---|---|---|
| F1 | **Two automation entry points with different words and different engines.** "Open in Automation Portal…" hands paths to the AP app by link (AP then asks, then shows a picker). "Run a flow…" (opt-in preview) loads a `.autoportal` file through a file picker *every time* and runs it inside Files Portal with its own copy of the engine, one at a time, with no run history. Users will not understand why one records history, shows AP's tiles and supports document actions and the other does not. | `src\FilesPortal.App\Commands\FileOps\OpenInAutomationPortalCommand.cs:14-47`; `src\FilesPortal.App\Automation\FlowRunner.cs:17-21`, `:40`, `:64`, `:93-113` |
| F2 | **"Flow" versus "shortcut".** AP calls them shortcuts everywhere; Files Portal's setting and menu call them flows ("Enable running automation flows (preview)", "Run a flow…"). | `src\FilesPortal.App\Views\Settings\GeneralPage.Automation.cs:9-11` |
| F3 | **Selections over 100 items are cut silently.** `SelectLocalPaths` stops at 100 and the command sends what is left; cloud placeholders, archive entries and non-file results are also dropped without a word. A user who selects 300 photos gets a shortcut run on 100. | `src\FilesPortal.Core\Commands\AutomationPortalPaths.cs:13`, `:21-37`; `OpenInAutomationPortalCommand.cs:42-47` |
| F4 | **Three prompts for one intent.** Right-click, "Open in Automation Portal…", AP's picker dialog, then Run. That is correct for an anonymous link, but Files Portal is a known sibling; a pipe call with a caller check can skip straight to a submenu of shortcut names (recommendation 4). | `OpenInAutomationPortalCommand.cs:47`; AP `src\AutomationPortal.App\AppHost.Launch.cs:167-181` |
| F5 | **In-process engine is pinned to a preview package** (`1.0.0-preview.4`) while AP itself is at 1.0.0. A flow saved by a newer AP can use actions the embedded engine lacks; the document action pack is deliberately left out. The failure text a user sees in that case should name the cause ("needs a newer Files Portal, or run it in Automation Portal"). | `Directory.Packages.props:30-34`; `src\FilesPortal.App\FilesPortal.App.csproj:151-155` |
| F6 | **External-link dialogs are a model for the suite**: they say who asked, what will happen, why it is risky per item, never launch programs from links and refuse to forward non-web schemes. Keep; reuse the wording pattern in AP and Desk Pal. | `src\FilesPortal.App\Activation\ExternalLaunchDialog.cs:19-20`, `:36`, `:58`, `:86-91`; `src\FilesPortal.Core\Activation\ExternalLaunchPolicy.cs:8-34`, `:72` |
| F7 | **`reveal` fails silently for a missing path.** A link to a file that no longer exists is dropped with no target and no message, so the app either does nothing or opens its default view. For a "Show in Files Portal" step at the end of a shortcut that just moved the file, this looks broken. Open the nearest existing parent and say "That item is no longer there". | `src\FilesPortal.Core\Activation\CommandLine.cs:389-417` |
| F8 | **Only two link verbs exist** (`open`, `reveal`); anything else falls back to a default request. There is no way to ask which verbs a build supports. | `CommandLine.cs:312-314` |
| F9 | **First-run card:** British and American spelling side by side ("Import favourites" heading, "Import favorites…" button), and a disabled "Coming soon" switch for the default file manager in non-Store builds. A disabled promise on the first screen is weaker than leaving the row out. | `src\FilesPortal.App\Views\Onboarding\FirstRunCard.xaml:129`, `:134`, `:146-150` |
| F10 | **Open items its own docs still carry** that are user-visible: a stale `settings.json` snapshot can overwrite edits made while the app runs; a read-only portable folder crashes at start; plugins in languages other than JavaScript are not sandboxed. Dates of those rows were not re-verified in code. | `docs\FOLLOWUPS.md` (WP06 rows), `docs\GAPS-OUTSTANDING.md` (N1) |

#### Feature gaps against peers

| Gap | Peer | Note |
|---|---|---|
| Scriptable state: current folder, selection, tabs, panes readable from outside | Directory Opus (DOpusRT, script objects), Total Commander (`%P %S`) | Custom buttons have placeholders (`%T` added in 0.1.0) but nothing outside the app can ask. `selection.get` was proposed; see the critique in 3.1. |
| Events: folder changed, selection changed, operation finished | Directory Opus script events | Proposed as an event pipe. Operation-finished is the valuable one; selection-changed is noisy and private. |
| Toolbar or context-menu items contributed by another app | Directory Opus, Explorer verbs | The "Run shortcut ▸" submenu is this, for one trusted sibling. |
| Folder-level automation ("when files arrive here…") created from the folder itself | Hazel, File Juggler | AP has `file.changed`; Files Portal has no "Automate this folder…" entry that opens AP's wizard pre-filled. |
| Saved operation recipes (rename many then move then tag) | Directory Opus commands | Covered by AP shortcuts once the submenu exists; no separate recipe system needed. |

#### New or improved features

| Feature | Effort | Risk | Notes |
|---|---|---|---|
| "Run shortcut ▸" submenu (pinned first, then those whose "accepts" fits the selection, then "More…") over AP's pipe, with the link picker as the fallback when AP is not running | M | Med | Files are passed as paths only; Files Portal does not touch them. AP shows progress and history. |
| Tell the user when a selection is trimmed ("Sending the first 100 of 312 items") or send in batches the user confirms | S | Low | |
| Rename "flow" to "shortcut"; make "Run a shortcut file…" clearly the offline option | S | Low | |
| `filesportal:` verb reference in the repo, versioned, plus `filesportal.exe --capabilities` printing JSON | S | Low | Already proposed; make the output the same `hello` shape as the pipes. |
| "Automate this folder…" on a folder's menu, opening `automationportal://new?path=<folder>&trigger=file.changed` | S | Low | Opens AP's wizard unsaved; nothing is created without the user finishing it. |
| Event feed limited to *operation finished* (copy, move, delete, extract: counts and target folder, no file names by default) | M | Med | Privacy: names only if the user turns on "Include item names". |
| `selection.get` only as a reply to a request Files Portal itself started (see 3.1), not as a free query | S | Low | |
| `reveal` on a missing item: open nearest parent with a notice | S | Low | Read-only navigation. |

### 2.3 Power Plan Manager

#### What the existing review says, checked in code

| Claim | Check |
|---|---|
| One constant pipe name for both editions; single instance; the loser logs "giving up" and never retries | Confirmed: `src\PowerPlanTray.Core\Services\StatusPipeServer.cs:40`, `:87-88`, `:120` |
| Pipe is read-only: `GET STATUS`, `GET PLANS`, `GET PRESETS`; 64-byte requests | Confirmed: `StatusPipeServer.cs:41`, `:189-191` |
| `ppm-test` scheme accepted in release code | Confirmed: `src\PowerPlanTray.Core\Services\PowerLink.cs:54` |
| Link verbs `plan, preset, limit, topup, pause, resume, calibrate, keepawake, show` | Confirmed: `PowerLink.cs:88-105` |

#### What it missed or got slightly wrong

| # | Finding | Where |
|---|---|---|
| P1 | **`show?page=EnergyMode` is refused.** The settings window has an `EnergyMode` page tag, but the link allow-list has 15 pages without it. AP copied the same 15, so both sides agree and both miss the page. | `PowerLink.cs:57-61` versus nav tags in `src\PowerPlanTray\SettingsWindow.xaml`; AP `src\AutomationPortal.Actions.Windows\Devices\PowerPlanActions.cs:125-129` |
| P2 | **AP uses more verbs than the review lists.** AP ships Top Up, Pause Charging, Resume Charging and Calibrate actions as well. Each one opens PPM's in-window confirmation, so an unattended shortcut stops there with no result and no timeout on AP's side. This strengthens the case for request ids and results (recommendation 6). | AP `PowerPlanActions.cs:232-272`, note text `:181-182` |
| P3 | **The pipe's doc comment still names "Flow Mate".** Harmless, but it is the kind of drift a shared contract page ends. | `StatusPipeServer.cs:34` |
| P4 | **AP's server check does not pin the publisher.** It accepts any package whose *name* matches `34458MindtheApp.PowerPlanManager[-Lite]` and ignores the publisher id after the underscore, and it trusts any exe under `WindowsApps` whose folder name starts that way. The data read is low-value, so this is a tidy-up, not an emergency; but the same helper is about to be generalised for Desk Pal, where it will gate write commands. Pin the full family name. | AP `src\AutomationPortal.Actions.Windows\Devices\PowerPlanManagerStatus.cs:82-83`, `:90-110` |

#### Position on the open design argument (pipe `SET` commands versus request ids)

Keep the pipe read-only. Reasons beyond those in PPM's review: (a) charging and preset actions change firmware
and fan behaviour, and the confirmation belongs in PPM's own window; (b) the URI already carries the user
gate, the Pro check and the foreground rule in one code path (`PowerLink.Decide`, `PowerLink.cs:117-125`),
and a second write path would have to copy all three; (c) AP gets what it actually needs, a result, from
`rid` plus `GET RESULT`. Extension: publish the same result as an event line on `SUBSCRIBE`
(`{"event":"result","rid":…,"ok":…,"error":…}`) so AP can wait on one connection instead of polling
`GET RESULT`, with a hard 10 s wait on AP's side and "still waiting for you to confirm in Power Plan
Manager" as the message for confirmable actions.

#### Feature gaps against peers (beyond the existing review)

| Gap | Peer | Note |
|---|---|---|
| Timed keep-awake with a visible countdown | PowerToys Awake, Amphetamine | In the existing review as item 10; raise its priority, because Desk Pal focus sessions and AP shortcuts both want "awake for N minutes" and an expiry is safer than a forgotten switch. |
| Per-app "while this runs" rules created from outside ("use High performance while this shortcut runs") | Process Lasso, G-Helper | A lease, like Desk Pal's holds: `plan?name=…&minutes=N` reverting to the previous plan. Reverting by time is safer than relying on a second link that may never arrive. |
| Status readable while control is off | AlDente (read-only widgets), BatteryBar | Today status is hidden unless links are allowed. Split "let other apps read status" from "let other apps control". |

#### New or improved features (in addition to the 20 in the existing review)

| Feature | Effort | Risk | Notes |
|---|---|---|---|
| Add `EnergyMode` to the `show` page list on both sides | S | Low | |
| Plan lease: `plan?name=X&minutes=N` restores the plan that was active before, unless the user switched by hand in between | S | Med | Never restores over a manual change; shows in history as "Other app (until 15:30)". |
| Separate read switch from control switch | S | Low | Read exposes plan, mode, on-AC, battery percent only. |
| Results on the event stream as well as `GET RESULT` | S (after rid) | Low | |
| "Requests from other apps" log names the caller when it came with a `rid` issued after a pipe `hello` (the pipe knows the client process; the link does not) | M | Low | Makes the log useful: "Automation Portal asked for Balanced, 14:02, done". |

### 2.4 Desk Pal

The repository is a toolchain proof: one 97-line `App.xaml.cs`, a tray icon and a test toast. The brief and
the integration plan are thoughtful; the review below is of the scaffold and of the design.

#### UI/UX findings (scaffold)

| # | Finding | Where |
|---|---|---|
| D1 | **A nudge fires 3 seconds after every start.** Fine for the toolchain test; it must not survive into beta 2, where "start with Windows" would greet every sign-in with a stretch toast. | `src\DeskPal\App.xaml.cs:46` |
| D2 | **Tray menu is "Test nudge" and "Exit"; left click does nothing.** The tray is the whole product surface until the Today window exists. Minimum for a usable beta: status line ("Next: stretch in 12 min"), "+250 ml", Start focus, Pause for 30 min / 1 h / until tomorrow, Open, Exit. | `App.xaml.cs:28-35`, `:38-43` |
| D3 | **Clicking the toast body does nothing** (logged as "(body click)"). Users click bodies more than buttons; open the stretch card. | `App.xaml.cs:63-64` |
| D4 | **"Done" and "Skip" are logged, not recorded,** and Snooze lives in a timer that is lost on exit. This is exactly what the planned hold model fixes; build that before any more toast work. | `App.xaml.cs:62-70` |
| D5 | **No single-instance guard.** Starting the exe twice gives two tray icons and double nudges. The plan mentions detecting the first instance by pipe at step 5b; a mutex is needed from beta 2. | `App.xaml.cs:22-47` |
| D6 | **Log file grows without limit** (append-only, never trimmed). Add a size cap with one rollover file. Append-only is the safe direction; do not "fix" it by truncating in place. | `App.xaml.cs:86-96` |
| D7 | **Toast with three buttons and two lines is right.** Keep "Done / Snooze 5 min / Skip" in that order; add the merged-nudge wording from the brief early, because it is the product's main promise ("never stacks"). | `App.xaml.cs:51-59`; `docs\BRIEF.md` (Concept) |

#### Design critique (brief and integration plan)

- **Strong:** leases instead of a global pause; seated holds for calls; "pipe for machines, link for humans";
  caps on outside water logging so streaks cannot be farmed; no window titles or calendar subjects in any
  payload; Desk Pal fully usable without AP.
- **Risk of a naggy first week.** Defaults in the brief add up to a stretch every 30 minutes, eyes every 20
  and hydration on top. Even merged, that is 3-4 prompts an hour. Start new users on a "gentle" preset
  (movement 45 min, eyes folded into movement, water only when behind) and let Sprout suggest stepping up.
- **Full-screen and Do Not Disturb are listed for step 5; they are needed at step 1.** A wellbeing toast over
  a presentation in the first beta is the review that sinks the app. `SHQueryUserNotificationState` is one call.
- **Accessibility of a mascot app:** every Bloop card needs a text equivalent, reduced-motion support
  (respect the Windows animation setting) and no information carried by Sprout's colour alone.
- **The mapping screen inside Desk Pal duplicates what AP is for.** See 3.1: prefer events pushed to AP and
  mapped there, with Desk Pal's settings page showing a short "Works with Automation Portal" card and five
  one-click starters.
- **`cheer` is delightful and cheap to abuse.** Keep the plan's 1 per 10 min cap and make it obey holds, Do
  Not Disturb and full-screen like any nudge.

#### Feature gaps against peers

| Gap | Peer | Note |
|---|---|---|
| Strict mode (break cannot be skipped) and "postpone only once" | Stretchly, Workrave | Optional; off by default, fits "calm" tone users. |
| Natural-break detection crediting micro-pauses, with a visible "you've been away, timer reset" | Workrave, BreakTimer | In the brief (idle 5+ min). Show it, so users trust the timer. |
| Per-monitor break card choice and never on a shared screen | Stretchly (all screens), Teams-aware tools | Covered by the "presenting" hold through AP; needs a native fallback for users without AP. |
| Weekly schedule with different hours per day, holidays | Stretchly | Brief has working hours and days; add per-day hours before Store. |
| Export of the user's own history (CSV) | Workrave stats, hydration apps | Local, user-started, new file each time. |

#### New or improved features

| Feature | Effort | Risk | Notes |
|---|---|---|---|
| Hold (lease) scheduler, command router, internal events | M | Low | As planned; the base for everything else. |
| Single-instance guard, real tray menu, left click opens Today flyout | S | Low | |
| Do Not Disturb and full-screen check before any card or toast | S | Low | Move from step 5 to step 1. |
| Gentle / Standard / Active presets on first run, with a preview of "what a day looks like" | S | Low | |
| "Why now?" line on every nudge ("47 min sitting") and a "Too often" button that lengthens the interval | S | Low | Turns skips into settings without a settings visit. |
| History export to CSV | S | Low | Never overwrites: `DeskPal history (2).csv`. |
| Pipe, `dp`, `deskpal://` | M | Med | Step 5b, on the suite contract. |

---

## 3. Cross-app integration

### 3.1 Suite contract proposal

One page, copied into all four repos, versioned (`suite-contract: 1`). It does **not** force the existing
pipes into one message shape; it fixes the parts where they must agree.

**Naming**

| Thing | Rule | Today |
|---|---|---|
| Link scheme | lower-case product name, no punctuation: `automationportal`, `filesportal`, `powerplanmanager`, `deskpal` | `powerplanmanager-lite` exists as a second scheme; keep it, but treat it as a legacy alias once per-edition pipes report the edition |
| Pipe | `<Product>-<user SID>` with `CurrentUserOnly`; optional `-<8 hex>` for a non-default data root | AP follows this (`src\AutomationPortal.Core\Integration\Pipe.cs:76-77`); PPM uses the machine-wide name `PowerPlanManager.Status` with an ACL instead |
| CLI | two-letter alias next to the exe: `ap`, `dp`; `fp` and `ppm` only if a real need appears | |

**Hello (capabilities).** Every pipe answers a first request (`ping` for AP, `GET HELLO` or a richer
`GET STATUS` for PPM, `ping` for Desk Pal) with the same fields:

```json
{ "app":"PowerPlanManager", "version":"1.0.13", "contract":1,
  "edition":"pro|lite|store|portable", "entitled":true,
  "allowRead":true, "allowControl":false,
  "capabilities":["link.plan","link.keepawake","link.preset","events.plan","events.battery"] }
```

Callers branch on `capabilities`, never on edition names or version numbers. This removes AP's hard-coded
Lite/Pro table (`PowerPlanActions.cs:160-168`) and lets AP's editor badge a step before it runs. For apps
without a pipe (Files Portal today) the same JSON comes from `--capabilities`.

**Division of labour**

| Channel | Use | Rule |
|---|---|---|
| Link | A human or an unknown app | Never changes state without a click in the receiving app, except navigation (`show`, `reveal`, `open`). Per-shortcut or per-verb "always allow" is the receiving app's choice. |
| Pipe, read | Status, lists, results, event stream | Allowed when `allowRead`; size and time caps; one line in, one line out (or a stream after `SUBSCRIBE`). |
| Pipe, write | Only where the receiving app defines it (AP `run`/`import`/`event`, Desk Pal verbs). PPM stays read-only. | Caller is identified; first use confirmed in the receiving app; revocable in its Settings. |

**Trust model (the biggest inconsistency today).**

| App | Today |
|---|---|
| AP pipe | Any same-user process may `run` any shortcut and `import` new ones, with no prompt and no caller check (`src\AutomationPortal.Core\Integration\ShortcutCommands.cs:25-31`, `:121-139`). Links, by contrast, always prompt. |
| PPM | Links gated by a global switch; pipe read-only; the *client* (AP) checks the server's package. |
| Files Portal | Links judged per target by risk; no global switch. |
| Desk Pal (planned) | Caller resolved by process, confirmed once per calling app, remembered, revocable. |

Proposal: adopt Desk Pal's planned model as the suite rule for every pipe write. The server resolves the
client process (`GetNamedPipeClientProcessId`, then full package family name, else signed exe path), shows
"**Files Portal** wants to run your shortcuts. Allow / Not now" once, and lists allowed apps in Settings.
`ap.exe` beside the app is always allowed (it is the user's own terminal). Unknown unpackaged callers are
shown by exe path with a stronger warning. Both directions pin the **full** family name (name plus publisher
id), fixing P4.

**Limits (same numbers everywhere).** Request line at most 64 KB for AP (it carries import text), 4 KB for
the others; read timeout 3 s; connect timeout 500 ms for "is it running", 2 s for a real request; replies
parsed strictly with a size cap. Desk Pal's planned 150 ms connect is too tight for a PC waking from sleep.

**Errors.** Stable lower-case codes shared by all apps, with a human message beside them:
`not-allowed`, `needs-pro`, `declined`, `not-found`, `unsupported`, `invalid`, `busy`, `rate-limited`,
`timeout`. PPM's proposed `links_off` becomes `not-allowed`.

**Events: one hub, two transports.** Three proposals on the table point in different directions: Files
Portal *emits* on an event pipe, PPM offers `SUBSCRIBE`, and Desk Pal *pushes* by running AP shortcuts by
id from its own mapping screen. Recommendation:

1. AP gains one trigger, **"When another app reports…"** (`app.event`), with a picker of sibling, event
   name and optional field conditions.
2. Siblings that are allowed to talk first (Desk Pal, Files Portal) send
   `{"kind":"event","name":"deskpal.focus.ended","input":"<plain sentence>","json":"{…}"}` on AP's pipe.
   No shortcut ids are stored outside AP, so renames, deletes, pause-all and history all keep working.
3. Siblings that never initiate (PPM) expose `SUBSCRIBE`; AP holds one connection while at least one
   automation uses a PPM event and turns each line into the same trigger.
4. If AP is not running, pushers drop the event and count it (as Desk Pal's plan says). Nobody starts
   another app in the background.
5. Loop guard as in Desk Pal's plan: events caused by an outside command carry `cause:"external"` and do
   not fire triggers; each event name fires at most once per 60 s per source.

Desk Pal keeps a small card instead of a mapping grid: sibling status, five starter automations (sent
through `import`, reviewed in AP) and a "Manage in Automation Portal" button.

**Critique of the proposals already exchanged**

| Proposal | Verdict |
|---|---|
| Files Portal event pipe | Build only *operation finished* and *folder opened* (opt-in); skip selection-changed (noisy, private, and a polling temptation). Push to AP rather than hosting a second server. |
| `selection.get` | Do not expose as a free query: any same-user process could read what the user has selected at any moment. Offer it only as the payload of a request Files Portal itself started (the submenu run already carries the paths), or behind the per-caller confirmation. |
| `automationportal://new?path=` | Good and cheap. Must open an unsaved editor only. Extend with `&trigger=file.changed` for "Automate this folder…". |
| "Run shortcut ▸" submenu | Highest-value item for Files Portal. Needs AP's "accepts" field and the caller check; fall back to the `pick` link when AP is not running. |
| Versioned verb doc plus `--capabilities` | Yes; make the output the suite `hello` so one parser serves pipes and CLIs. |
| PPM pipe commands with results | Replace with `rid` plus `GET RESULT` and results on the stream; the pipe stays read-only. |
| PPM `SUBSCRIBE`, capabilities | Yes to both; capabilities first (S), stream second (M). |
| Desk Pal pipe verbs | Good as designed. Change: events go to AP as `event`, not as runs by stored id; connect timeout 500 ms; full family-name pinning. |

### 3.2 Mismatches between code and docs

| # | Mismatch | Evidence |
|---|---|---|
| 1 | **Pipe `import` shows no review in the app.** Desk Pal's plan (section 4) relies on "AP shows its own import review". In code, `import` on the pipe saves the shortcut immediately and returns a one-line warning to the caller; the review dialog (`FinishImportAsync`) is used for files, links and the gallery only. Hotkey, alias and Send To are stripped, so nothing binds by itself, but a shortcut with destructive steps lands in the library unseen and can then be started with `run`. | AP `ShortcutCommands.cs:121-139`; DP `docs\INTEGRATION-AUTOMATION-PORTAL.md` section 4; AP `docs\review\app-followups.md` ("CLI import prints warnings") |
| 2 | **Pipe has six kinds, not four.** `launch` and `export` exist besides `ping`, `list`, `run`, `import`. The Desk Pal corrections note says they "stay internal", but nothing enforces that; any same-user client can send them. Under the caller check, `launch` and `export` should be accepted only from the app's own exe and `ap.exe`. | AP `Pipe.cs:22`; `ShortcutCommands.cs:25-31`; `src\AutomationPortal.App\AppHost.Launch.cs:144-150` |
| 3 | **No size cap or read timeout on AP's pipe** (`ReadLineAsync` on an open stream), and `ping` returns an empty success with no version. PPM caps at 64 bytes and 3 s. | AP `Pipe.cs:101`; `ShortcutCommands.cs:27` |
| 4 | **"Registry probe misses packaged apps"** (Desk Pal plan, Detection) does not match this PC: `HKCR\powerplanmanager` and `HKCR\filesportal` both exist with a `URL Protocol` value and no shell subkey, which is how AP's probe finds them. AP's probe is therefore adequate; Desk Pal's suggested `FindUriSchemeHandlersAsync` is also fine. Pick one for the suite and confirm on a clean Store-only machine. | AP `src\AutomationPortal.Actions.Windows\Apps\FilesPortalActions.cs:32-40`, `PowerPlanActions.cs:96-108` |
| 5 | **Files Portal detects AP by package family; AP detects Files Portal by scheme.** A portable AP build registered through its own unpackaged registration is invisible to Files Portal, which then offers the Store page instead. | FP `OpenInAutomationPortalCommand.cs:18`, `:31`, `:54` |
| 6 | **`show` page lists omit `EnergyMode`** on both sides (P1). | PPM `PowerLink.cs:57-61`; AP `PowerPlanActions.cs:125-129` |
| 7 | **PPM's review under-counts AP's verbs** (P2): top-up, pause, resume and calibrate are shipped AP actions. | AP `PowerPlanActions.cs:232-272` |
| 8 | **Stale product name** "Flow Mate" in PPM's pipe comment; "flow" and `.flowmate` wording in Files Portal's UI text. | PPM `StatusPipeServer.cs:34`; FP `GeneralPage.Automation.cs:9-11` |
| 9 | **Engine version skew.** Files Portal references engine packages `1.0.0-preview.4`; AP is `1.0.0`. Nothing tells the user which engine a flow ran on. | FP `Directory.Packages.props:30-34`; AP `Directory.Build.props:8` |
| 10 | **AP pre-check before a PPM link is advisory only.** When the pipe is absent or slow, the link is sent anyway and AP reports success; PPM may then refuse it and only a toast (if notifications are on) says so. Documented in AP's action note, but the shortcut continues as if it worked. | AP `PowerPlanManagerStatus.cs:36-44`; `PowerPlanActions.cs:170-179` |

### 3.3 Cross-app scenarios

1. **Call-aware desk.** AP `call.started` → Desk Pal `pause source=call` (seated hold, 90 min cap) and PPM
   keep-awake for 90 minutes. `call.ended` → `resume`, then `nudge stretch --soon`. Needs: Desk Pal pipe,
   PPM timed keep-awake. Nothing touches files.
2. **Deep focus.** Desk Pal `focus.started` event → AP shortcut: mute chat apps, PPM plan lease "Quiet" for
   the session length, Windows Do Not Disturb left alone. `focus.ended` → restore; AP's history shows both
   runs with source "Desk Pal".
3. **Process these files.** In Files Portal select 40 scans → right-click → Run shortcut ▸ "OCR and file
   by date". AP runs with the 40 paths, shows progress on its tile, and the last step is "Show in Files
   Portal" on the output folder. Needs: submenu, "accepts", reveal on the result.
4. **Battery-aware batch.** A long AP shortcut (video convert) first reads PPM status: on battery below
   30 % → ask; on AC → plan lease "High performance" for the run, keep-awake until done, then back.
   Needs: `GET STATUS` v2 (`onAc`, `batteryPercent`), plan lease, result ids.
5. **Automate this folder.** In Files Portal right-click Downloads → "Automate this folder…" → AP's wizard
   opens with trigger "When files change in Downloads" pre-filled and the gallery's "Organize Downloads"
   suggested. The user reviews and saves in AP.
6. **Charge limit by place.** AP `net.wifiJoined` (home) → PPM limit 80 %; on leaving, with a calendar
   event tagged travel tomorrow → top-up. PPM asks for confirmation in its own window for top-up; AP waits
   on the result id and reports "Waiting for you to confirm in Power Plan Manager".
7. **Walk means walk.** Desk Pal `break.started kind=walk` → AP pauses media and, after 30 s, locks the PC
   (opt-in). On unlock Desk Pal treats the session lock as "away" natively and Sprout celebrates.
8. **End of day.** Desk Pal `day.summary` → AP appends a row to a spreadsheet (append only), PPM switches
   to "Power saver", and Files Portal is not involved. If AP is not running, Desk Pal counts the skipped
   event and says so in tomorrow's summary.

### 3.4 Consistency across the suite

| Area | Today | Suggestion |
|---|---|---|
| Outside-control setting | AP: "Allow automationportal:// links" (`SettingsPage.xaml:182`). PPM: "Allow links from other apps". Files Portal: no setting, per-link prompt. Desk Pal plan: per-app allow list. | One section name in every app: **"Other apps"**, with (a) links: Ask every time / Never, (b) allowed apps list with Remove, (c) a recent-requests log. |
| Vocabulary | shortcut (AP), flow (Files Portal), preset and plan (PPM), nudge and hold (Desk Pal) | "Shortcut" everywhere for AP items. Keep the others; they are domain words. |
| Theme choice wording | AP: "Same as Windows" (`SettingsPage.xaml:52`). Files Portal: "Use Windows setting" (`FirstRunCard.xaml:41`). | Pick one ("Use Windows setting" matches Windows 11). |
| Spelling | British in AP ("colour", "favourites"), mixed in Files Portal (same card: `FirstRunCard.xaml:129` and `:134`), British strings in PPM's en-US resources. | Decide en-GB or en-US for the en-US resource set and run one pass; Store listings are en-US. |
| Tray left click | AP: Quick Panel (`TrayService.cs:35`). PPM: tile popup. Desk Pal: nothing yet. | Left click = the app's small flyout; right click = menu ending in Settings, then Exit `<App name>`. AP's menu lacks Settings; Desk Pal should follow from beta 2. |
| Paused state | AP: tooltip text only (`TrayService.cs:42-46`). Desk Pal plan: icon and tooltip change during a hold. | Same visual rule: a small pause badge on the icon whenever the app is not doing its normal job. |
| Tray library | AP and Desk Pal use the same notify-icon package; PPM and Files Portal have their own. | No change needed; note that the dev shell is elevated and `CurrentUserOnly` pipes refuse mixed-elevation pairs, so sibling tests must be started from Explorer. |
| First run | Files Portal: full welcome with skip. PPM: three steps. AP: none. Desk Pal: none yet. | Same skeleton: 3 cards, Skip changes nothing, re-runnable from Settings › General. |
| Sibling promotion | Files Portal: menu item that opens the Store when AP is missing. AP: error sentence with a Store search link (`PowerPlanActions.cs:121-122`). | One "Works with" card per app in Settings, user-clicked Store links by product id only, never at start-up, never as a toast. |
| Localisation | PPM 50 languages, Files Portal 8 in progress, AP none, Desk Pal none. | AP needs string resources before it grows further; Desk Pal should start with them. |
| Accessibility | AP names most controls; PPM names few (per its review); Files Portal handles high contrast with a workaround (`docs\FOLLOWUPS.md`, WP05). | Release gate for all four: keyboard-only pass, one contrast theme, Narrator on the main flow. |

---

## 4. Safety: features that touch user data

The owner's rules apply to every item: never overwrite, never modify or move originals, recursive deletes
only on folders the app created and can identify, moves are copy, verify, then delete; write verbs are
confirmed inside the receiving app.

| Feature | How it avoids destroying or overwriting data |
|---|---|
| AP "Recently deleted" | Delete becomes a move of the shortcut file into `<library>\Deleted\<id>-<timestamp>.autoportal` inside the app's own library: copy, verify the copy parses, then remove the original. Purge after 30 days touches only files in that folder that match the naming pattern. Restore never overwrites: if the id or name exists again it restores as "Name (2)". The delete prompt changes to "You can restore it for 30 days". |
| AP "Back up library now" / export all | Writes a new single file chosen by the user; if the name exists it saves as `Name (2).apbackup`, as `ap export` already does. Import from a backup adds copies and never replaces existing shortcuts; it goes through the normal import review with hotkeys off. |
| AP pipe `import` from siblings | Shown in AP's import review before saving (today it saves first). Declining leaves nothing behind. Imported starters arrive with hotkey, alias and Send To off. |
| AP pipe `run` from Files Portal with file paths | Files Portal passes paths only and performs no file operation itself. What the shortcut does to those files is governed by AP's existing rules (destructive steps badged; Replace made recoverable in review 1, C1). First use per calling app is confirmed in AP; shortcuts containing delete or replace steps always show a one-line confirmation naming the count of items when started from another app. |
| Files Portal "Run shortcut ▸" | No write verb is added to `filesportal:` links. The selection is never changed by the menu itself. More than 100 items: the user is told and chooses "first 100" or "all, in batches"; nothing is silently dropped. |
| `automationportal://new?path=` and "Automate this folder…" | Opens an unsaved editor or wizard. Nothing is created, run or watched until the user saves in AP. Path text is displayed, not acted on. |
| Files Portal `reveal` on a missing item | Navigation only: opens the nearest existing parent and shows a notice. No create, no probe of network paths before consent (current rule kept, `CommandLine.cs:389-395`). |
| Files Portal operation-finished events | Read-only notifications after the fact; counts and target folder only, item names off by default. No verb lets another app start, cancel or alter an operation. |
| PPM plan lease and timed keep-awake | No user files involved. Restores the *previous* plan only if the user has not switched by hand meanwhile; never edits or deletes plans; expires by itself so a crashed caller cannot leave the PC in the wrong state. Fan, firmware and helper actions stay unreachable from links and pipes. |
| PPM `GET RESULT`, `SUBSCRIBE`, status v2 | Read-only; results kept in memory only (last 16). |
| Desk Pal `water`, `pause`, `nudge` from other apps | Outside entries are tagged with their source, capped per day, and undoable in Today; undo removes only the tagged entry. Holds always expire. No file access. |
| Desk Pal history export | User-started, writes a new CSV, unique name if one exists (`DeskPal history (2).csv`); the stats store is opened read-only for the export. |
| Desk Pal log size cap | Roll over by renaming to `deskpal.1.log` only after the new file is open; never truncate the live file in place. Only files in Desk Pal's own log folder are touched. |
| AP per-shortcut link permission (migration) | Settings migration writes a new settings version alongside the `.bak` the store already makes; no shortcut file is rewritten until the user changes its permission. |

Any item above that adds or changes a write path (first four rows, and the Files Portal submenu) should get
an independent code review before a build is handed over, with the reviewer given the code and not a summary.

---

## 5. Open questions for the owner

1. **Hub or mesh?** Should AP be the single place where sibling events are mapped to shortcuts (this
   report's recommendation), or should each sibling keep its own mapping screen as Desk Pal's plan has it?
2. **Trust on AP's pipe.** Is a one-time "Allow Files Portal / Desk Pal to run your shortcuts" confirmation
   acceptable, given that today any same-user process may run and import without one? `ap.exe` would stay
   exempt.
3. **Files Portal's in-process engine.** Keep "Run a flow…" as an offline fallback, or retire it once the
   submenu over AP's pipe exists? Keeping both means maintaining engine parity (document actions, version).
4. **Lite and Pro side by side** in PPM: supported or not? It decides per-edition pipe names versus a
   "the other edition is running" message. (Also asked in PPM's own review.)
5. **Read versus control in PPM.** May other apps read plan and battery state when control is off?
6. **Per-shortcut link permission** in AP instead of the global switch: wanted before the next Store update?
7. **Language of the en-US strings:** British or American spelling across the suite?
8. **Localisation order:** does AP get string resources now (before more UI), and does Desk Pal start with
   them?
9. **Desk Pal defaults:** is a "gentle" preset as the default acceptable, even though it shows less of the
   product in the first hour?
10. **End-to-end test ownership.** None of the pipes has been exercised against the real sibling on a Store
    install (PPM's review says the same). Which machine and which session owns the first run, started from
    Explorer because the dev shell is elevated?

Questions that could not be settled from code and were not put to the other project sessions: whether Files
Portal already has a branch for the event pipe or `selection.get`; whether Desk Pal's hold scheduler has
been started outside the repository; and whether the `settings.json` and read-only portable folder rows in
Files Portal's follow-up list are still current.
