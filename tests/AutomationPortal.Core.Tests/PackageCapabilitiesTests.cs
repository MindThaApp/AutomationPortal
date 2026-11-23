using Xunit;

namespace AutomationPortal.Core.Tests;

public class PackageCapabilitiesTests
{
    private const string WithCapability = """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
          <Capabilities>
            <Capability Name="internetClient" />
            <rescap:Capability Name="runFullTrust" />
            <rescap:Capability Name="unvirtualizedResources" />
          </Capabilities>
        </Package>
        """;

    private const string Without = """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
          <!-- <rescap:Capability Name="unvirtualizedResources" /> -->
          <Capabilities>
            <Capability Name="internetClient" />
            <rescap:Capability Name="runFullTrust" />
          </Capabilities>
        </Package>
        """;

    [Fact]
    public void Manifest_with_the_capability_allows_writes() => Assert.True(PackageCapabilities.ManifestTextDeclaresUnvirtualizedResources(WithCapability));

    [Fact]
    public void Manifest_without_the_capability_does_not_even_when_commented() => Assert.False(PackageCapabilities.ManifestTextDeclaresUnvirtualizedResources(Without));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not xml")]
    public void Unreadable_text_fails_safe(string? text) => Assert.False(PackageCapabilities.ManifestTextDeclaresUnvirtualizedResources(text));

    [Fact]
    public void Missing_file_fails_safe() => Assert.False(PackageCapabilities.ManifestDeclaresUnvirtualizedResources(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "AppxManifest.xml")));

    [Fact]
    public void File_is_read_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ap-caps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "AppxManifest.xml");
            File.WriteAllText(path, WithCapability);
            Assert.True(PackageCapabilities.ManifestDeclaresUnvirtualizedResources(path));
            File.WriteAllText(path, Without);
            Assert.False(PackageCapabilities.ManifestDeclaresUnvirtualizedResources(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string App = @"C:\U\AppData\Roaming";
    private const string Local = @"C:\U\AppData\Local";

    [Fact]
    public void Store_flavour_uses_the_package_folders()
    {
        Assert.Equal(Path.Combine(Local, "Packages", "Fam_123", "RoamingState"), AutomationPortalPaths.ChooseRoot(true, packaged: true, writesAllowed: false, "Fam_123", App, Local));
        Assert.Equal(Path.Combine(Local, "Packages", "Fam_123", "LocalState"), AutomationPortalPaths.ChooseRoot(false, packaged: true, writesAllowed: false, "Fam_123", App, Local));
    }

    [Theory]
    [InlineData(false, true, "Fam_123")]   // unpackaged
    [InlineData(true, true, "Fam_123")]    // full flavour
    [InlineData(true, false, null)]        // packaged but no family name: fall back
    public void Other_cases_keep_the_AppData_folders(bool packaged, bool allowed, string? family)
    {
        Assert.Equal(Path.Combine(App, "Automation Portal"), AutomationPortalPaths.ChooseRoot(true, packaged, allowed, family, App, Local));
        Assert.Equal(Path.Combine(Local, "Automation Portal"), AutomationPortalPaths.ChooseRoot(false, packaged, allowed, family, App, Local));
    }

    [Fact]
    public void Explicit_roots_still_win()
    {
        var paths = AutomationPortalPaths.ForRoot(Path.Combine(Path.GetTempPath(), "ap-root-x"));
        Assert.EndsWith("roaming", paths.RoamingRoot);
        Assert.EndsWith("local", paths.LocalRoot);
    }
}
