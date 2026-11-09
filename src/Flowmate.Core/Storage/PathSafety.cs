using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Flowmate.Core;

/// <summary>
/// Guards for actions that delete, move, rename or rewrite the user's files.
/// <see cref="IsProtected"/> names the places a file action must never act on as a whole: drive roots, share roots,
/// Windows, Program Files, the user profile and the user's known folders (Desktop, Documents, Downloads …).
/// Their contents are not protected, only the folders themselves.
/// </summary>
public static class PathSafety
{
    private static readonly Lazy<HashSet<string>> Protected = new(BuildProtectedSet);
    private static readonly Regex BareDrive = new(@"^[A-Za-z]:[\\/]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// True when <paramref name="path"/> is a drive root ("E:", "E:\"), a share root ("\\server\share"), the Windows or
    /// Program Files folder, the user profile or the Users folder, or one of the known folders themselves. A blank path
    /// counts as protected. Anything inside those folders is not.
    /// </summary>
    public static bool IsProtected(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var raw = path.Trim().Trim('"');
        if (BareDrive.IsMatch(raw)) return true;
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        var trimmed = Normalize(full);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || string.Equals(trimmed, Normalize(root), StringComparison.OrdinalIgnoreCase)) return true;
        if (IsShareRoot(trimmed)) return true;
        return Protected.Value.Contains(trimmed);
    }

    /// <summary>
    /// Writes a new version of a file without ever leaving it half-written: <paramref name="writeTemp"/> writes the new
    /// contents to a temporary file in the same folder, which then takes the original's place in one step
    /// (<see cref="File.Replace(string, string, string?, bool)"/>). When <paramref name="backupPath"/> is given the old
    /// version is kept there. A file that doesn't exist yet is simply created; nothing that exists is overwritten by
    /// that path. If anything fails, the original stays as it was.
    /// </summary>
    public static void WriteViaTemp(string path, Action<string> writeTemp, string? backupPath = null)
    {
        ArgumentNullException.ThrowIfNull(writeTemp);
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        // Same folder (so the swap is a rename on one volume) and same extension (some writers pick the format by it).
        var temp = Path.Combine(dir, $"~{Path.GetFileNameWithoutExtension(full)}.{Guid.NewGuid().ToString("N")[..8]}.flowmate-tmp{Path.GetExtension(full)}");
        try
        {
            writeTemp(temp);
            if (File.Exists(full)) File.Replace(temp, full, backupPath, ignoreMetadataErrors: true);
            else File.Move(temp, full);
        }
        catch
        {
            // The temp file is ours. Remove it only while the original is still in place, so no version is ever lost.
            try { if (File.Exists(temp) && File.Exists(full)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Keeps one copy of <paramref name="path"/> as "name.bak.ext" next to it, made before the first rewrite. Returns the
    /// backup's path, or null when there was nothing to back up. A file already at that name is never overwritten; it is
    /// taken to be the earlier backup.
    /// </summary>
    public static string? KeepFirstBackup(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return null;
        var backup = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".bak" + Path.GetExtension(full));
        if (File.Exists(backup) || Directory.Exists(backup)) return backup;
        try { File.Copy(full, backup, overwrite: false); }
        catch (IOException) when (File.Exists(backup)) { }
        return backup;
    }

    private static string Normalize(string full)
    {
        var t = full.TrimEnd('\\', '/');
        return t.Length == 0 ? full : t;
    }

    private static bool IsShareRoot(string trimmed)
    {
        if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var rest = trimmed[2..];
        if (rest.StartsWith(@"?\UNC\", StringComparison.OrdinalIgnoreCase)) rest = rest[6..];
        else if (rest.StartsWith(@"?\", StringComparison.Ordinal) || rest.StartsWith(@".\", StringComparison.Ordinal)) return BareDrive.IsMatch(rest[2..]);
        return rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length <= 2;
    }

    private static HashSet<string> BuildProtectedSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try { set.Add(Normalize(Path.GetFullPath(p))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        foreach (var sf in new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.CommonDocuments,
            Environment.SpecialFolder.CommonPictures, Environment.SpecialFolder.CommonMusic, Environment.SpecialFolder.CommonVideos,
        })
        {
            try { Add(Environment.GetFolderPath(sf, Environment.SpecialFolderOption.DoNotVerify)); }
            catch (PlatformNotSupportedException) { }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            Add(Path.GetDirectoryName(Normalize(profile)));   // C:\Users
            foreach (var name in new[] { "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "OneDrive", "AppData" })
                Add(Path.Combine(profile, name));
        }

        foreach (var env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial", "SystemRoot", "ProgramData", "PUBLIC" })
            Add(Environment.GetEnvironmentVariable(env));

        if (OperatingSystem.IsWindows())
            foreach (var id in KnownFolderIds) Add(KnownFolder(id));
        return set;
    }

    private static readonly Guid[] KnownFolderIds =
    {
        new("374DE290-123F-4565-9164-39C4925E467B"), // Downloads
        new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), // Desktop
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), // Documents
        new("33E28130-4E1E-4676-835A-98395C3BC3BB"), // Pictures
        new("4BD8D571-6D19-48D3-BE97-422220080E43"), // Music
        new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), // Videos
        new("A52BBA46-E9E1-435F-B3D9-28DAA648C0F6"), // OneDrive
    };

    private static string? KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0x00004000 /* KF_FLAG_DONT_VERIFY */, IntPtr.Zero, out var ptr) != 0) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
