using System.Runtime.InteropServices;
using System.Text;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>What a shortcut takes when items are dropped on its tile, from its input settings.</summary>
public readonly record struct DropKinds(bool Files, bool Images, bool Folders)
{
    public bool Any => Files || Images || Folders;
}

public sealed record DroppedItem(string Path, bool IsFolder);

/// <summary>A drop sorted for one shortcut: what it takes, what it doesn't, and the shortcut files that are added to the library instead.</summary>
public sealed class DroppedSelection
{
    public required IReadOnlyList<DroppedItem> Accepted { get; init; }
    public required IReadOnlyList<DroppedItem> LeftOut { get; init; }
    public required IReadOnlyList<string> ShortcutFiles { get; init; }
    public required bool HasNetworkPath { get; init; }
    /// <summary>The run's input: the same kind of value a file picker gives when the shortcut asks for files.</summary>
    public required ContentValue Input { get; init; }
}

/// <summary>
/// Files and folders dropped on a library tile. The ones the shortcut takes become its input (Shortcut Input), as if they had been
/// chosen when it asks for files. Only paths are read here; nothing is opened, changed, moved or deleted.
/// </summary>
public static class DroppedFiles
{
    public const int ListedItems = 8;

    /// <summary>
    /// Files when it takes files or anything, or asks for files; only pictures when pictures are all it takes; folders only when it
    /// takes folders or anything.
    /// </summary>
    public static DropKinds Kinds(Shortcut shortcut)
    {
        var k = shortcut.AcceptsInput;
        var any = k.Contains(ContentKind.Any);
        var files = any || k.Contains(ContentKind.File) || shortcut.WhenNoInput == WhenNoInput.AskForFiles;
        return new DropKinds(files, files || k.Contains(ContentKind.Image), any || k.Contains(ContentKind.Folder));
    }

    public static bool TakesFiles(Shortcut shortcut) => Kinds(shortcut).Any;

    /// <summary>The full paths of the dropped files and folders; empty when the drop carries none (text, a tile, a file inside a zip).</summary>
    public static IReadOnlyList<string> Paths(System.Windows.IDataObject data)
    {
        try
        {
            return Paths(data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? data.GetData(System.Windows.DataFormats.FileDrop) as string[] : null);
        }
        catch (COMException) { return Array.Empty<string>(); }   // some drag sources fail when asked for their data
    }

    public static IReadOnlyList<string> Paths(IEnumerable<string>? raw) =>
        raw is null ? Array.Empty<string>()
        : raw.Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p) && !IsDevicePath(p))
             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>\\.\ and \\?\ paths name devices or skip Windows' path checks; a drop never hands those on.</summary>
    public static bool IsDevicePath(string path) =>
        path.Length >= 4 && path[0] is '\\' or '/' && path[1] is '\\' or '/' && path[2] is '.' or '?' && path[3] is '\\' or '/';

    public static bool IsShortcutFile(string path) => ShortcutPackage.HasPackageExtension(path);

    /// <summary>False when the drop is only shortcut files: the library adds those through the usual review.</summary>
    public static bool IsForTile(IReadOnlyList<string> paths) => paths.Any(p => !IsShortcutFile(p));

    /// <summary>Whether the tile shows it takes this drop, without touching the disk (folders are sorted out after the drop).</summary>
    public static bool CanRunWith(Shortcut shortcut, IReadOnlyList<string> paths) => IsForTile(paths) && TakesFiles(shortcut);

    public static bool IsUncPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>A share or a mapped network drive. Reads the drive type, so call it off the UI thread.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (IsUncPath(path)) return true;
        try { return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>Sorts a drop for a shortcut. <paramref name="isFolder"/> and <paramref name="isNetwork"/> touch the disk: call it off the UI thread.</summary>
    public static DroppedSelection Select(Shortcut shortcut, IReadOnlyList<string> paths, Func<string, bool> isFolder, Func<string, bool> isNetwork)
    {
        var kinds = Kinds(shortcut);
        var accepted = new List<DroppedItem>();
        var leftOut = new List<DroppedItem>();
        var shortcutFiles = new List<string>();
        foreach (var p in paths)
        {
            if (IsShortcutFile(p)) { shortcutFiles.Add(p); continue; }
            var item = new DroppedItem(p, isFolder(p));
            var takes = item.IsFolder ? kinds.Folders : kinds.Files || (kinds.Images && FileKinds.IsImage(p));
            (takes ? accepted : leftOut).Add(item);
        }
        return new DroppedSelection
        {
            Accepted = accepted, LeftOut = leftOut, ShortcutFiles = shortcutFiles,
            HasNetworkPath = accepted.Any(i => isNetwork(i.Path)),
            Input = ContentValue.Concat(accepted.Select(i =>
                i.IsFolder ? ContentValue.Folder(i.Path) : FileKinds.IsImage(i.Path) ? ContentValue.Image(i.Path) : ContentValue.File(i.Path)).ToArray()),
        };
    }

    /// <summary>The confirm sheet's list: the first few dropped items (folders marked), then what was left out and why.</summary>
    public static string Describe(Shortcut shortcut, DroppedSelection drop)
    {
        var sb = new StringBuilder(Strings.Get("DropRun_Dropped")).Append('\n');
        AppendItems(sb, drop.Accepted);
        if (drop.LeftOut.Count > 0)
        {
            sb.Append('\n').Append(Strings.Format("DropRun_LeftOut", shortcut.Name)).Append('\n');
            AppendItems(sb, drop.LeftOut);
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendItems(StringBuilder sb, IReadOnlyList<DroppedItem> items)
    {
        foreach (var i in items.Take(ListedItems))
            sb.Append(i.IsFolder ? Strings.Format("DropRun_Folder", i.Path) : i.Path).Append('\n');
        if (items.Count > ListedItems) sb.Append(Strings.Format("DropRun_AndMore", items.Count - ListedItems)).Append('\n');
    }
}
