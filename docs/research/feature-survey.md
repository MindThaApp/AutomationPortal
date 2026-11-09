# Automation Portal — competitor feature survey (gap analysis vs SPEC v1)

Date: 2026-10-02. Compared against `docs/SPEC.md` §4 (actions), §5 (triggers), §6 (UI), §7 (run methods).
Products surveyed: Apple Shortcuts (iOS/macOS 26, plus iOS/macOS 27, which shipped Sept 2026), Power Automate
for desktop (simple features only), Keyboard Maestro 11, Raycast (Mac + Windows), Alfred 5.5, PowerToys
0.95–0.101, AutoHotkey v2 hotstrings, Espanso 2.3, TextExpander, Tasker 6.5, MacroDroid, IFTTT, Elgato Stream
Deck 7.x, and new Windows features: Click to Do, AI actions in File Explorer, App Actions API, Copilot Actions,
and MCP agent connectors.

Excluded: anything that needs coding, enterprise or IT infrastructure, or is impossible on a PC (HomeKit,
location geofences, Health). Items already in SPEC are not listed. Where SPEC has only part of a feature, the
row describes just the missing part.

**Effort:** S = up to about 3 dev-days · M = 1–2 weeks · L = 3+ weeks.
**Value** (everyday users): ★★★ high · ★★ medium · ★ niche.

> **Strategic note: package identity.** Six of the gaps below only work if Automation Portal has *package identity*,
> either as a signed MSIX or as a "sparse package with external location" next to the normal exe:
> Share target (G8), Notification trigger (D3), Copilot key (G7), on-device Phi Silica AI (F7),
> Click to Do App Actions and MCP agent connector (G10), and Windows AI imaging (F2/F4 on Copilot+ PCs).
> Decide on a sparse package early in phase E. It also makes toasts more reliable.

---

## 1. Gap features

### A. Context and input ("act on what I'm looking at")

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| A1 | **Run on selection**: a "Get Selected Text" action, plus a keyboard shortcut that opens Quick Run with the current selection (text, link or files) as input and lists only the shortcuts that accept it | Alfred Universal Actions, Raycast `{selection}`, Windows Click to Do, macOS Services | Select anything in any app and act on it in two keystrokes. No copy-and-paste dance | UIA `TextPattern.GetSelection()` on the focused element. Fallback: save clipboard → `SendInput` Ctrl+C → read → restore clipboard. Files already come from `file.selected` | S | ★★★ |
| A2 | **Get Current Web Page** (URL, title and optionally page text) from Edge, Chrome, Brave or Firefox | Apple Shortcuts (Safari "Get Current URL"), Keyboard Maestro browser tokens, Raycast browser extension | "Save this page to my notes", "Send this page to my phone", "Summarize this page". SPEC's *Summarize Web Page* starter needs this | UI Automation: find the address-bar Edit control in the foreground browser window (`ValuePattern`) plus the window title. Fallback: Ctrl+L, Ctrl+C with clipboard restore | M | ★★★ |
| A3 | **Current File Explorer folder** as input or variable ("here") | macOS Tahoe Spotlight ("input from open documents"), Keyboard Maestro `%FrontDocumentPath%` | "New text file here", "Tidy this folder", "Zip this folder" without picking a folder | `Shell.Application.Windows()` COM → match the foreground HWND → `Document.Folder.Self.Path` | S | ★★ |
| A4 | **Clipboard history** actions: "Get recent copies", "Choose from clipboard history", "Combine last N copies" | Raycast, Alfred, macOS Tahoe Spotlight clipboard, PowerToys Advanced Paste | Paste something copied earlier, or merge several copies into one note | `Clipboard.GetHistoryItemsAsync()` (only works when the user has Win+V history on, so offer to open that setting), or Automation Portal's own opt-in history built on the existing `clip.changed` listener. Respect `ExcludeClipboardContentFromMonitorProcessing` so password-manager copies are skipped | S | ★★ |

### B. Text expansion and typing

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| B1 | **Snippets page**: a plain two-column list (abbreviation → text) with folders, separate from shortcuts. Placeholders `{cursor}`, `{clipboard}`, `{date +2d}`, `{ask "Name"}`. A "Find snippet" keyboard shortcut searches by name for when you forget the abbreviation | Raycast Snippets, Espanso, TextExpander, Alfred, Keyboard Maestro "Prompt for Snippet" | Text expansion is the most-used everyday automation. In SPEC, each snippet needs a shortcut **and** an automation, which is too heavy | Reuse `TypedPhraseService`. Insert long or rich text by pasting from the clipboard and restoring it afterwards. `{cursor}` = press Left N times after inserting | M | ★★★ |
| B2 | **Expansion polish**: Backspace right after an expansion undoes it; case follows what you typed (`;Addr` → capitalised); choose "expand immediately" or "after space or punctuation"; never expand inside a word; never in password fields | AutoHotkey hotstring options (`*`, `C`, `O`, `?`), Espanso (backspace undo), TextExpander | Stops the "it expanded when I didn't want it to" frustration that makes people turn expansion off | Same keyboard hook. UIA `IsPassword` on the focused element | S | ★★★ |
| B3 | **Fill-in snippets**: a small form appears before the text is inserted (text fields, dropdown, date picker, optional sections) | TextExpander fill-ins and Conditional Sections, Espanso forms | One snippet covers every variant of a reply email | Built on C1 (form dialog), with the answers substituted into the snippet | S (after C1) | ★★ |
| B4 | **Autocorrect pack**: one switch installs a list of about 4,000 common English typos ("teh" → "the") | AutoHotkey AutoCorrect community script | Autocorrect in every app, including ones that don't have it | Stored as snippets with "inside words off" and "match case on". Build the list from Wikipedia's *Lists of common misspellings* (CC BY-SA, needs attribution) | S | ★★ |
| B5 | **Snippet suggestions**: "You've typed this 5 times. Make it a snippet?" | TextExpander (local model, 2025–26) | People don't know what to automate | A local-only phrase counter in the keyboard hook. Off by default, never stores raw keystrokes. Needs careful privacy wording | M | ★ |

### C. Show and Ask

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| C1 | **Ask for Several Things**: one form with text, long text, number, date, choice, checkbox and file fields → a dictionary | Keyboard Maestro "Prompt for User Input", Espanso forms, TextExpander fill-ins | One dialog instead of four "Ask for Input" pop-ups in a row | WPF dialog generated from a list of fields. Output is a Dictionary keyed by field label | S–M | ★★★ |
| C2 | **Notification with buttons or a reply box**, e.g. "Battery 15 % → [Turn on saver] [Ignore]". The button pressed becomes the output | Tasker and MacroDroid notification actions, iOS actionable notifications | Asks without interrupting. The user can answer later from Notification Center | Windows App SDK `AppNotificationBuilder` with buttons and a text input (works unpackaged once an AUMID is registered) | S | ★★ |
| C3 | **Show Text Large** (big text across the whole screen) | Alfred Large Type, Keyboard Maestro "Display Text Large" | Show a Wi-Fi password or phone number to someone across the room | Borderless topmost window | S | ★ |

### D. Triggers

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| D1 | **"When I'm in a call"**: microphone or camera starts or stops being used (by any app or a chosen app) | MacroDroid "Camera In Use", busy-light apps | Automatic meeting mode: pause music, quiet notifications, set a status light, open notes | Watch `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone,webcam}\**`. `LastUsedTimeStop == 0` means "in use" (the same data behind the taskbar privacy icon). `RegNotifyChangeKeyValue` | S | ★★★ |
| D2 | **When I take a screenshot** | iOS/macOS 27 Screenshot automation | Rename, move, read text from or share each screenshot automatically | `FileSystemWatcher` on `Pictures\Screenshots` (Win+PrtSc and Snipping Tool auto-save there), plus Snipping Tool clipboard images through `clip.changed`. Skip duplicates | S | ★★★ |
| D3 | **When a notification arrives** from an app (optionally "containing …") | iOS/macOS 27 Notification automation, MacroDroid, Tasker | "When the delivery app says 'delivered' → remind me", "When Teams says 'urgent' → play a sound" | `UserNotificationListener` (Windows.UI.Notifications.Management). Needs package identity plus a one-time user permission. Title and body text are available | M–L | ★★★ |
| D4 | **Before or when a calendar event starts** ("2 min before every meeting → open the meeting link") | IFTTT Google Calendar, Tasker and MacroDroid calendar triggers | Never miss a meeting. Joining a call becomes one step | Classic Outlook COM (already used by `cal.getEvents`), **or** a pasted "secret iCal link" from Google or Outlook.com polled every 15 min (Ical.Net handles repeats), so no sign-in is needed. Meeting link via `text.find` links | M | ★★★ |
| D5 | **New item in an RSS feed / web page changed / weather alert** ("rain expected tomorrow", "below 0 °C tonight") | IFTTT (RSS and weather are among its most popular applets), Visualping-style monitors | Price drops, ticket availability, "bring an umbrella" | Polling providers that reuse `web.rss`, `web.pageText` (hash, or "now contains …") and Open-Meteo forecast. Seen items are kept in saved values | S | ★★ |
| D6 | **Monthly and "every N days/weeks"** schedules ("on the 1st", "last Friday of the month") | Apple Shortcuts Time of Day (daily/weekly/monthly), IFTTT | Bills, backups, month-end reports | Extend the `time.daily` calculation | S | ★★ |
| D7 | **Any USB device connects or disconnects** (keyboard, mouse, dock, webcam, game controller) | iOS 27 Keyboard automation, Keyboard Maestro USB Device trigger | "When I dock my laptop → work layout and headset mic" | `DeviceWatcher` (Windows.Devices.Enumeration) filtered by device class or name | S | ★★ |
| D8 | **When a window titled "…" appears or closes** | Keyboard Maestro Window trigger, AutoHotkey `WinWait` | React to the "Zoom Meeting" window, a particular document or a dialog. Finer-grained than "app opens" | `SetWinEventHook` (`EVENT_OBJECT_SHOW`, `DESTROY`, `NAMECHANGE`) | S | ★★ |
| D9 | **Double-tap or hold a key** ("tap Ctrl twice"), **mouse side buttons**, **screen hot corners** | Keyboard Maestro tap-count triggers, macOS Hot Corners | Easy-to-remember triggers without finger-twisting shortcuts. Hot corners are a well-loved Mac habit | The existing low-level keyboard hook, plus `WH_MOUSE_LL` for side buttons and for the pointer resting in a corner | S–M | ★★ |
| D10 | **When I get an email from …** (classic Outlook only) | macOS Tahoe Email automation, IFTTT Gmail | "Email from my boss → notify me even in Focus" | Outlook COM `Application.NewMailEx`. New Outlook has no local API, so the UI must say so | M | ★ |
| D11 | **Virtual desktop switched**, plus Switch/Move-to-desktop actions | Keyboard Maestro "Space Changed", macOS Tahoe Stage Manager trigger | Different setups per desktop | Read `HKCU\…\Explorer\VirtualDesktops\CurrentVirtualDesktop`. Switch with Win+Ctrl+←/→. Moving other apps' windows needs undocumented, build-specific COM (fragile) | M | ★ |

### E. Device and Windows actions

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| E1 | **App volume**: set or mute one app ("Spotify to 20 % while I'm in a call") | EarTrumpet (a Windows favourite), Stream Deck volume-mixer plugins | The single most-wanted Windows sound tweak. Pairs with D1 | Core Audio `IAudioSessionManager2` → `ISimpleAudioVolume` per process | S | ★★★ |
| E2 | **Set microphone** (default and communications input device) | Keyboard Maestro "Set Audio Input Device", SoundSwitch | Headset mic for calls, studio mic for recording | The same `IPolicyConfig` path as `dev.audioOutput`, using the `eCapture` and `eCommunications` roles | S | ★★ |
| E3 | **Display actions**: projection mode (PC only / Duplicate / Extend / Second only), resolution and refresh rate, HDR on/off, main display, **monitor input source** | Keyboard Maestro "Set Screen Resolution", PowerToys PowerDisplay (input source, linked brightness), macOS Tahoe display automations | Gaming, presenting and docking. Switching a monitor between the PC and a console or work laptop | `SetDisplayConfig` (`SDC_TOPOLOGY_*`), `ChangeDisplaySettingsEx`, `DisplayConfigSetDeviceInfo` (advanced colour), DDC/CI VCP 0x60 via dxva2 (already used for brightness) | S–M | ★★ |
| E4 | **Save and restore window layout** ("Remember my windows" → "Restore *Work setup*", which also opens missing apps) | PowerToys Workspaces, Raycast window layouts | One click to a full work or streaming setup. Fixes scattered windows after undocking | `EnumWindows` → process path or AUMID, `GetWindowPlacement`, monitor ID. Restore = `app.open` + wait for window + `win.arrange` | M | ★★★ |
| E5 | **Click on text or image on screen**; **Wait until text or image appears** | Keyboard Maestro Find/Click Image (wait for image), Power Automate for desktop image/OCR actions, AutoHotkey `ImageSearch` | Automates apps that have no other way in. Recorded macros keep working when windows move | `Windows.Media.Ocr` (already used) for text. Small template-matching routine for images. The existing region overlay picks the target | M | ★★ |
| E6 | **Focus session**: "Block distracting apps for 25 min", with a goal and a snooze | Raycast Focus, Apple Focus | Real distraction blocking, not just closing apps once | Version 1 blocks apps only: on `app.opened`, minimise or close with a toast. Websites need a browser extension (L). Reading Windows Focus state via `FocusSessionManager` is fine, but *starting* one is a Microsoft Limited Access Feature, so fall back to opening the Focus setting | M | ★★ |
| E7 | **Connect or disconnect a Bluetooth device** ("Connect my headphones") | Apple Shortcuts (Bluetooth controls), Tasker | Headphones that keep pairing to the wrong device | No public API. The known approach sends a KS reconnect property to the Bluetooth audio endpoint (as ToothTray does), which is fragile. Fallback: open the Bluetooth settings page | M | ★★ |
| E8 | **Time spent in apps** (today or this week) | Apple Shortcuts "Get App & Website Data" (Screen Time, iOS/macOS 26) | Gentle self-tracking, e.g. a weekly digest | Automation Portal already watches the foreground app, so it can record durations locally | M | ★ |
| E9 | **Record screen or window** for N seconds or until stopped → MP4 or GIF | ZoomIt, Snipping Tool | Quick how-to clips and bug reports | `Windows.Graphics.Capture` + Media Foundation encoder | M | ★ |
| E10 | **Set default printer**; **Mobile hotspot on/off** | Tasker, MacroDroid | Small daily irritations | WMI `Win32_Printer.SetDefaultPrinter`; `NetworkOperatorTetheringManager` | S | ★ |

### F. Files, media and AI

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| F1 | **Convert or compress video and audio**: shrink for email, extract audio (MP3/M4A), trim, make a GIF from a video | Apple Shortcuts Encode Media / Trim Media / Make GIF | "This video is too big to send" is a very common everyday problem | `Windows.Media.Transcoding.MediaTranscoder` + `MediaEncodingProfile` (MP4/HEVC/M4A/MP3/WAV) are built into Windows. GIF via frame grabs + `GifBitmapEncoder` | M | ★★★ |
| F2 | **Remove background from image** (→ transparent PNG) | Apple Shortcuts Remove Background, Windows AI actions in File Explorer, Click to Do | Product photos, stickers, profile pictures | `ImageObjectExtractor` (Windows AI APIs) on Copilot+ PCs. Elsewhere, a downloadable ONNX segmentation model (check licence, e.g. U²-Net Apache-2.0, BiRefNet MIT) via ONNX Runtime + DirectML | M | ★★★ |
| F3 | **Transcribe an audio or video file** to text | Apple Shortcuts Transcribe Audio (improved in 26) | Voice memos and meeting recordings become searchable notes | The AI provider's speech-to-text (existing key), or an on-demand local whisper.cpp model (Whisper weights are MIT) | M | ★★ |
| F4 | **Upscale image / erase object** | Windows AI APIs `ImageScaler` and `ImageObjectRemover` | Enlarge small photos, remove photobombers | Copilot+ PCs only. Hide the action elsewhere | M | ★ |
| F5 | **AI answer types**: "Ask AI → answer as Text / Yes-No / Number / List / Dictionary", so an AI answer can drive If and Repeat | Apple "Use Model" (typed output, iOS/macOS 26) | "Is this email urgent? → If yes, notify me" works without parsing text | Provider structured output (JSON schema), then validate and convert to the right ContentKind | S | ★★★ |
| F6 | **Writing presets** on Rewrite: Proofread, Friendlier, More professional, Shorter, Make list, **Make table** (→ list of dictionaries → spreadsheet) | Apple Writing Tools actions (Proofread, Adjust Tone, Make List/Table from Text), PowerToys Advanced Paste | One-tap fixes. No prompt writing | Prompt presets on `ai.rewrite`; Make table uses F5 | S | ★★ |
| F7 | **AI without an API key**: on-device Phi Silica on Copilot+ PCs, auto-detect a local Ollama or Foundry Local, and a provider picker (OpenAI, Anthropic, Gemini, local) | Apple "Use Model" (on-device / Private Cloud / ChatGPT), PowerToys Advanced Paste (multi-provider + Phi Silica) | Removes the biggest setup barrier for non-technical users ("what is an API key?") and keeps data on the PC | `Microsoft.Windows.AI.Text.LanguageModel` (Windows App SDK; needs package identity + an NPU). Local servers expose OpenAI-compatible endpoints on localhost | M | ★★★ |
| F8 | **Ask AI about my screen** (screenshot of the front window plus its text as context) | Raycast Screen Awareness, Click to Do "Ask Copilot", Apple Visual Intelligence / "Get What's On Screen" | "Explain this error", "Reply to this", "What's this chart saying?" | Combine `screen.capture` + `screen.ocr` + `ai.ask` into one action, and add a gallery starter | S | ★★ |

### G. Run methods and panels

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| G1 | **Quick Run fallbacks**: inline calculator and conversions ("12 usd in eur", "5 ft in cm", "time in Tokyo"), "Search the web for …", "Ask AI …", "Create a shortcut named …" | Raycast, PowerToys Command Palette (fallback ranking), Spotlight, Alfred | The palette becomes useful every hour, not just for running shortcuts, which builds the habit | Reuse `num.expression`, `num.convertUnits` and `num.currency` | S | ★★★ |
| G2 | **Quick Run aliases ("Quick Keys")**: a user-set 2–4 letter code per shortcut ("gm" → Good Morning) | macOS Tahoe Spotlight Quick Keys, Raycast aliases | Muscle memory: the same three keys every time | `Alias` field on Shortcut; exact match ranks first | S | ★★ |
| G3 | **Fill several inputs inline in Quick Run** (Tab moves between the "Ask Each Time" parameters) | macOS Tahoe Spotlight actions | Run "Add reminder: [text] [time]" without any dialogs | Read the shortcut's `{"t":"ask"}` parameters and render them as inline fields | M | ★★ |
| G4 | **Button panel at the mouse pointer**, opened by its own keyboard shortcut. Several panels; auto-hide after a press; **smart panels** that switch with the front app | Virtual Stream Deck (Stream Deck 7.0, dynamic mode and Smart Profiles), Keyboard Maestro palettes, PowerToys Command Palette Dock | A Stream Deck without buying one, and the right buttons for the current app | Reuse the Quick Panel window, positioned at `GetCursorPos`. A per-app panel is a folder linked to an app | S–M | ★★★ |
| G5 | **Live toggle tiles**: the tile shows the current state (mic muted, dark mode on, Wi-Fi off) and flips it | Stream Deck stateful keys, iOS Control Center controls | You can see the state at a glance and toggle with one click | Optional "status" check per tile, reusing condition providers; refresh by polling or events | M | ★★ |
| G6 | **Taskbar Jump List**: right-click the Automation Portal taskbar or Start icon → pinned and recent shortcuts | Windows equivalent of Shortcuts widgets / Dock menu | Uses a place Windows users already right-click | WPF `System.Windows.Shell.JumpList` with `--run` arguments (the single-instance pipe already exists) | S | ★★ |
| G7 | **Copilot key opens Quick Run** | Windows 11 Copilot-key customisation | A dedicated hardware key for Automation Portal on new laptops | Windows only offers this for **signed MSIX** apps, so it depends on the packaging decision | S (if packaged) | ★★ |
| G8 | **Windows Share target**: Share → Automation Portal → pick a shortcut, from Photos, Edge or Snipping Tool | Apple share sheet (the most common way shortcuts run on iOS) | Run shortcuts on content from inside other apps | A share-target extension needs package identity (sparse package or MSIX) | M | ★★ |
| G9 | **Phone as a remote button panel** (local-network page, QR pairing plus PIN) | Stream Deck Mobile, MacroDroid Remote Control | Run PC shortcuts from the sofa or a second screen | Small LAN web server, firewall prompt, pairing token. Needs a security review | M–L | ★ |
| G10 | **Let Copilot and Windows agents run your shortcuts** (Windows' answer to Siri): App Actions in Click to Do, plus an MCP "agent connector" in the Windows on-device registry | Apple App Intents/Siri, Windows App Actions API (Build 2025), Windows MCP / on-device registry (2026) | "Hey Copilot, run Meeting Mode" | Needs package identity. Click to Do needs a Copilot+ PC | M–L | ★★ |

### H. Building and fixing shortcuts

| # | Feature | Source product | Why everyday users like it | Windows feasibility (API / approach) | Effort | Value |
|---|---|---|---|---|---|---|
| H1 | **Describe a shortcut** (AI builder) with **"Describe a change"** follow-ups and **clarifying questions** ("What does 'home' mean: a Wi-Fi network?") | iOS/macOS 27 Shortcuts, Power Automate for desktop Copilot, Tasker 6.5 AI generator, MacroDroid AI Macro Builder, IFTTT AI Applet creator | Every competitor added this in 2025–26. It is now the main way non-technical users start | LLM gets the action catalog descriptors → produces ActionInstance JSON → existing validator → preview before saving. M for straight-line shortcuts, L with control flow. *SPEC has this only as a phase-4 stretch goal* | L | ★★★ |
| H2 | **"Explain this error" / "Fix it for me"** on a failed run, and **"Explain this shortcut"** | Power Automate for desktop Copilot (repair errors), MacroDroid AI help for existing macros | Failed runs stop being dead ends | Send the error, card summaries and the inspector snapshot to `ai.ask` | S | ★★ |
| H3 | **"Run from this step"** / **"Test just this action"** in the editor, and **"Try it now"** for automations | Power Automate for desktop (Run from here, Run next action), MacroDroid (test actions / test trigger) | You can check one step without running the whole thing | Engine already runs a list. Seed the variables from the last run's inspector | S–M | ★★ |
| H4 | **Keyboard-shortcut conflict chooser**: when one key combination is set on several shortcuts, show a mini list (press 1, 2, 3) instead of an error | Keyboard Maestro Conflict Palette | One easy key can host a family of related shortcuts | `HotkeyService` owns the registration and pops a small chooser | S | ★ |

---

## 2. Ranked shortlist (value ÷ effort)

Value 1–5; effort S=1, S–M=1.5, M=2, L=3. Closely related rows are bundled.

| Rank | Feature (rows) | Score |
|---|---|---|
| 1 | Run on selection (A1) | 5.0 |
| 2 | "When I'm in a call" trigger (D1) | 5.0 |
| 3 | Quick Run calculator, fallbacks and aliases (G1+G2) | 4.0 |
| 4 | App volume + Set microphone (E1+E2) | 4.0 |
| 5 | Text-expansion polish: Backspace undo, case matching, no password fields, `{cursor}` (B2) | 4.0 |
| 6 | AI answer types + writing presets (F5+F6) | 4.0 |
| 7 | Screenshot trigger (D2) | 4.0 |
| 8 | Ask for Several Things form (C1) | 3.3 |
| 9 | Panel at the pointer + smart per-app panels (G4) | 3.3 |
| 10 | Watch triggers: RSS / page changed / weather, monthly schedule, USB device (D5+D6+D7) | 3.0 |
| 11 | Notifications with buttons (C2) | 3.0 |
| 12 | Taskbar Jump List (G6) | 3.0 |
| 13 | Display actions (E3) | 2.7 |
| 14 | Snippets page with fill-ins (B1+B3) | 2.5 |
| 15 | Get Current Web Page (A2) | 2.5 |
| next | Window layouts (E4), calendar trigger (D4), video/audio convert (F1), remove background (F2), AI without a key (F7) | 2.5–2.25 |
| big bet | Describe-a-shortcut AI builder (H1): every competitor shipped it in 2025–26 | 1.7 |

---

## 3. Things in SPEC that competitors do notably better (UX details worth copying)

1. **Triggers live on the shortcut, and there can be several** (iOS/macOS 27 merged Automations into
   Shortcuts). SPEC has one trigger per Automation, created in a separate wizard. Add "+ Add trigger" to
   the editor header and allow "At 6 pm **or** when I join Home Wi-Fi". Keep the Automations page as a view.
2. **The AI builder is now the front door.** In iOS 27, *New Shortcut* opens a "Describe a shortcut" box with
   "build it yourself" as the secondary option. The result runs with ▶ and is refined with "Describe a change".
   Tasker asks clarifying questions instead of guessing. SPEC treats this as a stretch goal; promote it.
3. **Text expansion is a simple list, not a shortcut** (Raycast, Espanso, TextExpander). In SPEC a snippet is
   an automation plus a shortcut. Add a Snippets page, and copy these details: Backspace undoes, case follows
   what you typed, never in password fields, long text is pasted and the clipboard restored, a snippet can be
   found by name.
4. **Quick Run should be a daily habit.** macOS Tahoe Spotlight Quick Keys (user-set short codes) and Tab
   between parameters; Raycast/PowerToys fallbacks (calculator, web search, ask AI) when nothing matches; show
   each shortcut's keyboard shortcut next to it.
5. **The button panel goes where the user is.** Virtual Stream Deck appears at the mouse pointer with its own
   keyboard shortcut, hides after a press, can be docked to a screen edge, switches with the front app
   (Smart Profiles) and shows live on/off state. SPEC's panel exists only as a tray flyout.
6. **Folder triggers handle real downloads** (macOS Tahoe added an "Ignore subfolders" checkbox after
   complaints). Wait until a file has stopped changing, ignore `.crdownload`/`.part`/`.tmp`, and group a burst
   of files into one run. This matters for SPEC's "Organize Downloads" starter.
7. **Recorded macros need anchors, not coordinates.** Keyboard Maestro and Power Automate click *images or
   text*. Microsoft deprecated its "Record with Copilot" AI recorder in Aug 2026. When SPEC's
   `MacroRecorder` records a click, also capture the text or image under it and replay with
   "Click on text 'Save'", falling back to coordinates.
8. **Debugging is step-level.** Power Automate for desktop offers *Run from here*, *Run next action* and
   breakpoints; MacroDroid offers *Test trigger*. SPEC's run feedback is good; add "Run from this step" and
   "Try it now" on automations.
9. **Keyboard shortcuts have more shapes.** Keyboard Maestro supports tap twice, hold, and a conflict chooser,
   and lets a group of macros be active only in certain apps. SPEC has chords and one optional app condition.
10. **AI choice and transparency.** Apple shows a model picker on each Use Model action (on-device /
    cloud / ChatGPT) and in iOS 27 a *transcript inspector* showing exactly what was sent. PowerToys Advanced
    Paste supports OpenAI, Azure, Gemini, Mistral, Ollama, Foundry Local and on-device Phi Silica. SPEC has
    one key with Fast/Smart/Best. Add a provider picker, a no-key local path, and show "what was sent to AI"
    in the Variables inspector.
11. **Gallery items say what they need before you add them.** Stream Deck 7.0 lists the plugins a profile
    needs and installs them in one click; MacroDroid shows ratings and usage counts. Gallery tiles should flag
    "Needs: AI key / classic Outlook / Copilot+ PC" up front.
12. **Theme switching has two halves.** PowerToys Light Switch can change the *apps* theme and the *Windows*
    theme separately, with sunrise/sunset built in. `dev.theme` should offer Apps / Windows / Both.

Skipped on purpose: website blocking (needs a browser extension), location geofences (weak on PCs), a
community template store (needs a server), Caps-Lock "Hyper key" and key remapping (PowerToys Keyboard
Manager already does this well), Windows Widgets board (low usage, needs MSIX).

---

## Sources

- Apple: [What's new in Shortcuts 26 (Apple Support)](https://support.apple.com/en-us/125148) ·
  [9to5Mac: Shortcuts in iOS 26](https://9to5mac.com/2025/07/30/shortcuts-in-ios-26-everything-new-coming-to-your-iphone-more/) ·
  [9to5Mac: 25+ new actions](https://9to5mac.com/2025/12/09/ios-26s-shortcuts-app-adds-25-new-actions-heres-everything-new/) ·
  [Six Colors: macOS 26 review](https://sixcolors.com/post/2025/09/macos-26-tahoe-review-power-under-glass/) ·
  [Six Colors: Tahoe folder automation update](https://sixcolors.com/link/2025/11/updated-folder-automation-in-macos-tahoe/) ·
  [MacRumors: Tahoe Spotlight Quick Keys](https://www.macrumors.com/2025/06/09/apple-supercharges-spotlight-in-macos-tahoe-with-quick-keys-and-more/) ·
  [MacMost: Spotlight actions](https://macmost.com/how-to-use-spotlight-actions-in-macos-tahoe.html) ·
  [WWDC26: What's new in Shortcuts](https://developer.apple.com/videos/play/wwdc2026/310/) ·
  [MacRumors: iOS 27 Shortcuts](https://www.macrumors.com/guide/ios-27-shortcuts/) ·
  [iThinkDiff: iOS 27 Shortcuts](https://www.ithinkdiff.com/ios-27-shortcuts-natural-language-creation/) ·
  [MacRumors: macOS 27 features](https://www.macrumors.com/2026/09/17/new-features-changes-macos-27/)
- Power Automate for desktop: [Copilot in PAD](https://learn.microsoft.com/en-us/power-automate/desktop-flows/copilot-in-power-automate-for-desktop) ·
  [2026 wave 1 planned features](https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/power-automate/planned-features) ·
  [Self-healing (preview)](https://learn.microsoft.com/en-us/power-automate/desktop-flows/self-healing)
- Keyboard Maestro: [What's New (v11)](https://wiki.keyboardmaestro.com/manual/Whats_New) ·
  [Hot Key trigger](https://wiki.keyboardmaestro.com/trigger/Hot_Key)
- Raycast: [PCWorld: Raycast on Windows](https://www.pcworld.com/article/3016518/raycast-is-finally-on-windows-and-it-totally-changed-how-i-use-my-pc.html) ·
  [Raycast Focus](https://www.raycast.com/core-features/focus) · [Dynamic placeholders](https://manual.raycast.com/dynamic-placeholders) ·
  [Quicklinks](https://manual.raycast.com/quicklinks)
- Alfred: [What's new in 5.5](https://www.alfredapp.com/whats-new/) · [Universal Actions](https://www.alfredapp.com/universal-actions/)
- PowerToys: [0.97 (devblogs)](https://devblogs.microsoft.com/commandline/powertoys-0-97-is-here-a-big-command-palette-update-and-a-new-mouse-utility/) ·
  [0.98 (WinBuzzer)](https://winbuzzer.com/2026/03/20/microsoft-powertoys-0-98-dockable-command-palette-keyboard-manager-xcxwbn/) ·
  [0.95 Light Switch (Neowin)](https://www.neowin.net/news/microsoft-releases-powertoys-095-with-highly-anticipated-light-switch-feature/) ·
  [0.101 features](https://en.valley4techs.com/2026/08/powertoys-new-features.html)
- Text expansion: [AutoHotkey v2 hotstrings](https://www.autohotkey.com/docs/v2/Hotstrings.htm) ·
  [AutoCorrect for v2](https://www.autohotkey.com/boards/viewtopic.php?t=120220) ·
  [Espanso vs TextExpander](https://textexpander.com/blog/espanso-vs-textexpander) · [TextExpander What's New](https://textexpander.com/whats-new)
- Android / web: [Tasker 6.5 AI generator](https://joaoapps.com/tasker-6-5-ai-generator-receive-shares-calendar-and-more/) ·
  [MacroDroid (Play Store)](https://play.google.com/store/apps/details?id=com.arlosoft.macrodroid&hl=en_US) ·
  [IFTTT AI Applet maker](https://ifttt.com/explore/introducing-ai-applet-maker) · [IFTTT April 2026](https://ifttt.com/explore/new-on-ifttt-for-april-2026)
- Stream Deck: [7.0 release notes](https://help.elgato.com/hc/en-us/articles/38011940576273-Elgato-Stream-Deck-7-0-Release-Notes) ·
  [Virtual Stream Deck overview](https://www.elgato.com/us/en/explorer/products/stream-deck/virtual-stream-deck-beta-overview/)
- Windows platform: [AI actions in File Explorer](https://www.windowscentral.com/software-apps/windows-11/windows-11-is-getting-ai-actions-in-file-explorer-heres-how-to-try-them-right-now) ·
  [App Actions API (Build 2025)](https://www.windowscentral.com/software-apps/windows-11/windows-11-app-actions-api-announcement-build-2025) ·
  [Copilot Actions](https://www.bleepingcomputer.com/news/microsoft/microsoft-debuts-copilot-actions-for-agentic-ai-driven-windows-tasks/) ·
  [Experimental agentic features / MCP](https://support.microsoft.com/en-us/windows/ai/ai-features/experimental-agentic-features) ·
  [FocusSessionManager (Limited Access)](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager?view=winrt-22621) ·
  [Notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener) ·
  [Copilot key customisation](https://pureinfotech.com/customize-copilot-key-action-windows-11/) ·
  [Windows AI Imaging APIs](https://learn.microsoft.com/en-us/windows/ai/apis/imaging-api-ref) ·
  [Click to Do](https://www.windowscentral.com/software-apps/windows-11/what-is-click-to-do-and-how-do-you-get-started-ai-actions-for-windows-11-explained)
