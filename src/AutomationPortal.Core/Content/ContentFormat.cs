using System.Globalization;

namespace AutomationPortal.Core;

public enum ContentKind { Text, Number, Boolean, Date, Url, File, Folder, Image, Dictionary, Location, App, Window, Any }

/// <summary>Human-friendly formatting helpers shared by content items and actions.</summary>
public static class ContentFormat
{
    /// <summary>12.5 → "12.5", 3 → "3" (invariant culture, no exponent for normal values).</summary>
    public static string Number(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    /// <summary>1536 → "1.5 KB", 512 → "512 bytes".</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        string[] units = { "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture)} {units[i]}";
    }

    /// <summary>Local short date + time, e.g. "02/10/2026 09:00".</summary>
    public static string Date(DateTimeOffset d) => d.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";
}

/// <summary>Classifies files into the plain-English kinds shown in the editor and used by "sort into folders by kind".</summary>
public static class FileKinds
{
    private static readonly Dictionary<string, string> Map = BuildMap();

    private static Dictionary<string, string> BuildMap()
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string kind, string exts) { foreach (var e in exts.Split(' ')) m[e] = kind; }
        Add("Image", "jpg jpeg png gif bmp tif tiff webp heic heif ico svg");
        Add("PDF", "pdf");
        Add("Document", "doc docx rtf odt txt md pages");
        Add("Spreadsheet", "xls xlsx csv ods numbers");
        Add("Presentation", "ppt pptx odp key");
        Add("Audio", "mp3 wav flac aac m4a ogg wma");
        Add("Video", "mp4 mkv avi mov wmv webm m4v");
        Add("Archive", "zip rar 7z tar gz");
        Add("Program", "exe msi bat cmd ps1");
        return m;
    }

    /// <summary>"Image", "PDF", "Document", "Spreadsheet", "Presentation", "Audio", "Video", "Archive", "Program" or "Other".</summary>
    public static string OfExtension(string? extension)
    {
        var e = (extension ?? "").TrimStart('.');
        return Map.TryGetValue(e, out var k) ? k : "Other";
    }

    public static string Of(string path) => OfExtension(Path.GetExtension(path));
    public static bool IsImage(string path) => Of(path) == "Image";
}
