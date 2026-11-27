using System.Text.RegularExpressions;

namespace AutomationPortal.Actions.Documents;

public static class FileKindFilter
{
    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
        { "txt", "md", "log", "json", "xml", "yaml", "yml", "ini", "cfg", "csv", "tsv", "html", "htm", "css", "js", "ts", "py", "cs", "sql" };
    private static readonly HashSet<string> InstallerExt = new(StringComparer.OrdinalIgnoreCase)
        { "exe", "msi", "msix", "msixbundle", "appx", "appxbundle" };

    public static readonly Choice[] Choices =
    {
        new("any", Strings.Get("FileKindFilter_Label_Anything")), new("image", Strings.Get("FileKindFilter_Label_Pictures")), new("video", Strings.Get("FileKindFilter_Label_Videos")), new("audio", Strings.Get("FileKindFilter_Label_MusicAudio")),
        new("document", Strings.Get("FileKindFilter_Label_Documents")), new("pdf", Strings.Get("FileKindFilter_Label_PDFs")), new("spreadsheet", Strings.Get("FileKindFilter_Label_Spreadsheets")),
        new("presentation", Strings.Get("FileKindFilter_Label_Presentations")), new("archive", Strings.Get("FileKindFilter_Label_ZipArchives")), new("installer", Strings.Get("FileKindFilter_Label_Installers")),
        new("text", Strings.Get("FileKindFilter_Label_PlainTextFiles")),
    };

    public static bool Matches(string kind, string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return kind.ToLowerInvariant() switch
        {
            "" or "any" => true,
            "image" => FileKinds.OfExtension(ext) == "Image",
            "video" => FileKinds.OfExtension(ext) == "Video",
            "audio" => FileKinds.OfExtension(ext) == "Audio",
            "document" => FileKinds.OfExtension(ext) == "Document",
            "pdf" => FileKinds.OfExtension(ext) == "PDF",
            "spreadsheet" => FileKinds.OfExtension(ext) == "Spreadsheet",
            "presentation" => FileKinds.OfExtension(ext) == "Presentation",
            "archive" => FileKinds.OfExtension(ext) == "Archive",
            "installer" => InstallerExt.Contains(ext),
            "text" => TextExt.Contains(ext),
            _ => true,
        };
    }
}

/// <summary>The filters shared by Find Files and Filter Files.</summary>
public sealed class FileQuery
{
    public string Kind { get; init; } = "any";
    public string NameContains { get; init; } = "";
    /// <summary>Extensions without dots, e.g. "jpg", "png". Empty = any.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public long? MinBytes { get; init; }
    public long? MaxBytes { get; init; }
    /// <summary>"modified" or "created".</summary>
    public string DateField { get; init; } = "modified";
    public double? ChangedWithinDays { get; init; }
    public double? OlderThanDays { get; init; }
    /// <summary>Leave out files that are still downloading or were left half-finished (see <see cref="IsPartialDownload"/>).</summary>
    public bool SkipPartialDownloads { get; init; }

    /// <summary>Endings browsers and download tools give a file until it is complete.</summary>
    public static readonly IReadOnlySet<string> PartialDownloadExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload",
    };

    public static bool IsPartialDownload(string path) => PartialDownloadExtensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<string> ParseExtensions(string text) =>
        text.Split(new[] { ',', ';', ' ', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0).Distinct().ToList();

    public bool Matches(string path, DateTime now)
    {
        if (SkipPartialDownloads && IsPartialDownload(path)) return false;
        if (!FileKindFilter.Matches(Kind, path)) return false;
        var name = Path.GetFileName(path);
        if (NameContains.Length > 0 && !NameMatches(name)) return false;
        if (Extensions.Count > 0 && !Extensions.Contains(Path.GetExtension(path).TrimStart('.'), StringComparer.OrdinalIgnoreCase)) return false;
        if (MinBytes is not null || MaxBytes is not null)
        {
            long len;
            try { len = new FileInfo(path).Length; } catch { return false; }
            if (MinBytes is { } min && len < min) return false;
            if (MaxBytes is { } max && len > max) return false;
        }
        if (ChangedWithinDays is not null || OlderThanDays is not null)
        {
            DateTime when;
            try { when = DateField == "created" ? File.GetCreationTime(path) : File.GetLastWriteTime(path); } catch { return false; }
            var ageDays = (now - when).TotalDays;
            if (ChangedWithinDays is { } within && ageDays > within) return false;
            if (OlderThanDays is { } older && ageDays < older) return false;
        }
        return true;
    }

    private bool NameMatches(string name)
    {
        if (NameContains.Contains('*') || NameContains.Contains('?'))
        {
            var rx = "^" + Regex.Escape(NameContains).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return name.Contains(NameContains, StringComparison.OrdinalIgnoreCase);
    }

    public static readonly Choice[] SortChoices =
    {
        new("name", Strings.Get("FileQuery_Label_Name")), new("modified", Strings.Get("FileQuery_Label_DateModified")), new("created", Strings.Get("FileQuery_Label_DateCreated")),
        new("size", Strings.Get("FileQuery_Label_Size")), new("kind", Strings.Get("FileQuery_Label_Kind")),
    };

    public static IEnumerable<string> Sort(IEnumerable<string> paths, string sortBy, bool descending)
    {
        IOrderedEnumerable<string> ordered = sortBy switch
        {
            "modified" => descending ? paths.OrderByDescending(p => Safe(() => File.GetLastWriteTime(p))) : paths.OrderBy(p => Safe(() => File.GetLastWriteTime(p))),
            "created" => descending ? paths.OrderByDescending(p => Safe(() => File.GetCreationTime(p))) : paths.OrderBy(p => Safe(() => File.GetCreationTime(p))),
            "size" => descending ? paths.OrderByDescending(p => Safe(() => new FileInfo(p).Length)) : paths.OrderBy(p => Safe(() => new FileInfo(p).Length)),
            "kind" => descending ? paths.OrderByDescending(p => FileKinds.Of(p)) : paths.OrderBy(p => FileKinds.Of(p)),
            _ => descending ? paths.OrderByDescending(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase) : paths.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase),
        };
        return ordered.ThenBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase);
    }

    private static T Safe<T>(Func<T> f) where T : struct { try { return f(); } catch { return default; } }

    public static IEnumerable<string> Enumerate(string folder, bool subfolders, bool includeHidden)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = subfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? FileAttributes.None : FileAttributes.Hidden | FileAttributes.System,
        };
        return Directory.EnumerateFiles(folder, "*", options);
    }
}
