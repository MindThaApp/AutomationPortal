using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Apps;

/// <summary>One app that can be opened: a classic desktop program or a Microsoft Store app.</summary>
/// <param name="Id">Stable id. For Store apps this is the AppUserModelId ("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"); for classic apps the id from the Apps folder (usually a path).</param>
/// <param name="Name">Name as shown in the Start menu.</param>
/// <param name="Path">Full path of the program file when known (null for most Store apps).</param>
/// <param name="IconSource">Parsing name that <see cref="AppIcons.Load"/> understands (also usable as "shell:AppsFolder\..." in the shell).</param>
public sealed record InstalledApp(string Id, string Name, string? Path, bool IsStoreApp, string IconSource);

/// <summary>
/// The list of apps installed on this PC (classic + Store apps, read from the same "Apps" folder as the Start menu).
/// Used by the app pickers and the "Open App" family of actions. The list is cached for a few minutes.
/// </summary>
public static class InstalledApps
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IReadOnlyList<InstalledApp>? _cache;
    private static DateTime _cachedAt;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static async Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache is { } fresh && DateTime.UtcNow - _cachedAt < MaxAge) return fresh;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cache is { } again && DateTime.UtcNow - _cachedAt < MaxAge) return again;
            var list = await RunOnStaThread(Enumerate).ConfigureAwait(false);
            _cache = list;
            _cachedAt = DateTime.UtcNow;
            return list;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Choices for App parameters: id = app id, label = app name.</summary>
    public static ChoicesProvider ChoicesProvider { get; } = async (_, ct) =>
    {
        var apps = await GetInstalledAppsAsync(false, ct).ConfigureAwait(false);
        return apps.Select(a => new Choice(a.Id, a.Name)).ToList();
    };

    /// <summary>Finds an installed app by id, path, exact name, program file name or (last) part of its name.</summary>
    public static async Task<InstalledApp?> FindAsync(string idOrName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return Find(await GetInstalledAppsAsync(false, ct).ConfigureAwait(false), idOrName);
    }

    internal static InstalledApp? Find(IReadOnlyList<InstalledApp> apps, string idOrName)
    {
        var key = idOrName.Trim().Trim('"');
        var cmp = StringComparison.OrdinalIgnoreCase;
        var hit = apps.FirstOrDefault(a => a.Id.Equals(key, cmp))
                  ?? apps.FirstOrDefault(a => a.Path is not null && a.Path.Equals(key, cmp))
                  ?? apps.FirstOrDefault(a => a.Name.Equals(key, cmp));
        if (hit is not null) return hit;
        var file = System.IO.Path.GetFileName(key);
        if (file.EndsWith(".exe", cmp))
            hit = apps.FirstOrDefault(a => a.Path is not null && System.IO.Path.GetFileName(a.Path).Equals(file, cmp));
        if (hit is not null) return hit;
        if (key.Length >= 3) hit = apps.FirstOrDefault(a => a.Name.StartsWith(key, cmp)) ?? apps.FirstOrDefault(a => a.Name.Contains(key, cmp));
        return hit;
    }

    private static List<InstalledApp> Enumerate()
    {
        var result = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return new List<InstalledApp>();
        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic folder = shell!.NameSpace("shell:AppsFolder");
            dynamic items = folder.Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string id = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    var isStore = id.Contains('!');
                    var path = isStore ? null : ResolvePath(id);
                    result.TryAdd(id, new InstalledApp(id, name.Trim(), path, isStore, @"shell:AppsFolder\" + id));
                }
                catch (COMException) { }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally { if (shell is not null) try { Marshal.FinalReleaseComObject(shell); } catch { } }
        return result.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Turns an Apps-folder parsing name ("{KnownFolderGuid}\Sub\app.exe" or "C:\...") into a real path, or null.</summary>
    internal static string? ResolvePath(string parsingName)
    {
        try
        {
            if (parsingName.Length > 2 && parsingName[1] == ':' && File.Exists(parsingName)) return parsingName;
            if (parsingName.StartsWith('{'))
            {
                var close = parsingName.IndexOf('}');
                if (close > 0 && Guid.TryParse(parsingName[..(close + 1)], out var guid))
                {
                    var rest = parsingName[(close + 1)..].TrimStart('\\');
                    var root = Win32.KnownFolder(guid);
                    if (root is not null)
                    {
                        var full = System.IO.Path.Combine(root, rest);
                        return File.Exists(full) ? full : null;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Flowmate app list" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}

/// <summary>Loads app icons through the shell (works for classic programs and Store apps).</summary>
public static class AppIcons
{
    /// <summary>Icon for an app (size in pixels) as a frozen WPF image, or null when the shell has none. Safe to call from any thread.</summary>
    public static BitmapSource? Load(InstalledApp app, int size = 48) => Load(app.IconSource, size);

    public static BitmapSource? Load(string parsingNameOrPath, int size = 48)
    {
        try
        {
            if (Win32.SHCreateItemFromParsingName(parsingNameOrPath, IntPtr.Zero, ComGuids.IShellItemImageFactory, out var item) != 0) return null;
            var factory = (IShellItemImageFactory)item;
            // SIIGBF_BIGGERSIZEOK (1) | SIIGBF_ICONONLY (4)
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4, out var hbitmap) != 0 || hbitmap == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(hbitmap); }
        }
        catch { return null; }
    }

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
