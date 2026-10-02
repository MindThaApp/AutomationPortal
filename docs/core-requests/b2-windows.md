# Core requests from B2 (Flowmate.Actions.Windows)

## 1. Faithful clipboard snapshot / restore (nice to have)

**What:** `IClipboardService` can only read and write a `ContentValue` (text, files, image). Get Selected Text
(`text.getSelected`) has to press Ctrl+C when the focused app doesn't expose its selection through UI Automation, then put
the user's clipboard back. Anything a `ContentValue` can't represent (rich text / HTML, custom app formats, several
formats at once) is lost or reduced to plain text by that restore.

**Why:** the action promises "restores the clipboard"; for rich content that is only approximately true today.

**Suggested API:**
```csharp
public interface IClipboardService
{
    // existing members ...
    /// <summary>Opaque copy of every format currently on the clipboard (null when it is empty).</summary>
    Task<object?> SnapshotAsync();
    /// <summary>Puts a snapshot back exactly as it was (clears the clipboard when null).</summary>
    Task RestoreAsync(object? snapshot);
}
```
The host implements it on the UI thread by copying every `IDataObject` format (WPF `Clipboard.GetDataObject()` +
`SetDataObject(..., copy: true)`). B2 would then call `SnapshotAsync` / `RestoreAsync` instead of `GetAsync` / `SetAsync`.

Workaround in place: B2 uses `GetAsync` / `SetAsync` / `ClearAsync` and the action description says rich formatting may be reduced.
