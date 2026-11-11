using System.IO.Compression;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>
/// "Back up library now": one zip with the library's files (shortcuts, folders, automations, snippets) as they are on disk, plus each
/// shortcut as a .autoportal file that can be imported. Only reads the library and only creates one new file in the chosen folder.
/// </summary>
public static class LibraryBackup
{
    public const string LibraryEntryFolder = "Library";
    public const string ImportEntryFolder = "Shortcuts to import";

    /// <summary>Documents\Automation Portal backups.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Automation Portal backups");

    /// <summary>Writes a new zip in <paramref name="targetFolder"/> under a name not used yet and returns its path.</summary>
    public static string Create(AutomationPortalPaths paths, string targetFolder, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(targetFolder)) throw new LibraryException("Choose a folder for the backup first.");
        var target = Path.GetFullPath(targetFolder);
        var library = Path.GetFullPath(paths.LibraryFolder).TrimEnd('\\', '/');
        if (IsInside(target, library) || IsInside(RealPath(target), RealPath(library)))
            throw new LibraryException("Choose a folder outside your library for the backup.");

        var files = new List<(string Path, string Entry)>();
        try
        {
            if (Directory.Exists(library))
                foreach (var f in Directory.EnumerateFiles(library, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    files.Add((f, $"{LibraryEntryFolder}/{Path.GetFileName(f)}"));
            if (Directory.Exists(paths.ShortcutsFolder))
                foreach (var f in Directory.EnumerateFiles(paths.ShortcutsFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    files.Add((f, $"{LibraryEntryFolder}/shortcuts/{Path.GetFileName(f)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't read your library. {ex.Message}", ex);
        }

        try { Directory.CreateDirectory(target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't create the backup folder. {ex.Message}", ex);
        }
        var stem = $"Automation Portal backup {now:yyyy-MM-dd HHmmss}";
        var zipPath = Path.Combine(target, stem + ".zip");
        for (var n = 2; File.Exists(zipPath) || Directory.Exists(zipPath); n++) zipPath = Path.Combine(target, $"{stem} ({n}).zip");

        FileStream fs;
        try { fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't create the backup file. {ex.Message}", ex);
        }
        try
        {
            using (fs)
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var importNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, entryName) in files)
                {
                    var r = StoreFile.ReadBytes(path);
                    if (r.Status == StoreReadStatus.Missing) continue;
                    if (r.Status == StoreReadStatus.Unreadable)
                        throw new LibraryException($"Couldn't read '{Path.GetFileName(path)}', so the backup wasn't made. Close anything that has it open and try again. {r.Error}");
                    Add(zip, entryName, r.Bytes!);

                    if (!entryName.StartsWith(LibraryEntryFolder + "/shortcuts/", StringComparison.Ordinal)) continue;
                    Shortcut? s = null;
                    try { s = AutomationPortalJson.Deserialize<Shortcut>(new System.Text.UTF8Encoding(false).GetString(r.Bytes!).TrimStart('﻿')); }
                    catch (JsonException) { }
                    if (s is null) continue;
                    var name = SafeName(s.Name);
                    var unique = name;
                    for (var n = 2; !importNames.Add(unique); n++) unique = $"{name} ({n})";
                    Add(zip, $"{ImportEntryFolder}/{unique}{ShortcutPackage.Extension}", new System.Text.UTF8Encoding(false).GetBytes(ShortcutPackage.Serialize(s)));
                }
            }
        }
        catch (Exception ex)
        {
            // The half-written zip was created by this call (CreateNew above), so removing it touches nothing of the user's.
            try { File.Delete(zipPath); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            if (ex is LibraryException) throw;
            if (ex is IOException or UnauthorizedAccessException) throw new LibraryException($"Couldn't write the backup. {ex.Message}", ex);
            throw;
        }
        return zipPath;
    }

    private static bool IsInside(string path, string folder) =>
        (path.TrimEnd('\\', '/') + "\\").StartsWith(folder.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where a folder really is, after junctions, links and subst drives; parts that don't exist yet are kept as they are.</summary>
    private static string RealPath(string full)
    {
        var rest = new Stack<string>();
        // TrimEndingDirectorySeparator keeps "C:\" whole; "C:" alone would mean the drive's current folder.
        var existing = Path.TrimEndingDirectorySeparator(full);
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
        {
            rest.Push(Path.GetFileName(existing));
            existing = Path.TrimEndingDirectorySeparator(parent);
        }
        var real = (Directory.Exists(existing) ? PathSafety.FinalPath(existing) : null) ?? existing;
        while (rest.Count > 0) real = Path.Combine(real, rest.Pop());
        return real;
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(bytes);
    }

    private static string SafeName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "Shortcut" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n.Length > 80 ? n[..80] : n;
    }
}
