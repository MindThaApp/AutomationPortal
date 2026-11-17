namespace AutomationPortal.App.Logic;

/// <summary>
/// Keeps the Ctrl+K list so it is built once, not on every open. It is rebuilt after <see cref="Invalidate"/> (a shortcut,
/// folder or page changed) and after <see cref="MaxAge"/>, which also picks up Gallery files added on disk.
/// </summary>
public sealed class PaletteCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly Func<IReadOnlyList<PaletteEntry>> _build;
    private readonly Func<DateTime> _now;
    private IReadOnlyList<PaletteEntry>? _entries;
    private DateTime _builtAt;

    public PaletteCache(Func<IReadOnlyList<PaletteEntry>> build, Func<DateTime>? now = null)
    {
        _build = build;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>How many times the list has been built (for tests and the speed log).</summary>
    public int Builds { get; private set; }

    public IReadOnlyList<PaletteEntry> Get()
    {
        if (_entries is null || _now() - _builtAt > MaxAge)
        {
            _entries = _build();
            _builtAt = _now();
            Builds++;
        }
        return _entries;
    }

    public void Invalidate() => _entries = null;
}
