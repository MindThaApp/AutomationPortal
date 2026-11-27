using AutomationPortal.Actions.Windows.Apps;
using NAudio.CoreAudioApi;

namespace AutomationPortal.Actions.Windows.Devices;

public sealed class SetVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.setVolume", Title = Strings.Get("SetVolumeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Speaker224",
        Keywords = new[] { "sound", "loud", "quiet", "audio", "speaker" },
        Summary = Strings.Get("SetVolumeAction_Summary"),
        Description = Strings.Get("SetVolumeAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("SetVolumeAction_OutputName"),
        Parameters = new[] { P.Percent("level", Strings.Get("SetVolumeAction_Param_Level_Label"), 50), AudioService.DeviceParam(DataFlow.Render) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 50));
        if (level is < 0 or > 100) throw new ActionException(Strings.Get("SetVolumeAction_Error_The_Volume_Has_To_Be"));
        var device = await ctx.GetChoiceAsync("device", "default");
        await Task.Run(() => AudioService.SetVolumePercent(DataFlow.Render, level, device), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(level);
    }
}

public sealed class ChangeVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.changeVolume", Title = Strings.Get("ChangeVolumeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Speaker124",
        Keywords = new[] { "louder", "quieter", "increase", "decrease", "sound" },
        Summary = Strings.Get("ChangeVolumeAction_Summary"),
        Description = Strings.Get("ChangeVolumeAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("ChangeVolumeAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", Strings.Get("ChangeVolumeAction_Param_Direction_Label"), "up", new Choice("up", Strings.Get("ChangeVolumeAction_Param_Direction_Choice_Up")), new Choice("down", Strings.Get("ChangeVolumeAction_Param_Direction_Choice_Down"))),
            ParameterDescriptor.Number("amount", Strings.Get("ChangeVolumeAction_Param_Amount_Label"), 10, 1, 100, "%") with { Step = 1 },
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
        Id = "dev.getVolume", Title = Strings.Get("GetVolumeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Speaker224",
        Keywords = new[] { "sound", "level", "loudness" },
        Summary = Strings.Get("GetVolumeAction_Summary"),
        Description = Strings.Get("GetVolumeAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("GetVolumeAction_OutputName"),
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
        ParameterDescriptor.ChoiceOf("state", noun, "mute", new Choice("mute", Strings.Get("MuteActionBase_Param_Amount_Choice_Mute")), new Choice("unmute", Strings.Get("MuteActionBase_Param_Amount_Choice_Unmute")), new Choice("toggle", Strings.Get("MuteActionBase_Param_Amount_Choice_Toggle")));
}

public sealed class MuteSoundAction : MuteActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.mute", Title = Strings.Get("MuteSoundAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "SpeakerMute24",
        Keywords = new[] { "silence", "unmute", "sound", "quiet" },
        Summary = Strings.Get("MuteSoundAction_Summary"),
        Description = Strings.Get("MuteSoundAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("MuteSoundAction_OutputName"),
        Parameters = new[] { State("Action"), AudioService.DeviceParam(DataFlow.Render) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Render;
}

public sealed class MuteMicrophoneAction : MuteActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.micMute", Title = Strings.Get("MuteMicrophoneAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "MicOff24",
        Keywords = new[] { "mic", "unmute", "call", "meeting", "silence" },
        Summary = Strings.Get("MuteMicrophoneAction_Summary"),
        Description = Strings.Get("MuteMicrophoneAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("MuteMicrophoneAction_OutputName"),
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
            throw new ActionException(Flow == DataFlow.Render ? Strings.Get("SetDefaultDeviceActionBase_Error_ChooseOutput") : Strings.Get("SetDefaultDeviceActionBase_Error_ChooseMicrophone"));
        return await Task.Run(() =>
        {
            var devices = AudioService.GetDevices(Flow);
            var match = devices.FirstOrDefault(d => d.Id == id)
                        ?? devices.FirstOrDefault(d => d.Name.Equals(id, StringComparison.OrdinalIgnoreCase))
                        ?? devices.FirstOrDefault(d => d.Name.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ActionException(Strings.Format("SetDefaultDeviceActionBase_Error_Couldn_T_Find_0_Check_It", id));
            AudioService.SetDefaultDevice(match.Id);
            return ContentValue.Text(match.Name);
        }, ctx.CancellationToken).ConfigureAwait(false);
    }
}

public sealed class SetSoundOutputAction : SetDefaultDeviceActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.audioOutput", Title = Strings.Get("SetSoundOutputAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Headphones24",
        Keywords = new[] { "speakers", "headphones", "audio", "switch", "default device" },
        Summary = Strings.Get("SetSoundOutputAction_Summary"),
        Description = Strings.Get("SetSoundOutputAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("SetSoundOutputAction_OutputName"),
        Parameters = new[] { AudioService.DeviceParam(DataFlow.Render, includeDefault: false) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Render;
}

public sealed class SetMicrophoneAction : SetDefaultDeviceActionBase
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.audioInput", Title = Strings.Get("SetMicrophoneAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Mic24",
        Keywords = new[] { "mic", "input", "headset", "audio", "switch", "default device" },
        Summary = Strings.Get("SetMicrophoneAction_Summary"),
        Description = Strings.Get("SetMicrophoneAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("SetMicrophoneAction_OutputName"),
        Parameters = new[] { AudioService.DeviceParam(DataFlow.Capture, includeDefault: false) },
    };
    public override ActionDescriptor Descriptor => D;
    protected override DataFlow Flow => DataFlow.Capture;
}

public sealed class SetAppVolumeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.appVolume", Title = Strings.Get("SetAppVolumeAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "SpeakerSettings24",
        Keywords = new[] { "app", "mixer", "mute app", "quieter", "spotify", "browser" },
        Summary = Strings.Get("SetAppVolumeAction_Summary"),
        Description = Strings.Get("SetAppVolumeAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("SetAppVolumeAction_OutputName"),
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("action", Strings.Get("SetAppVolumeAction_Param_Action_Label"), "set", new Choice("set", Strings.Get("SetAppVolumeAction_Param_Action_Choice_Set")), new Choice("mute", Strings.Get("SetAppVolumeAction_Param_Action_Choice_Mute")), new Choice("unmute", Strings.Get("SetAppVolumeAction_Param_Action_Choice_Unmute"))),
            P.Percent("level", Strings.Get("SetAppVolumeAction_Param_Level_Label"), 50) with { VisibleWhen = new VisibleWhen("action", "set") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.GetAppAsync() ?? throw new ActionException(Strings.Get("SetAppVolumeAction_Error_Choose_Which_App_To_Change"));
        var action = await ctx.GetChoiceAsync("action", "set");
        var level = (int)Math.Round(await ctx.GetNumberAsync("level", 50));
        var result = await Task.Run(() =>
        {
            var sessions = AudioService.FindSessions(app);
            if (sessions.Count == 0)
                throw new ActionException(Strings.Format("SetAppVolumeAction_Error_0_Isn_T_Using_Sound_Right", app.Name));
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
