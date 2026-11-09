using System.Drawing;
using System.Drawing.Imaging;
using AutomationPortal.Actions.Windows.Advanced;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Actions.Windows.Devices;
using AutomationPortal.Actions.Windows.Media;
using AutomationPortal.Actions.Windows.Screen;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Windows.Tests;

/// <summary>Runs read-only actions against the real PC. Nothing is changed: no volume, display, clipboard, keys or clicks.</summary>
[Trait("Category", "Live")]
public class LiveTests
{
    [Fact]
    public async Task Get_volume_reads_the_current_level()
    {
        using var host = new TestHost();
        var level = await ActionTestHarness.RunAsync(host, new GetVolumeAction());
        Assert.InRange(level.AsNumber()!.Value, 0, 100);
    }

    [Fact]
    public async Task Battery_level_is_a_percentage_or_a_friendly_no_battery_message()
    {
        using var host = new TestHost();
        try
        {
            var level = await ActionTestHarness.RunAsync(host, new GetBatteryAction());
            Assert.InRange(level.AsNumber()!.Value, 0, 100);
        }
        catch (ActionException ex) { Assert.Contains("doesn't have a battery", ex.Message); }
        var all = await ActionTestHarness.RunAsync(host, new GetBatteryAction(), ("info", ParamValue.Choice("all")));
        Assert.Equal(ContentKind.Dictionary, all[0].Kind);
        Assert.NotNull(all.AsDictionary()!.Get("Plugged In").AsBool());
    }

    [Fact]
    public async Task Network_details_say_whether_we_are_online()
    {
        using var host = new TestHost();
        var all = (await ActionTestHarness.RunAsync(host, new GetNetworkDetailsAction())).AsDictionary()!;
        Assert.True(all.TryGet("Connected", out _));
        Assert.True(all.TryGet("Type", out var type));
        Assert.False(string.IsNullOrWhiteSpace(type.AsText()));
        var online = await ActionTestHarness.RunAsync(host, new GetNetworkDetailsAction(), ("info", ParamValue.Choice("online")));
        Assert.Equal(ContentKind.Boolean, online[0].Kind);
    }

    [Fact]
    public async Task Device_details_include_the_pc_name_and_windows_version()
    {
        using var host = new TestHost();
        var d = (await ActionTestHarness.RunAsync(host, new GetDeviceDetailsAction())).AsDictionary()!;
        Assert.Equal(Environment.MachineName, d.Get("Device Name").AsText());
        Assert.Contains("Windows", d.Get("Windows").AsText());
        Assert.True(d.Get("Memory (GB)").AsNumber() > 0);
        var name = await ActionTestHarness.RunAsync(host, new GetDeviceDetailsAction(), ("info", ParamValue.Choice("name")));
        Assert.Equal(Environment.MachineName, name.AsText());
    }

    [Fact]
    public async Task Installed_apps_are_listed_with_names_and_ids()
    {
        var apps = await InstalledApps.GetInstalledAppsAsync(forceRefresh: true);
        Assert.True(apps.Count > 5, $"found {apps.Count} apps");
        Assert.All(apps, a => { Assert.False(string.IsNullOrWhiteSpace(a.Id)); Assert.False(string.IsNullOrWhiteSpace(a.Name)); });
        Assert.Contains(apps, a => a.IsStoreApp);
        Assert.Contains(apps, a => a.Path is not null && File.Exists(a.Path));
        var cached = await InstalledApps.GetInstalledAppsAsync();
        Assert.Same(apps, cached);
    }

    [Fact]
    public async Task App_choices_provider_returns_choices()
    {
        using var host = new TestHost();
        var choices = await InstalledApps.ChoicesProvider(host.Services, CancellationToken.None);
        Assert.NotEmpty(choices);
        Assert.All(choices, c => Assert.False(string.IsNullOrEmpty(c.Id)));
    }

    [Fact]
    public async Task An_installed_app_has_an_icon()
    {
        var apps = await InstalledApps.GetInstalledAppsAsync();
        var withIcon = apps.Take(15).Select(a => Sta.Run(() => AutomationPortal.Host.Wpf.WpfAppIcons.Load(a, 32)).Result).Count(i => i is not null);
        Assert.True(withIcon > 0, "at least one of the first apps should have an icon");
    }

    [Fact]
    public async Task Running_apps_include_explorer_and_the_front_app_is_known()
    {
        using var host = new TestHost();
        var apps = await ActionTestHarness.RunAsync(host, new GetRunningAppsAction());
        Assert.NotEmpty(apps.Items);
        Assert.All(apps.Items, i => Assert.Equal(ContentKind.App, i.Kind));

        var explorer = await ActionTestHarness.RunAsync(host, new IsAppRunningAction(), ("app", ParamValue.App("explorer.exe", "File Explorer")));
        Assert.True(explorer.AsBool());
        var nothing = await ActionTestHarness.RunAsync(host, new IsAppRunningAction(), ("app", ParamValue.App("NoSuchAppZZ.exe", "No Such App ZZ")));
        Assert.False(nothing.AsBool());

        try
        {
            var front = await ActionTestHarness.RunAsync(host, new GetFrontAppAction());
            Assert.Equal(ContentKind.App, front[0].Kind);
        }
        catch (ActionException ex) { Assert.Contains("no app window", ex.Message); }
    }

    [Fact]
    public async Task Window_list_gives_titles_and_apps()
    {
        using var host = new TestHost();
        var windows = await ActionTestHarness.RunAsync(host, new GetWindowsAction());
        Assert.All(windows.Items, w =>
        {
            var win = Assert.IsType<WindowItem>(w);
            Assert.False(string.IsNullOrWhiteSpace(win.Title));
            Assert.NotEqual(0, win.Handle);
        });
    }

    [Fact]
    public async Task Saving_a_layout_records_the_open_windows_into_the_json_file()
    {
        using var host = new TestHost();
        var count = await ActionTestHarness.RunAsync(host, new SaveWindowLayoutAction(), ("name", "Live test"));
        var store = new WindowLayoutStore(host.Services.Paths.RoamingRoot);
        var layout = store.Find("live test")!;
        Assert.Equal((int)count.AsNumber()!.Value, layout.Windows.Count);
        Assert.All(layout.Windows, w => { Assert.True(w.Width > 0 && w.Height > 0); Assert.Contains(w.State, new[] { "normal", "maximized", "minimized" }); });
    }

    [Fact]
    public async Task Now_playing_answers_even_when_nothing_plays()
    {
        using var host = new TestHost();
        var info = await ActionTestHarness.RunAsync(host, new GetNowPlayingAction());
        Assert.Equal(ContentKind.Dictionary, info[0].Kind);
        Assert.True(info.AsDictionary()!.TryGet("Playing", out _));
    }

    [Fact]
    public async Task Current_web_page_gives_a_link_or_a_friendly_message()
    {
        using var host = new TestHost();
        try
        {
            var page = await ActionTestHarness.RunAsync(host, new GetCurrentWebPageAction());
            Assert.True(page.AsText().Length > 0);
        }
        catch (ActionException ex) { Assert.True(ex.Message.Contains("browser") || ex.Message.Contains("address")); }
    }

    // ---- screen ------------------------------------------------------------------------------

    [Fact]
    public async Task Screenshots_are_saved_as_png_in_the_temp_folder()
    {
        using var host = new TestHost();
        var all = await ActionTestHarness.RunAsync(host, new TakeScreenshotAction(), ("area", ParamValue.Choice("all")));
        var image = Assert.IsType<ImageItem>(all[0]);
        Assert.True(File.Exists(image.Path));
        Assert.StartsWith(host.Root, image.Path);
        Assert.True(image.Width > 100 && image.Height > 100);
        using (var bmp = new Bitmap(image.Path)) Assert.Equal(image.Width, bmp.Width);

        host.UI.Enqueue(new ScreenRect(0, 0, 200, 120));
        var region = await ActionTestHarness.RunAsync(host, new TakeScreenshotAction(), ("area", ParamValue.Choice("select")));
        Assert.Equal(200, ((ImageItem)region[0]).Width);
        Assert.Equal(120, ((ImageItem)region[0]).Height);
        Assert.Contains("region", host.UI.Calls);

        var main = await ActionTestHarness.RunAsync(host, new TakeScreenshotAction());
        Assert.True(File.Exists(((ImageItem)main[0]).Path));
    }

    [Fact]
    public async Task The_front_window_can_be_captured()
    {
        using var host = new TestHost();
        try
        {
            var shot = await ActionTestHarness.RunAsync(host, new TakeScreenshotAction(), ("area", ParamValue.Choice("window")));
            Assert.True(File.Exists(((ImageItem)shot[0]).Path));
        }
        catch (ActionException ex) { Assert.Contains("window", ex.Message); }
    }

    [Fact]
    public async Task Cancelling_the_area_selection_ends_quietly()
    {
        using var host = new TestHost();
        host.UI.EnqueueCancel();
        await Assert.ThrowsAsync<UserCancelledException>(() => ActionTestHarness.RunAsync(host, new TakeScreenshotAction(), ("area", ParamValue.Choice("select"))));
    }

    [Fact]
    public async Task Pick_color_reads_a_pixel_as_hex_or_rgb()
    {
        using var host = new TestHost();
        host.UI.Enqueue(new ScreenPoint(5, 5));
        var hex = await ActionTestHarness.RunAsync(host, new PickColorAction());
        Assert.Matches("^#[0-9A-F]{6}$", hex.AsText());
        host.UI.Enqueue(new ScreenPoint(5, 5));
        var rgb = await ActionTestHarness.RunAsync(host, new PickColorAction(), ("format", ParamValue.Choice("rgb")));
        Assert.Matches(@"^\d{1,3}, \d{1,3}, \d{1,3}$", rgb.AsText());
    }

    [Fact]
    public async Task Ocr_reads_text_from_a_generated_picture()
    {
        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        var path = Path.Combine(host.Root, "ocr.png");
        using (var bmp = new Bitmap(1100, 260))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Arial", 56, FontStyle.Bold);
            g.DrawString("Hello Automation Portal", font, Brushes.Black, 30, 20);
            g.DrawString("Invoice 2026", font, Brushes.Black, 30, 130);
            bmp.Save(path, ImageFormat.Png);
        }

        var text = (await ActionTestHarness.RunAsync(host, new GetTextFromImageAction(), ("image", ParamValue.File(path)))).AsText();
        Assert.Contains("Hello", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automation Portal", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", text);
        Assert.Contains("\n", text);
    }

    [Fact]
    public async Task Ocr_on_a_missing_picture_is_a_friendly_error()
    {
        using var host = new TestHost();
        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            ActionTestHarness.RunAsync(host, new GetTextFromImageAction(), ("image", ParamValue.File(Path.Combine(host.Root, "nope.png")))));
        Assert.Contains("nope.png", ex.Message);
    }

    [Fact]
    public void Ocr_languages_are_listed()
    {
        Assert.NotEmpty(OcrService.Languages());
    }

    // ---- devices & sound (read-only checks) --------------------------------------------------

    [Fact]
    public void Monitors_are_found_with_work_areas_inside_their_bounds()
    {
        var monitors = DisplayService.GetMonitors();
        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.All(monitors, m =>
        {
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0);
            Assert.Equal(m.WorkArea, m.WorkArea.Intersect(m.Bounds));
            Assert.NotNull(DisplayService.CurrentMode(m.DeviceName));
            Assert.NotEmpty(DisplayService.SupportedModes(m.DeviceName));
        });
        Assert.All(DisplayService.GetActiveTargets(), t => _ = DisplayService.GetHdr(t)); // reading only
    }

    [Fact]
    public async Task Display_choices_are_offered()
    {
        var choices = await DisplayService.MonitorChoicesAsync(includeAll: true, CancellationToken.None);
        Assert.Equal("all", choices[0].Id);
        Assert.True(choices.Count >= 2);
    }

    [Fact]
    public void Audio_devices_and_sessions_can_be_listed()
    {
        var outputs = AudioService.GetDevices(NAudio.CoreAudioApi.DataFlow.Render);
        Assert.NotEmpty(outputs);
        Assert.Single(outputs, d => d.IsDefault);
        _ = AudioService.GetDevices(NAudio.CoreAudioApi.DataFlow.Capture);
        _ = AudioService.FindSessions(new AppItem("explorer.exe", "File Explorer")); // lists sessions only
    }

    [Fact]
    public async Task Audio_choices_start_with_the_current_device()
    {
        using var host = new TestHost();
        var choices = await AudioService.DeviceParam(NAudio.CoreAudioApi.DataFlow.Render).ChoicesProvider!(host.Services, CancellationToken.None);
        Assert.Equal("default", choices[0].Id);
        Assert.True(choices.Count >= 2);
    }

    [Fact]
    public async Task Speech_can_be_made_in_memory_without_playing_it()
    {
        using var ms = await SpeechService.SynthesizeAsync("Testing one two three", "default", 1.0, CancellationToken.None);
        var header = new byte[4];
        await ms.ReadExactlyAsync(header);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header));
        Assert.True(ms.Length > 10_000);
        var voices = await new SpeakTextAction().Descriptor.FindParameter("voice")!.ChoicesProvider!(new TestHost().Services, CancellationToken.None);
        Assert.True(voices.Count >= 2);
    }

    [Fact]
    public void Windows_sounds_can_be_opened_without_playing_them()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
        if (!File.Exists(path)) return;
        using var stream = AudioPlayer.OpenFile(path);
        Assert.True(stream.TotalTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Windows_sound_and_voice_lists_are_offered()
    {
        using var host = new TestHost();
        var sounds = await new PlaySoundAction().Descriptor.FindParameter("sound")!.ChoicesProvider!(host.Services, CancellationToken.None);
        Assert.NotEmpty(sounds);
        Assert.All(sounds, s => Assert.EndsWith(".wav", s.Id));
    }

    [Fact]
    public async Task Saved_wifi_names_and_ocr_languages_are_listed_without_error()
    {
        _ = await NetworkService.GetSavedWifiNamesAsync(CancellationToken.None);
        using var host = new TestHost();
        var langs = await new GetTextFromImageAction().Descriptor.FindParameter("language")!.ChoicesProvider!(host.Services, CancellationToken.None);
        Assert.Equal("auto", langs[0].Id);
    }

    // ---- commands (harmless echo) ------------------------------------------------------------

    [Fact]
    public async Task Run_command_captures_powershell_output()
    {
        using var host = new TestHost();
        var result = await ActionTestHarness.RunAsync(host, new RunCommandAction(), ("command", "Write-Output \"it's café\""));
        Assert.Equal("it's café", result.AsText());
    }

    [Fact]
    public async Task Run_command_captures_cmd_output_and_reports_failures()
    {
        using var host = new TestHost();
        var ok = await ActionTestHarness.RunAsync(host, new RunCommandAction(), ("shell", ParamValue.Choice("cmd")), ("command", "echo hello world"));
        Assert.Equal("hello world", ok.AsText());

        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            ActionTestHarness.RunAsync(host, new RunCommandAction(), ("shell", ParamValue.Choice("cmd")), ("command", "exit 3")));
        Assert.Contains("exit code 3", ex.Message);

        var tolerated = await ActionTestHarness.RunAsync(host, new RunCommandAction(), ("shell", ParamValue.Choice("cmd")), ("command", "echo partial & exit 2"), ("failOnError", false));
        Assert.Equal("partial", tolerated.AsText().Trim());
    }

    [Fact]
    public async Task Run_command_stops_commands_that_take_too_long()
    {
        using var host = new TestHost();
        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            ActionTestHarness.RunAsync(host, new RunCommandAction(), ("shell", ParamValue.Choice("cmd")), ("command", "ping -n 20 127.0.0.1 >nul"), ("timeout", ParamValue.Duration(1))));
        Assert.Contains("still running after 1 seconds", ex.Message);
    }

    [Fact]
    public async Task Run_command_runs_in_the_chosen_folder()
    {
        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        var result = await ActionTestHarness.RunAsync(host, new RunCommandAction(), ("shell", ParamValue.Choice("cmd")), ("command", "cd"), ("folder", ParamValue.File(host.Root)));
        Assert.Equal(host.Root, result.AsText().Trim(), ignoreCase: true);
    }
}
