# Packaging Automation Portal for the Microsoft Store

Automation Portal ships as a full-trust MSIX (Store ID `9PB9C8FHG105`, package name `34458MindtheApp.AutomationPortal`, package family name `34458MindtheApp.AutomationPortal_24e29cj9741rt`, display name "Automation Portal"). The same code also runs unpackaged (`scripts\publish.ps1`); at runtime `PackageIdentity.IsPackaged` picks the right behaviour.

## What differs by mode

| Feature | Packaged (Store) | Unpackaged (zip / dev) |
|---|---|---|
| Start with Windows | `windows.startupTask` (`AutomationPortalStartup`, via the StartupTask API; Windows can also turn it off in Settings > Apps > Startup, and the app follows) | HKCU `Run` value `AutomationPortal` = `AutomationPortal.exe --background` |
| `automationportal://` and `.autoportal` | manifest `windows.protocol` / `windows.fileTypeAssociation` | HKCU\Software\Classes, written on start, removed by `AutomationPortal.exe --unregister` |
| `ap` command | `windows.appExecutionAlias` (`ap.exe` only) | `ap.exe` next to `AutomationPortal.exe` |
| Send To, Desktop, Start menu links | `.lnk` files whose target is the `ap.exe` alias (a path that survives updates), minimised so no console shows | `.lnk` files that target `AutomationPortal.exe` |
| Right-click cascade "Automation Portal" | not registered (see below) | classic HKCU verbs under `*` and `Directory` (Windows 11: "Show more options") |
| Jump List, icons | same | same |
| Toast buttons and reply | `windows.toastNotificationActivation` + COM exe server | Toolkit shortcut registration |
| Writes to registry and AppData | virtualised, except the five locations under [Virtualization](#virtualization-what-is-written-outside-the-package) (flexible virtualization, Windows 11) | normal |

Everything the unpackaged build registers is listed in `%LOCALAPPDATA%\Automation Portal\shell-state.json` and in the registry keys above, and `--unregister` removes exactly that.

### Packaged right-click menu (not implemented)

Windows 11's modern menu needs an `IExplorerCommand` COM server declared with `desktop4`/`desktop5` `windows.fileExplorerContextMenus` plus `com:ExeServer`. It needs an out-of-process COM exe with its own class factory and a dynamic sub-command enumerator. That is a separate piece of native-style work that cannot be verified without clicking through Explorer, so it is left out of v1. Packaged users get **Send to > Automation Portal - <shortcut>** (kept in sync with "Show in Send to") and the same shortcuts on Desktop, Start and the Jump List. The package does not write classic HKCU verbs: `HKCU\Software\Classes` stays virtualised, and such entries would be left behind as dead menu items after uninstall.

## Build the package

Needs the Windows SDK (already present at `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0`; otherwise `winget install Microsoft.WindowsSDK.10.0.26100`, or add "Windows SDK Signing Tools for Desktop Apps" in the Visual Studio Installer). No Visual Studio is required.

```powershell
scripts\make-logos.ps1       # only when the icon design changes (logos are committed in packaging\Assets)
scripts\package-msix.ps1       # x64 + arm64, then one .msixbundle
scripts\package-msix.ps1 -Arch x64     # a single architecture (.msix) for quick local tests
```

Output: `builds\AutomationPortal_1.0.2.0.msixbundle` (x64 + arm64, unsigned; the Store signs on submission) and the loose layout `builds\msix-layout\`. `makeappx pack` validates the manifest, so a successful pack means the manifest is schema-valid.

### Fill in the identity from Partner Center

1. Partner Center > Apps and games > Automation Portal > Product management > **Product identity**.
2. Copy these into `packaging\identity.json`: `Package/Identity/Name` -> `PackageName`, `Package/Identity/Publisher` (the `CN=...` string) -> `Publisher`, `Package/Properties/PublisherDisplayName` -> `PublisherDisplayName`.
3. Keep `Version` as `major.minor.build.0` (the Store reserves the last number) and raise it for every submission. Keep the application version in `Directory.Build.props` in step.
4. Re-run `scripts\package-msix.ps1`. It warns while the placeholder identity is still there.

## Test locally

Developer Mode must be on (Settings > System > For developers). Nothing needs signing or certificates:

```powershell
Add-AppxPackage -Register builds\msix-layout\AppxManifest.xml    # installs the loose layout
# try it from the Start menu, then:
Get-AppxPackage AutomationPortal* | Remove-AppxPackage
```

To avoid touching your own data use `Invoke-CommandInDesktopPackage -PackageFamilyName <family> -AppId App -Command <layout>\AutomationPortal.exe -Args '--data-root C:\temp\ap-test --background'`; with `--data-root` shell integration stays off. `--shell-sandbox <folder>` turns it on against a throwaway folder and registry key instead. Verified this way: package identity detected, the `ap` alias works (list, import, run over the pipe to the packaged app), StartupTask is queried.

## Windows AI (on this PC)

The "Windows AI (on this PC)" provider (`src\AutomationPortal.Ai.Windows`) uses Phi Silica, the on-device text summarizer and rewriter, and Windows AI image description from the Windows App SDK (`Microsoft.WindowsAppSDK.AI`, 2.5.x). Free, private and offline, but only where Windows allows it:

| Situation | What Settings > AI shows |
|---|---|
| Unpackaged build (zip, dev) | "Needs the Microsoft Store version of Automation Portal." The Windows App SDK is never touched. |
| Store build on a PC without a Copilot+ NPU, or Windows older than 11 24H2 | "Needs a Copilot+ PC." |
| Store build on a Copilot+ PC, model still downloading | "Windows is still setting up on-device AI." plus a **Get it ready** button (`LanguageModel.EnsureReadyAsync`) |
| Store build on a Copilot+ PC, ready | Row is live; if no other AI is set up it is badged **Suggested** and becomes the default when turned on |

How it is wired, and why:

- **No Windows App SDK build targets reach the app.** The package reference in `AutomationPortal.Ai.Windows.csproj` has `PrivateAssets="build;buildtransitive;native"`. Without that, WinAppSDK 2.x silently switches any referencing exe to *self-contained* mode and wants a bootstrapper, which the unpackaged build must not need (it has to start on PCs without the Windows App Runtime). Only the small projection DLLs (about 0.7 MB) flow to the app; they are loaded the first time the Store build asks Windows AI something. The published zip contains no bootstrapper initialisation and no runtime.
- **The runtime comes from the package.** `AppxManifest.template.xml` declares `<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.5.1.0" ...>`. The Store installs that framework (about 47 MB x64) with the app; the framework registers the `Microsoft.Windows.AI.*` activatable classes. If you raise the `Microsoft.WindowsAppSDK.AI` version, raise `MinVersion` to the matching runtime. For sideload / loose-layout testing the framework must be installed (`Get-AppxPackage Microsoft.WindowsAppRuntime.2`; it is installed with any WinAppSDK 2.x app or from the WinAppSDK runtime installer).
- **Capability.** `<systemai:Capability Name="systemAIModels" />` (namespace `http://schemas.microsoft.com/appx/manifest/systemai/windows10`, listed in `IgnorableNamespaces` so older Windows ignores it).
- **Detection never throws.** `WindowsAiProvider` checks `PackageIdentity.IsPackaged`, then asks `LanguageModel.GetReadyState()` inside a try/catch; a missing class, missing runtime or old Windows all end as "Needs a Copilot+ PC".
- Summaries ("a paragraph", "key points") use `TextSummarizer`; rewrites (professional, friendlier, shorter) use `TextRewriter`; everything else, and anything those decline, goes to the Phi Silica language model with Automation Portal's normal prompt. Pictures are described with `ImageDescriptionGenerator`; questions about a picture are answered from that description. Reading the text inside a picture is not supported (use another AI).

Limits: the Phi Silica context window is small (long documents get "too much text for the on-device AI"); structured answers rely on the prompt asking for JSON (no schema enforcement); answers are slower and less capable than cloud models. **Verified on a PC with no NPU only**: the unpackaged build and the loose-registered package (`Add-AppxPackage -Register builds\msix-layout\AppxManifest.xml`, then removed) both start normally and show the plain reasons above. The success path (a real Phi Silica answer) could not be tried here; its routing is covered by unit tests with a fake backend, and `PhiSilicaBackend` itself needs a Copilot+ PC to exercise.
## Submit

1. Partner Center > Automation Portal > Start your submission. Pricing, age rating, properties (category: Productivity), privacy policy URL `https://mindthaapp.github.io/automation-portal/privacy.html`.
2. Packages: upload `builds\AutomationPortal_<version>.msixbundle`. Listing: screenshots, and the icons in `docs\store\assets` (300, 512, 1080 px).
3. Notes for certification: paste the text below.
4. Reserved names lapse if there is no submission by about 2027-01-02.

## Virtualization: what is written outside the package

Version 1.0.1.0 was rejected under policy 10.6.3 because it turned off all registry and AppData write virtualization (`desktop6:RegistryWriteVirtualization` and `desktop6:FileSystemWriteVirtualization` set to `disabled`). The manifest now uses [flexible virtualization](https://learn.microsoft.com/windows/msix/desktop/flexible-virtualization) instead: the `desktop6` properties are gone, every HKCU and AppData write is virtualized as usual, and only the five locations below are excluded. `rescap:unvirtualizedResources` stays, because `virtualization:ExcludedDirectory` and `virtualization:ExcludedKey` require it ([ExcludedDirectory](https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-virtualization-excludeddirectory), [ExcludedKey](https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-virtualization-excludedkey)).

**Windows 11 only.** The exclusion lists need build 20348 or later, and Windows 10 ignores them. On Windows 10 every write would then be virtualized: dark mode would silently do nothing, Start menu links would not appear, and Explorer could not see the app's data, run results, link icons or wallpaper copy. `MinVersion` is therefore `10.0.22000.0`. Keeping Windows 10 would mean declaring the old `desktop6` properties again as a fallback, which is exactly what was rejected.

### The five exclusions

| # | Location | Written by | Why it can't be virtualized |
|---|---|---|---|
| 1 | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | Only the **Set Light/Dark Mode** step (`AppsUseLightTheme`, `SystemUsesLightTheme`), when a person runs it | Windows reads this key. A private copy has no effect. |
| 2 | `$(KnownFolder:RoamingAppData)\Automation Portal` | The app: settings, the library (default location), reminders, recently deleted shortcuts, and the copy of a wallpaper picture that came from a run's temporary files | Settings > Open data folder and Open library show it in Explorer; Windows reads the wallpaper copy. The unpackaged build uses the same folder, so data stays in one place. |
| 3 | `$(KnownFolder:LocalAppData)\Automation Portal` | The app: run history, logs, run temporary files, the file-change records behind Undo file changes, and link icons | Settings > Open logs folder; run results are shown in Explorer and opened by other apps; Explorer draws Desktop, Start and Send to links with icons from here. |
| 4 | `$(KnownFolder:Programs)\Automation Portal Shortcuts` | **Add to Start menu** on a shortcut | Windows virtualizes new folders created directly under Start Menu\Programs, so the Start menu would never see them. |
| 5 | `$(KnownFolder:SendTo)` | **Show in Send to** on a shortcut: one `Automation Portal - <name>.lnk` per shortcut, removed again when it is turned off | Explorer builds the Send to menu from this folder only. A narrower path is not possible: entries must sit directly in it. The app records every file it creates (`shell-state.json`) and only ever deletes those. |

Nothing broader is excluded: not `HKCU\Software`, not `HKCU\Software\Classes`, not the AppData root, and no folder or key of another app.

### Inventory from the source

Registry writes in `src\` (checked with Grep for `SetValue`, `CreateSubKey`, `DeleteValue`, `DeleteSubKeyTree`, `writable: true`, `reg.exe` and the `Reg*` Win32 functions):

| Code | What | Packaged build |
|---|---|---|
| `Actions.Windows\Devices\SystemActions.cs` (`dev.theme`) | `HKCU\...\Themes\Personalize` | Written when the step runs: exclusion 1 |
| `App\Integration\WindowsShellIntegration.cs` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `HKCU\Software\Classes` (protocol, file type, right-click menu) | **Never written.** Every write is behind `if (_o.Packaged) return` or `!_o.Packaged`; the package uses `windows.startupTask`, `windows.protocol` and `windows.fileTypeAssociation` instead |
| Everything else (`WindowsFontResolver`, `RecycleBin`, `Monitors`, `PowerPlanActions`, `FilesPortalActions`, `ThemeService`, `ActionPanelViewModel`) | Reads only | No writes |

Files under AppData that are not the app's own: Send to links (exclusion 5) and the Start menu folder (exclusion 4). Desktop links go to the Desktop folder, which is outside AppData and never virtualized. The `ap.exe` alias under `%LOCALAPPDATA%\Microsoft\WindowsApps` is created by Windows from the manifest. If a person points a file step at a folder inside AppData, normal virtualization applies there (new files stay private to the package; changes to existing files go through, as Windows does since version 1903).

Changes made through Windows APIs are done by Windows itself and are not affected by virtualization: wallpaper (`IDesktopWallpaper`, falling back to `SystemParametersInfo`), default sound devices (`IPolicyConfig`), volume and mute, display mode, resolution, HDR and brightness, power mode and power plan, Wi-Fi and Bluetooth radios, Start with Windows (`StartupTask`), the Jump List and notifications.

What the app writes **by itself**, without a step: its own two data folders (2 and 3), and updates to links the person already added (renames and icon changes are followed; links of deleted shortcuts are removed). It changes no Windows setting and no other app's files on its own. Start with Windows goes through the `StartupTask` API and is off until the person turns it on.

To check: whether programs started by a **Run program** or script step run inside the package's virtualization. If they do, their own HKCU and AppData writes are now private to the package. Try once on Windows 11 with the loose layout (`reg add HKCU\Software\APCheck` from a step, then look in regedit).

### After uninstall, and how to undo

| What | Stays after uninstall? | How to restore |
|---|---|---|
| Dark or light mode | Yes, as last set | Settings > Personalization > Colors > Choose your mode, or run the step again with the other mode |
| Wallpaper | Yes | Settings > Personalization > Background |
| Default sound devices, volume, display, power mode or plan, Wi-Fi, Bluetooth | Yes (normal Windows settings) | The matching Windows Settings page |
| Files a step created, changed, moved or renamed | Yes | Run history > **Undo file changes** for that run (puts back what it can, sends new files to the Recycle Bin, never overwrites) while the app is installed |
| Desktop, Start menu and Send to links the person added | Yes; they stop working, because they start the removed `ap.exe` | Delete them like any file (`shell:sendto`, Start menu folder "Automation Portal Shortcuts"); Send to entries also go when **Show in Send to** is turned off before uninstalling |
| `%APPDATA%\Automation Portal`, `%LOCALAPPDATA%\Automation Portal` | Yes, so a reinstall keeps the person's shortcuts | Delete both folders |
| Anything in the package's private registry and AppData | No, Windows removes it | |

There is no undo for Windows settings in the app. Run history covers file changes only.

## Notes for certification

Paste this into Partner Center > Submission options > Notes for certification (about 1,950 characters with Windows line breaks; Partner Center's limit for this box is not documented, so keep it under 2,000). The longer notes below are for the restricted capability boxes if they are asked for again.

```text
unvirtualizedResources: this package now uses MSIX flexible virtualization (Windows 11, MinVersion 10.0.22000). Write virtualization is no longer disabled; all other HKCU and AppData writes stay virtualized.

Excluded, the complete list:
1. HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize. Only the "Set Light/Dark Mode" step writes here (AppsUseLightTheme, SystemUsesLightTheme). Windows reads this key; a private copy would do nothing.
2. $(KnownFolder:RoamingAppData)\Automation Portal
3. $(KnownFolder:LocalAppData)\Automation Portal
The app's own data. Explorer must see it: Settings opens these folders, results are shown in Explorer, and Explorer reads the wallpaper copy and link icons kept here.
4. $(KnownFolder:Programs)\Automation Portal Shortcuts. Start menu links the user adds.
5. $(KnownFolder:SendTo). "Automation Portal - <name>.lnk" for shortcuts the user sets to Show in Send to. Explorer reads only this folder; the app removes only its own files there.
Nothing broader: not HKCU\Software, not the AppData root, no other app's keys or folders.

Other apps' settings and files change only through steps the user adds to a shortcut and runs, or an automation the user set up. By itself the app writes only its own folders. Start with Windows uses the StartupTask API, off until the user turns it on.

After uninstall these stay: Windows settings as last set (dark mode, wallpaper, sound device), files the user's steps made or changed, the two data folders, and links the user added to Desktop, Start or Send to (they stop working). To restore: Windows Settings > Personalization > Colors or Background, System > Sound, or run the step with the other value. Delete the links and folders like any file. Run history > Undo file changes reverts a run's file changes.

Testing: no sign-in. Gallery > add "Clean Link", copy a link with ?utm_source=x, run it; the clean link is on the clipboard.
```

### Longer notes

**Why `runFullTrust`.** Automation Portal is a desktop automation app. Its shortcuts run other programs, work with the user's files and folders anywhere on disk, control windows, audio, display and power settings, read and write the clipboard, and send keystrokes and mouse input on the user's behalf. None of this is possible from the app container. The app is a packaged WPF (.NET) desktop application launched as `Windows.FullTrustApplication`.

**Keyboard hooks.** Two features install low-level keyboard hooks (`WH_KEYBOARD_LL`):
- *Text expansion and typed phrases*: the user creates snippets such as `;date`. The hook watches typed characters only to match those abbreviations.
- *The macro recorder*: when the user presses Record, it captures their keystrokes and clicks so they can be replayed as a shortcut.

Both are started by the user (text expansion is a setting the user controls; the recorder runs only while a recording is in progress and shows that it is recording). Keystrokes are matched and processed in memory on the PC, never written to disk except as the macro the user chose to record, and never sent anywhere. The hooks skip password fields and secure input, and ignore keystrokes while a password box has focus. Global keyboard shortcuts (Quick Run) use `RegisterHotKey`, not a hook.

**Other capabilities.** `internetClient` (online actions the user adds, such as weather or web pages), `location` (location triggers and sunrise/sunset), `microphone` (speech actions the user adds). Each is used only when the user runs a shortcut or automation that needs it; see the privacy policy. `systemAIModels` lets the optional "Windows AI (on this PC)" provider use the on-device models (Phi Silica) on Copilot+ PCs for the AI actions the user adds; nothing is sent anywhere, and on other PCs the provider simply says it is not available. The package depends on the Windows App Runtime framework (`Microsoft.WindowsAppRuntime.2`) for this.

**Testing notes.** No account or sign-in is needed. Open the app, go to Gallery, add "Clean Link", copy a link containing `?utm_source=x`, run it from the Library; the cleaned link is placed on the clipboard. The `ap` command works in a terminal (`ap list`).
