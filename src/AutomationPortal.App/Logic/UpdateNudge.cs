using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>What the Store said about pending updates. <see cref="Version"/> is the newest pending version when it is known.</summary>
internal readonly record struct StoreUpdateInfo(bool Found, string? Version);

/// <summary>Asks the Store for pending updates. Null when it could not be asked.</summary>
internal interface IStoreUpdateProbe
{
    Task<StoreUpdateInfo?> QueryAsync();
}

/// <summary>
/// The decision behind the "new version is ready" notice. Only the packaged (Store) build asks, at most once per session,
/// and a dismissed version stays dismissed; a newer version shows again. Never throws: a failed check shows nothing.
/// </summary>
internal sealed class UpdateNudge
{
    private readonly bool _packaged;
    private readonly IStoreUpdateProbe _probe;
    private readonly Func<string?> _dismissed;
    private readonly Action<string> _remember;
    private int _asked;

    public UpdateNudge(bool packaged, IStoreUpdateProbe probe, Func<string?> dismissedVersion, Action<string> rememberDismissed)
    {
        _packaged = packaged;
        _probe = probe;
        _dismissed = dismissedVersion;
        _remember = rememberDismissed;
    }

    /// <summary>The version the notice is showing for, or null when it is hidden.</summary>
    public string? Visible { get; private set; }

    /// <summary>Asks the Store (once per session) and returns the version to announce, or null for no notice.</summary>
    public async Task<string?> CheckAsync()
    {
        if (!_packaged || Interlocked.Exchange(ref _asked, 1) == 1) return null;
        try
        {
            var info = await _probe.QueryAsync().ConfigureAwait(false);
            if (info is not { Found: true } found) return null;
            var version = string.IsNullOrWhiteSpace(found.Version) ? "unknown" : found.Version!;
            if (IsDismissed(version, _dismissed())) return null;
            Visible = version;
            return version;
        }
        catch (Exception ex)
        {
            AppLog.Write("Update check failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>"Later": hides the notice and remembers the version so it stays hidden until a newer one comes.</summary>
    public void Dismiss()
    {
        if (Visible is not { } version) return;
        Visible = null;
        try { _remember(version); }
        catch (Exception ex) { AppLog.Write("Update dismissal not saved: " + ex.Message); }
    }

    /// <summary>True when the dismissed version is the same as, or newer than, the one found.</summary>
    public static bool IsDismissed(string found, string? dismissed)
    {
        if (string.IsNullOrWhiteSpace(dismissed)) return false;
        if (Version.TryParse(found, out var f) && Version.TryParse(dismissed, out var d)) return d >= f;
        return string.Equals(found, dismissed, StringComparison.OrdinalIgnoreCase);
    }
}
