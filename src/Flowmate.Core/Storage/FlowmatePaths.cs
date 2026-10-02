namespace Flowmate.Core;

/// <summary>All on-disk locations (SPEC §3.9). Construct with roots to relocate everything (tests, portable mode).</summary>
public sealed class FlowmatePaths
{
    /// <summary>%APPDATA%\Flowmate (settings, secrets, reminders, saved values).</summary>
    public string RoamingRoot { get; }
    /// <summary>%LOCALAPPDATA%\Flowmate (history, logs, temp, icons).</summary>
    public string LocalRoot { get; }
    /// <summary>The library folder (default %APPDATA%\Flowmate\Library; may live in OneDrive).</summary>
    public string LibraryFolder { get; }

    public FlowmatePaths(string? roamingRoot = null, string? localRoot = null, string? libraryFolder = null)
    {
        RoamingRoot = roamingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flowmate");
        LocalRoot = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flowmate");
        LibraryFolder = string.IsNullOrWhiteSpace(libraryFolder) ? Path.Combine(RoamingRoot, "Library") : libraryFolder;
    }

    private static FlowmatePaths? _default;
    public static FlowmatePaths Default => _default ??= new FlowmatePaths();

    /// <summary>Everything under one folder (<c>root\roaming</c>, <c>root\local</c>). Use in tests.</summary>
    public static FlowmatePaths ForRoot(string root) =>
        new(Path.Combine(root, "roaming"), Path.Combine(root, "local"));

    /// <summary>Same paths but with the library elsewhere (Settings › Library).</summary>
    public FlowmatePaths WithLibrary(string? libraryFolder) => new(RoamingRoot, LocalRoot, libraryFolder);

    public string ShortcutsFolder => Path.Combine(LibraryFolder, "shortcuts");
    public string FoldersFile => Path.Combine(LibraryFolder, "folders.json");
    public string AutomationsFile => Path.Combine(LibraryFolder, "automations.json");
    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
    public string SecretsFile => Path.Combine(RoamingRoot, "secrets.dat");
    public string RemindersFile => Path.Combine(RoamingRoot, "reminders.json");
    public string SavedValuesFile => Path.Combine(RoamingRoot, "saved-values.json");
    public string HistoryFolder => Path.Combine(LocalRoot, "history");
    public string LogsFolder => Path.Combine(LocalRoot, "logs");
    public string TempRoot => Path.Combine(LocalRoot, "temp");
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
