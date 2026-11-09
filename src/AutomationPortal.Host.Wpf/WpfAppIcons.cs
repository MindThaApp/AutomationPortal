using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Flowmate.Actions.Windows.Apps;

namespace Flowmate.Host.Wpf;

/// <summary>App icons from the shell as frozen WPF images.</summary>
public static class WpfAppIcons
{
    /// <summary>Icon for an app (size in pixels), or null when the shell has none. Safe to call from any thread.</summary>
    public static BitmapSource? Load(InstalledApp app, int size = 48) => AppIcons.Load(app, size, ToBitmapSource);

    public static BitmapSource? Load(string parsingNameOrPath, int size = 48) => AppIcons.Load(parsingNameOrPath, size, ToBitmapSource);

    private static BitmapSource ToBitmapSource(IntPtr hbitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
