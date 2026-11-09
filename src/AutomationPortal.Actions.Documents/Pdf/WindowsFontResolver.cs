using Microsoft.Win32;
using PdfSharp.Fonts;

namespace Flowmate.Actions.Documents;

/// <summary>
/// Finds TrueType fonts installed in Windows for PDFsharp (its core build doesn't read the Windows font list by itself).
/// Unknown font names fall back to Segoe UI, then Arial.
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    private sealed record Family(string? Regular, string? Bold, string? Italic, string? BoldItalic);

    private static readonly Lazy<Dictionary<string, Family>> Families = new(Load);

    private static Dictionary<string, Family> Load()
    {
        var found = new Dictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);
        var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

        void Read(RegistryKey? root, string dir)
        {
            if (root is null) return;
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string file) continue;
                var path = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
                if (!path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                var n = name;
                var paren = n.IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0) n = n[..paren];
                var bold = false; var italic = false;
                bool Strip(string suffix) { if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { n = n[..^suffix.Length]; return true; } return false; }
                if (Strip(" Bold Italic")) { bold = true; italic = true; }
                else if (Strip(" Bold")) bold = true;
                else if (Strip(" Italic")) italic = true;
                else Strip(" Regular");
                if (!found.TryGetValue(n, out var slots)) found[n] = slots = new string?[4];
                slots[(bold ? 1 : 0) + (italic ? 2 : 0)] ??= path;
            }
        }
        Read(Registry.LocalMachine, fontDir);
        using (var hkcu = Registry.CurrentUser) Read(hkcu, userDir);
        return found.ToDictionary(kv => kv.Key, kv => new Family(kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]), StringComparer.OrdinalIgnoreCase);
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var families = Families.Value;
        foreach (var candidate in new[] { familyName, "Segoe UI", "Arial", "Calibri", "Tahoma" })
        {
            if (!families.TryGetValue(candidate, out var fam)) continue;
            var path = (isBold, isItalic) switch
            {
                (true, true) => fam.BoldItalic, (true, false) => fam.Bold, (false, true) => fam.Italic, _ => fam.Regular,
            } ?? fam.Regular ?? fam.Bold ?? fam.Italic ?? fam.BoldItalic;
            if (path is not null)
                return new FontResolverInfo(path, mustSimulateBold: isBold && path != fam.Bold && path != fam.BoldItalic, mustSimulateItalic: false);
        }
        return null;
    }

    public byte[]? GetFont(string faceName) => File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;
}
