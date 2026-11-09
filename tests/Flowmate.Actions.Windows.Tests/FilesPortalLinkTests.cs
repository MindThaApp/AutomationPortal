using System.Diagnostics;
using Flowmate.Actions.Windows.Apps;

namespace Flowmate.Actions.Windows.Tests;

public class FilesPortalLinkTests
{
    [Fact]
    public void Builds_an_escaped_reveal_link_for_local_and_unc_paths()
    {
        Assert.Equal("filesportal:reveal?path=C%3A%5CUsers%5Cme%5Ca%20b%26c.txt", FilesPortalLink.BuildUri(@"C:\Users\me\a b&c.txt"));
        Assert.Equal("filesportal:reveal?path=%5C%5Cnas%5Cshare%5Cx.png", FilesPortalLink.BuildUri(@"\\nas\share\x.png"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"docs\a.txt")]
    [InlineData("C:a.txt")]
    [InlineData("C:\\a\u0001b")]
    [InlineData(@"\\?\C:\Windows")]
    public void Refuses_paths_that_are_not_absolute_or_have_control_characters(string path) =>
        Assert.Throws<ArgumentException>(() => FilesPortalLink.BuildUri(path));

    [Fact]
    public void Uses_the_link_when_registered_and_never_touches_the_path()
    {
        var started = new List<ProcessStartInfo>();
        Assert.True(FilesPortalLink.Reveal(@"C:\x\y.txt", false, () => true, started.Add));
        var psi = Assert.Single(started);
        Assert.Equal("filesportal:reveal?path=C%3A%5Cx%5Cy.txt", psi.FileName);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void Falls_back_to_explorer_with_an_argument_list()
    {
        var started = new List<ProcessStartInfo>();
        Assert.False(FilesPortalLink.Reveal(@"C:\my docs\y.txt", false, () => false, started.Add));
        var file = Assert.Single(started);
        Assert.Equal("explorer.exe", file.FileName);
        Assert.Equal(new[] { @"/select,C:\my docs\y.txt" }, file.ArgumentList);
        Assert.Equal(string.Empty, file.Arguments);

        started.Clear();
        FilesPortalLink.Reveal(@"C:\my docs", true, () => false, started.Add);
        Assert.Equal(new[] { @"C:\my docs" }, started[0].ArgumentList);
    }
}
