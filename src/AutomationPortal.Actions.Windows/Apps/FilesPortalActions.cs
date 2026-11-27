using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>Builds and starts <c>filesportal:reveal?path=...</c> links. Launch only: the target is never read, changed or deleted.</summary>
public static class FilesPortalLink
{
    public const string Scheme = "filesportal";

    /// <summary>
    /// <c>filesportal:reveal?path=&lt;escaped absolute path&gt;</c>. The path must be absolute, local (C:\...) or UNC (\\server\share\...),
    /// with no control characters. Text only; nothing is touched.
    /// </summary>
    public static string BuildUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("Files Portal needs the full path of a file or folder.", nameof(path));
        return $"{Scheme}:reveal?path={Uri.EscapeDataString(path)}";
    }

    /// <summary>Explorer's own "show it selected" start info, used when Files Portal isn't installed. A folder is simply opened.</summary>
    public static ProcessStartInfo ExplorerFallback(string path, bool isFolder)
    {
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        if (isFolder) psi.ArgumentList.Add(path); else psi.ArgumentList.Add("/select," + path);
        return psi;
    }

    /// <summary>True when something handles filesportal: links on this PC (reads HKEY_CLASSES_ROOT only).</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(Scheme);
            return key?.GetValue("URL Protocol") is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>Opens the path in Files Portal, or in Explorer when Files Portal isn't registered. Returns true when Files Portal was used.</summary>
    public static bool Reveal(string path, bool isFolder, Func<bool>? isRegistered = null, Action<ProcessStartInfo>? start = null)
    {
        start ??= psi => Process.Start(psi)?.Dispose();
        if ((isRegistered ?? IsRegistered)())
        {
            start(new ProcessStartInfo(BuildUri(path)) { UseShellExecute = true });
            return true;
        }
        start(ExplorerFallback(path, isFolder));
        return false;
    }
}

public sealed class ShowInFilesPortalAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.revealInFilesPortal", Title = Strings.Get("ShowInFilesPortalAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "FolderOpen24",
        Keywords = new[] { "files portal", "reveal", "locate", "folder", "find file", "phone", "portal" },
        Summary = Strings.Get("ShowInFilesPortalAction_Summary"),
        Description = Strings.Get("ShowInFilesPortalAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder, ContentKind.Image }, OutputKind = ContentKind.File, OutputName = Strings.Get("ShowInFilesPortalAction_OutputName"),
        Parameters = new[] { ParameterDescriptor.FileParam("file", Strings.Get("ShowInFilesPortalAction_Param_File_Label"), isInput: true) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = (await ctx.GetFilesAsync("file", includeFolders: true)).ToList();
        if (items.Count == 0) throw new ActionException(Strings.Get("ShowInFilesPortalAction_Error_There_S_Nothing_To_Show_Choose"));
        var item = items[0];
        if (!item.Exists) throw new ActionException(Strings.Format("ShowInFilesPortalAction_Error_Couldn_T_Find_0_Check_It", item.FullName));
        var isFolder = Directory.Exists(item.Path);
        try { await Task.Run(() => FilesPortalLink.Reveal(item.Path, isFolder), ctx.CancellationToken).ConfigureAwait(false); }
        catch (ArgumentException) { throw new ActionException(Strings.Format("ShowInFilesPortalAction_Error_Files_Portal_Can_T_Open_0", item.FullName)); }
        catch (Win32Exception ex) { throw new ActionException(Strings.Get("ShowInFilesPortalAction_Error_Windows_Couldn_T_Open_Files_Portal"), ex); }
        return ContentValue.From(items[0]);
    }
}
