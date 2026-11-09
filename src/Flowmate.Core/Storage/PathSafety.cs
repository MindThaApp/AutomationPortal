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
    /// Program Files folder, the user profile or the Users folder, one of the known folders themselves, or a folder that
    /// holds one of those (for example the OneDrive folder when Documents was moved into it). A blank path counts as
    /// protected. Anything else inside those folders is not.
    /// Device forms (\\?\C:\..., \\.\C:\..., \\?\UNC\...) and this PC's admin shares (\\localhost\C$\...) are read as the
    /// local path they stand for, and an existing path is also checked after following junctions and symbolic links.
    /// </summary>
    public static bool IsProtected(string? path) => IsProtected(path, Protected.Value);

    /// <summary>Part of the name of the temporary file <see cref="WriteViaTemp"/> writes before the swap.</summary>
    public const string TempMarker = ".flowmate-tmp";

    /// <summary>
    /// True for the short-lived files and folders Flow Mate makes while it writes (a file being swapped in, a folder copy
    /// being built, an item moved aside during a replace). Folder triggers ignore them.
    /// </summary>
    public static bool IsWorkInProgressName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Contains(TempMarker, StringComparison.OrdinalIgnoreCase)
            || name.Contains(".flowmate-copying-", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".flowmate-old-", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsProtected(string? path, IReadOnlyCollection<string> protectedFolders)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var raw = path.Trim().Trim('"');
        if (BareDrive.IsMatch(raw)) return true;
        // A device path Flow Mate can't map to a drive or a share (\\?\Volume{...}, \\.\GLOBALROOT, pipes) is never acted on.
        if (ToWin32Form(raw) is not { } local) return true;
        if (BareDrive.IsMatch(local)) return true;
        string full;
        try { full = Path.GetFullPath(local); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        if (IsProtectedForm(full, protectedFolders)) return true;
        // "C:\Documents and Settings\me\Desktop" or a link to Documents: what the path really opens.
        return FinalPath(full) is { } final && !string.Equals(final, full, StringComparison.OrdinalIgnoreCase) && IsProtectedForm(final, protectedFolders);
    }

    private static bool IsProtectedForm(string full, IReadOnlyCollection<string> protectedFolders)
    {
        var trimmed = Normalize(full);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || string.Equals(trimmed, Normalize(root), StringComparison.OrdinalIgnoreCase)) return true;
        if (IsShareRoot(trimmed)) return true;
        var prefix = trimmed + @"\";
        foreach (var p in protectedFolders)
            if (string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase) || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// The ordinary Windows form of a path: \\?\C:\x and \\.\C:\x become C:\x, \\?\UNC\server\share becomes \\server\share,
    /// and an admin share of this PC (\\localhost\C$\x, \\127.0.0.1\D$, \\THIS-PC\ADMIN$) becomes the local folder.
    /// Null for a device path that stands for no drive letter or share. Text only; nothing is opened.
    /// </summary>
    internal static string? ToWin32Form(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal) || p.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            var rest = p[4..];
            if (rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + rest[4..];
            else if (rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':') p = rest;
            else return null;
        }
        if (!p.StartsWith(@"\\", StringComparison.Ordinal)) return p;

        var parts = p[2..].Split('\\', 3);
        if (parts.Length < 2 || !IsThisComputer(parts[0])) return p;
        var share = parts[1];
        string? local = null;
        if (share.Length == 2 && char.IsAsciiLetter(share[0]) && share[1] == '$') local = char.ToUpperInvariant(share[0]) + @":\";
        else if (share.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase))
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrEmpty(windows)) local = windows;
        }
        if (local is null) return p;
        return parts.Length == 3 && parts[2].Length > 0 ? Path.Combine(local, parts[2].TrimStart('\\')) : local;
    }

    private static bool IsThisComputer(string host)
    {
        var h = host.Trim('[', ']').TrimEnd('.');
        if (h.Length == 0 || h == "." || h.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || h.StartsWith(Environment.MachineName + ".", StringComparison.OrdinalIgnoreCase)) return true;
        // IPv6 in UNC form: 0--1.ipv6-literal.net is ::1.
        if (h.EndsWith(".ipv6-literal.net", StringComparison.OrdinalIgnoreCase)) h = h[..^".ipv6-literal.net".Length].Replace('-', ':').Replace('s', '%');
        return System.Net.IPAddress.TryParse(h, out var ip) && System.Net.IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Where an existing file or folder really is, after junctions and symbolic links (GetFinalPathNameByHandle), in
    /// ordinary form; null when it doesn't exist or can't be asked. Opens it for no access at all, so nothing is changed.
    /// </summary>
    internal static string? FinalPath(string full)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = CreateFileW(full, 0, 7 /* share read, write and delete */, IntPtr.Zero, 3 /* OPEN_EXISTING */,
                0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS, so folders open too */, IntPtr.Zero);
            if (handle.IsInvalid) return null;
            var buffer = new char[1024];
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length >= buffer.Length)
            {
                buffer = new char[length + 1];
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            }
            if (length == 0 || length >= buffer.Length) return null;
            return ToWin32Form(new string(buffer, 0, (int)length)) is { } form ? Path.GetFullPath(form) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException
                                   or DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle, [Out] char[] buffer, uint length, uint flags);

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
        // A symbolic link is written through, as a plain write would: the swap happens at the file it points to, so the link keeps working.
        try
        {
            if (new FileInfo(full) is { Exists: true, LinkTarget: not null } link && link.ResolveLinkTarget(returnFinalTarget: true) is FileInfo { Exists: true } target)
                full = target.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var dir = Path.GetDirectoryName(full)!;
        // Same folder (so the swap is a rename on one volume) and same extension (some writers pick the format by it).
        var temp = Path.Combine(dir, $"~{Path.GetFileNameWithoutExtension(full)}.{Guid.NewGuid().ToString("N")[..8]}{TempMarker}{Path.GetExtension(full)}");
        var written = false;
        try
        {
            writeTemp(temp);
            written = true;
            if (File.Exists(full)) File.Replace(temp, full, backupPath, ignoreMetadataErrors: true);
            else File.Move(temp, full);
        }
        catch
        {
            // The temp file is ours. Remove it while the original is still in place, or when it never got finished, so no version is ever lost.
            try { if (File.Exists(temp) && (File.Exists(full) || !written)) File.Delete(temp); }
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
