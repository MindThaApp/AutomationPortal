using System.Runtime.InteropServices;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>
/// Files and folders dropped on a library tile. They become the shortcut's input (Shortcut Input), exactly as if they had been
/// chosen when the shortcut asks for files. Only the paths are read here; nothing is opened, changed, moved or deleted.
/// </summary>
public static class DroppedFiles
{
    /// <summary>A shortcut takes files when its input settings name files, folders, pictures or anything, or when it asks for files.</summary>
    public static bool TakesFiles(Shortcut shortcut) =>
        shortcut.WhenNoInput == WhenNoInput.AskForFiles
        || shortcut.AcceptsInput.Any(k => k is ContentKind.File or ContentKind.Folder or ContentKind.Image or ContentKind.Any);

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
        : raw.Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Only shortcut files (.autoportal): the library adds them through the usual review instead of a tile running with them.</summary>
    public static bool AreShortcutFiles(IReadOnlyList<string> paths) => paths.Count > 0 && paths.All(ShortcutPackage.HasPackageExtension);

    /// <summary>True when a tile handles this drop itself, to run with it or to refuse it; false leaves it to the library.</summary>
    public static bool IsForTile(IReadOnlyList<string> paths) => paths.Count > 0 && !AreShortcutFiles(paths);

    public static bool CanRunWith(Shortcut shortcut, IReadOnlyList<string> paths) => IsForTile(paths) && TakesFiles(shortcut);

    /// <summary>The run's input: the same value a file picker gives when the shortcut asks for files.</summary>
    public static ContentValue Input(IReadOnlyList<string> paths) => ContentValue.Files(paths);
}
