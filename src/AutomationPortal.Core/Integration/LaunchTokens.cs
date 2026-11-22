using System.Security.Cryptography;
using System.Text;

namespace AutomationPortal.Core;

/// <summary>
/// The <c>--token</c> that Send To, right-click, desktop, Start menu and Jump List entries made by Automation Portal carry next to
/// <c>--run &lt;id&gt;</c>: an HMAC of the shortcut id and the kind of entry, with a per-install key kept in the secret store (DPAPI).
/// A token only counts for a launch that Explorer started, without <c>--input</c>, and with files only for Send To and right-click
/// entries. Anything else is asked about like any other app's request.
/// </summary>
public sealed class LaunchTokens
{
    public const string SecretName = "launch-token-key";

    /// <summary>The kinds of entry a token can be made for.</summary>
    public static IReadOnlyList<string> EntryKinds { get; } = new[] { "sendto", "verb", "desktop", "start", "jumplist" };

    private readonly ISecretStore _secrets;
    private readonly object _gate = new();
    private byte[]? _key;

    public LaunchTokens(ISecretStore secrets) => _secrets = secrets;

    private byte[] Key()
    {
        lock (_gate)
        {
            if (_key is not null) return _key;
            var stored = _secrets.Get(SecretName);
            if (stored is not null)
            {
                try { var bytes = Convert.FromBase64String(stored); if (bytes.Length >= 32) return _key = bytes; }
                catch (FormatException) { }
            }
            var fresh = RandomNumberGenerator.GetBytes(32);
            _secrets.Set(SecretName, Convert.ToBase64String(fresh));
            return _key = fresh;
        }
    }

    /// <summary>The token for one shortcut in one kind of entry: 32 hex characters.</summary>
    public string For(Guid shortcutId, string entryKind)
    {
        if (!EntryKinds.Contains(entryKind)) throw new ArgumentException("Unknown entry kind: " + entryKind, nameof(entryKind));
        return Convert.ToHexString(HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes($"run:{shortcutId:D}:{entryKind}")))[..32].ToLowerInvariant();
    }

    /// <summary>The kind of entry <paramref name="token"/> was made for, or null when it isn't one of this install's tokens for the shortcut.</summary>
    public string? KindOf(Guid shortcutId, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 32) return null;
        var given = Encoding.ASCII.GetBytes(token.ToLowerInvariant());
        string? found = null;
        foreach (var kind in EntryKinds)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(For(shortcutId, kind)), given)) found = kind;
        return found;
    }

    /// <summary>Only Send To and right-click entries hand files over; the others never do.</summary>
    public static bool MayCarryFiles(string entryKind) => entryKind is "sendto" or "verb";

    /// <summary>True when <paramref name="imagePath"/> is %WINDIR%\explorer.exe.</summary>
    public static bool IsExplorer(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        try { return string.Equals(Path.GetFullPath(imagePath), explorer, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>
    /// True when a <c>--run</c> launch may run as before: a valid token for this shortcut, started by Explorer
    /// (<paramref name="launcherParent"/>: the program that started ap.exe or AutomationPortal.exe; null when it couldn't be found),
    /// no <c>--input</c>, and files only from a Send To or right-click entry. Fails closed.
    /// </summary>
    public bool Accepts(Guid shortcutId, string? token, bool hasInput, bool hasFiles, string? launcherParent) =>
        !hasInput && IsExplorer(launcherParent) && KindOf(shortcutId, token) is { } kind && (!hasFiles || MayCarryFiles(kind));
}
