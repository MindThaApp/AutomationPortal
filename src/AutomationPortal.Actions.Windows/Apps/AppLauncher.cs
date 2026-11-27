using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Apps;

internal static class AppLauncher
{
    /// <summary>Opens the app (optionally with files). Throws a friendly <see cref="ActionException"/> when it can't.</summary>
    public static async Task<AppItem> LaunchAsync(AppItem app, IReadOnlyList<string>? files, CancellationToken ct)
    {
        var all = await InstalledApps.GetInstalledAppsAsync(false, ct).ConfigureAwait(false);
        var installed = InstalledApps.Find(all, app.Id)
                        ?? (app.Path is { Length: > 0 } ? InstalledApps.Find(all, app.Path) : null)
                        ?? (app.Name.Length > 0 ? InstalledApps.Find(all, app.Name) : null);
        var id = installed?.Id ?? app.Id;
        var path = installed?.Path ?? (File.Exists(app.Path ?? "") ? app.Path : null) ?? (AppMatcher.LooksLikePath(app.Id) && File.Exists(app.Id) ? app.Id : null);
        var name = installed?.Name ?? (app.Name.Length > 0 ? app.Name : id);
        files ??= Array.Empty<string>();

        try
        {
            if (id.Contains('!'))
                await ActivateStoreAppAsync(id, files).ConfigureAwait(false);
            else if (path is not null)
                StartFile(path, files);
            else if (installed is not null && files.Count == 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{installed.Id}") { UseShellExecute = true })?.Dispose();
            else if (installed is not null)
                throw new ActionException(Strings.Format("AppLauncher_Error_Automation_Portal_Can_T_Open_Files", name));
            else
                StartFile(app.Id, files);
        }
        catch (Win32Exception ex)
        {
            throw new ActionException(Strings.Format("AppLauncher_Error_Couldn_T_Open_0_Check_That", name), ex);
        }
        return new AppItem(id, name, path);
    }

    private static void StartFile(string program, IReadOnlyList<string> files)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = true };
        var dir = Path.GetDirectoryName(program);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
        if (files.Count > 0) psi.Arguments = string.Join(" ", files.Select(f => $"\"{f}\""));
        Process.Start(psi)?.Dispose();
    }

    private static Task ActivateStoreAppAsync(string aumid, IReadOnlyList<string> files) => Sta.Run(() =>
    {
        var manager = ComGuids.Create<IApplicationActivationManager>(ComGuids.ApplicationActivationManager);
        try
        {
            if (files.Count == 0)
            {
                var hr = manager.ActivateApplication(aumid, null, 0, out _);
                if (hr != 0) throw new ActionException(Strings.Format("AppLauncher_Error_Windows_Couldn_T_Start_That_App", hr));
                return;
            }
            foreach (var file in files)
            {
                if (Win32.SHCreateItemFromParsingName(file, IntPtr.Zero, ComGuids.IShellItem, out var item) != 0)
                    throw new ActionException(Strings.Format("AppLauncher_Error_Couldn_T_Open_The_File_0", Path.GetFileName(file)));
                if (Win32.SHCreateShellItemArrayFromShellItem(item, ComGuids.IShellItemArray, out var array) != 0)
                    throw new ActionException(Strings.Get("AppLauncher_Error_Windows_Couldn_T_Prepare_The_File"));
                var hr = manager.ActivateForFile(aumid, array, "open", out _);
                if (hr != 0) throw new ActionException(Strings.Format("AppLauncher_Error_That_App_Couldn_T_Open_0", Path.GetFileName(file), hr));
            }
        }
        finally { Marshal.ReleaseComObject(manager); }
    });
}

internal static class ShellReveal
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attrs, out uint outAttrs);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);
    [DllImport("shell32.dll")] private static extern IntPtr ILClone(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern bool ILRemoveLastID(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);

    /// <summary>Opens File Explorer with the items selected, one window per containing folder.</summary>
    public static void Select(IEnumerable<string> paths)
    {
        foreach (var group in paths.GroupBy(p => Path.GetDirectoryName(p.TrimEnd('\\', '/')) ?? p, StringComparer.OrdinalIgnoreCase))
        {
            var absolute = new List<IntPtr>();
            var folder = IntPtr.Zero;
            try
            {
                foreach (var p in group)
                {
                    if (SHParseDisplayName(p.TrimEnd('\\', '/'), IntPtr.Zero, out var pidl, 0, out _) == 0) absolute.Add(pidl);
                }
                if (absolute.Count == 0) continue;
                folder = ILClone(absolute[0]);
                ILRemoveLastID(folder);
                var children = absolute.Select(ILFindLastID).ToArray();
                SHOpenFolderAndSelectItems(folder, (uint)children.Length, children, 0);
            }
            finally
            {
                foreach (var p in absolute) ILFree(p);
                if (folder != IntPtr.Zero) ILFree(folder);
            }
        }
    }
}
