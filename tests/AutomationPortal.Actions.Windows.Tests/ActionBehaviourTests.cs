using Flowmate.Actions.Windows.Apps;
using Flowmate.Actions.Windows.Clipboard;
using Flowmate.Actions.Windows.Devices;
using Flowmate.Actions.Windows.Input;
using Flowmate.Actions.Windows.Location;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Windows.Tests;

/// <summary>
/// Parameter checks and "Ask first" behaviour. Every case here stops before anything on the PC is touched
/// (no volume, display, power, keyboard or mouse changes).
/// </summary>
public class ActionBehaviourTests
{
    private static async Task<ActionException> Fails(ShortcutAction action, params (string, ParamValue)[] parameters)
    {
        using var host = new TestHost();
        return await Assert.ThrowsAsync<ActionException>(() => ActionTestHarness.RunAsync(host, action, parameters));
    }

    [Fact]
    public async Task Volume_must_be_between_0_and_100() =>
        Assert.Contains("between 0 and 100", (await Fails(new SetVolumeAction(), ("level", 150))).Message);

    [Fact]
    public async Task Brightness_must_be_between_0_and_100() =>
        Assert.Contains("between 0 and 100", (await Fails(new SetBrightnessAction(), ("level", -5))).Message);

    [Fact]
    public async Task Unknown_display_mode_is_refused_before_anything_changes() =>
        Assert.Contains("PC screen only", (await Fails(new SetDisplayModeAction(), ("mode", ParamValue.Choice("bogus")))).Message);

    [Fact]
    public async Task Resolution_needs_a_real_size() =>
        Assert.Contains("1920 x 1080", (await Fails(new SetResolutionAction(), ("resolution", ParamValue.Choice("big")))).Message);

    [Fact]
    public async Task Monitor_input_rejects_unknown_inputs() =>
        Assert.Contains("scart", (await Fails(new SwitchMonitorInputAction(), ("input", ParamValue.Choice("scart")))).Message);

    [Fact]
    public async Task Sound_device_must_be_chosen() =>
        Assert.Contains("Choose which sound output", (await Fails(new SetSoundOutputAction())).Message);

    [Fact]
    public async Task Power_mode_rejects_unknown_modes() =>
        Assert.Contains("turbo", (await Fails(new SetPowerModeAction(), ("mode", ParamValue.Choice("turbo")))).Message);

    [Fact]
    public async Task Settings_page_rejects_unknown_pages() =>
        Assert.Contains("nonsense", (await Fails(new OpenSettingsPageAction(), ("page", ParamValue.Choice("nonsense")))).Message);

    [Fact]
    public async Task Power_action_rejects_unknown_actions_without_asking()
    {
        using var host = new TestHost();
        await Assert.ThrowsAsync<ActionException>(() => ActionTestHarness.RunAsync(host, new PowerAction(), ("action", ParamValue.Choice("explode"))));
        Assert.Empty(host.UI.Calls);
    }

    [Fact]
    public async Task Power_action_asks_first_and_cancelling_stops_everything()
    {
        using var host = new TestHost();
        host.UI.Enqueue(false);
        await Assert.ThrowsAsync<UserCancelledException>(() => ActionTestHarness.RunAsync(host, new PowerAction(), ("action", ParamValue.Choice("shutdown"))));
        Assert.Equal(new[] { "alert" }, host.UI.Calls);
        Assert.Contains("Shut down", host.UI.Prompts.Single());
    }

    [Fact]
    public async Task Recycle_bin_asks_first_and_cancelling_stops_everything()
    {
        using var host = new TestHost();
        host.UI.Enqueue(false);
        await Assert.ThrowsAsync<UserCancelledException>(() => ActionTestHarness.RunAsync(host, new EmptyRecycleBinAction()));
        Assert.Equal(new[] { "alert" }, host.UI.Calls);
    }

    [Fact]
    public async Task Sleep_can_ask_first_too()
    {
        using var host = new TestHost();
        host.UI.Enqueue(false);
        await Assert.ThrowsAsync<UserCancelledException>(() => ActionTestHarness.RunAsync(host, new SleepAction(), ("askFirst", true)));
    }

    // ---- keyboard & mouse (validation only) --------------------------------------------------

    [Fact]
    public async Task Press_keys_explains_unknown_keys()
    {
        var ex = await Fails(new PressKeysAction(), ("keys", "Ctrl+Banana"));
        Assert.Contains("Banana", ex.Message);
    }

    [Fact]
    public async Task Press_keys_needs_keys() =>
        Assert.Contains("keys to press", (await Fails(new PressKeysAction())).Message);

    [Fact]
    public async Task Type_text_needs_text() =>
        Assert.Contains("no text", (await Fails(new TypeTextAction())).Message);

    [Fact]
    public async Task Click_needs_both_coordinates_or_none() =>
        Assert.Contains("both", (await Fails(new ClickAction(), ("x", 100))).Message);

    [Fact]
    public async Task Click_in_a_window_of_an_app_that_is_not_open_explains_it()
    {
        var ex = await Fails(new ClickAction(), ("x", 10), ("y", 10), ("relativeTo", ParamValue.Choice("window")), ("app", ParamValue.App("NoSuchAppZZ", "NoSuchAppZZ")));
        Assert.Contains("NoSuchAppZZ", ex.Message);
        Assert.Contains("isn't open", ex.Message);
    }

    [Fact]
    public async Task Click_in_a_window_needs_an_app() =>
        Assert.Contains("Choose which app", (await Fails(new ClickAction(), ("x", 10), ("y", 10), ("relativeTo", ParamValue.Choice("window")))).Message);

    [Fact]
    public async Task Move_mouse_needs_a_position() =>
        Assert.Contains("where to move", (await Fails(new MoveMouseAction())).Message);

    [Fact]
    public async Task Failures_inside_a_shortcut_carry_the_friendly_message()
    {
        using var host = new TestHost().With(typeof(PressKeysAction).Assembly);
        var result = await ActionTestHarness.RunInShortcutAsync(host, "input.keys", null, ("keys", "Ctrl+Banana"));
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("Banana", result.ErrorMessage);
    }

    // ---- apps & windows ----------------------------------------------------------------------

    [Fact]
    public async Task Switching_to_an_app_that_is_not_open_explains_it()
    {
        var ex = await Fails(new SwitchToAppAction(), ("app", ParamValue.App("NoSuchAppZZ", "No Such App ZZ")));
        Assert.Contains("No Such App ZZ", ex.Message);
        Assert.Contains("isn't open", ex.Message);
    }

    [Fact]
    public async Task Switching_needs_an_app() =>
        Assert.Contains("Choose an app", (await Fails(new SwitchToAppAction())).Message);

    [Fact]
    public async Task Waiting_for_an_app_gives_up_with_a_message()
    {
        var ex = await Fails(new WaitForAppAction(), ("app", ParamValue.App("NoSuchAppZZ", "No Such App ZZ")), ("timeout", ParamValue.Duration(1)));
        Assert.Contains("didn't open", ex.Message);
    }

    [Fact]
    public async Task Layout_actions_validate_names_and_never_touch_windows_when_the_layout_is_empty()
    {
        Assert.Contains("name", (await Fails(new SaveWindowLayoutAction(), ("name", " "))).Message);
        Assert.Contains("no saved layout", (await Fails(new RestoreWindowLayoutAction(), ("name", ParamValue.Choice("Nope")))).Message);

        using var host = new TestHost();
        new WindowLayoutStore(host.Services.Paths.RoamingRoot).Save(new SavedLayout { Name = "Empty" });
        var restored = await ActionTestHarness.RunAsync(host, new RestoreWindowLayoutAction(), ("name", ParamValue.Choice("empty")), ("launchMissing", false));
        Assert.Equal(0, restored.AsNumber());
    }

    [Fact]
    public async Task Layout_choices_list_the_saved_layouts()
    {
        using var host = new TestHost();
        var store = new WindowLayoutStore(host.Services.Paths.RoamingRoot);
        store.Save(new SavedLayout { Name = "B" });
        store.Save(new SavedLayout { Name = "A" });
        var choices = await new RestoreWindowLayoutAction().Descriptor.FindParameter("name")!.ChoicesProvider!(host.Services, CancellationToken.None);
        Assert.Equal(new[] { "A", "B" }, choices.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task Open_files_needs_files() =>
        Assert.Contains("nothing to open", (await Fails(new OpenFilesAction())).Message);

    [Fact]
    public async Task Open_files_reports_missing_files()
    {
        var ex = await Fails(new OpenFilesAction(), ("files", ParamValue.File(@"C:\definitely\not\here.txt")));
        Assert.Contains("here.txt", ex.Message);
    }

    // ---- clipboard (through the fake clipboard service) --------------------------------------

    [Fact]
    public async Task Clipboard_actions_copy_read_and_clear()
    {
        using var host = new TestHost();
        var copied = await ActionTestHarness.RunAsync(host, new CopyToClipboardAction(), ("content", "secret"));
        Assert.Equal("secret", copied.AsText());
        Assert.Equal("secret", host.Clipboard.Value.AsText());

        var read = await ActionTestHarness.RunAsync(host, new GetClipboardAction());
        Assert.Equal("secret", read.AsText());

        host.Clipboard.Value = ContentValue.Number(12.5);
        var asText = await ActionTestHarness.RunAsync(host, new GetClipboardAction(), ("as", ParamValue.Choice("text")));
        Assert.Equal(ContentKind.Text, asText[0].Kind);

        await ActionTestHarness.RunAsync(host, new ClearClipboardAction());
        Assert.True(host.Clipboard.Value.IsEmpty);
        Assert.True((await ActionTestHarness.RunAsync(host, new GetClipboardAction())).IsEmpty);
    }

    [Fact]
    public async Task Copying_nothing_is_an_error() =>
        Assert.Contains("nothing to copy", (await Fails(new CopyToClipboardAction(), ("content", ""))).Message);

    [Fact]
    public async Task Copy_can_clear_the_clipboard_after_a_delay()
    {
        using var host = new TestHost();
        await ActionTestHarness.RunAsync(host, new CopyToClipboardAction(), ("content", "temp"), ("clearAfter", ParamValue.Duration(1)));
        Assert.Equal("temp", host.Clipboard.Value.AsText());
        for (var i = 0; i < 40 && !host.Clipboard.Value.IsEmpty; i++) await Task.Delay(100);
        Assert.True(host.Clipboard.Value.IsEmpty, "the clipboard should have been wiped after one second");
    }

    // ---- location (no network) ---------------------------------------------------------------

    [Fact]
    public async Task Distance_between_two_coordinates_is_worked_out_offline()
    {
        using var host = new TestHost();
        var km = await ActionTestHarness.RunAsync(host, new GetDistanceAction(), ("from", "48.8566, 2.3522"), ("to", "51.5074, -0.1278"));
        Assert.InRange(km.AsNumber()!.Value, 343, 345);
        var miles = await ActionTestHarness.RunAsync(host, new GetDistanceAction(), ("from", "48.8566, 2.3522"), ("to", "51.5074, -0.1278"), ("unit", ParamValue.Choice("miles")));
        Assert.InRange(miles.AsNumber()!.Value, 213, 214);
    }

    [Fact]
    public async Task Distance_accepts_location_values()
    {
        using var host = new TestHost();
        var previousOutput = ContentValue.From(new LocationItem(0, 1));
        var result = await ActionTestHarness.RunAsync(host, new GetDistanceAction(), previousOutput, ("from", "0, 0"));
        Assert.InRange(result.AsNumber()!.Value, 111, 112);
    }

    [Fact]
    public async Task Distance_needs_a_destination() =>
        Assert.Contains("where to measure", (await Fails(new GetDistanceAction(), ("from", "1, 1"))).Message);

    [Fact]
    public async Task Run_command_needs_a_command() =>
        Assert.Contains("command", (await Fails(new Flowmate.Actions.Windows.Advanced.RunCommandAction())).Message);
}
