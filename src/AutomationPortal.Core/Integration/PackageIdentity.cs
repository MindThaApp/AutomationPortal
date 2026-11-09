using System.Runtime.InteropServices;
using System.Text;

namespace AutomationPortal.Core;

/// <summary>Tells whether the process runs with MSIX package identity (the Store build) or unpackaged (dev / zip build).</summary>
public static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    private static bool? _isPackaged;

    /// <summary>Test hook: force a mode. Null = detect.</summary>
    public static bool? Override { get; set; }

    public static bool IsPackaged => Override ?? (_isPackaged ??= Detect());

    /// <summary>Package family name (e.g. "Publisher.AutomationPortal_8wekyb3d8bbwe"), or null when unpackaged.</summary>
    public static string? FamilyName
    {
        get
        {
            if (!IsPackaged || !OperatingSystem.IsWindows()) return null;
            try
            {
                var length = 0;
                GetCurrentPackageFamilyName(ref length, null);
                if (length <= 0) return null;
                var sb = new StringBuilder(length);
                return GetCurrentPackageFamilyName(ref length, sb) == 0 ? sb.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
        }
    }

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var length = 0;
            var rc = GetCurrentPackageFullName(ref length, null);
            return rc != AppModelErrorNoPackage && (rc == ErrorInsufficientBuffer || rc == 0);
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);
}
