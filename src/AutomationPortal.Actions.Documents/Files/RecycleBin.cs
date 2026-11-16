using System.Runtime.InteropServices;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Sends items to the Recycle Bin. When the drive has no bin (network share, USB stick, too big) the shell asks before deleting
/// for good; runs with nobody watching never delete for good and leave such items alone instead.
/// </summary>
internal static class RecycleBin
{
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;
    private const uint FO_DELETE = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    /// <summary>True when the item was deleted (recycled, or deleted after the user agreed). False when the user declined or the shell failed.</summary>
    public static bool TrySend(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = ShellPath(path) + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING),
        };
        var rc = SHFileOperationW(ref op);
        return rc == 0 && op.fAnyOperationsAborted == 0 && !File.Exists(path) && !Directory.Exists(path);
    }

    /// <summary>The full path without a trailing separator, except for a root: "E:\" stays "E:\", never the drive-relative "E:".</summary>
    internal static string ShellPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return full;
        var trimmed = full.TrimEnd('\\', '/');
        return trimmed.Length <= root.TrimEnd('\\', '/').Length ? full : trimmed;
    }

    /// <summary>Whether the drive of <paramref name="path"/> has a Recycle Bin (tests can stand in for it on this async flow).</summary>
    internal static bool DriveHasBin(string path) => (TestDriveHasBin.Value ?? FileUndo.DriveHasRecycleBin)(path);

    internal static readonly AsyncLocal<Func<string, bool>?> TestDriveHasBin = new();

    /// <summary>
    /// For runs with nobody watching: never deletes for good and never shows any window. When the item can't certainly go to
    /// the Recycle Bin (no bin, the bin is set to delete right away, the item is too big for it, or its settings can't be read)
    /// nothing is touched and false is returned.
    /// </summary>
    public static bool TrySendUnattended(string path)
    {
        try { return UnattendedRefusal(path) is null && SendGuarded(path) == ShellRecycle.Outcome.Recycled; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <param name="unattended">
    /// The run has no window (a schedule or a trigger): the item goes to the Recycle Bin or is left as it is, never deleted for
    /// good and never with a prompt nobody would see.
    /// </param>
    public static void Send(string path, bool unattended = false)
    {
        ProtectedPaths.Ensure(path, "delete");
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Not found.", path);
        if (unattended)
        {
            if (UnattendedRefusal(path) is { } refusal) throw new ActionException(refusal);
            switch (SendGuarded(path))
            {
                case ShellRecycle.Outcome.Recycled: return;
                case ShellRecycle.Outcome.WouldBePermanent:
                    throw new ActionException(Refused(path, "it can't go to the Recycle Bin, so it would be deleted for good"));
                case ShellRecycle.Outcome.GoneUnconfirmed:
                    throw new ActionException($"'{Name(path)}' is gone, but Windows didn't confirm that it went to the Recycle Bin. Look for it there; if it isn't there, it may have been deleted for good.");
                default:
                    throw new ActionException($"'{Name(path)}' wasn't deleted. It may be open in another program, or it couldn't go to the Recycle Bin. Nothing was deleted for good.");
            }
        }
        if (!TrySend(path))
            throw new ActionException($"'{Path.GetFileName(path)}' wasn't deleted. It may be open in another program, or it can't go to the Recycle Bin on this drive and you chose to keep it.");
    }

    private static string Name(string path) => Path.GetFileName(path.TrimEnd('\\', '/'));

    private static string Refused(string path, string why, string never = "never do that") =>
        $"'{Name(path)}' wasn't deleted: {why}. Shortcuts that run on their own {never}. Run the shortcut yourself to choose.";

    /// <summary>Sends the item to the bin with no window, refusing any delete the shell says wouldn't go to the bin. Tests stand in for it.</summary>
    private static ShellRecycle.Outcome SendGuarded(string path) => (TestSendGuarded.Value ?? ShellRecycle.Send)(ShellPath(path));

    internal static readonly AsyncLocal<Func<string, ShellRecycle.Outcome>?> TestSendGuarded = new();

    // ---- Will it certainly go to the Recycle Bin? ----

    internal enum BinCheck { Fits, NoBin, DeletesRightAway, TooBig, Unknown }

    /// <summary>The bin of one drive: whether it deletes right away, and how big it may grow. Null when it can't be told.</summary>
    internal readonly record struct BinSettings(bool? DeletesRightAway, long? CapacityBytes);

    /// <summary>Why a run with nobody watching must leave the item alone, or null when it certainly fits in the Recycle Bin.</summary>
    internal static string? UnattendedRefusal(string path)
    {
        var hasBin = DriveHasBin(path);
        var settings = hasBin ? (TestBinSettings.Value ?? ReadBinSettings)(path) : default;
        var size = hasBin && settings is { DeletesRightAway: false, CapacityBytes: { } cap } ? ItemSize(path, cap) : null;
        return Check(hasBin, settings, size) switch
        {
            BinCheck.Fits => null,
            BinCheck.NoBin => Refused(path, "its drive has no Recycle Bin, so it would be deleted for good"),
            BinCheck.DeletesRightAway => Refused(path, "the Recycle Bin on its drive is set to remove files right away, so it would be deleted for good"),
            BinCheck.TooBig => Refused(path, "it is too big for the Recycle Bin, so it would be deleted for good"),
            _ => Refused(path, "Automation Portal couldn't check that it fits in the Recycle Bin, so it might be deleted for good", "never risk that"),
        };
    }

    /// <summary>Only a definite "fits" lets the delete go ahead; anything unknown is refused.</summary>
    internal static BinCheck Check(bool driveHasBin, BinSettings settings, long? itemBytes)
    {
        if (!driveHasBin) return BinCheck.NoBin;
        if (settings.DeletesRightAway is not { } rightAway) return BinCheck.Unknown;
        if (rightAway) return BinCheck.DeletesRightAway;
        if (settings.CapacityBytes is not { } capacity || itemBytes is not { } size || capacity <= 0 || size < 0) return BinCheck.Unknown;
        return size <= capacity ? BinCheck.Fits : BinCheck.TooBig;
    }

    internal static readonly AsyncLocal<Func<string, BinSettings>?> TestBinSettings = new();

    private const long Cluster = 4096;
    private const long MegaByte = 1024 * 1024;

    /// <summary>
    /// The space the item takes, each file rounded up to a whole cluster; a folder counts everything inside it, but links and
    /// junctions only as themselves (they go to the bin as they are). Stops early once past <paramref name="stopAbove"/>.
    /// Null when part of it can't be read.
    /// </summary>
    internal static long? ItemSize(string path, long stopAbove)
    {
        try
        {
            var attributes = File.GetAttributes(path);   // throws when it is gone
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) return Cluster;
            if (!attributes.HasFlag(FileAttributes.Directory)) return RoundUp(new FileInfo(path).Length);
            var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
            long total = Cluster;
            var folders = new Stack<DirectoryInfo>();
            folders.Push(new DirectoryInfo(path));
            while (folders.Count > 0)
            {
                foreach (var entry in folders.Pop().EnumerateFileSystemInfos("*", options))
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) total += Cluster;
                    else if (entry is DirectoryInfo dir) { total += Cluster; folders.Push(dir); }
                    else total += RoundUp(((FileInfo)entry).Length);
                    if (total > stopAbove) return total;
                }
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return null; }
    }

    private static long RoundUp(long bytes) => Math.Max(Cluster, (bytes + Cluster - 1) / Cluster * Cluster);

    private const string PoliciesKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string VolumesKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";

    /// <summary>
    /// The bin settings Windows keeps per volume (Recycle Bin properties: "Custom size" and "Don't move files to the Recycle
    /// Bin"), with the group policies that override them. A volume without saved settings counts as unknown.
    /// </summary>
    internal static BinSettings ReadBinSettings(string path)
    {
        try
        {
            if (PolicyNumber("NoRecycleFiles") is 1) return new BinSettings(true, null);
            var mount = new char[1024];
            if (!GetVolumePathNameW(Path.GetFullPath(path), mount, (uint)mount.Length)) return default;
            var mountPoint = new string(mount).TrimEnd('\0');
            var volume = new char[100];
            if (!GetVolumeNameForVolumeMountPointW(mountPoint, volume, (uint)volume.Length)) return default;
            var name = new string(volume).TrimEnd('\0');   // \\?\Volume{guid}\
            var open = name.IndexOf('{');
            var close = name.IndexOf('}');
            if (open < 0 || close < open) return default;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VolumesKey + name[open..(close + 1)]);
            if (key?.GetValue("NukeOnDelete") is not int nuke) return default;
            if (nuke != 0) return new BinSettings(true, null);
            long? capacity = key.GetValue("MaxCapacity") is int mb ? (uint)mb * MegaByte : null;
            if (PolicyNumber("RecycleBinSize") is >= 0 and <= 100 and var percent
                && GetDiskFreeSpaceExW(mountPoint, out _, out var total, out _))
                capacity = Math.Min(capacity ?? long.MaxValue, (long)(total / 100 * (ulong)percent));
            return new BinSettings(false, capacity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return default; }
    }

    /// <summary>A Recycle Bin group policy number; the machine policy wins over the user's.</summary>
    private static int? PolicyNumber(string name)
    {
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(PoliciesKey);
            if (key?.GetValue(name) is int value) return value;
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string lpszFileName, [Out] char[] lpszVolumePathName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string lpszVolumeMountPoint, [Out] char[] lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
}
