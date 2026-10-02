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
