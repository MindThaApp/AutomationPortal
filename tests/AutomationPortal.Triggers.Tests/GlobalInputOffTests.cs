using AutomationPortal.Triggers;
using AutomationPortal.Triggers.Platform;

namespace AutomationPortal.Triggers.Tests;

/// <summary>With global input switched off (test harnesses), nothing may register a hotkey or install a hook.</summary>
public class GlobalInputOffTests
{
    [Fact]
    public void Hotkeys_register_nothing()
    {
        using var hub = new SystemEventHub();
        using var hotkeys = new HotkeyService(hub, globalInputDisabled: true);
        var result = hotkeys.Register("owner", "Ctrl+Alt+F11", () => { });
        Assert.True(result.Success);
        Assert.False(hotkeys.IsRegistered("owner"));
    }

    [Fact]
    public void Typed_phrases_install_no_keyboard_hook()
    {
        using var phrases = new TypedPhraseService(null, globalInputDisabled: true);
        using var reg = phrases.Register(";off", false, _ => Task.CompletedTask);
        Assert.False(phrases.IsHookInstalled);
    }

    [Fact]
    public void Recorders_refuse_to_start_with_a_clear_message()
    {
        using var macro = new MacroRecorder(globalInputDisabled: true);
        var ex = Assert.Throws<InvalidOperationException>(() => macro.Start());
        Assert.Contains("global input is turned off", ex.Message);
        Assert.False(macro.IsRecording);

        using var app = new AppRecorder(new RecordTarget(1, "App", "Window"), null, globalInputDisabled: true);
        ex = Assert.Throws<InvalidOperationException>(() => app.Start());
        Assert.Contains("global input is turned off", ex.Message);
        Assert.False(app.IsRecording);
    }
}
