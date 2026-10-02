using Wpf.Ui.Controls;

namespace Flowmate.App.Infrastructure;

/// <summary>Maps stored icon names ("Globe24") to WPF-UI symbols. Unknown names fall back to Flash24.</summary>
public static class Icons
{
    public static SymbolRegular Parse(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse<SymbolRegular>(name, ignoreCase: false, out var s) ? s : SymbolRegular.Flash24;

    /// <summary>Segoe Fluent glyph character for a symbol name (for tray / text based icon rendering).</summary>
    public static string Glyph(string? name) => char.ConvertFromUtf32((int)Parse(name));

    public static bool IsKnown(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse<SymbolRegular>(name, false, out _);
}
