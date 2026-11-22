using System.Security.Cryptography;
using System.Text;

namespace AutomationPortal.Core;

/// <summary>
/// The <c>--token</c> that Send To, desktop, Start menu, Jump List and right-click entries made by Automation Portal carry next to
/// <c>--run &lt;id&gt;</c>: an HMAC of the shortcut id with a per-install key kept in the secret store (DPAPI). A <c>--run</c> without a
/// valid token came from somewhere else and is asked about like any other app's request.
/// </summary>
public sealed class LaunchTokens
{
    public const string SecretName = "launch-token-key";

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

    /// <summary>The token for one shortcut: 32 hex characters.</summary>
    public string For(Guid shortcutId) =>
        Convert.ToHexString(HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes("run:" + shortcutId.ToString("D"))))[..32].ToLowerInvariant();

    /// <summary>True when <paramref name="token"/> is this install's token for the shortcut. Compared in constant time.</summary>
    public bool IsValid(Guid shortcutId, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 32) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(For(shortcutId)), Encoding.ASCII.GetBytes(token.ToLowerInvariant()));
    }
}
