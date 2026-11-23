namespace AutomationPortal.Core;

/// <summary>All on-disk locations (SPEC §3.9). Construct with roots to relocate everything (tests, portable mode).</summary>
public sealed class AutomationPortalPaths
{
    /// <summary>%APPDATA%\Automation Portal (settings, secrets, reminders, saved values).</summary>
    public string RoamingRoot { get; }
    /// <summary>%LOCALAPPDATA%\Automation Portal (history, logs, temp, icons).</summary>
    public string LocalRoot { get; }
    /// <summary>The library folder (default %APPDATA%\Automation Portal\Library; may live in OneDrive).</summary>
    public string LibraryFolder { get; }

    /// <summary>With no roots given the real folders are used, and data from the folders used before the rename is copied over once.</summary>
    public AutomationPortalPaths(string? roamingRoot = null, string? localRoot = null, string? libraryFolder = null)
        : this(roamingRoot, localRoot, libraryFolder,
            roamingRoot is null && !UsesPackageFolders ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataRootMigration.LegacyFolderName) : null,
            localRoot is null && !UsesPackageFolders ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataRootMigration.LegacyFolderName) : null)
    {
    }

    /// <param name="legacyRoamingRoot">Where the data used to live; when given and <paramref name="roamingRoot"/> is missing, it is copied (tests inject temp folders here).</param>
    /// <param name="legacyLocalRoot">Same for the local root.</param>
    internal AutomationPortalPaths(string? roamingRoot, string? localRoot, string? libraryFolder, string? legacyRoamingRoot, string? legacyLocalRoot)
    {
        var lines = new List<string>();
        var newRoaming = roamingRoot ?? DefaultRoamingRoot();
        var newLocal = localRoot ?? DefaultLocalRoot();
        RoamingRoot = legacyRoamingRoot is null ? newRoaming : DataRootMigration.Resolve(legacyRoamingRoot, newRoaming, lines.Add);
        LocalRoot = legacyLocalRoot is null ? newLocal : DataRootMigration.Resolve(legacyLocalRoot, newLocal, lines.Add);
        LibraryFolder = string.IsNullOrWhiteSpace(libraryFolder) ? Path.Combine(RoamingRoot, "Library") : libraryFolder;
        foreach (var line in lines) StoreDiagnostics.Report(line);
        // One line in the new log. Never written into an old folder, which stays untouched.
        if (lines.Count > 0 && string.Equals(LocalRoot, newLocal, StringComparison.Ordinal))
        {
            try
            {
                Directory.CreateDirectory(LogsFolder);
                File.AppendAllText(Path.Combine(LogsFolder, "migration.log"), DateTime.Now.ToString("s") + " " + string.Join(" ", lines) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The Microsoft Store package has no unvirtualized-resources capability, so AppData writes would be private to the package and
    /// invisible to Explorer. There the data lives in the package's own folders (<c>%LOCALAPPDATA%\Packages\&lt;family&gt;\RoamingState</c> and
    /// <c>LocalState</c>), which are real folders. Unpackaged and the full package keep <c>%APPDATA%\Automation Portal</c>.
    /// </summary>
    private static bool UsesPackageFolders => PackageIdentity.IsPackaged && !PackageCapabilities.UnvirtualizedWritesAllowed && PackageIdentity.FamilyName is not null;

    private static string DefaultRoamingRoot() => ChooseRoot(roaming: true, PackageIdentity.IsPackaged, PackageCapabilities.UnvirtualizedWritesAllowed, PackageIdentity.FamilyName,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static string DefaultLocalRoot() => ChooseRoot(roaming: false, PackageIdentity.IsPackaged, PackageCapabilities.UnvirtualizedWritesAllowed, PackageIdentity.FamilyName,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Picks the default data folder. Pure, so tests can cover every case.</summary>
    internal static string ChooseRoot(bool roaming, bool packaged, bool writesAllowed, string? familyName, string appData, string localAppData)
    {
        if (packaged && !writesAllowed && !string.IsNullOrEmpty(familyName))
            return Path.Combine(localAppData, "Packages", familyName, roaming ? "RoamingState" : "LocalState");
        return Path.Combine(roaming ? appData : localAppData, "Automation Portal");
    }

    private static AutomationPortalPaths? _default;
    public static AutomationPortalPaths Default => _default ??= new AutomationPortalPaths();

    /// <summary>Everything under one folder (<c>root\roaming</c>, <c>root\local</c>). Use in tests.</summary>
    public static AutomationPortalPaths ForRoot(string root) =>
        new(Path.Combine(root, "roaming"), Path.Combine(root, "local"));

    /// <summary>Same paths but with the library elsewhere (Settings › Library).</summary>
    public AutomationPortalPaths WithLibrary(string? libraryFolder) => new(RoamingRoot, LocalRoot, libraryFolder);

    public string ShortcutsFolder => Path.Combine(LibraryFolder, "shortcuts");
    public string FoldersFile => Path.Combine(LibraryFolder, "folders.json");
    public string AutomationsFile => Path.Combine(LibraryFolder, "automations.json");
    /// <summary>Recently deleted shortcuts. Per PC, outside the library so it never syncs or reloads the library.</summary>
    public string DeletedFolder => Path.Combine(RoamingRoot, "Deleted");
    /// <summary>Earlier versions of each shortcut (<see cref="ShortcutVersions"/>). Per PC and never roams, outside the library.</summary>
    public string VersionsFolder => Path.Combine(LocalRoot, "Versions");
    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
    public string SecretsFile => Path.Combine(RoamingRoot, "secrets.dat");
    public string RemindersFile => Path.Combine(RoamingRoot, "reminders.json");
    public string SavedValuesFile => Path.Combine(RoamingRoot, "saved-values.json");
    public string HistoryFolder => Path.Combine(LocalRoot, "history");
    public string LogsFolder => Path.Combine(LocalRoot, "logs");
    public string TempRoot => Path.Combine(LocalRoot, "temp");
    /// <summary>Per-run records of file changes, for "Undo file changes". Only <see cref="FileJournal"/> writes and trims it.</summary>
    public string JournalFolder => Path.Combine(LocalRoot, "file-changes");
    public string IconsFolder => Path.Combine(LocalRoot, "icons");

    public string ShortcutFile(Guid id) => Path.Combine(ShortcutsFolder, id.ToString("D") + ".json");
    public string RunTempFolder(string runId) => Path.Combine(TempRoot, runId);
    public string HistoryFile(DateTimeOffset when) => Path.Combine(HistoryFolder, $"runs-{when:yyyy-MM}.jsonl");
}

/// <summary>Atomic file writes: temp file in the same folder, then replace.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        File.WriteAllText(tmp, contents, new System.Text.UTF8Encoding(false));
        try { File.Move(tmp, path, overwrite: true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        File.WriteAllBytes(tmp, contents);
        try { File.Move(tmp, path, overwrite: true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }
}

/// <summary>Raised when a store hits a problem it can't show itself (background reload failed, file unreadable …). The host logs it and may tell the user.</summary>
public static class StoreDiagnostics
{
    public static event Action<string, Exception?>? Error;
    public static void Report(string message, Exception? ex = null)
    {
        try { Error?.Invoke(message, ex); } catch { }
    }
}

public enum StoreReadStatus { Missing, Ok, Unreadable }
public readonly record struct StoreRead(StoreReadStatus Status, string? Text, byte[]? Bytes, string? Error);

/// <summary>
/// Reads that tell "file isn't there" from "file is there but can't be read right now" (locked by OneDrive / antivirus, no access).
/// Callers must never treat Unreadable as empty and write over it.
/// </summary>
public static class StoreFile
{
    private const int Attempts = 5;

    public static StoreRead ReadBytes(string path)
    {
        string? error = null;
        for (var i = 0; i < Attempts; i++)
        {
            try { return new(StoreReadStatus.Ok, null, File.ReadAllBytes(path), null); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(StoreReadStatus.Missing, null, null, null); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; if (i < Attempts - 1) Thread.Sleep(40 << i); }
        }
        return new(StoreReadStatus.Unreadable, null, null, error);
    }

    public static StoreRead ReadText(string path)
    {
        var r = ReadBytes(path);
        return r.Status == StoreReadStatus.Ok ? r with { Text = new System.Text.UTF8Encoding(false).GetString(StripBom(r.Bytes!)) } : r;
    }

    private static byte[] StripBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? b[3..] : b;

    /// <summary>Keeps the last good version as <c>file.bak</c>, then writes atomically.</summary>
    public static void WriteText(string path, string text, bool backup = true)
    {
        if (backup) Backup(path);
        AtomicFile.WriteAllText(path, text);
    }

    public static void WriteBytes(string path, byte[] bytes)
    {
        Backup(path);
        AtomicFile.WriteAllBytes(path, bytes);
    }

    public static void Backup(string path)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Copies an unparseable file aside (<c>file.corrupt-yyyyMMdd-HHmmss</c>) so a later save can't destroy the only copy.</summary>
    public static void Quarantine(string path)
    {
        try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
