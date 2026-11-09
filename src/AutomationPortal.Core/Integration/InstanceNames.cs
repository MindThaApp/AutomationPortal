using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AutomationPortal.Core;

/// <summary>
/// Names that tie the app, its second launches and the <c>ap</c> CLI together: <c>AutomationPortal-&lt;user SID&gt;</c>.
/// A custom data root (tests, demos) gets its own suffix so it never talks to the user's real running app.
/// </summary>
public static class InstanceNames
{
    [SupportedOSPlatform("windows")]
    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    [SupportedOSPlatform("windows")]
    public static string For(string? dataRoot)
    {
        var name = "AutomationPortal-" + UserSid;
        if (string.IsNullOrWhiteSpace(dataRoot)) return name;
        var full = Path.GetFullPath(dataRoot).TrimEnd('\\', '/').ToLowerInvariant();
        return name + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8];
    }

    /// <summary>The data root the caller asked for: <c>--data-root &lt;path&gt;</c> or AUTOMATIONPORTAL_DATA_ROOT, else null (real data).</summary>
    public static string? DataRootFrom(IReadOnlyList<string> args)
    {
        args = LaunchArguments.Trusted(args);
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals("--data-root", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        var env = Environment.GetEnvironmentVariable("AUTOMATIONPORTAL_DATA_ROOT");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }
}
