# Test data root

`--data-root <folder>` (or `AUTOMATIONPORTAL_DATA_ROOT`) runs the app or `ap` on a throwaway copy of its data. The root also keeps the run away from the user's real folders. Normal launches are unchanged.

## Known folders

Desktop, Documents, Downloads, Pictures, Screenshots, Music, Videos, Home, OneDrive and the app-data folders map to `<root>\user\<Name>` (made on first use, only inside the root). `Get Folder`, notes, backups, screenshots, window layouts, AI provider files, and the "Tidy Downloads" template all use these. Portal AI Host discovery reads only the env override and a copy of the discovery file under `<root>\user\LocalAppData\Packages\...`; it never probes the real LocalAppData or the default port.

## No dialogs

No file, folder or save dialog is shown. Every picker goes through one `IPicker`. Answers come from `<root>\picker-answers.json`, a JSON queue:

```json
["input\\report.csv", "C:\\Temp\\ap-test\\out", ["a.txt", "b.txt"]]
```

- An item is a path (relative paths start at the root) or, for multi-file pickers, an array of paths.
- Each request takes the next item. An answer outside the root, or an empty queue, is a cancel.
- Every request and its outcome is logged to `<root>\picker-requests.log`.

## Write guard

Under a root, `TestRootGuard.Check(path)` runs before actions write, create, move, rename, replace or delete (file actions, zip, downloads, new result files, spreadsheet/CSV writes, safe-write helper). A target outside the root stops the action with a clear error. "Move to OneDrive" and "Change library folder" may only target paths inside the root.

## Tests

Tests use `UserFolders.Scope(root)` to apply a root to the current test only.
