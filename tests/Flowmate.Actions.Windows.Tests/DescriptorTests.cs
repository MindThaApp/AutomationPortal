using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Windows.Tests;

public class DescriptorTests
{
    private static readonly string[] ExpectedIds =
    {
        // Device & Settings
        "dev.setVolume", "dev.changeVolume", "dev.mute", "dev.getVolume", "dev.audioOutput", "dev.audioInput", "dev.appVolume", "dev.micMute",
        "dev.displayMode", "dev.resolution", "dev.hdr", "dev.monitorInput", "dev.brightness", "dev.theme", "dev.wallpaper", "dev.wifi",
        "dev.bluetooth", "dev.joinWifi", "dev.network", "dev.battery", "dev.details", "dev.powerMode", "dev.keepAwake", "dev.lock",
        "dev.sleep", "dev.power", "dev.screenOff", "dev.settings", "dev.recycleBin",
        // Apps & Windows
        "app.open", "app.quit", "app.isRunning", "app.list", "app.front", "app.switch", "app.waitFor", "app.openFile", "app.reveal",
        "win.arrange", "win.onTop", "win.showDesktop", "win.close", "win.list", "win.saveLayout", "win.restoreLayout",
        "text.getSelected", "web.currentPage", "adv.runCommand",
        // Keyboard & Mouse
        "input.type", "input.keys", "input.click", "input.moveMouse", "input.scroll",
        // Clipboard, Screen, Sound & Media, Location
        "clip.get", "clip.set", "clip.clear",
        "screen.capture", "screen.ocr", "screen.grabText", "screen.pickColor",
        "media.playPause", "media.next", "media.previous", "media.nowPlaying", "media.playSound", "media.speak", "media.dictate", "media.record",
        "loc.current", "loc.distance", "loc.maps",
    };

    private static ActionRegistry Registry()
    {
        var r = new ActionRegistry();
        r.RegisterAssembly(typeof(Flowmate.Actions.Windows.Devices.SetVolumeAction).Assembly);
        return r;
    }

    [Fact]
    public void Every_action_in_the_catalog_is_registered_without_clashes()
    {
        var r = Registry();
        Assert.Empty(r.Diagnostics);
        foreach (var id in ExpectedIds) Assert.True(r.TryGet(id, out _), $"Missing action {id}");
        Assert.Equal(ExpectedIds.Length, r.All.Count);
    }

    [Fact]
    public void Ids_do_not_clash_with_the_core_actions()
    {
        var all = ActionRegistry.CreateWithBuiltIns();
        var mine = Registry();
        Assert.All(mine.All, a => Assert.False(all.TryGet(a.Descriptor.Id, out _), a.Descriptor.Id));
    }

    [Fact]
    public void Descriptors_are_complete_and_consistent()
    {
        foreach (var action in Registry().All)
        {
            var d = action.Descriptor;
            var who = d.Id;
            Assert.False(string.IsNullOrWhiteSpace(d.Title), who);
            Assert.False(string.IsNullOrWhiteSpace(d.Icon), who);
            Assert.False(string.IsNullOrWhiteSpace(d.Summary), who);
            Assert.False(string.IsNullOrWhiteSpace(d.Description), who);
            Assert.Contains(d.Category, ActionCategories.All);
            Assert.NotEmpty(d.Keywords);
            if (d.HasOutput) Assert.False(string.IsNullOrWhiteSpace(d.OutputName), $"{who} needs an output name");

            var keys = d.Parameters.Select(p => p.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());

            foreach (var seg in d.SummarySegments.Where(s => s.IsToken))
                Assert.True(keys.Contains(seg.ParamKey!), $"{who}: Summary token {{{seg.ParamKey}}} is not a parameter");

            foreach (var p in d.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Label), $"{who}.{p.Key}");
                if (p.VisibleWhen is { } vw) Assert.True(keys.Contains(vw.Key), $"{who}.{p.Key} VisibleWhen points at unknown '{vw.Key}'");
                if (p.Type == ParamType.Choice)
                {
                    Assert.True(p.Choices is { Count: > 0 } || p.ChoicesProvider is not null, $"{who}.{p.Key} has no choices");
                    if (p.Choices is not null)
                    {
                        Assert.Equal(p.Choices.Count, p.Choices.Select(c => c.Id).Distinct().Count());
                        if (p.Default is ChoiceValue cv) Assert.Contains(p.Choices, c => c.Id == cv.Value);
                    }
                }
                if (p.Type == ParamType.App) Assert.NotNull(p.ChoicesProvider);
                if (p.Min is { } min && p.Max is { } max) Assert.True(min < max, $"{who}.{p.Key}");
            }
        }
    }

    [Fact]
    public void Descriptors_use_plain_english()
    {
        var banned = new[] { "regex", "hotkey", "directory", "HKCU", "WMI" };
        foreach (var d in Registry().All.Select(a => a.Descriptor))
        {
            var text = d.Title + " " + d.Summary + " " + d.Description + " " + string.Join(" ", d.Parameters.Select(p => p.Label));
            foreach (var word in banned) Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Destructive_and_advanced_flags_are_set()
    {
        var r = Registry();
        Assert.True(r.Get("dev.power")!.Descriptor.IsDestructive);
        Assert.True(r.Get("dev.recycleBin")!.Descriptor.IsDestructive);
        Assert.True(r.Get("adv.runCommand")!.Descriptor.IsAdvanced);
        Assert.Equal(ActionCategories.Advanced, r.Get("adv.runCommand")!.Descriptor.Category);
        Assert.All(r.All.Where(a => a.Descriptor.Id != "adv.runCommand"), a => Assert.False(a.Descriptor.IsAdvanced, a.Descriptor.Id));
    }

    [Fact]
    public void Power_actions_ask_first_by_default()
    {
        var power = Registry().Get("dev.power")!.Descriptor.FindParameter("askFirst")!;
        Assert.True(((BoolValue)power.Default!).Value);
        var bin = Registry().Get("dev.recycleBin")!.Descriptor.FindParameter("askFirst")!;
        Assert.True(((BoolValue)bin.Default!).Value);
    }

    [Fact]
    public void Macro_recorder_parameter_contract_is_honoured()
    {
        var r = Registry();
        var click = r.Get("input.click")!.Descriptor;
        Assert.Equal(ParamType.Number, click.FindParameter("x")!.Type);
        Assert.Equal(ParamType.Number, click.FindParameter("y")!.Type);
        var rel = click.FindParameter("relativeTo")!;
        Assert.Equal(new[] { "screen", "window" }, rel.Choices!.Select(c => c.Id).ToArray());
        Assert.Equal(ParamType.App, click.FindParameter("app")!.Type);
        Assert.Equal(new[] { "left", "right", "middle" }, click.FindParameter("button")!.Choices!.Select(c => c.Id).ToArray());
        Assert.Equal(ParamType.Number, click.FindParameter("clicks")!.Type);
        Assert.Equal(1, click.FindParameter("clicks")!.Min);
        Assert.Equal(3, click.FindParameter("clicks")!.Max);

        var keys = r.Get("input.keys")!.Descriptor;
        Assert.Equal(ParamType.Text, keys.FindParameter("keys")!.Type);
        Assert.Equal(ParamType.Number, keys.FindParameter("times")!.Type);

        var type = r.Get("input.type")!.Descriptor;
        Assert.NotNull(type.FindParameter("text"));
        Assert.Equal(ParamType.Number, type.FindParameter("delayMs")!.Type);
        Assert.Equal(0, ((NumberValue)type.FindParameter("delayMs")!.Default!).Value);

        var sw = r.Get("app.switch")!.Descriptor;
        Assert.Equal(ParamType.App, sw.FindParameter("app")!.Type);
    }
}
