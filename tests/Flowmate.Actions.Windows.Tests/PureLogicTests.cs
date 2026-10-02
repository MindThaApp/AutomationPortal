using Flowmate.Actions.Windows.Apps;
using Flowmate.Actions.Windows.Clipboard;
using Flowmate.Actions.Windows.Devices;
using Flowmate.Actions.Windows.Input;
using Flowmate.Actions.Windows.Location;
using Flowmate.Actions.Windows.Screen;
using Flowmate.Actions.Windows.Advanced;
using Flowmate.Core;

namespace Flowmate.Actions.Windows.Tests;

public class KeyComboTests
{
    [Fact]
    public void Parses_a_simple_shortcut()
    {
        var chord = Assert.Single(KeyCombo.Parse("Ctrl+Shift+T"));
        Assert.Equal(new[] { KeyCombo.Ctrl, KeyCombo.Shift }, chord.Modifiers);
        Assert.Equal('T', chord.Key);
    }

    [Theory]
    [InlineData("ctrl+c", 0x43)]
    [InlineData("Control + C", 0x43)]
    [InlineData("Alt+F4", 0x73)]
    [InlineData("Win+D", 0x44)]
    [InlineData("Enter", 0x0D)]
    [InlineData("esc", 0x1B)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Ctrl+5", 0x35)]
    [InlineData("F12", 0x7B)]
    [InlineData("Ctrl+Plus", 0xBB)]
    [InlineData("Ctrl++", 0xBB)]
    [InlineData("Ctrl+,", 0xBC)]
    [InlineData("Shift+Tab", 0x09)]
    public void Reads_key_names(string text, int expectedKey) =>
        Assert.Equal(expectedKey, KeyCombo.Parse(text).Single().Key);

    [Fact]
    public void Accepts_several_chords_in_a_row()
    {
        var chords = KeyCombo.Parse("Ctrl+K Ctrl+C");
        Assert.Equal(2, chords.Count);
        Assert.Equal('K', chords[0].Key);
        Assert.Equal('C', chords[1].Key);
    }

    [Fact]
    public void A_lone_modifier_is_tapped()
    {
        var chord = KeyCombo.Parse("Win").Single();
        Assert.Empty(chord.Modifiers);
        Assert.Equal(KeyCombo.Win, chord.Key);
    }

    [Fact]
    public void Same_modifier_twice_is_counted_once() =>
        Assert.Single(KeyCombo.Parse("Ctrl+Ctrl+A").Single().Modifiers);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Banana")]
    [InlineData("A+B")]
    public void Rejects_what_it_cannot_read(string text)
    {
        var ex = Assert.Throws<FormatException>(() => KeyCombo.Parse(text));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(KeyCombo.TryParse(text, out _));
    }

    [Fact]
    public void Arrow_and_navigation_keys_are_extended()
    {
        Assert.True(KeyCombo.IsExtended(0x25));
        Assert.True(KeyCombo.IsExtended(0x2E));
        Assert.False(KeyCombo.IsExtended(0x41));
    }

    [Fact]
    public void Chords_print_back_readably() =>
        Assert.Equal("Shift+Ctrl+T", KeyCombo.Parse("shift+ctrl+t").Single().ToString());
}

public class WindowGeometryTests
{
    private static readonly Box Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(SnapPosition.LeftHalf, 0, 0, 960, 1040)]
    [InlineData(SnapPosition.RightHalf, 960, 0, 960, 1040)]
    [InlineData(SnapPosition.TopHalf, 0, 0, 1920, 520)]
    [InlineData(SnapPosition.BottomHalf, 0, 520, 1920, 520)]
    [InlineData(SnapPosition.TopLeft, 0, 0, 960, 520)]
    [InlineData(SnapPosition.BottomRight, 960, 520, 960, 520)]
    public void Snaps_to_halves_and_corners(SnapPosition pos, int x, int y, int w, int h) =>
        Assert.Equal(new Box(x, y, w, h), WindowGeometry.Snap(Work, pos));

    [Fact]
    public void Odd_sizes_leave_no_gap()
    {
        var work = new Box(-1920, 100, 1921, 1001);
        var left = WindowGeometry.Snap(work, SnapPosition.LeftHalf);
        var right = WindowGeometry.Snap(work, SnapPosition.RightHalf);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(work.Right, right.Right);
        var top = WindowGeometry.Snap(work, SnapPosition.TopLeft);
        var bottom = WindowGeometry.Snap(work, SnapPosition.BottomLeft);
        Assert.Equal(top.Bottom, bottom.Y);
        Assert.Equal(work.Bottom, bottom.Bottom);
    }

    [Fact]
    public void Thirds_cover_the_work_area()
    {
        var a = WindowGeometry.Snap(Work, SnapPosition.LeftThird);
        var b = WindowGeometry.Snap(Work, SnapPosition.MiddleThird);
        var c = WindowGeometry.Snap(Work, SnapPosition.RightThird);
        Assert.Equal(a.Right, b.X);
        Assert.Equal(b.Right, c.X);
        Assert.Equal(Work.Right, c.Right);
    }

    [Fact]
    public void Centering_keeps_the_size_and_shrinks_what_does_not_fit()
    {
        Assert.Equal(new Box(560, 270, 800, 500), WindowGeometry.CenterIn(Work, new Box(5, 5, 800, 500)));
        Assert.Equal(new Box(0, 0, 1920, 1040), WindowGeometry.CenterIn(Work, new Box(0, 0, 5000, 5000)));
    }

    [Fact]
    public void Invisible_borders_are_compensated()
    {
        // A Windows 11 window: 7px invisible resize borders left/right/bottom.
        var rect = new Box(93, 100, 814, 607);
        var visible = new Box(100, 100, 800, 600);
        var placed = WindowGeometry.AdjustForInvisibleBorders(new Box(0, 0, 960, 1040), rect, visible);
        Assert.Equal(new Box(-7, 0, 974, 1047), placed);
    }

    [Fact]
    public void Moving_to_another_monitor_scales_position_and_size()
    {
        var from = new Box(0, 0, 1920, 1080);
        var to = new Box(1920, 0, 3840, 2160);
        var moved = WindowGeometry.MoveToMonitor(new Box(480, 270, 960, 540), from, to);
        Assert.Equal(new Box(1920 + 960, 540, 1920, 1080), moved);
    }

    [Fact]
    public void Moved_windows_stay_inside_the_target()
    {
        var moved = WindowGeometry.MoveToMonitor(new Box(1800, 1000, 900, 500), new Box(0, 0, 1920, 1080), new Box(2000, 0, 800, 600));
        Assert.True(moved.X >= 2000 && moved.Right <= 2800 && moved.Y >= 0 && moved.Bottom <= 600);
    }

    [Fact]
    public void Finds_the_monitor_with_the_most_overlap()
    {
        var works = new[] { new Box(0, 0, 1920, 1080), new Box(1920, 0, 1920, 1080) };
        Assert.Equal(1, WindowGeometry.MonitorIndexFor(new Box(1800, 100, 800, 600), works));
        Assert.Equal(0, WindowGeometry.MonitorIndexFor(new Box(100, 100, 800, 600), works));
        Assert.Equal(1, WindowGeometry.MonitorIndexFor(new Box(4500, 100, 100, 100), works)); // off screen: nearest
    }

    [Fact]
    public void Next_monitor_goes_left_to_right_and_wraps()
    {
        var monitors = new[] { new Box(1920, 0, 1920, 1080), new Box(0, 0, 1920, 1080), new Box(-1920, 0, 1920, 1080) };
        Assert.Equal(1, WindowGeometry.NextMonitor(monitors, 2)); // leftmost -> middle
        Assert.Equal(0, WindowGeometry.NextMonitor(monitors, 1));
        Assert.Equal(2, WindowGeometry.NextMonitor(monitors, 0)); // wraps
        Assert.Equal(0, WindowGeometry.NextMonitor(monitors, 2, -1)); // previous wraps backwards
    }
}

public class WindowLayoutTests
{
    [Fact]
    public void Layouts_round_trip_through_the_json_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new WindowLayoutStore(root);
            Assert.Empty(store.Load());
            store.Save(new SavedLayout
            {
                Name = "Work",
                Windows = { new SavedWindow { ExePath = @"C:\x\code.exe", ProcessName = "Code", Title = "main.cs", Left = -1920, Top = 10, Width = 900, Height = 700, State = "maximized", AppId = "A!B" } },
            });
            store.Save(new SavedLayout { Name = "Play" });
            store.Save(new SavedLayout { Name = "work", Windows = { new SavedWindow { ProcessName = "Notepad" } } }); // replaces "Work"

            var loaded = new WindowLayoutStore(root).Load();
            Assert.Equal(new[] { "Play", "work" }, loaded.Select(l => l.Name).ToArray());
            Assert.Equal("Notepad", store.Find("WORK")!.Windows.Single().ProcessName);
            Assert.True(File.Exists(Path.Combine(root, "window-layouts.json")));

            store.Save(new SavedLayout { Name = "Work", Windows = { new SavedWindow { Left = -1920, Width = 900, State = "maximized" } } });
            var w = store.Find("Work")!.Windows.Single();
            Assert.Equal(-1920, w.Left);
            Assert.Equal("maximized", w.State);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void A_broken_file_reads_as_no_layouts()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "window-layouts.json"), "{ not json");
            Assert.Empty(new WindowLayoutStore(root).Load());
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Matching_prefers_same_program_and_title_and_uses_each_window_once()
    {
        var saved = new[]
        {
            new SavedWindow { ExePath = @"C:\a\notepad.exe", ProcessName = "notepad", Title = "one.txt" },
            new SavedWindow { ExePath = @"C:\a\notepad.exe", ProcessName = "notepad", Title = "two.txt" },
            new SavedWindow { ExePath = @"C:\a\code.exe", ProcessName = "code", Title = "x" },
            new SavedWindow { ExePath = @"C:\a\gone.exe", ProcessName = "gone", Title = "g" },
        };
        var live = new[]
        {
            new LiveWindow(1, @"C:\a\notepad.exe", "notepad", "two.txt"),
            new LiveWindow(2, @"C:\a\notepad.exe", "notepad", "other.txt"),
            new LiveWindow(3, @"c:\a\CODE.exe", "code", "different"),
        };
        var result = LayoutMatcher.Match(saved, live);
        Assert.Equal(2, result[0].Live!.Handle); // "one.txt" is not open: takes the leftover notepad window
        Assert.Equal(1, result[1].Live!.Handle); // exact title match wins
        Assert.Equal(3, result[2].Live!.Handle); // path compared ignoring case
        Assert.Null(result[3].Live);
    }
}

public class DisplayAndSettingsTests
{
    [Theory]
    [InlineData("pc", DisplayModeMap.SDC_TOPOLOGY_INTERNAL)]
    [InlineData("duplicate", DisplayModeMap.SDC_TOPOLOGY_CLONE)]
    [InlineData("extend", DisplayModeMap.SDC_TOPOLOGY_EXTEND)]
    [InlineData("second", DisplayModeMap.SDC_TOPOLOGY_EXTERNAL)]
    [InlineData(" Extend ", DisplayModeMap.SDC_TOPOLOGY_EXTEND)]
    public void Display_modes_map_to_windows_topologies(string mode, uint flag) =>
        Assert.Equal(flag, DisplayModeMap.TopologyFlag(mode));

    [Fact]
    public void Unknown_display_mode_is_a_friendly_error() =>
        Assert.Contains("PC screen only", Assert.Throws<ActionException>(() => DisplayModeMap.TopologyFlag("bogus")).Message);

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("2560 x 1440", 2560, 1440)]
    [InlineData("3840×2160", 3840, 2160)]
    public void Resolutions_are_parsed(string text, int w, int h)
    {
        Assert.True(DisplayModeMap.TryParseResolution(text, out var width, out var height));
        Assert.Equal((w, h), (width, height));
    }

    [Theory]
    [InlineData("")]
    [InlineData("big")]
    [InlineData("1920")]
    [InlineData("0x0")]
    [InlineData("-5x10")]
    public void Bad_resolutions_are_rejected(string text) => Assert.False(DisplayModeMap.TryParseResolution(text, out _, out _));

    [Fact]
    public void Settings_pages_are_unique_and_well_formed()
    {
        Assert.True(SettingsPages.All.Count >= 40);
        Assert.Equal(SettingsPages.All.Count, SettingsPages.All.Select(p => p.Id).Distinct().Count());
        Assert.All(SettingsPages.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Label));
            Assert.True(p.Uri.StartsWith("ms-settings:") || p.Uri == "windowsdefender:", p.Uri);
        });
        Assert.Equal("ms-settings:display", SettingsPages.ResolveUri("display"));
        Assert.Equal("ms-settings:sound", SettingsPages.ResolveUri("Sound"));
        Assert.Equal("ms-settings:bluetooth", SettingsPages.ResolveUri("ms-settings:bluetooth"));
        Assert.Null(SettingsPages.ResolveUri("nonsense"));
    }

    [Fact]
    public void Monitor_input_codes_follow_the_ddc_standard()
    {
        Assert.Equal(0x11u, MonitorInputs.CodeFor("hdmi1"));
        Assert.Equal(0x0Fu, MonitorInputs.CodeFor("DP1"));
        Assert.Equal(0x12u, MonitorInputs.CodeFor("hdmi2"));
        Assert.Null(MonitorInputs.CodeFor("scart"));
        Assert.Equal(MonitorInputs.Known.Count, MonitorInputs.Known.Select(k => k.Code).Distinct().Count());
    }

    [Fact]
    public void Battery_status_is_decoded()
    {
        var charging = BatteryInfo.From(acLine: 1, batteryFlag: 8, percent: 64, systemStatusFlag: 0, secondsLeft: uint.MaxValue);
        Assert.True(charging.HasBattery && charging.PluggedIn && charging.Charging);
        Assert.Equal(64, charging.Percent);
        Assert.Null(charging.MinutesLeft);

        var onBattery = BatteryInfo.From(0, 1, 40, 1, 7200);
        Assert.False(onBattery.PluggedIn);
        Assert.Equal(120, onBattery.MinutesLeft);
        Assert.True(onBattery.BatterySaverOn);

        var desktop = BatteryInfo.From(1, 128, 255, 0, uint.MaxValue);
        Assert.False(desktop.HasBattery);
        Assert.True(desktop.PluggedIn);
    }

    [Fact]
    public void Saved_wifi_names_are_read_from_netsh_output_in_any_language()
    {
        const string english = "\r\nProfiles on interface Wi-Fi:\r\n\r\nGroup policy profiles (read only)\r\n---------------------------------\r\n    <None>\r\n\r\nUser profiles\r\n-------------\r\n    All User Profile     : Home Network\r\n    All User Profile     : Café 5G\r\n    User Profile         : Office\r\n";
        Assert.Equal(new[] { "Home Network", "Café 5G", "Office" }, NetworkService.ParseProfiles(english).ToArray());
        const string german = "Profile auf Schnittstelle WLAN:\r\n\r\n    Profil für alle Benutzer    : Zuhause\r\n";
        Assert.Equal(new[] { "Zuhause" }, NetworkService.ParseProfiles(german).ToArray());
        Assert.Empty(NetworkService.ParseProfiles(""));
    }

    [Fact]
    public void Console_output_is_decoded_line_by_line_as_utf8_or_the_console_code_page()
    {
        var oem = ProcessRunner.OemEncoding();
        var bytes = System.Text.Encoding.UTF8.GetBytes("    All User Profile     : Ramin’s iPhone\r\n")
            .Concat(oem.GetBytes("    Profil für alle Benutzer    : Zuhause\r\n"))
            .Concat(System.Text.Encoding.ASCII.GetBytes("last line without newline")).ToArray();
        var lines = ConsoleText.Decode(bytes).Split("\r\n");
        Assert.Equal("    All User Profile     : Ramin’s iPhone", lines[0]);
        Assert.Equal("    Profil für alle Benutzer    : Zuhause", lines[1]);
        Assert.Equal("last line without newline", lines[2]);
        Assert.Equal("", ConsoleText.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void Keep_awake_reports_its_state_and_stops()
    {
        Assert.False(KeepAwakeService.IsActive);
        KeepAwakeService.Start(TimeSpan.FromHours(1), keepScreenOn: false);
        try
        {
            Assert.True(KeepAwakeService.IsActive);
            Assert.False(KeepAwakeService.KeepsScreenOn);
            Assert.InRange(KeepAwakeService.Until!.Value, DateTimeOffset.Now.AddMinutes(59), DateTimeOffset.Now.AddMinutes(61));
        }
        finally { Assert.True(KeepAwakeService.Stop()); }
        Assert.False(KeepAwakeService.IsActive);
        Assert.False(KeepAwakeService.Stop());
    }
}

public class AppMatchingTests
{
    private static AppCandidate Proc(string name, string? path = null, string? friendly = null, string? title = null, bool host = false) =>
        new(name, path, friendly, title, host);

    [Fact]
    public void Matches_by_exact_path()
    {
        var app = new AppItem(@"C:\Tools\Foo\foo.exe", "Foo", @"C:\Tools\Foo\foo.exe");
        Assert.True(AppMatcher.Matches(app, Proc("foo", @"c:\tools\foo\FOO.EXE")));
        Assert.False(AppMatcher.Matches(app, Proc("bar", @"C:\Tools\Bar\bar.exe")));
    }

    [Fact]
    public void Matches_by_program_name_when_the_id_is_just_a_file_name()
    {
        Assert.True(AppMatcher.Matches(new AppItem("notepad.exe", "Notepad"), Proc("notepad")));
        Assert.True(AppMatcher.Matches(new AppItem("Spotify", "Spotify"), Proc("Spotify")));
        Assert.False(AppMatcher.Matches(new AppItem("notepad.exe", "Notepad"), Proc("notepad2")));
    }

    [Fact]
    public void Matches_store_apps_by_package_folder()
    {
        var calc = new AppItem("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator");
        Assert.True(AppMatcher.Matches(calc, Proc("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2.0.0_x64__8wekyb3d8bbwe\CalculatorApp.exe")));
        Assert.False(AppMatcher.Matches(calc, Proc("Other", @"C:\Program Files\WindowsApps\Microsoft.Other_1.0_x64__8wekyb3d8bbwe\Other.exe")));
        Assert.False(AppMatcher.Matches(calc, Proc("chrome", @"C:\Program Files\Google\Chrome\chrome.exe")));
    }

    [Fact]
    public void Matches_by_friendly_name_and_frame_host_title()
    {
        var app = new AppItem("whatever", "Google Chrome");
        Assert.True(AppMatcher.Matches(app, Proc("chrome", @"C:\x\chrome.exe", friendly: "Google Chrome")));
        Assert.True(AppMatcher.Matches(new AppItem("x!y", "Settings"), Proc("ApplicationFrameHost", null, title: "Settings", host: true)));
        Assert.False(AppMatcher.Matches(new AppItem("x!y", "Settings"), Proc("chrome", null, title: "Settings tips - Chrome")));
    }

    [Fact]
    public void Aumid_is_split_into_package_and_publisher()
    {
        var parsed = AppMatcher.ParseAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")!.Value;
        Assert.Equal("Microsoft.WindowsCalculator", parsed.Package);
        Assert.Equal("8wekyb3d8bbwe", parsed.Publisher);
    }

    [Fact]
    public void Installed_app_lookup_finds_by_id_name_exe_and_part_of_name()
    {
        var apps = new[]
        {
            new InstalledApp("{X}\\Notepad++\\notepad++.exe", "Notepad++", @"C:\Program Files\Notepad++\notepad++.exe", false, "x"),
            new InstalledApp("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator", null, true, "y"),
        };
        Assert.Equal("Calculator", InstalledApps.Find(apps, "calculator")!.Name);
        Assert.Equal("Calculator", InstalledApps.Find(apps, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")!.Name);
        Assert.Equal("Notepad++", InstalledApps.Find(apps, "notepad++.exe")!.Name);
        Assert.Equal("Notepad++", InstalledApps.Find(apps, @"C:\Program Files\Notepad++\notepad++.exe")!.Name);
        Assert.Equal("Calculator", InstalledApps.Find(apps, "Calc")!.Name);
        Assert.Null(InstalledApps.Find(apps, "zzzz"));
    }

    [Fact]
    public void Apps_folder_names_with_a_known_folder_guid_become_paths()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        // FOLDERID_Windows
        var path = InstalledApps.ResolvePath(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\System32\notepad.exe");
        Assert.Equal(Path.Combine(windows, @"System32\notepad.exe"), path, ignoreCase: true);
        Assert.Null(InstalledApps.ResolvePath("Microsoft.Windows.Explorer"));
    }
}

public class BrowserTests
{
    [Theory]
    [InlineData("example.com/page", "https://example.com/page")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("  http://example.com/a?b=1  ", "http://example.com/a?b=1")]
    [InlineData("localhost:3000/app", "http://localhost:3000/app")]
    [InlineData("192.168.1.1/admin", "http://192.168.1.1/admin")]
    [InlineData("chrome://settings", "chrome://settings")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("file:///C:/x.html", "file:///C:/x.html")]
    public void Address_bar_text_becomes_a_full_link(string raw, string expected) => Assert.Equal(expected, BrowserUrl.Normalize(raw));

    [Fact]
    public void Empty_address_is_null()
    {
        Assert.Null(BrowserUrl.Normalize(""));
        Assert.Null(BrowserUrl.Normalize(null));
    }

    [Theory]
    [InlineData("Flowmate - Google Chrome", "Flowmate")]
    [InlineData("Docs – Mozilla Firefox", "Docs")]
    [InlineData("News - Microsoft\u200b Edge", "News")]
    [InlineData("Page and 3 more pages - Personal - Microsoft\u200b Edge", "Page")]
    [InlineData("Plain title", "Plain title")]
    public void Browser_names_are_removed_from_titles(string title, string expected) => Assert.Equal(expected, BrowserUrl.CleanTitle(title));

    [Fact]
    public void Browsers_are_recognised()
    {
        Assert.True(BrowserUrl.IsBrowser("msedge"));
        Assert.True(BrowserUrl.IsBrowser("Firefox"));
        Assert.False(BrowserUrl.IsBrowser("notepad"));
    }
}

public class LocationLogicTests
{
    [Fact]
    public void Distance_is_great_circle()
    {
        Assert.Equal(343.5, GeoMath.DistanceKm(48.8566, 2.3522, 51.5074, -0.1278), 0); // Paris - London
        Assert.Equal(0, GeoMath.DistanceKm(10, 10, 10, 10), 6);
        Assert.Equal(111.19, GeoMath.DistanceKm(0, 0, 1, 0), 1);
    }

    [Theory]
    [InlineData("48.8584, 2.2945", 48.8584, 2.2945)]
    [InlineData("(48.8584,2.2945)", 48.8584, 2.2945)]
    [InlineData("-33.86 151.21", -33.86, 151.21)]
    [InlineData("48,8584; 2,2945", 48.8584, 2.2945)]
    public void Coordinates_are_parsed(string text, double lat, double lon)
    {
        var c = GeoMath.TryParseCoordinates(text);
        Assert.NotNull(c);
        Assert.Equal(lat, c!.Value.Lat, 4);
        Assert.Equal(lon, c.Value.Lon, 4);
    }

    [Theory]
    [InlineData("Paris")]
    [InlineData("91, 10")]
    [InlineData("10, 181")]
    [InlineData("1 2 3")]
    public void Addresses_are_not_coordinates(string text) => Assert.Null(GeoMath.TryParseCoordinates(text));

    [Fact]
    public void Nominatim_reverse_results_become_places()
    {
        const string json = """{"name":"","display_name":"10, Rue de Rivoli, Paris, France","address":{"house_number":"10","road":"Rue de Rivoli","city":"Paris","country":"France"}}""";
        var place = LocationParsers.ParseReverse(json, 48.85, 2.35)!;
        Assert.Equal("10 Rue de Rivoli, Paris", place.Name);
        Assert.Equal("Paris", place.City);
        Assert.Equal("France", place.Country);
        Assert.Equal(48.85, place.Latitude);
    }

    [Fact]
    public void Nominatim_errors_and_empty_searches_give_nothing()
    {
        Assert.Null(LocationParsers.ParseReverse("""{"error":"Unable to geocode"}""", 0, 0));
        Assert.Null(LocationParsers.ParseSearch("[]"));
        var hit = LocationParsers.ParseSearch("""[{"lat":"51.5073","lon":"-0.1276","name":"London","display_name":"London, England, United Kingdom","address":{"city":"London","country":"United Kingdom"}}]""")!;
        Assert.Equal(51.5073, hit.Latitude, 4);
        Assert.Equal("London", hit.Name);
    }

    [Fact]
    public void Ip_lookup_json_from_both_services_is_understood()
    {
        var a = LocationParsers.ParseIpLocation("""{"success":true,"latitude":52.37,"longitude":4.89,"city":"Amsterdam","country":"Netherlands"}""")!;
        Assert.Equal("Amsterdam", a.City);
        var b = LocationParsers.ParseIpLocation("""{"latitude":40.7,"longitude":-74.0,"city":"New York","country_name":"United States"}""")!;
        Assert.Equal("United States", b.Country);
        Assert.Null(LocationParsers.ParseIpLocation("""{"success":false,"message":"limit"}"""));
        Assert.Null(LocationParsers.ParseIpLocation("""{"error":true}"""));
    }

    [Fact]
    public void Map_links_are_built_for_each_service()
    {
        var spot = new LocationItem(48.8584, 2.2945);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=48.8584%2C2.2945", MapLinks.Build("google", "show", spot, null));
        Assert.Equal("https://www.google.com/maps/dir/?api=1&destination=Eiffel%20Tower", MapLinks.Build("google", "directions", null, "Eiffel Tower"));
        Assert.StartsWith("https://www.bing.com/maps?cp=48.8584~2.2945", MapLinks.Build("bing", "show", spot, null));
        Assert.Equal("https://www.bing.com/maps?q=Big%20Ben", MapLinks.Build("bing", "show", null, "Big Ben"));
        Assert.Equal("https://www.bing.com/maps?rtp=~pos.48.8584_2.2945", MapLinks.Build("bing", "directions", spot, null));
        Assert.Contains("mlat=48.8584&mlon=2.2945", MapLinks.Build("osm", "show", spot, null));
    }
}

public class SmallLogicTests
{
    [Fact]
    public void Colors_are_formatted()
    {
        Assert.Equal("#FF8000", ColorFormat.Hex(255, 128, 0));
        Assert.Equal("#000A0B", ColorFormat.Hex(0, 10, 11));
        Assert.Equal("255, 128, 0", ColorFormat.Rgb(255, 128, 0));
    }

    [Fact]
    public void Clipboard_is_only_wiped_when_it_still_holds_our_content()
    {
        Assert.True(CopyToClipboardAction.ShouldClear(42, 42));
        Assert.False(CopyToClipboardAction.ShouldClear(42, 43));
    }

    [Fact]
    public void PowerShell_commands_travel_encoded_so_quotes_survive()
    {
        var args = RunCommandAction.PowerShellArguments("Write-Output \"it's $env:USERNAME\"");
        Assert.StartsWith("-NoLogo -NoProfile -NonInteractive", args);
        var b64 = args[(args.IndexOf("-EncodedCommand ") + "-EncodedCommand ".Length)..];
        var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(b64));
        Assert.EndsWith("Write-Output \"it's $env:USERNAME\"", script);
    }

    [Theory]
    [InlineData("on", false, true)]
    [InlineData("off", true, false)]
    [InlineData("toggle", true, false)]
    [InlineData("toggle", false, true)]
    [InlineData("mute", false, true)]
    [InlineData("unmute", true, false)]
    public void On_off_toggle_choices_resolve(string choice, bool current, bool expected) =>
        Assert.Equal(expected, Toggle.Resolve(choice, current));
}
