using System.Runtime.InteropServices;

namespace Flowmate.Actions.Documents;

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

    public static void Send(string path)
    {
        ProtectedPaths.Ensure(path, "delete");
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Not found.", path);
        if (!TrySend(path))
            throw new ActionException($"'{Path.GetFileName(path)}' wasn't deleted. It may be open in another program, or it can't go to the Recycle Bin on this drive and you chose to keep it.");
    }
}
