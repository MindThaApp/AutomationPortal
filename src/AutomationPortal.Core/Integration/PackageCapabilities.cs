namespace AutomationPortal.Core;

/// <summary>
/// Tells whether this copy may write outside its own sandbox: the app's data folders in AppData, the Start menu, Send to and the
/// Windows colour settings. True when unpackaged. When packaged, true only if the installed package's manifest declares the
/// <c>unvirtualizedResources</c> capability (the "full" package); the Microsoft Store package does not. The same binaries run in both,
/// so this is read at run time from the manifest next to the exe.
/// </summary>
public static class PackageCapabilities
{
    private static bool? _cached;

    /// <summary>Test hook: force the answer. Null = detect.</summary>
    public static bool? Override { get; set; }

    public static bool UnvirtualizedWritesAllowed => Override ?? (_cached ??= Detect());

    private static bool Detect()
    {
        if (!PackageIdentity.IsPackaged) return true;
        return ManifestDeclaresUnvirtualizedResources(Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml"));
    }

    /// <summary>True when the manifest file declares the capability. Missing, unreadable or malformed file: false (fail safe).</summary>
    public static bool ManifestDeclaresUnvirtualizedResources(string manifestPath)
    {
        try { return ManifestTextDeclaresUnvirtualizedResources(File.ReadAllText(manifestPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>True when the manifest text has a Capability element named unvirtualizedResources (comments are ignored).</summary>
    public static bool ManifestTextDeclaresUnvirtualizedResources(string? manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText)) return false;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(manifestText);
            return doc.Descendants().Any(e => e.Name.LocalName == "Capability"
                && string.Equals((string?)e.Attribute("Name"), "unvirtualizedResources", StringComparison.Ordinal));
        }
        catch (System.Xml.XmlException) { return false; }
    }

    /// <summary>Message shown when a feature needs the website version.</summary>
    public const string NotInStoreVersion = "This isn't available in the Microsoft Store version of Automation Portal. It is in the version from our website.";
}
