# Automation Portal User Guide

Automation Portal is a Windows tray app that turns small chores into one-click shortcuts. A shortcut is a list of steps the app does for you. You can run a shortcut from a button, a keyboard shortcut, a search bar, a command line, or let an automation run it for you when something happens.

This guide is written in plain English for everyday users. Wording in **bold** is the wording used in the app. Places where a picture belongs are marked `[screenshot: ...]`.

## Contents

1. [What you need](#1-what-you-need)
2. [Install, update and uninstall](#2-install-update-and-uninstall)
3. [A first look](#3-a-first-look)
4. [Shortcuts and the Library](#4-shortcuts-and-the-library)
5. [The shortcut editor](#5-the-shortcut-editor)
6. [Quick Run and the command palette](#6-quick-run-and-the-command-palette)
7. [The tray icon and the Quick Panel](#7-the-tray-icon-and-the-quick-panel)
8. [Keyboard shortcuts](#8-keyboard-shortcuts)
9. [Automations](#9-automations)
10. [Snippets and typed phrases](#10-snippets-and-typed-phrases)
11. [The macro recorder](#11-the-macro-recorder)
12. [The Gallery](#12-the-gallery)
13. [Run History and Undo file changes](#13-run-history-and-undo-file-changes)
14. [AI steps](#14-ai-steps)
15. [The `ap` command line](#15-the-ap-command-line)
16. [Explorer, Send to, Desktop and Start menu](#16-explorer-send-to-desktop-and-start-menu)
17. [Other apps, links and the tools pipe](#17-other-apps-links-and-the-tools-pipe)
18. [Settings](#18-settings)
19. [Plans: Free, Plus and Pro](#19-plans-free-plus-and-pro)
20. [Limits of the Store version](#20-limits-of-the-store-version)
21. [Where your data lives, backup and removal](#21-where-your-data-lives-backup-and-removal)
22. [Privacy](#22-privacy)
23. [Troubleshooting and FAQ](#23-troubleshooting-and-faq)
24. [Support](#24-support)

---

## 1. What you need

- Windows 11 (build 22000 or later). Automation Portal needs Windows 11.
- A PC with an x64 or Arm64 processor. Both are supported.
- An internet connection only for steps that use the internet (weather, web pages, AI services you set up) and for buying add-ons.
- On-device Windows AI needs a Copilot+ PC and the Microsoft Store version (see [AI steps](#14-ai-steps)).

## 2. Install, update and uninstall

### Install from the Microsoft Store

The Microsoft Store is the only way to install and update Automation Portal.

1. Open the Microsoft Store and search for **Automation Portal**, or open its Store page.
2. Select **Get** (or **Install**).
3. Start it from the Start menu. It then lives in the tray, near the clock.

Updates come through the Store like any other app. To remove it, use Settings > Apps > Installed apps > Automation Portal > Uninstall. Windows then deletes the app's own data folders (see [Where your data lives](#21-where-your-data-lives-backup-and-removal)).

`[screenshot: the Microsoft Store page for Automation Portal]`

## 3. A first look

The first time you open the app, a short welcome appears (**Welcome to Automation Portal**). It takes about a minute and you can skip it any time (**Skip for now**).

1. **Hello! Let's do the boring bits for you.** An introduction.
2. **Pick a few to start.** Tick ready-made shortcuts you like. They are yours to change or remove later.
3. **Try one right now.** A harmless sample, "Make it shout", turns text into capital letters. It touches nothing on your PC.
4. **Send crash reports.** A ticked box. Untick it if you do not want crash reports (see [Privacy](#22-privacy)).
5. Quick Run keys and **Start with Windows**.

You can change all of this later in Settings. To see the welcome again, go to Settings > About > **Show welcome again**.

`[screenshot: the welcome window, step "Pick a few to start"]`

The main window has a sidebar with these pages: **All Shortcuts**, **Pinned**, **Automations**, **Snippets**, **Gallery**, **Run History**, and **Settings**. Folders you create appear under All Shortcuts. The search box at the top searches your shortcuts and the Gallery (Ctrl+F). Ctrl+K opens the command palette.

`[screenshot: the main window with the sidebar and the Library]`

Closing the window does not quit the app. A message says **Automation Portal is still running**. Find it in the tray icons near the clock; right-click it to quit.

## 4. Shortcuts and the Library

### What it does

The Library (**All Shortcuts**) holds your shortcuts as tiles. Each shortcut has a name, an icon, a colour, and optional extras: an alias for Quick Run, a hotkey, a folder, pinned or not.

### Where it is

Sidebar > **All Shortcuts** (Ctrl+1). **Pinned** (Ctrl+2) shows only pinned shortcuts.

### Make a shortcut

Choose **New shortcut** (Ctrl+N). The menu offers:

- **Start blank**: an empty shortcut you fill with steps.
- **Describe it**: type a sentence such as "Every morning, tell me the weather and open Outlook" and choose **Create draft**. This needs an AI set up (see [AI steps](#14-ai-steps)). You always get a draft you can edit.
- **Browse the Gallery**: ready-made shortcuts.
- **Record a new shortcut**: do it once and Automation Portal writes down your clicks and typing (see [The macro recorder](#11-the-macro-recorder)).
- **Import...**: add a shortcut from an `.autoportal` file. You see what it does first, and nothing is replaced.

`[screenshot: the New shortcut menu]`

### Everyday actions

Right-click a tile (or select it and use the keys):

| Action | How |
|---|---|
| Run | Enter, or click the tile |
| Edit | Ctrl+E |
| Duplicate | Ctrl+D |
| Rename shortcut | F2 |
| Delete shortcut | Delete (Ctrl+Z brings it back) |
| Pin / unpin | Right-click > Pin |
| Move to folder | Right-click > **Move to folder**, or drag it onto a folder |
| Export shortcut | Right-click > **Export shortcut** (saves an `.autoportal` file) |
| Give it a hotkey | Right-click > **Give it a hotkey**, then press the keys. Use at least one of Ctrl, Alt or Win |
| Add to desktop | Right-click > **Add to desktop** |

Make a **New Folder** from the sidebar. Folders can be renamed, given an icon (**Change icon**) and deleted (**Delete folder**).

### Dropping files on a tile

Drag files or folders onto a tile to run the shortcut on them. A sheet asks **Run "<name>" with what you dropped?** and lists what will be used, any steps that change files, and anything left out because the shortcut does not take it.

### Recently deleted

**Recently deleted** (in the Library toolbar) restores shortcuts you deleted in the last 30 days. Automations that used a restored shortcut work again. You can **Delete permanently** or **Empty Recently deleted**.

### Settings and defaults

- A new shortcut can be shown in Quick Run (on by default).
- Tile look: Settings > Appearance > Shortcut tiles (**Cards** by default or **Compact**), and Settings > General > Shortcut tiles (**Neutral** by default or **Gradients**).

### Free and paid

Free holds up to 5 shortcuts; Plus up to 25; Pro as many as you like. See [Plans](#19-plans-free-plus-and-pro). If you have more than your plan allows, they all keep working; only making new ones stops.

### Data

Each shortcut is one `.json` file in the Library folder (`shortcuts` subfolder). See [Where your data lives](#21-where-your-data-lives-backup-and-removal).

## 5. The shortcut editor

### What it does

The editor is where you build a shortcut from steps (called actions in some places). Steps run top to bottom. Each step has a short sentence with blanks you fill in, such as "Resize {images} to {width} wide".

### Where it is

Select a shortcut and press Ctrl+E, or choose **New shortcut**.

### How to use it

1. Search the list of steps (Ctrl+F in the editor), then add one.
2. Fill in the blanks. A blank can use the output of an earlier step (a "magic variable"), the **Shortcut Input**, the clipboard, today's date, or **Ask Each Time**.
3. Use **If**, **Choose from Menu**, **Repeat** and **Repeat with Each** for choices and loops. Their parts move together.
4. Press F5 to try it, Shift+F5 to stop.
5. For a step that can fail, set what happens on error: stop, carry on, or retry.

`[screenshot: the editor with a few steps and the step list]`

Handy editor keys: Ctrl+C, Ctrl+X, Ctrl+V (copy, cut, paste steps), Ctrl+D (duplicate), Ctrl+A (select all), Ctrl+Z and Ctrl+Y (undo, redo), Delete, Alt+Up and Alt+Down (move), Shift+F10 (step menu), Esc (clear selection).

### Settings and defaults

- **Show advanced actions** (Settings > Advanced) is off by default. It adds power-user steps such as running commands.
- Steps that can change or delete things are flagged when you add them or import a shortcut, and the app shows which powerful steps an imported shortcut contains before you add it.

### Data

Editing saves into the shortcut's file. Earlier versions of each shortcut are kept per PC in the `Versions` folder.

## 6. Quick Run and the command palette

### Quick Run

Press **Win+Alt+Space** anywhere (change it in Settings > Keyboard > **Quick Run**). A small bar opens. Type a shortcut name or its alias (for example "gm" for Good Morning) and press Enter.

- Enter runs the shortcut.
- Tab lets you give the shortcut some text to work on (**Type the input**), or choose a shortcut to run on the text.
- Ctrl+Enter opens it in the editor.
- Esc closes the bar.
- If nothing matches, you will see **Nothing found. Try another word...**

`[screenshot: Quick Run bar with a few matches]`

**Run on selected text**: select text in any app and press **Ctrl+Alt+R** (change it in Settings > Keyboard). Pick a shortcut to run on it.

### Command palette

Press Ctrl+K inside the main window. Type a shortcut, a setting or a command (new shortcut, new automation, open a page, back up, restore, import, Recently deleted, Keyboard shortcuts). Enter runs or opens, Ctrl+Enter edits, arrows choose, Esc closes. Gallery shortcuts also appear, labelled ready-made.

## 7. The tray icon and the Quick Panel

### Tray icon

Automation Portal keeps running in the tray when you close the window (Settings > General > **Keep running in the tray**, on by default). Hover the icon to see what is running ("Running ...", "... didn't finish") and the next scheduled automation.

Right-click the icon for:

- **Open Automation Portal**
- Your pinned shortcuts (and **More pinned...**)
- **Pause automations** (**For 30 minutes**, **For 1 hour**, **Until tomorrow**, **Until I resume**) and **Resume now**
- **Exit Automation Portal**

`[screenshot: the tray menu]`

### Quick Panel

Press **Ctrl+Alt+P** (change it in Settings > Keyboard > **Quick Panel at the mouse pointer**) to pop up your pinned shortcuts at the mouse pointer. The panel also offers **Open Automation Portal**, **Quick Run**, **Record**, and **Pause automations** or **Resume automations**. If nothing is pinned, it says so and tells you how to pin.

## 8. Keyboard shortcuts

The in-app cheat sheet is **Keyboard shortcuts**: press Ctrl+/ or F1.

**Global (work in any app)**

| Keys | What it does | Change it in |
|---|---|---|
| Win+Alt+Space | Quick Run | Settings > Keyboard |
| Ctrl+Alt+R | Run on selected text | Settings > Keyboard |
| Ctrl+Alt+P | Quick Panel at the mouse pointer | Settings > Keyboard |
| Ctrl+Alt+S | Stop recording (when you record an app) | not changeable in the settings page |
| Ctrl+Alt+Esc | Stop a macro recording | not changeable in the settings page |
| Your own hotkey | Run a shortcut | The shortcut's **Give it a hotkey** |

The recording bar shows the stop key for the recording you started. Ctrl+Alt+S stops an app recording; Ctrl+Alt+Esc stops a macro recording. The bar's Stop button works too.

**In the app**

| Keys | What it does |
|---|---|
| Ctrl+K | Command palette |
| Ctrl+/ or F1 | Show the cheat sheet |
| Ctrl+F | Search shortcuts and the Gallery |
| Ctrl+N | New shortcut |
| Ctrl+, | Settings |
| Ctrl+1 to Ctrl+6 | All Shortcuts, Pinned, Automations, Snippets, Gallery, Run History |
| Enter / Ctrl+E / Ctrl+D / F2 / Delete / Ctrl+Z | In the Library: run, edit, duplicate, rename, delete, undo delete |
| F5 / Shift+F5 | In the editor: run, stop |

## 9. Automations

### What it does

An automation runs a shortcut for you when something happens, like when you open an app, plug in headphones, or it is 8 in the morning. Example: "When I plug in headphones, set the volume to 40%."

### Where it is

Sidebar > **Automations** (Ctrl+3). Choose **New Automation** (or use the command palette).

### How to use it

The wizard walks you through it:

1. Search or pick **when** it should start (a trigger). See the list below.
2. Optionally add **conditions** (**Add a condition**), such as time of day, day of the week, power source, battery level, Wi-Fi network or whether a full-screen app is open.
3. Pick the shortcut to run: **A shortcut I already have**, or **A new shortcut (I'll add its steps next)**.
4. Choose how it runs: **Run it right away**, **Ask me first (a notification with Run and Skip)**, or **Tell me when it runs**.
5. Optional: **If missed, run when Automation Portal next starts or the PC wakes**, and **Don't pause this automation when it runs often**.
6. Use **Test it now** to try it. The test runs the shortcut once on sample files in a temporary folder, so your own files are not touched.

`[screenshot: the automation wizard, picking a trigger]`

Examples of triggers:

- **Time**: At a time of day, Once a month, Every few minutes or hours, On a date and time, At sunrise or sunset (uses Settings > General > **Where I am**), Before a calendar event (from a calendar .ics link).
- **Your PC**: When the charger is plugged in or unplugged, the battery level or power plan changes, Battery saver changes, I sign in, the PC wakes up, is locked or unlocked, I've been away, I come back.
- **Devices and network**: a Bluetooth device or USB device connects, a monitor is connected, the sound output changes, a USB drive is plugged in or removed, I join or leave Wi-Fi, the internet comes back or drops.
- **Apps**: an app opens, closes or comes to the front; a notification arrives; I'm in a call or a call ends; I take a screenshot.
- **Typing and clipboard**: a keyboard shortcut, I type a phrase, I copy something.
- **Files and web**: files change in a folder, a web page changes, a new item arrives in an RSS feed, a weather alert.
- **Other apps**: When another app sends an event (for example Desk Pal's break and focus events).

### Managing automations

Each automation row has a switch to turn it on or off, **Test now**, **Activity...** (when it fired, ran or was skipped), **Edit this automation**, **Open the shortcut**, and **Delete automation** (the shortcut is not deleted).

**Pause all automations** (toolbar, or from the tray) stops every automation until you turn it off. A banner reminds you.

Safety: an automation that runs too often is paused automatically so it cannot loop forever, and the app tells you. Turning it back on is up to you; check first what it changes. Automations also respect a minimum time between runs. If the shortcut an automation runs was deleted, the row says so; edit the automation to choose another.

### Free and paid

Making **new automations** needs Pro (or the free Pro trial). Automations you already have keep running on every plan. See [Plans](#19-plans-free-plus-and-pro).

### Data

Stored in `automations.json` in your Library folder. Activity is in the log and run history.

## 10. Snippets and typed phrases

### What it does

Type a short code (for example `;sig`) anywhere and Automation Portal swaps it for longer text. It works in any app.

### Where it is

Sidebar > **Snippets** (Ctrl+4).

### How to use it

1. Choose **New Snippet**.
2. **Short code**: for example `;sig`. Add an optional **Folder**.
3. **Replace it with**: the text. Use **Add a placeholder** for **Date**, **Date format...** (for example `yyyy-MM-dd` or `dddd d MMMM`), **Time**, **Clipboard**, **Cursor spot** (where the cursor lands) or **Ask me...** (the app asks each time).
4. See the result in the **Preview**.
5. Turn it on with the switch.

The first time, **Add ;date and ;addr to start** adds a date snippet and an address snippet for you.

`[screenshot: the Snippets page with the editor]`

Settings:

- **Match capital letters exactly** is off by default. When it is off, typing `;Hi` gives a capital first letter and `;HI` gives all capitals.
- Snippets can be searched and put in folders.

**Typed phrases**: an automation trigger **When I type a phrase** runs a shortcut when you type a phrase you choose. Both features watch your typing only to match the abbreviations you set up. What you type is processed in memory and not saved or sent. Password fields are skipped.

### Free and paid

Snippets have no limit by plan. The plan limits in the app apply to shortcuts and automations only.

### Data

Snippets are stored with your library, in a file named `snippets.json` in the library folder (by default `%APPDATA%\Automation Portal\Library`).

## 11. The macro recorder

### What it does

Records what you do once (clicks and typing) and turns it into steps.

### Where it is

**New shortcut** > **Record a new shortcut**, or **Record** in the Quick Panel.

### How to use it

1. Choose which app you will show (**Which app will you show?**), then **Start recording**.
2. A 3, 2, 1 countdown runs. Do the task. Passwords are never recorded.
3. Stop with the bar's Stop button or the stop keys (Ctrl+Alt+S for an app recording).
4. **Review the recording**. Change anything before adding the steps. Each step finds its control by name, so it still works when the window moves. Choose **Add steps**.

`[screenshot: Review the recording, with steps listed]`

### Notes

Recording uses a keyboard hook while recording is running and shows that it is recording. Nothing is recorded outside a recording.

## 12. The Gallery

### What it does

Ready-made shortcuts. Add the ones you like, then make them your own.

### Where it is

Sidebar > **Gallery** (Ctrl+5).

### How to use it

1. Select a shortcut to see **What it does** and **What it will do**.
2. Fill in any **Set up** questions.
3. Choose **Add shortcut**, or **Add and schedule...** to also make an automation (needs Pro).
4. To open a shortcut file from elsewhere, choose **Open an .autoportal file...**.

Examples that ship with the app include Clean Link, Weather Now, Quick Note, Tidy Desktop, Combine PDFs, Resize Images, Dark Mode at Sunset, Meeting Mode, Pomodoro, and Translate Clipboard.

`[screenshot: the Gallery]`

Free and paid: adding a gallery shortcut counts as a shortcut for your plan's limit.

## 13. Run History and Undo file changes

### What it does

Run History shows what happened in each run, step by step, so you can find out why something did not work. It also lets you put back files a run changed.

### Where it is

Sidebar > **Run History** (Ctrl+6).

### How to use it

- Search by name, and filter by result (**All runs**, **You stopped it**, **Stopped itself**) and source (**All sources**, **Links and other apps**, and so on).
- Select a run to see its steps and results.
- **Run again** repeats the shortcut (shortcuts that need files or text cannot be repeated from here; run them from the Library or Quick Run).
- **Open at failed step** opens the editor with the step that did not finish selected.
- **Copy details** copies a short summary without your files, passwords or keys.
- **Files changed** lists what the run created, moved, renamed or replaced.
- **Undo file changes** puts back the files the run created, moved, renamed or replaced. You see the list first. New files go to the Recycle Bin and nothing is overwritten. If Windows says an item is too big for the Recycle Bin, choose No to keep it.
- **Clear history...** removes the whole history. Your shortcuts are not affected.

`[screenshot: Run History with a run selected and the Undo file changes button]`

### Limits and defaults

- The app keeps the most recent 1,000 runs.
- Undo works while the record of the run exists; a run's file changes older than a week can no longer be undone ("Nothing to undo").
- By default the app asks before a run that changes files when it comes from outside the app (see [Other apps](#17-other-apps-links-and-the-tools-pipe)).

### Data

History: `history` folder in the local data folder. File-change records: `file-changes` folder. Logs: `logs` folder. See section 21.

## 14. AI steps

### What it does

Some steps use AI: summarise or rewrite text, translate, describe pictures, answer a question, or draft a shortcut from a sentence (**Describe it**). AI is only used when a shortcut you run has an AI step, or when you choose Describe it.

### Where it is

Settings > **AI** (the AI section; the command palette command "ai" jumps there).

### Ways to get an AI

- **Portal AI Host (on this PC)**: a free app from the same maker that runs AI on your PC and shares it with all Portal apps. Nothing leaves your PC. Choose **Get Portal AI Host**, then **Allow** once so Automation Portal can use it. Use **Check again** if you just installed it, and **Forget** to remove the permission.
- **Windows on-device AI (Windows AI)**: built into Copilot+ PCs. Free, private and works offline. Needs the Microsoft Store version of Automation Portal. If Windows is still preparing it, choose **Get it ready**. The on-device model is small, so long documents may be refused as too much text.
- **Your own AI account (API key)**: use your own account with ChatGPT (OpenAI), Claude, Gemini, DeepSeek, Moonshot (Kimi), Microsoft Azure OpenAI and others. Paste your key and choose **Find models** (or type a model name). You can also use local services such as Ollama or LM Studio.

Choose which one to use with **Use as default**. The **Get AI ready** window appears the first time an AI step needs one.

`[screenshot: Get AI ready window with the three choices]`

### Settings and defaults

- **Model level** (**Fast**, **Smart**, **Best**): Fast costs least and answers quickest; Best costs the most and takes longest. The default is **Best**. A model name you type for a provider overrides it.
- Keys are stored encrypted on this PC and tied to your Windows account.

### Privacy

When you use your own key, the text, images or documents you send go to that provider under its privacy terms. On-device options send nothing out.

### Free and paid

AI steps count as normal steps in shortcuts. Your own provider may charge you. The app does not restrict AI steps by plan.

### Data

Keys are in `secrets.dat` in the roaming data folder (encrypted). Remove a key in the AI settings (**The key was removed.**).

## 15. The `ap` command line

### What it does

`ap` runs and manages your shortcuts from a terminal, a script or another program.

### Where it is

- Type `ap` in any terminal.

### How to use it

```
ap list [--json]
ap run "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--json] [--no-wait]
ap preview "Name" [--input <text>] [--file <path>]...
ap tools
ap new --file <path> [--file <path>]...
ap import <file.autoportal>
ap export "Name" <file.autoportal> [--force]
ap capabilities [--json]
ap --help | --version
```

- **Name** is the shortcut's name (capitals do not matter), its Quick Run alias, its id, or the start of its name when only one shortcut starts that way.
- `ap run` waits for the shortcut and returns its result. If Automation Portal is running, it does the work (tiles show progress and history is recorded). If not, `ap` runs it itself and asks any questions in the terminal.
- `ap preview` says, step by step, what a shortcut would do with that input, without running or opening anything.
- `ap export` never replaces an existing file: it saves as `Name (2).autoportal` unless you add `--force`.
- `ap import` shows the shortcut in the app and waits until you choose **Add** or **Cancel**.

Exit codes: 0 done, 1 a step failed, 2 wrong arguments, 3 no such shortcut, 4 cancelled, 5 file or import problem, 6 not run because you chose Don't run, nobody answered in 60 seconds, or the same app already had a request waiting.

Full details: `docs/CLI.md`.

## 16. Explorer, Send to, Desktop and Start menu

### What it does

Lets you run a shortcut on files from File Explorer, or from the desktop or Start menu.

### Where it is

On a shortcut's right-click menu and its details sheet (**Show in Send to**, **Add to Start menu**, **Add to desktop**).

The Microsoft Store version has no right-click menu entries in Explorer, no Send to entries and no Start menu links. Add to desktop and the Jump List work.

Its **Set Light/Dark Mode** step does not switch dark mode. It opens Windows Settings > Personalization > Colors and reports that the step isn't available.

Links and entries the app makes carry a private token so only your own entries run without asking.

### Removing

Turning **Show in Send to** off removes the entry. Deleting a shortcut removes its links. Desktop links are ordinary files; delete them like any file.

## 17. Other apps, links and the tools pipe

### What it does

Other programs can ask Automation Portal to run a shortcut, import one, or send it an event, through `ap`, an `automationportal://` link, or a named pipe on your PC. Examples: Files Portal ("Run Tidy on these files"), Power Plan Manager, Desk Pal, and AI assistants.

### Your control

When another app asks, a **request sheet** appears: "Files Portal wants to run "Tidy" on 3 files". It shows a badge (**No file changes**, **Makes copies**, **Changes files**), the full path of each item, and what the shortcut would do. You choose **Run** or **Don't run**. No answer in 60 seconds counts as Don't run.

- Requests from an AI assistant are always asked first, and **Don't ask again** is not offered for them.
- Shortcuts that make or change files, click and type in other apps, run programs, download files or send data off your PC always ask.
- **Don't ask again** remembers an app with that shortcut until the shortcut's steps change.
- Manage this in Settings > **Connected apps** (**Apps allowed to run shortcuts**).

`[screenshot: the request sheet for a run from another app]`

### Links

`automationportal://` links open the app or ask to run a shortcut. In Settings > **automationportal:// links** choose **Never**, **Ask every time**, or **Per shortcut** (the default: links ask first, except for shortcuts whose Details say "Links can run this without asking"). A link that makes or changes files always asks.

### Connected apps

Settings > **Connected apps** lists other apps from the same maker that Automation Portal can work with (Files Portal, Power Plan Manager, Desk Pal). Each has **Open** or **Get {app} from the Microsoft Store**, and a **Refresh** button.

### Tools

`ap tools` prints your shortcuts as tool definitions for apps that have an AI assistant (see `docs/CLI.md` for the format). Nothing runs from a tool definition list.

### Caution

The caller's name is whatever the other program says. The sheet is a consent step; it does not prove who asked. Only run programs you trust.

## 18. Settings

Open with Ctrl+, or the sidebar. Use **Jump to a section** at the top.

### General

| Setting | Default |
|---|---|
| **Start with Windows** (opens quietly in the tray when you sign in) | Off |
| **Keep running in the tray** | On |
| **Theme**: **Same as Windows**, **Light**, **Dark** | Same as Windows |
| **Shortcut tiles**: **Neutral**, **Gradients** | Neutral |
| **Where I am**: City, Latitude, Longitude, **Use my current location**, **Enter coordinates instead** | Empty (Windows works it out) |

"Where I am" is used for sunrise and sunset triggers and weather alerts.

### Keyboard

**Quick Run** (Win+Alt+Space), **Run on selected text** (Ctrl+Alt+R), **Quick Panel at the mouse pointer** (Ctrl+Alt+P). Press the keys you want; use at least one of Ctrl, Alt or Win.

### Library and backup

- **Where your shortcuts are saved**: **Open** shows the folder; **Change...** moves your library (the app restarts).
- **Keep your shortcuts in OneDrive** (**Move to OneDrive**) copies your library into OneDrive so it syncs between your PCs. Needs a signed-in OneDrive folder.
- **Back up your library** (**Back up library now**): saves shortcuts, folders and automations into one zip. **Restore from a backup...** adds the shortcuts in a backup next to the ones you have. Nothing is replaced.

### Notifications

- **Tell me when a shortcut started by hotkey finishes**: on.
- **Play a sound when a shortcut I started finishes**: a soft sound for done and another for a problem. Shortcuts that start by themselves stay silent, and so does everything while Focus assist or a full-screen app is on. Off by default; turn it on in Settings.
- **Phone notifications topic**: used by the Send to My Phone step through the ntfy.sh service. Pick a private, hard-to-guess name and subscribe to it in the ntfy app.

### Other apps and links

**automationportal:// links** (Per shortcut by default) and **Connected apps**. See [section 17](#17-other-apps-links-and-the-tools-pipe).

### AI

See [AI steps](#14-ai-steps). **Model level**: Fast, Smart, Best (default Best).

### Advanced

**Show advanced actions**: off by default. **Send crash reports to help fix problems** also appears under Privacy.

### Appearance

| Setting | Options | Default |
|---|---|---|
| **Accent colour** | **Windows accent** or one of ten colours (Blue, Indigo, Violet, Magenta, Red, Orange, Amber, Green, Teal, Slate) | Windows accent |
| **Window background** | **Mica (soft, default)**, **Acrylic (frosted)**, **Solid** | Mica |
| **Corners** | **Rounded (default)**, **Square**, **Extra round** | Rounded |
| **Density** | **Compact**, **Standard (default)**, **Comfortable** | Standard |
| **Text size** | Bigger or smaller on top of the Windows setting | Standard |
| **Motion** | **Same as Windows (default)**, **Reduced**, **Off** | Same as Windows |
| **Shortcut tiles** | **Cards (default)**, **Compact** | Cards |

**Reset appearance** puts all of these back. Windows high contrast always uses a solid background.

`[screenshot: Settings > Appearance]`

### Privacy

**Send crash reports** (on by default), with **What's sent**. See [Privacy](#22-privacy).

### About

**Privacy policy**, **Open data folder**, **Open log folder**, **Show welcome again**.

## 19. Plans: Free, Plus and Pro

| | Free | Plus | Pro |
|---|---|---|---|
| Shortcuts | Up to 5 | Up to 25 | As many as you like |
| New automations (schedules, triggers) | No | No | Yes |
| Gallery, Quick Run, snippets, recorder, history | Yes | Yes | Yes |

Plus and Pro are add-ons bought in the Microsoft Store from inside the app (Settings > **Automation Portal Plus and Pro**). People who own Plus are offered **Upgrade to Pro**, not full Pro; Plus plus the upgrade is Pro.

- **Get Plus**, **Get Pro**, **Upgrade to Pro**: opens the Microsoft Store purchase.
- **Restore purchases**: checks the Microsoft Store again for what you bought, for example on a new PC.

Add-ons are bought in the Microsoft Store.

### Free 14-day Pro trial

- Choose **Start 14-day free trial** in the plan card. No card is needed.
- It starts only when you press the button, not before, and only once per Windows user.
- While it runs, a chip shows **Pro trial: N days left**. A day before it ends, you are told.
- When it ends: nothing you made is removed. Existing shortcuts and automations keep running, and you can still edit, run, turn on or off, and delete them. What stops: making new automations, and making new shortcuts beyond your plan's cap.

`[screenshot: the plan card with the Start 14-day free trial button]`

### Over the limit

If you have more shortcuts than your plan allows (for example after a trial), they all keep working. New ones need more room. When a gallery shortcut is added with a schedule and your plan has no automations, the shortcut is added and runs only when you start it.

## 20. Limits of the Store version

Automation Portal comes from the Microsoft Store, and the Store sets a few limits:

- There are no Start menu links, no Send to entries and no right-click menu entries in Explorer.
- The **Set Light/Dark Mode** step opens Windows Settings > Personalization > Colors instead of switching. Windows does not let the app switch it.
- Updates come through the Microsoft Store, and add-ons are bought there.
- Windows on-device AI works on a Copilot+ PC.

You can check your edition with `ap capabilities`.

## 21. Where your data lives, backup and removal

### Store data folders

`%LOCALAPPDATA%\Packages\34458MindtheApp.AutomationPortal_24e29cj9741rt\` with two folders:

- `RoamingState`: settings (`settings.json`), the encrypted AI key (`secrets.dat`), reminders, saved values, recently deleted shortcuts, and by default your **Library** folder (shortcuts, folders, automations).
- `LocalState`: run history, logs, temporary run files, file-change records for Undo, earlier versions of shortcuts, icons.

Windows deletes these when you uninstall. Back up your library first (below).

### Library

Default: the `Library` folder inside the roaming data folder. Move it in Settings > Library and backup > **Change...**, or to OneDrive.

Notes created by **Add to Note** are saved in `Documents\Automation Portal Notes`.

### Back up and restore

- **Back up library now** (Settings, or the main window menu): one zip with shortcuts, folders and automations.
- **Restore from a backup...** adds the shortcuts in a backup next to what you have. Nothing is replaced.
- Export a single shortcut with **Export shortcut** (an `.autoportal` file). Exported files contain only the shortcut, never your history, settings or AI key.
- To copy everything, also copy `settings.json`, `reminders.json` and `saved-values.json` from the roaming folder.

### Remove your data

1. Uninstall (see section 2).
2. Windows removes the data folders.
3. Delete any library you moved to OneDrive or another folder, and any notes, exports or backups you saved.

### What stays after uninstall

Files that steps created or changed, Windows settings as last set (wallpaper, sound device, power plan), and Desktop links (they stop working). Use **Undo file changes** in Run History while the app is installed to put files back.

## 22. Privacy

- Your shortcuts, automations, snippets and run history stay on your PC. There are no servers that receive them, no accounts, no ads and no analytics.
- **Crash reports are on by default.** If the app crashes, the error type, a cleaned error message, the stack trace, the app version and the Windows version are sent to Sentry (hosted in Germany). File names, paths, your user name and your PC name are removed. No shortcut contents or names, files, clipboard or typed text are included. Reports are kept for 90 days.
- Turn crash reports off any time: Settings > **Privacy** > **Send crash reports**. It takes effect straight away and the app does not ask again.
- **Nothing else is sent** unless a step you add does so: for example a weather step (Open-Meteo), a web page or download, Send to My Phone (ntfy.sh), currency conversion, an AI step that uses your own provider, or location look-ups when Windows location is unavailable.
- Keystrokes, clipboard, location, microphone, screen and files are used only to do what you asked and processed on your PC.
- Imported shortcuts show which powerful steps they contain before you add them.

Full policy: Settings > About > **Privacy policy**, or `docs/store/privacy-policy.md`.

## 23. Troubleshooting and FAQ

**Nothing happens when I press the Quick Run keys.**
Another program may use Win+Alt+Space. Choose different keys in Settings > Keyboard. If another app already has the key, Settings shows: "Another app already uses Win+Alt+Space. Choose a different keyboard shortcut." If another Automation Portal feature uses it, the message names that feature instead.

**I closed the window and the app disappeared.**
It is in the tray (near the clock). Click the icon, or turn **Keep running in the tray** off in Settings to quit on close.

**My automation does not run.**
Check: the switch is on; **Pause all automations** or a tray pause is not active; the shortcut it runs still exists; the condition (time, Wi-Fi, power) is met; open **Activity...** to see when it fired, ran or was skipped. Automations that ran too often are paused automatically.

**I cannot make a new automation or shortcut.**
You are at your plan's limit, or your Pro trial ended. Existing items still work. See [Plans](#19-plans-free-plus-and-pro).

**A step failed. How do I see why?**
Run History > select the run > **Open at failed step**. Use **Copy details** to share a summary with support.

**I deleted a shortcut by mistake.**
Press Ctrl+Z right away, or open **Recently deleted** (30 days).

**A run changed my files and I want them back.**
Run History > the run > **Undo file changes**. It works for roughly the last week of runs.

**Windows asks for permission or a link asks me first.**
That is the consent sheet for requests from other apps. See [section 17](#17-other-apps-links-and-the-tools-pipe).

**The right-click menu or Start menu link is missing.**
The Store version does not offer these. See [section 20](#20-limits-of-the-store-version).

**"Set Light/Dark Mode" opens Windows Settings.**
That is expected. The step opens Windows Settings for you to switch it.

**AI steps say they need an AI.**
Open Settings > AI and set up Portal AI Host, Windows AI or your own key.

**On-device AI says "Needs a Copilot+ PC".**
It needs a Copilot+ PC and the Store version. Use Portal AI Host or your own key instead.

**I lost my purchases on a new PC.**
Sign in to the Microsoft Store with the same account, then choose **Restore purchases**.

**Where are the logs?**
Settings > About > **Open log folder**. The app log is `Logs\app.log` inside the local data folder.

## 24. Support

Questions, feedback, privacy requests and Store support: **mindthaapp@gmail.com**.

When you write, tell us your Windows version and what you were doing. Use **Copy details** in Run History for a run summary. It leaves out your files, passwords and keys.
