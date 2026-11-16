using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The Undo on the "Deleted ..." bar: brings the shortcut back from Recently deleted, never overwriting anything.</summary>
public static class DeleteUndo
{
    /// <summary>How long the bar stays.</summary>
    public static readonly TimeSpan BarTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The Recently deleted entry that this delete made: same shortcut id, deleted at or after <paramref name="since"/> (less a
    /// little for rounding), readable, newest first. Null when it is gone, for example restored or deleted for good meanwhile.
    /// </summary>
    public static DeletedShortcut? FindEntry(IEnumerable<DeletedShortcut> entries, Guid shortcutId, DateTimeOffset since) =>
        entries.Where(d => d.ShortcutId == shortcutId && d.CanRestore && !d.Damaged && d.DeletedAt >= since.AddSeconds(-2))
               .OrderByDescending(d => d.DeletedAt)
               .FirstOrDefault();

    /// <summary>Restores what this delete made. Returns the bar's title and message.</summary>
    public static (bool Ok, string Title, string? Message) Undo(JsonShortcutLibrary library, Guid shortcutId, string name, DateTimeOffset since)
    {
        var entry = FindEntry(library.RecentlyDeleted.List(), shortcutId, since);
        if (entry is null) return (false, "Couldn't undo", $"\"{name}\" isn't in Recently deleted any more.");
        RestoreResult r;
        try { r = library.Restore(entry); }
        catch (Exception ex) when (ex is LibraryException or IOException or UnauthorizedAccessException)
        {
            return (false, "Couldn't restore the shortcut", ex.Message);
        }
        var notes = Notes(r, library.Automations.Any(a => a.ShortcutId == r.Shortcut.Id));
        return (true, $"Restored \"{r.Shortcut.Name}\".", notes.Count == 0 ? null : string.Join(" ", notes));
    }

    /// <summary>What the restore had to change so that nothing was overwritten.</summary>
    public static List<string> Notes(RestoreResult r, bool automationsUseIt)
    {
        var notes = new List<string>();
        if (r.AsCopy)
            notes.Add("A shortcut with the same ID is already in your library, so it came back as a copy without its hotkey, Quick Run name or Send To entry.");
        else if (automationsUseIt)
            notes.Add(Strings.Get("DeleteUndo_AutomationsThatUseIt"));
        if (r.RenamedFrom is { } old) notes.Add($"Another shortcut is called \"{old}\", so it got a number.");
        if (r.ClearedHotkey is not null) notes.Add($"Another shortcut now uses the hotkey {r.ClearedHotkey}, so it was left off.");
        if (r.ClearedAlias is not null) notes.Add($"Another shortcut now uses the Quick Run name \"{r.ClearedAlias}\", so it was left off.");
        return notes;
    }
}
