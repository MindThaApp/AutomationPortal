using System.IO;
using AutomationPortal.App.Integration;
using AutomationPortal.Core;
using AutomationPortal.Triggers;
using Microsoft.Win32;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>Runs the whole Windows integration against a throwaway HKCU key and temp folders; nothing real is touched.</summary>
public sealed class ShellIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-shell-tests", Guid.NewGuid().ToString("N")[..8]);
    private readonly string _reg = @"Software\AutomationPortalTest-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _exe = @"C:\Apps\Automation Portal\AutomationPortal.exe";
    private readonly List<Shortcut> _library = new();
    private readonly List<IReadOnlyList<JumpEntry>> _jump = new();
    private readonly WindowsShellIntegration _shell;

    public ShellIntegrationTests()
    {
        _shell = new WindowsShellIntegration(ShellOptions.Sandbox(_root, _reg, _exe), () => _library) { JumpListSink = _jump.Add };
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_reg, false); } catch { }
        try { Directory.Delete(_root, true); } catch { }
    }

    private static Shortcut Sc(string name, bool sendTo = false, bool pinned = false, string icon = "Globe24", string color = "teal")
    {
        var s = new ShortcutBuilder(name).Build();
        s.ShowInSendTo = sendTo; s.Pinned = pinned; s.Icon = icon; s.Color = color;
        return s;
    }

    private static string? ReadDefault(string path, string? name = null)
    {
        using var k = Registry.CurrentUser.OpenSubKey(path);
        return k?.GetValue(name) as string;
    }

    private string Classes(string sub) => _reg + @"\Classes\" + sub;

    private static (string Target, string Args, string Icon) ReadLink(string lnk)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic sh = Activator.CreateInstance(type)!;
        var l = sh.CreateShortcut(lnk);
        return ((string)l.TargetPath, (string)l.Arguments, (string)l.IconLocation);
    }

    [Fact]
    public void Start_with_windows_uses_the_run_key_and_removes_it()
    {
        Assert.False(_shell.IsStartWithWindowsEnabled);
        _shell.SetStartWithWindows(true);
        Assert.True(_shell.IsStartWithWindowsEnabled);
        Assert.Equal($"\"{_exe}\" --background", ReadDefault(_reg + @"\Run", "AutomationPortal"));
        _shell.SetStartWithWindows(false);
        Assert.False(_shell.IsStartWithWindowsEnabled);
    }

    [Fact]
    public void Links_and_file_type_register_and_unregister_cleanly()
    {
        _shell.EnsureRegistered();
        Assert.Equal("AutomationPortal.Shortcut", ReadDefault(Classes(".autoportal")));
        Assert.Equal($"\"{_exe}\" \"%1\"", ReadDefault(Classes(@"AutomationPortal.Shortcut\shell\open\command")));
        Assert.Equal("URL:Automation Portal", ReadDefault(Classes("automationportal")));
        Assert.Equal("", ReadDefault(Classes("automationportal"), "URL Protocol"));
        Assert.Equal($"\"{_exe}\" --link \"%1\"", ReadDefault(Classes(@"automationportal\shell\open\command")));

        _shell.EnsureRegistered();   // idempotent
        _shell.SetStartWithWindows(true);
        _shell.UnregisterAll();

        Assert.Null(ReadDefault(Classes(".autoportal")));
        Assert.Null(ReadDefault(Classes("automationportal")));
        Assert.Null(ReadDefault(Classes(@"AutomationPortal.Shortcut\shell\open\command")));
        Assert.False(_shell.IsStartWithWindowsEnabled);
    }

    [Fact]
    public void Another_apps_automationportal_file_type_is_not_removed()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Classes(".autoportal"))) k.SetValue(null, "SomethingElse.File");
        _shell.UnregisterAll();
        Assert.Equal("SomethingElse.File", ReadDefault(Classes(".autoportal")));
    }

    [Fact]
    public void Send_to_entries_follow_the_library()
    {
        var a = Sc("Resize Images", sendTo: true);
        var b = Sc("Plain");
        _library.AddRange(new[] { a, b });
        _shell.Sync(_library);

        var sendTo = Path.Combine(_root, "SendTo");
        var lnk = Path.Combine(sendTo, "Automation Portal - Resize Images.lnk");
        Assert.True(File.Exists(lnk));
        Assert.Single(Directory.GetFiles(sendTo));
        var (target, args, icon) = ReadLink(lnk);
        Assert.Equal(_exe, target);
        Assert.Equal($"--run {a.Id:D} --files", args);
        Assert.StartsWith(Path.Combine(_root, "icons"), icon);
        Assert.True(File.Exists(icon.Split(',')[0]));

        // rename, then switch the option off
        a.Name = "Shrink Images";
        _shell.Sync(_library);
        Assert.False(File.Exists(lnk));
        Assert.True(File.Exists(Path.Combine(sendTo, "Automation Portal - Shrink Images.lnk")));
        a.ShowInSendTo = false;
        _shell.Sync(_library);
        Assert.Empty(Directory.GetFiles(sendTo));
    }

    [Fact]
    public void Right_click_menu_lists_the_send_to_shortcuts_and_goes_away_with_them()
    {
        var a = Sc("Resize & Shrink", sendTo: true);
        var b = Sc("Zip", sendTo: true);
        _library.AddRange(new[] { a, b });
        _shell.Sync(_library);

        foreach (var root in new[] { "*", "Directory" })
        {
            var menu = Classes($@"{root}\shell\AutomationPortal");
            Assert.Equal("Automation Portal", ReadDefault(menu, "MUIVerb"));
            Assert.Equal("", ReadDefault(menu, "SubCommands"));
            Assert.Equal("Resize && Shrink", ReadDefault(menu + @"\shell\001", "MUIVerb"));
            Assert.Equal($"\"{_exe}\" --run {a.Id:D} --files \"%1\"", ReadDefault(menu + @"\shell\001\command"));
            Assert.Equal("Zip", ReadDefault(menu + @"\shell\002", "MUIVerb"));
        }

        _library.Clear();
        _shell.Sync(_library);
        Assert.Null(ReadDefault(Classes(@"*\shell\AutomationPortal"), "MUIVerb"));
        Assert.Null(ReadDefault(Classes(@"Directory\shell\AutomationPortal"), "MUIVerb"));
    }

    [Fact]
    public void Desktop_and_start_menu_links_get_an_icon_and_follow_renames_and_deletes()
    {
        var s = Sc("Good Morning", icon: "WeatherSunny24", color: "orange");
        _library.Add(s);
        _shell.AddToDesktop(s);
        _shell.AddToStartMenu(s);

        var desktop = Path.Combine(_root, "Desktop", "Good Morning.lnk");
        var start = Path.Combine(_root, "Programs", "Automation Portal Shortcuts", "Good Morning.lnk");
        Assert.True(File.Exists(desktop));
        Assert.True(File.Exists(start));
        var (target, args, icon) = ReadLink(desktop);
        Assert.Equal(_exe, target);
        Assert.Equal($"--run {s.Id:D}", args);
        var ico = icon.Split(',')[0];
        Assert.Equal("WeatherSunny24-orange.ico", Path.GetFileName(ico));
        AssertValidIco(ico);

        s.Name = "Good Day";
        _shell.Sync(_library);
        Assert.False(File.Exists(desktop));
        Assert.True(File.Exists(Path.Combine(_root, "Desktop", "Good Day.lnk")));
        Assert.True(File.Exists(Path.Combine(_root, "Programs", "Automation Portal Shortcuts", "Good Day.lnk")));

        _library.Clear();
        _shell.Sync(_library);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Desktop")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Programs", "Automation Portal Shortcuts")));
    }

    [Fact]
    public void A_file_that_is_not_ours_is_never_overwritten_or_removed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Desktop"));
        var theirs = Path.Combine(_root, "Desktop", "Zip.lnk");
        File.WriteAllText(theirs, "someone else's file");
        var s = Sc("Zip");
        _library.Add(s);
        _shell.AddToDesktop(s);
        Assert.Equal("someone else's file", File.ReadAllText(theirs));
        Assert.True(File.Exists(Path.Combine(_root, "Desktop", "Zip (2).lnk")));
        _shell.UnregisterAll();
        Assert.True(File.Exists(theirs));
        Assert.False(File.Exists(Path.Combine(_root, "Desktop", "Zip (2).lnk")));
    }

    [Fact]
    public void Unregister_removes_every_link_icon_and_state()
    {
        var s = Sc("Zip", sendTo: true, pinned: true);
        _library.Add(s);
        _shell.Sync(_library);
        _shell.AddToDesktop(s);
        _shell.AddToStartMenu(s);
        _shell.EnsureRegistered();
        _shell.UnregisterAll();

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "SendTo")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Desktop")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Programs", "Automation Portal Shortcuts")));
        Assert.False(Directory.Exists(Path.Combine(_root, "icons")));
        Assert.False(File.Exists(Path.Combine(_root, "shell-state.json")));
        Assert.Null(ReadDefault(Classes(@"*\shell\AutomationPortal"), "MUIVerb"));
    }

    [Fact]
    public void Jump_list_gets_the_pinned_shortcuts_only()
    {
        var jump = new List<IReadOnlyList<JumpEntry>>();
        var shell = new WindowsShellIntegration(ShellOptions.Sandbox(_root, _reg, _exe, useJumpList: true), () => _library) { JumpListSink = jump.Add };
        var pinned = Sc("Pinned One", pinned: true);
        _library.AddRange(new[] { pinned, Sc("Not pinned") });
        shell.Sync(_library);
        var entry = Assert.Single(jump.Last());
        Assert.Equal("Pinned One", entry.Title);
        Assert.Equal($"--run {pinned.Id:D}", entry.Arguments);
        Assert.True(File.Exists(entry.IconPath));
    }

    [Fact]
    public void Disabled_shell_does_nothing_and_refuses_to_add_links()
    {
        var shell = new WindowsShellIntegration(ShellOptions.Disabled(AutomationPortalPaths.ForRoot(_root)), () => _library);
        shell.EnsureRegistered();
        shell.Sync(new[] { Sc("X", sendTo: true) });
        shell.SetStartWithWindows(true);
        Assert.False(shell.IsStartWithWindowsEnabled);
        Assert.Throws<NotSupportedException>(() => shell.AddToDesktop(Sc("X")));
    }

    [Fact]
    public void Icons_have_every_size_and_differ_by_glyph_and_colour()
    {
        var icons = new ShortcutIcons(Path.Combine(_root, "icons"));
        var a = icons.Ensure("Globe24", "teal");
        var b = icons.Ensure("Globe24", "red");
        var c = icons.Ensure("Flash24", "teal");
        var d = icons.Ensure("NotARealIcon", "nope");
        Assert.Equal(icons.Ensure("Flash24", "blue"), d);
        foreach (var f in new[] { a, b, c, d }) AssertValidIco(f);
        Assert.NotEqual(File.ReadAllBytes(a), File.ReadAllBytes(b));
        Assert.NotEqual(File.ReadAllBytes(a), File.ReadAllBytes(c));
    }

    private static void AssertValidIco(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.Equal(ShortcutIcons.Sizes.Length, count);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var size = bytes[entry] == 0 ? 256 : bytes[entry];
            Assert.Equal(ShortcutIcons.Sizes[i], size);
            var length = (int)BitConverter.ToUInt32(bytes, entry + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, entry + 12);
            Assert.True(length > 80);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[offset..(offset + 4)]);
        }
    }

    [Theory]
    [InlineData("Win+Alt+Space")]
    [InlineData("Ctrl+Alt+M")]
    public void Default_and_typical_keys_are_valid_global_shortcuts(string keys)
    {
        Assert.True(HotkeyParser.TryParse(keys, out var chord));
        Assert.True(HotkeyParser.IsSafeGlobal(chord));
    }

    [Fact]
    public void Default_quick_run_key_is_the_new_one()
    {
        Assert.Equal("Win+Alt+Space", new AppSettings().QuickRunHotkey);
        Assert.Equal(AppSettings.CurrentVersion, new JsonSettingsStore(AutomationPortalPaths.ForRoot(_root)).Current.SettingsVersion);
    }
}
