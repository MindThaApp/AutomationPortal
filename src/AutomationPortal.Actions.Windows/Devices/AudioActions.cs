using AutomationPortal.Actions.Windows.Apps;
using NAudio.CoreAudioApi;

namespace AutomationPortal.Actions.Windows.Devices;

public sealed class SetVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.setVolume", Title = "Set Volume", Category = ActionCategories.DeviceAndSettings, Icon = "Speaker224",
        Keywords = new[] { "sound", "loud", "quiet", "audio", "speaker" },
        Summary = "Set volume to {level}",
        Description = "Sets the PC's sound volume to a percentage from 0 to 100.",
        OutputKind = ContentKind.Number, OutputName = "Volume",
        Parameters = new[] { P.Percent("level", "Volume", 50), AudioService.DeviceParam(DataFlow.Render) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 50));
        if (level is < 0 or > 100) throw new ActionException("The volume has to be between 0 and 100.");
        var device = await ctx.GetChoiceAsync("device", "default");
        await Task.Run(() => AudioService.SetVolumePercent(DataFlow.Render, level, device), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(level);
    }
}

public sealed class ChangeVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.changeVolume", Title = "Turn Volume Up/Down", Category = ActionCategories.DeviceAndSettings, Icon = "Speaker124",
        Keywords = new[] { "louder", "quieter", "increase", "decrease", "sound" },
        Summary = "Turn volume {direction} by {amount}",
        Description = "Makes the sound louder or quieter by a number of percentage points.",
        OutputKind = ContentKind.Number, OutputName = "Volume",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", "Direction", "up", new Choice("up", "up"), new Choice("down", "down")),
            ParameterDescriptor.Number("amount", "By", 10, 1, 100, "%") with { Step = 1 },
            AudioService.DeviceParam(DataFlow.Render),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var amount = (int)Math.Round(await ctx.GetNumberAsync("amount", 10));
        var down = await ctx.GetChoiceAsync("direction", "up") == "down";
        var device = await ctx.GetChoiceAsync("device", "default");
        var result = await Task.Run(() =>
        {
            var now = AudioService.GetVolumePercent(DataFlow.Render, device);
            var next = Math.Clamp(now + (down ? -amount : amount), 0, 100);
            AudioService.SetVolumePercent(DataFlow.Render, next, device);
            return next;
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(result);
    }
}

public sealed class GetVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.getVolume", Title = "Get Volume", Category = ActionCategories.DeviceAndSettings, Icon = "Speaker224",
        Keywords = new[] { "sound", "level", "loudness" },
        Summary = "Get the volume",
        Description = "Gets the PC's current sound volume as a percentage (0 to 100).",
        OutputKind = ContentKind.Number, OutputName = "Volume",
        Parameters = new[] { AudioService.DeviceParam(DataFlow.Render) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var device = await ctx.GetChoiceAsync("device", "default");
        return ContentValue.Number(await Task.Run(() => AudioService.GetVolumePercent(DataFlow.Render, device), ctx.CancellationToken).ConfigureAwait(false));
    }
}

public abstract class MuteActionBase : ShortcutAction
{
    protected abstract DataFlow Flow { get; }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var state = await ctx.GetChoiceAsync("state", "toggle");
        var device = await ctx.GetChoiceAsync("device", "default");
        var muted = await Task.Run(() =>
        {
            var target = Toggle.Resolve(state, AudioService.GetMute(Flow, device));
            AudioService.SetMute(Flow, target, device);
            return target;
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Bool(muted);
    }

    protected static ParameterDescriptor State(string noun) =>
        ParameterDescriptor.ChoiceOf("state", noun, "mute", new Choice("mute", "Mute"), new Choice("unmute", "Unmute"), new Choice("toggle", "Switch between muted and not"));
}

public sealed class MuteSoundAction : MuteActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.mute", Title = "Mute Sound", Category = ActionCategories.DeviceAndSettings, Icon = "SpeakerMute24",
        Keywords = new[] { "silence", "unmute", "sound", "quiet" },
        Summary = "{state} the sound",
        Description = "Mutes or unmutes the PC's sound. Gives back true when the sound is muted afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "Is Muted",
        Parameters = new[] { State("Action"), AudioService.DeviceParam(DataFlow.Render) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Render;
}

public sealed class MuteMicrophoneAction : MuteActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.micMute", Title = "Mute Microphone", Category = ActionCategories.DeviceAndSettings, Icon = "MicOff24",
        Keywords = new[] { "mic", "unmute", "call", "meeting", "silence" },
        Summary = "{state} the microphone",
        Description = "Mutes or unmutes the microphone for the whole PC. Gives back true when the microphone is muted afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "Is Muted",
        Parameters = new[] { State("Action"), AudioService.DeviceParam(DataFlow.Capture) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Capture;
}

public abstract class SetDefaultDeviceActionBase : ShortcutAction
{
    protected abstract DataFlow Flow { get; }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var id = await ctx.GetChoiceAsync("device");
        if (string.IsNullOrWhiteSpace(id) || id == "default")
            throw new ActionException(Flow == DataFlow.Render ? "Choose which sound output device to switch to." : "Choose which microphone to switch to.");
        return await Task.Run(() =>
        {
            var devices = AudioService.GetDevices(Flow);
            var match = devices.FirstOrDefault(d => d.Id == id)
                        ?? devices.FirstOrDefault(d => d.Name.Equals(id, StringComparison.OrdinalIgnoreCase))
                        ?? devices.FirstOrDefault(d => d.Name.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ActionException($"Couldn't find '{id}'. Check it is plugged in and turned on, then choose it again.");
            AudioService.SetDefaultDevice(match.Id);
            return ContentValue.Text(match.Name);
        }, ctx.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class SetSoundOutputAction : SetDefaultDeviceActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.audioOutput", Title = "Set Sound Output Device", Category = ActionCategories.DeviceAndSettings, Icon = "Headphones24",
        Keywords = new[] { "speakers", "headphones", "audio", "switch", "default device" },
        Summary = "Play sound through {device}",
        Description = "Switches where the PC plays sound (speakers, headphones, a monitor, a Bluetooth speaker…).",
        OutputKind = ContentKind.Text, OutputName = "Sound Output",
        Parameters = new[] { AudioService.DeviceParam(DataFlow.Render, includeDefault: false) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Render;
}

public sealed class SetMicrophoneAction : SetDefaultDeviceActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.audioInput", Title = "Set Microphone", Category = ActionCategories.DeviceAndSettings, Icon = "Mic24",
        Keywords = new[] { "mic", "input", "headset", "audio", "switch", "default device" },
        Summary = "Use {device} as the microphone",
        Description = "Switches which microphone the PC listens to.",
        OutputKind = ContentKind.Text, OutputName = "Microphone",
        Parameters = new[] { AudioService.DeviceParam(DataFlow.Capture, includeDefault: false) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Capture;
}

public sealed class SetAppVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.appVolume", Title = "Set App Volume", Category = ActionCategories.DeviceAndSettings, Icon = "SpeakerSettings24",
        Keywords = new[] { "app", "mixer", "mute app", "quieter", "spotify", "browser" },
        Summary = "{action} {app}",
        Description = "Changes the volume of one app, or mutes it, without touching the rest of the PC's sound. The app must be open and have played sound (Windows only lists those).",
        OutputKind = ContentKind.Number, OutputName = "App Volume",
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("action", "Action", "set", new Choice("set", "Set volume of"), new Choice("mute", "Mute"), new Choice("unmute", "Unmute")),
            P.Percent("level", "Volume", 50) with { VisibleWhen = new VisibleWhen("action", "set") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.GetAppAsync() ?? throw new ActionException("Choose which app to change the volume of.");
        var action = await ctx.GetChoiceAsync("action", "set");
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 50));
        var result = await Task.Run(() =>
        {
            var sessions = AudioService.FindSessions(app);
            if (sessions.Count == 0)
                throw new ActionException($"{app.Name} isn't using sound right now. Open it and play something, then try again. Windows only lists apps that have played sound.");
            foreach (var (s, _, _) in sessions)
            {
                switch (action)
                {
                    case "mute": s.SimpleAudioVolume.Mute = true; break;
                    case "unmute": s.SimpleAudioVolume.Mute = false; break;
                    default: s.SimpleAudioVolume.Volume = Math.Clamp(level, 0, 100) / 100f; break;
                }
            }
            return (int)Math.Round(sessions[0].Session.SimpleAudioVolume.Volume * 100);
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(result);
    }
}
