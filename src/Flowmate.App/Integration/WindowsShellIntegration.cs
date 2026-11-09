using System.Runtime.InteropServices;
using System.Text.Json;
using Flowmate.Core;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Flowmate.App.Integration;

/// <summary>One entry of the Jump List.</summary>
public sealed record JumpEntry(string Title, string Description, string Arguments, string IconPath);

/// <summary>
/// Windows integration for both the unpackaged build (HKCU registry, shell links) and the MSIX build (manifest
/// extensions + StartupTask; links and icons are the same). Everything it creates is recorded in a small state file so
/// <see cref="UnregisterAll"/> can remove exactly that and nothing else.
/// </summary>
public sealed class WindowsShellIntegration : IShellIntegration
{
    private const string ProgId = "FlowMate.Shortcut";
    private const string VerbKey = "FlowMate";
    private const int MaxCascadeItems = 30;
    private const int MaxJumpItems = 10;

    private sealed class LinkEntry
    {
        public string Kind { get; set; } = "";   // sendto | desktop | start
        public Guid ShortcutId { get; set; }
        public string Path { get; set; } = "";
        public string Signature { get; set; } = "";
    }

    private sealed class State
    {
        public List<LinkEntry> Links { get; set; } = new();
        public string? CascadeSignature { get; set; }
    }

    private readonly ShellOptions _o;
    private readonly ShortcutIcons _icons;
    private readonly Func<IReadOnlyList<Shortcut>> _all;
    private readonly object _gate = new();

    public WindowsShellIntegration(ShellOptions options, Func<IReadOnlyList<Shortcut>> allShortcuts)
    {
        _o = options;
        _icons = new ShortcutIcons(options.IconsFolder);
        _all = allShortcuts;
    }

    /// <summary>Called with the pinned shortcuts after every sync. The app applies them to its Jump List on the UI thread.</summary>
    public Action<IReadOnlyList<JumpEntry>>? JumpListSink { get; set; }

    /// <summary>Called with a short title and message when the user added something (so there is some feedback).</summary>
    public Action<string, string>? Notify { get; set; }

    public bool IsEnabled => _o.Enabled;

    // ---- Start with Windows ------------------------------------------------------------------

    private string RunCommand => $"\"{_o.ExePath}\" --background";

    public bool IsStartWithWindowsEnabled
    {
        get
        {
            if (!_o.Enabled) return false;
            if (_o.Packaged)
            {
                try { return Task.Run(async () => (await StartupTask.GetAsync(ShellOptions.StartupTaskId)).State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy).GetAwaiter().GetResult(); }
                catch (Exception ex) { AppLog.Write("Couldn't read the Start with Windows state: " + ex.Message); return false; }
            }
            using var key = Registry.CurrentUser.OpenSubKey(_o.RunKey);
            return key?.GetValue(ShellOptions.RunValueName) is string;
        }
    }

    public void SetStartWithWindows(bool enabled)
    {
        if (!_o.Enabled) return;
        if (_o.Packaged)
        {
            try { Task.Run(() => SetStartupTaskAsync(enabled)).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw new InvalidOperationException("Windows wouldn't change Start with Windows for Flow Mate. " + ex.Message, ex); }
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(_o.RunKey);
        if (enabled) key.SetValue(ShellOptions.RunValueName, RunCommand);
        else key.DeleteValue(ShellOptions.RunValueName, false);
    }

    private static async Task SetStartupTaskAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(ShellOptions.StartupTaskId);
        if (!enabled) { task.Disable(); return; }
        var state = await task.RequestEnableAsync();
        if (state is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy)
            throw new InvalidOperationException("Windows is blocking Flow Mate from starting with Windows. Open Windows Settings, go to Apps, then Startup, and turn Flow Mate on.");
    }

    // ---- links (flowmate://) and the .flowmate file type -------------------------------------

    /// <summary>
    /// Unpackaged only: makes sure flowmate:// and .flowmate point at this exe, and that Start with Windows keeps working
    /// after the app moved. The MSIX build declares all of this in its manifest.
    /// </summary>
    public void EnsureRegistered()
    {
        if (!_o.Enabled || _o.Packaged) return;
        var open = $"\"{_o.ExePath}\" \"%1\"";
        // Links pass through the link switch: Flow Mate then trusts only the link, however its text splits into arguments.
        var openLink = $"\"{_o.ExePath}\" {LaunchArguments.LinkSwitch} \"%1\"";
        var icon = $"\"{_o.ExePath}\",0";
        var changed = false;
        changed |= SetClasses(".flowmate", null, ProgId);
        changed |= SetClasses(ProgId, null, "Flow Mate shortcut");
        changed |= SetClasses(ProgId + @"\DefaultIcon", null, icon);
        changed |= SetClasses(ProgId + @"\shell\open\command", null, open);
        changed |= SetClasses("flowmate", null, "URL:Flow Mate");
        changed |= SetClasses("flowmate", "URL Protocol", "");
        changed |= SetClasses(@"flowmate\DefaultIcon", null, icon);
        changed |= SetClasses(@"flowmate\shell\open\command", null, openLink);
        if (changed && _o.NotifyShell) NotifyAssociationsChanged();

        using var run = Registry.CurrentUser.OpenSubKey(_o.RunKey, writable: true);
        if (run?.GetValue(ShellOptions.RunValueName) is string current && current != RunCommand)
            run.SetValue(ShellOptions.RunValueName, RunCommand);
    }

    private bool SetClasses(string subKey, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_o.ClassesKey + "\\" + subKey);
        var existing = key.GetValue(name) as string;
        if (existing == value) return false;
        key.SetValue(name, value);
        return true;
    }

    private void DeleteClasses(string subKey)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_o.ClassesKey + "\\" + subKey, throwOnMissingSubKey: false); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    // ---- Desktop and Start menu --------------------------------------------------------------

    public void AddToDesktop(Shortcut shortcut) { AddLink("desktop", shortcut); Notify?.Invoke("Added to your desktop", $"\"{shortcut.Name}\" is now on your desktop."); }

    public void AddToStartMenu(Shortcut shortcut) { AddLink("start", shortcut); Notify?.Invoke("Added to the Start menu", $"\"{shortcut.Name}\" is now in the Start menu, in the folder \"{ShellOptions.StartMenuFolderName}\"."); }

    private void AddLink(string kind, Shortcut shortcut)
    {
        if (!_o.Enabled) throw new NotSupportedException("This copy of Flow Mate is running in a test mode that doesn't change Windows.");
        lock (_gate)
        {
            var state = LoadState();
            state.Links.RemoveAll(l => l.Kind == kind && l.ShortcutId == shortcut.Id && DeleteFileQuietly(l.Path));
            var path = UniquePath(FolderFor(kind), shortcut.Name, "", state);
            WriteLink(kind, shortcut, path);
            state.Links.Add(new LinkEntry { Kind = kind, ShortcutId = shortcut.Id, Path = path, Signature = SignatureOf(shortcut) });
            SaveState(state);
        }
    }

    private string FolderFor(string kind) => kind switch
    {
        "sendto" => _o.SendToFolder,
        "desktop" => _o.DesktopFolder,
        _ => Path.Combine(_o.ProgramsFolder, ShellOptions.StartMenuFolderName),
    };

    private string SignatureOf(Shortcut s) => $"{s.Name}|{s.Icon}|{s.Color}|{_o.LauncherPath}";

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length > 80) name = name[..80].TrimEnd();
        return name.Length == 0 ? "Shortcut" : name;
    }

    /// <summary>folder\[prefix]name.lnk, with " (2)" added when somebody else's file already has that name.</summary>
    private static string UniquePath(string folder, string name, string prefix, State state)
    {
        var baseName = prefix + SafeFileName(name);
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(folder, (n == 1 ? baseName : $"{baseName} ({n})") + ".lnk");
            if (!File.Exists(path) || state.Links.Any(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase))) return path;
        }
    }

    private void WriteLink(string kind, Shortcut s, string path)
    {
        var args = $"--run {s.Id:D}" + (kind == "sendto" ? " --files" : "");
        try
        {
            ShellLink.Create(path, _o.LauncherPath, args, _icons.Ensure(s.Icon, s.Color), s.Description is { Length: > 0 } d ? d : $"Run \"{s.Name}\" with Flow Mate",
                minimized: _o.Packaged);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Flow Mate couldn't create the shortcut file in \"{Path.GetDirectoryName(path)}\". {ex.Message}", ex);
        }
    }

    // ---- keeping everything in step with the library -----------------------------------------

    /// <summary>
    /// Brings Send To entries, the right-click menu, Desktop / Start menu links and the Jump List in line with the library.
    /// Call from any thread; cheap when nothing changed.
    /// </summary>
    public void Sync(IReadOnlyList<Shortcut> shortcuts)
    {
        if (!_o.Enabled) return;
        var byId = shortcuts.ToDictionary(s => s.Id);
        var sendTo = shortcuts.Where(s => s.ShowInSendTo).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var failures = new List<string>();

        lock (_gate)
        {
            var state = LoadState();

            // Send To: exactly the shortcuts that ask for it.
            foreach (var stale in state.Links.Where(l => l.Kind == "sendto" && !sendTo.Any(s => s.Id == l.ShortcutId)).ToList())
            { DeleteFileQuietly(stale.Path); state.Links.Remove(stale); }
            foreach (var s in sendTo) Refresh(state, "sendto", s, failures);

            // Desktop / Start menu links the user added: follow renames, icon and colour changes; drop deleted shortcuts.
            foreach (var l in state.Links.Where(l => l.Kind != "sendto").ToList())
            {
                if (!byId.TryGetValue(l.ShortcutId, out var s)) { DeleteFileQuietly(l.Path); state.Links.Remove(l); continue; }
                Refresh(state, l.Kind, s, failures, l);
            }

            if (!_o.Packaged) SyncCascade(state, sendTo, failures);
            SaveState(state);
        }

        if (failures.Count > 0) AppLog.Write("Shell sync: " + string.Join("; ", failures));

        if (_o.UseJumpList && JumpListSink is { } sink)
        {
            var pinned = shortcuts.Where(s => s.Pinned).Take(MaxJumpItems).Select(s =>
                new JumpEntry(s.Name, s.Description is { Length: > 0 } d ? d : $"Run \"{s.Name}\"", $"--run {s.Id:D}", SafeIcon(s))).ToList();
            sink(pinned);
        }
    }

    private string SafeIcon(Shortcut s)
    {
        try { return _icons.Ensure(s.Icon, s.Color); } catch (Exception ex) when (ex is IOException) { return _o.ExePath; }
    }

    private void Refresh(State state, string kind, Shortcut s, List<string> failures, LinkEntry? existing = null)
    {
        existing ??= state.Links.FirstOrDefault(l => l.Kind == kind && l.ShortcutId == s.Id);
        var signature = SignatureOf(s);
        if (existing is not null && existing.Signature == signature && File.Exists(existing.Path)) return;
        try
        {
            var oldPath = existing?.Path;
            var path = UniquePath(FolderFor(kind), s.Name, kind == "sendto" ? "Flow Mate - " : "", state);
            if (existing is not null) state.Links.Remove(existing);
            if (oldPath is not null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase)) DeleteFileQuietly(oldPath);
            WriteLink(kind, s, path);
            state.Links.Add(new LinkEntry { Kind = kind, ShortcutId = s.Id, Path = path, Signature = signature });
        }
        catch (IOException ex) { failures.Add(ex.Message); }
    }

    // ---- the classic right-click cascade (unpackaged) ---------------------------------------

    private static readonly string[] CascadeRoots = { "*", "Directory" };

    private void SyncCascade(State state, IReadOnlyList<Shortcut> items, List<string> failures)
    {
        var shown = items.Take(MaxCascadeItems).ToList();
        var signature = shown.Count == 0 ? "" : string.Join("\n", shown.Select(SignatureOf)) + "\n" + string.Join(",", shown.Select(s => s.Id));
        bool present;
        using (var probe = Registry.CurrentUser.OpenSubKey(_o.ClassesKey + @"\*\shell\" + VerbKey)) present = probe is not null;
        if (signature == (state.CascadeSignature ?? "") && present == (shown.Count > 0)) return;

        try
        {
            foreach (var root in CascadeRoots)
            {
                var verb = $@"{root}\shell\{VerbKey}";
                DeleteClasses(verb);
                if (shown.Count == 0) continue;

                using var menu = Registry.CurrentUser.CreateSubKey(_o.ClassesKey + "\\" + verb);
                menu.SetValue("MUIVerb", "Flow Mate");
                menu.SetValue("Icon", $"\"{_o.ExePath}\",0");
                menu.SetValue("SubCommands", "");
                for (var i = 0; i < shown.Count; i++)
                {
                    var s = shown[i];
                    using var item = menu.CreateSubKey($@"shell\{i + 1:000}");
                    item.SetValue("MUIVerb", s.Name.Replace("&", "&&"));
                    item.SetValue("Icon", SafeIcon(s));
                    using var cmd = item.CreateSubKey("command");
                    cmd.SetValue(null, $"\"{_o.LauncherPath}\" --run {s.Id:D} --files \"%1\"");
                }
            }
            state.CascadeSignature = signature;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            failures.Add("right-click menu: " + ex.Message);
        }
    }

    // ---- removing everything -----------------------------------------------------------------

    /// <summary>Removes everything this app registered or created (used by <c>Flowmate.exe --unregister</c>, i.e. on uninstall).</summary>
    public void UnregisterAll()
    {
        if (!_o.Enabled) return;
        lock (_gate)
        {
            var state = LoadState();
            foreach (var l in state.Links) DeleteFileQuietly(l.Path);
            state.Links.Clear();
            try
            {
                var startFolder = Path.Combine(_o.ProgramsFolder, ShellOptions.StartMenuFolderName);
                if (Directory.Exists(startFolder) && !Directory.EnumerateFileSystemEntries(startFolder).Any()) Directory.Delete(startFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            try { SetStartWithWindows(false); } catch (Exception) { }
            if (!_o.Packaged)
            {
                foreach (var root in CascadeRoots) DeleteClasses($@"{root}\shell\{VerbKey}");
                using (var ext = Registry.CurrentUser.OpenSubKey(_o.ClassesKey + @"\.flowmate", writable: true))
                    if (ext?.GetValue(null) as string == ProgId) DeleteClasses(".flowmate");
                DeleteClasses(ProgId);
                DeleteClasses("flowmate");
                if (_o.NotifyShell) NotifyAssociationsChanged();
            }
            try { if (Directory.Exists(_o.IconsFolder)) Directory.Delete(_o.IconsFolder, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { File.Delete(_o.StateFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        JumpListSink?.Invoke(Array.Empty<JumpEntry>());
    }

    // ---- state file --------------------------------------------------------------------------

    private State LoadState()
    {
        try { if (File.Exists(_o.StateFile)) return JsonSerializer.Deserialize<State>(File.ReadAllText(_o.StateFile)) ?? new State(); }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new State();
    }

    private void SaveState(State state)
    {
        try { AtomicFile.WriteAllText(_o.StateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }

    private static bool DeleteFileQuietly(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }
}
