using System.Runtime.InteropServices;

namespace AutomationPortal.Actions.Documents;

/// <summary>Sends items to the Recycle Bin. When the drive has no bin (network share, USB stick, too big) the shell asks before deleting for good.</summary>
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
    /// For runs with nobody watching: never deletes for good and never shows the shell's "delete permanently?" prompt. On a drive
    /// without a Recycle Bin nothing is touched and false is returned.
    /// </summary>
    public static bool TrySendUnattended(string path) => DriveHasBin(path) && TrySend(path);

    /// <param name="unattended">The run has no window (a schedule or a trigger): refuse instead of asking to delete for good.</param>
    public static void Send(string path, bool unattended = false)
    {
        ProtectedPaths.Ensure(path, "delete");
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Not found.", path);
        if (unattended && !DriveHasBin(path))
            throw new ActionException($"'{Path.GetFileName(path.TrimEnd('\\', '/'))}' wasn't deleted: its drive has no Recycle Bin, so it would be deleted for good. " +
                                      "Shortcuts that run on their own never do that. Run the shortcut yourself to choose.");
        if (!TrySend(path))
            throw new ActionException($"'{Path.GetFileName(path)}' wasn't deleted. It may be open in another program, or it can't go to the Recycle Bin on this drive and you chose to keep it.");
    }
}
