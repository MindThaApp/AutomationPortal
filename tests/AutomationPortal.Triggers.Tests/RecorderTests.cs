using AutomationPortal.Core;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class RecorderStepBuilderTests
{
    private static readonly RecordedWindow Notepad = new(1, "C:\\Windows\\notepad.exe", "Notepad", 100, 200, 800, 600);
    private static readonly RecordedWindow Excel = new(2, "C:\\Office\\excel.exe", "Excel", 0, 0, 1000, 700);

    private DateTime _t = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private readonly RecorderStepBuilder _b;

    public RecorderStepBuilderTests() => _b = new RecorderStepBuilder();

    private void Wait(double seconds) => _t += TimeSpan.FromSeconds(seconds);

    private void Type(string text, RecordedWindow? w = null, double gap = 0.1)
    {
        foreach (var c in text)
        {
            Wait(gap);
            var shift = char.IsUpper(c) || "!@#".Contains(c);
            _b.Add(new RecordedKey(_t, w ?? Notepad, char.ToUpperInvariant(c), shift ? HotkeyModifiers.Shift : HotkeyModifiers.None, c.ToString()));
        }
    }

    private void Press(int vk, HotkeyModifiers mods = HotkeyModifiers.None, string text = "", RecordedWindow? w = null, double gap = 0.1)
    {
        Wait(gap);
        _b.Add(new RecordedKey(_t, w ?? Notepad, vk, mods, text));
    }

    private void Click(int x, int y, string button = "left", RecordedWindow? w = null, double gap = 0.1)
    {
        Wait(gap);
        _b.Add(new RecordedClick(_t, w ?? Notepad, x, y, button));
    }

    private static string Id(ActionInstance a) => a.ActionId;

    [Fact]
    public void Keys_typed_in_a_password_box_are_never_stored_and_become_an_Ask_for_Input_step()
    {
        Type("user");
        foreach (var c in "hunter2")
        {
            Wait(0.1);
            _b.Add(new RecordedKey(_t, Notepad, char.ToUpperInvariant(c), HotkeyModifiers.None, "", Secret: true));
        }
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type", "ui.ask", "input.type" }, steps.Select(Id));
        Assert.DoesNotContain("hunter2", string.Concat(steps.SelectMany(s => s.Parameters.Values).Select(v => v.ToLiteralText())));
        Assert.False(steps[3].Parameters.ContainsKey("text"));
    }

    [Fact]
    public void Consecutive_printable_keys_become_one_Type_Text_step()
    {
        Type("Hello world");
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type" }, steps.Select(Id));
        Assert.Equal("Hello world", steps[1].Parameters["text"].ToLiteralText());
        Assert.Equal(20, ((NumberValue)steps[1].Parameters["delayMs"]).Value);
    }

    [Fact]
    public void A_short_pause_while_typing_does_not_split_the_text()
    {
        Type("Hi ");
        Wait(1.5);
        Type("there");
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type" }, steps.Select(Id));
        Assert.Equal("Hi there", steps[1].Parameters["text"].ToLiteralText());
    }

    [Fact]
    public void A_long_pause_while_typing_inserts_a_wait()
    {
        Type("Hi");
        Wait(3.0);
        Type("there");
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type", "flow.wait", "input.type" }, steps.Select(Id));
        Assert.Equal(3.1, ((DurationValue)steps[2].Parameters["seconds"]).Seconds, 1);
    }

    [Fact]
    public void Pauses_over_half_a_second_become_wait_steps_and_shorter_ones_do_not()
    {
        Click(10, 10);
        Click(300, 300, gap: 0.4);
        Click(500, 500, gap: 1.26);
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.click", "input.click", "flow.wait", "input.click" }, steps.Select(Id));
        Assert.Equal(1.3, ((DurationValue)steps[3].Parameters["seconds"]).Seconds, 1);
    }

    [Fact]
    public void Waits_can_be_dropped()
    {
        var b = new RecorderStepBuilder(new RecorderOptions { DropWaits = true });
        b.Add(new RecordedClick(_t, Notepad, 10, 10, "left"));
        b.Add(new RecordedClick(_t.AddSeconds(5), Notepad, 300, 300, "left"));
        Assert.DoesNotContain(b.Finish(), s => s.ActionId == "flow.wait");
    }

    [Fact]
    public void Waits_can_be_switched_off_while_recording()
    {
        var b = new RecorderStepBuilder();
        b.Add(new RecordedClick(_t, Notepad, 10, 10, "left"));
        b.Options.DropWaits = true;
        b.Add(new RecordedClick(_t.AddSeconds(5), Notepad, 300, 300, "left"));
        Assert.DoesNotContain(b.Finish(), s => s.ActionId == "flow.wait");
    }

    [Fact]
    public void Time_spent_paused_is_not_a_wait_and_pending_typing_is_finished()
    {
        Type("Hi");
        _b.Pause();
        Wait(30);
        Click(5, 5);
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type", "input.click" }, steps.Select(Id));
    }

    [Fact]
    public void Keys_with_modifiers_are_recorded_as_Press_Keys()
    {
        Press('T', HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.keys" }, steps.Select(Id));
        Assert.Equal("Ctrl+Shift+T", steps[1].Parameters["keys"].ToLiteralText());
        Assert.Equal(1, ((NumberValue)steps[1].Parameters["times"]).Value);
    }

    [Fact]
    public void Special_keys_are_Press_Keys_and_split_the_typing()
    {
        Type("abc");
        Press(0x0D);
        Type("def");
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type", "input.keys", "input.type" }, steps.Select(Id));
        Assert.Equal("Enter", steps[2].Parameters["keys"].ToLiteralText());
    }

    [Fact]
    public void A_repeated_special_key_is_merged_into_times()
    {
        Press(0x08); Press(0x08); Press(0x08);
        var steps = _b.Finish();
        Assert.Equal("Backspace", steps[1].Parameters["keys"].ToLiteralText());
        Assert.Equal(3, ((NumberValue)steps[1].Parameters["times"]).Value);
    }

    [Fact]
    public void Shift_and_AltGr_characters_count_as_typing_but_Ctrl_and_Alt_do_not()
    {
        Type("Ab!");
        Press('@', HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, text: "@");
        Press('A', HotkeyModifiers.Ctrl, text: "");
        var steps = _b.Finish();
        Assert.Equal("input.type", steps[1].ActionId);
        Assert.Equal("Ab!@", steps[1].Parameters["text"].ToLiteralText());
        Assert.Equal("Ctrl+A", steps[2].Parameters["keys"].ToLiteralText());
    }

    [Fact]
    public void Clicks_are_relative_to_the_window_with_the_app_attached()
    {
        Click(150, 260);
        var steps = _b.Finish();
        var click = steps.Single(s => s.ActionId == "input.click");
        Assert.Equal(50, ((NumberValue)click.Parameters["x"]).Value);
        Assert.Equal(60, ((NumberValue)click.Parameters["y"]).Value);
        Assert.Equal("window", ((ChoiceValue)click.Parameters["relativeTo"]).Value);
        Assert.Equal("left", ((ChoiceValue)click.Parameters["button"]).Value);
        Assert.Equal(1, ((NumberValue)click.Parameters["clicks"]).Value);
        var app = Assert.IsType<AppValue>(click.Parameters["app"]);
        Assert.Equal("Notepad", app.Name);
        Assert.Equal("C:\\Windows\\notepad.exe", app.Id);
    }

    [Fact]
    public void Clicks_with_no_known_window_use_screen_coordinates()
    {
        _b.Add(new RecordedClick(_t, null, 640, 480, "right"));
        var click = _b.Finish().Single();
        Assert.Equal("input.click", click.ActionId);
        Assert.Equal("screen", ((ChoiceValue)click.Parameters["relativeTo"]).Value);
        Assert.Equal(640, ((NumberValue)click.Parameters["x"]).Value);
        Assert.Equal("right", ((ChoiceValue)click.Parameters["button"]).Value);
        Assert.False(click.Parameters.ContainsKey("app"));
    }

    [Fact]
    public void Quick_clicks_in_one_spot_become_a_double_or_triple_click()
    {
        Click(150, 260); Click(151, 261, gap: 0.2);
        Click(400, 400, gap: 1);
        Click(150, 260, gap: 2); Click(150, 260, gap: 0.2); Click(150, 260, gap: 0.2); Click(150, 260, gap: 0.2);
        var clicks = _b.Finish().Where(s => s.ActionId == "input.click").Select(s => (int)((NumberValue)s.Parameters["clicks"]).Value).ToList();
        Assert.Equal(new[] { 2, 1, 3, 1 }, clicks);
    }

    [Fact]
    public void Different_buttons_or_places_are_separate_clicks()
    {
        Click(150, 260); Click(150, 260, "right", gap: 0.2); Click(250, 260, gap: 0.2);
        Assert.Equal(3, _b.Finish().Count(s => s.ActionId == "input.click"));
    }

    [Fact]
    public void Changing_app_inserts_Switch_to_App_steps()
    {
        Click(150, 260);
        Click(30, 40, w: Excel, gap: 0.2);
        Type("42", Excel);
        Click(200, 300, w: Notepad, gap: 0.2);
        var steps = _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.click", "app.switch", "input.click", "input.type", "app.switch", "input.click" }, steps.Select(Id));
        Assert.Equal("Excel", ((AppValue)steps[2].Parameters["app"]).Name);
    }

    [Fact]
    public void The_app_is_only_switched_when_it_actually_changes()
    {
        Click(150, 260); Click(300, 300, gap: 0.2); Type("x"); Press(0x0D);
        Assert.Single(_b.Finish(), s => s.ActionId == "app.switch");
    }

    [Fact]
    public void A_wait_comes_before_the_app_switch_it_belongs_to()
    {
        Click(150, 260);
        Click(30, 40, w: Excel, gap: 2);
        var ids = _b.Finish().Select(Id).ToList();
        Assert.Equal(new[] { "app.switch", "input.click", "flow.wait", "app.switch", "input.click" }, ids);
    }

    [Fact]
    public void StepRecorded_fires_when_a_step_is_finished()
    {
        var seen = new List<string>();
        _b.StepRecorded += s => seen.Add(s.ActionId);
        Type("hi");
        Assert.Equal(new[] { "app.switch" }, seen);
        Click(10, 10);
        Assert.Equal(new[] { "app.switch", "input.type" }, seen);
        _b.Finish();
        Assert.Equal(new[] { "app.switch", "input.type", "input.click" }, seen);
    }

    [Fact]
    public void Steps_includes_the_step_still_being_built()
    {
        Type("hi");
        Assert.Equal(new[] { "app.switch", "input.type" }, _b.Steps.Select(Id));
        Type("!");
        Assert.Equal("hi!", _b.Steps.Last().Parameters["text"].ToLiteralText());
    }

    [Fact]
    public void Every_step_gets_a_unique_id()
    {
        Type("a"); Press(0x0D); Click(1, 1); Click(500, 500, gap: 2);
        var steps = _b.Finish();
        Assert.Equal(steps.Count, steps.Select(s => s.Uid).Distinct().Count());
    }

    [Fact]
    public void An_empty_recording_is_empty() => Assert.Empty(_b.Finish());

    [Fact]
    public void Recorded_steps_use_only_the_parameter_keys_of_the_playback_contract()
    {
        Type("x"); Press(0x0D); Click(1, 1); Click(9, 9, gap: 2);
        foreach (var s in _b.Finish())
        {
            var allowed = s.ActionId switch
            {
                "input.click" => new[] { "x", "y", "relativeTo", "app", "button", "clicks" },
                "input.keys" => new[] { "keys", "times" },
                "input.type" => new[] { "text", "delayMs" },
                "flow.wait" => new[] { "seconds" },
                "app.switch" => new[] { "app" },
                _ => throw new Xunit.Sdk.XunitException("unexpected action " + s.ActionId),
            };
            Assert.All(s.Parameters.Keys, k => Assert.Contains(k, allowed));
        }
    }
}
