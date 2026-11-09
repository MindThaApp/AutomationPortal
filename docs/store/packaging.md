# Packaging Automation Portal for the Microsoft Store

Automation Portal ships as a full-trust MSIX (Store ID `9PB9C8FHG105`, display name "Automation Portal"). The same code also runs unpackaged (`scripts\publish.ps1`); at runtime `PackageIdentity.IsPackaged` picks the right behaviour.

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
| Writes to registry and AppData | un-virtualised (`desktop6` + `unvirtualizedResources`) | normal |

Everything the unpackaged build registers is listed in `%LOCALAPPDATA%\Automation Portal\shell-state.json` and in the registry keys above, and `--unregister` removes exactly that.

### Packaged right-click menu (not implemented)

Windows 11's modern menu needs an `IExplorerCommand` COM server declared with `desktop4`/`desktop5` `windows.fileExplorerContextMenus` plus `com:ExeServer`. It needs an out-of-process COM exe with its own class factory and a dynamic sub-command enumerator. That is a separate piece of native-style work that cannot be verified without clicking through Explorer, so it is left out of v1. Packaged users get **Send to > Automation Portal - <shortcut>** (kept in sync with "Show in Send to") and the same shortcuts on Desktop, Start and the Jump List. Writing classic HKCU verbs from the package is possible (virtualisation is off) but would leave dead menu entries behind after uninstall, so it is deliberately not done.

## Build the package

Needs the Windows SDK (already present at `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0`; otherwise `winget install Microsoft.WindowsSDK.10.0.26100`, or add "Windows SDK Signing Tools for Desktop Apps" in the Visual Studio Installer). No Visual Studio is required.

```powershell
scripts\make-logos.ps1       # only when the icon design changes (logos are committed in packaging\Assets)
scripts\package-msix.ps1 -Publish
```

Output: `builds\AutomationPortal_1.0.0.0_x64.msix` (unsigned; the Store signs on submission) and the loose layout `builds\msix-layout\`. `makeappx pack` validates the manifest, so a successful pack means the manifest is schema-valid.

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

1. Partner Center > Automation Portal > Start your submission. Pricing, age rating, properties (category: Productivity), privacy policy URL `https://mindthaapp.github.io/flow-mate/privacy.html`.
2. Packages: upload `builds\AutomationPortal_<version>_x64.msix`. Listing: screenshots, and the icons in `docs\store\assets` (300, 512, 1080 px).
3. Notes for certification: paste the text below.
4. Reserved names lapse if there is no submission by about 2027-01-02.

## Notes for certification (draft)

**Why `runFullTrust`.** Automation Portal is a desktop automation app. Its shortcuts run other programs, work with the user's files and folders anywhere on disk, control windows, audio, display and power settings, read and write the clipboard, and send keystrokes and mouse input on the user's behalf. None of this is possible from the app container. The app is a packaged WPF (.NET) desktop application launched as `Windows.FullTrustApplication`.

**Why `unvirtualizedResources` (with registry and file-system write virtualisation disabled).** Many actions exist to change the real Windows environment for the user: switch dark mode, change the wallpaper or default audio device, set a file association, create Send To / Desktop / Start menu links, and save files into the user's own folders. If those writes were redirected into the package's private store they would silently have no effect. The app only writes what the user's shortcut asks for; it does not change settings by itself.

**Keyboard hooks.** Two features install low-level keyboard hooks (`WH_KEYBOARD_LL`):
- *Text expansion and typed phrases*: the user creates snippets such as `;date`. The hook watches typed characters only to match those abbreviations.
- *The macro recorder*: when the user presses Record, it captures their keystrokes and clicks so they can be replayed as a shortcut.

Both are started by the user (text expansion is a setting the user controls; the recorder runs only while a recording is in progress and shows that it is recording). Keystrokes are matched and processed in memory on the PC, never written to disk except as the macro the user chose to record, and never sent anywhere. The hooks skip password fields and secure input, and ignore keystrokes while a password box has focus. Global keyboard shortcuts (Quick Run) use `RegisterHotKey`, not a hook.

**Other capabilities.** `internetClient` (online actions the user adds, such as weather or web pages), `location` (location triggers and sunrise/sunset), `microphone` (speech actions the user adds). Each is used only when the user runs a shortcut or automation that needs it; see the privacy policy. `systemAIModels` lets the optional "Windows AI (on this PC)" provider use the on-device models (Phi Silica) on Copilot+ PCs for the AI actions the user adds; nothing is sent anywhere, and on other PCs the provider simply says it is not available. The package depends on the Windows App Runtime framework (`Microsoft.WindowsAppRuntime.2`) for this.

**Testing notes.** No account or sign-in is needed. Open the app, go to Gallery, add "Clean Link", copy a link containing `?utm_source=x`, run it from the Library; the cleaned link is placed on the clipboard. The `ap` command works in a terminal (`ap list`).
