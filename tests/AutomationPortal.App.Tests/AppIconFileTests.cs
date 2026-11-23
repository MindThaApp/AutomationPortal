using System.IO;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>The app icon must list the sizes the taskbar, hover preview and title bar ask for.</summary>
public class AppIconFileTests
{
    private static readonly int[] ExpectedSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        return null;
    }

    [Fact]
    public void App_icon_lists_exactly_the_expected_sizes()
    {
        var root = Root();
        Assert.NotNull(root);
        var bytes = File.ReadAllBytes(Path.Combine(root!, "src", "AutomationPortal.App", "Assets", "automationportal.ico"));

        // ICONDIR: reserved (2 bytes, 0), type (2 bytes, 1 = icon), count (2 bytes, little endian).
        Assert.True(bytes.Length >= 6);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.Equal(ExpectedSizes.Length, count);

        // ICONDIRENTRY: 16 bytes each, width at offset 0 where 0 means 256.
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var width = bytes[6 + 16 * i];
            sizes.Add(width == 0 ? 256 : width);
        }
        Assert.Equal(ExpectedSizes, sizes.OrderBy(s => s).ToArray());
    }
}
