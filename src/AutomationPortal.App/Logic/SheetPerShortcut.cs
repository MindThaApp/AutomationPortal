namespace AutomationPortal.App.Logic;

/// <summary>
/// At most one check or confirm sheet per shortcut. A shortcut is reserved from the moment a sheet is being prepared until it closes;
/// a second click or drop meanwhile brings the open sheet forward instead of opening another.
/// </summary>
public sealed class SheetPerShortcut<T> where T : class
{
    private readonly Dictionary<Guid, T?> _open = new();

    /// <summary>False when one is already reserved; <paramref name="existing"/> is its sheet, or null while it is still being prepared.</summary>
    public bool TryReserve(Guid shortcutId, out T? existing)
    {
        if (_open.TryGetValue(shortcutId, out existing)) return false;
        _open[shortcutId] = null;
        return true;
    }

    public void Attach(Guid shortcutId, T sheet) => _open[shortcutId] = sheet;

    public void Release(Guid shortcutId) => _open.Remove(shortcutId);

    public bool IsReserved(Guid shortcutId) => _open.ContainsKey(shortcutId);
}
