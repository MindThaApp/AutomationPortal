using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public sealed class BluetoothTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.bluetooth", Title = Strings.Get("Trigger_DeviceBluetooth_Title"), Category = TP.CatDevices, Icon = "Bluetooth24",
        Summary = Strings.Get("Trigger_DeviceBluetooth_Summary"),
        Description = Strings.Get("Trigger_DeviceBluetooth_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_DeviceBluetooth_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", Strings.Get("Trigger_DeviceBluetooth_Param_Device_Label"), async (_, ct) =>
                new[] { new Choice("", Strings.Get("Trigger_DeviceBluetooth_Param_Device_Choice_X")) }.Concat((await DeviceWatch.PairedBluetoothNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
            ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_DeviceBluetooth_Param_Event_Label"), "connected", new Choice("connected", Strings.Get("Trigger_DeviceBluetooth_Param_Event_Choice_Connected")), new Choice("disconnected", Strings.Get("Trigger_DeviceBluetooth_Param_Event_Choice_Disconnected"))),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var wanted = config.GetChoice("device", "").Trim();
        var connect = config.GetChoice("event", "connected") != "disconnected";
        var watch = DeviceWatch.Bluetooth();
        watch.Changed += e =>
        {
            if (e.Connected != connect) return;
            if (wanted.Length > 0 && !string.Equals(wanted, e.Name, StringComparison.OrdinalIgnoreCase)) return;
            sink.Fire(ContentValue.Text(e.Name), connect ? $"{e.Name} connected" : $"{e.Name} disconnected");
        };
        watch.Start();
        return watch;
    }
}

public sealed class UsbDeviceTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.usb", Title = Strings.Get("Trigger_DeviceUsb_Title"), Category = TP.CatDevices, Icon = "UsbPlug24",
        Summary = Strings.Get("Trigger_DeviceBluetooth_Summary"),
        Description = Strings.Get("Trigger_DeviceUsb_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_DeviceBluetooth_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", Strings.Get("Trigger_DeviceUsb_Param_Device_Label"), async (_, ct) =>
                new[] { new Choice("", Strings.Get("Trigger_DeviceUsb_Param_Device_Choice_X")) }.Concat((await DeviceWatch.UsbNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
            ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_DeviceUsb_Param_Event_Label"), "connected", new Choice("connected", Strings.Get("Trigger_DeviceUsb_Param_Event_Choice_Connected")), new Choice("disconnected", Strings.Get("Trigger_DeviceUsb_Param_Event_Choice_Disconnected"))),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var wanted = config.GetChoice("device", "").Trim();
        var connect = config.GetChoice("event", "connected") != "disconnected";
        var watch = DeviceWatch.Usb();
        watch.Changed += e =>
        {
            if (e.Connected != connect) return;
            if (wanted.Length > 0 && !string.Equals(wanted, e.Name, StringComparison.OrdinalIgnoreCase)) return;
            sink.Fire(ContentValue.Text(e.Name), connect ? Strings.Format("Trigger_Device_Fired_PluggedIn", e.Name) : Strings.Format("Trigger_Device_Fired_Unplugged", e.Name));
        };
        watch.Start();
        return watch;
    }
}

public sealed class DisplayTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.display", Title = Strings.Get("Trigger_DeviceDisplay_Title"), Category = TP.CatDevices, Icon = "Desktop24",
        Summary = Strings.Get("Trigger_DeviceDisplay_Summary"),
        Description = Strings.Get("Trigger_DeviceDisplay_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_DeviceDisplay_OutputDescription"),
        Parameters = new[] { ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_DeviceDisplay_Param_Event_Label"), "connected", new Choice("connected", Strings.Get("Trigger_DeviceDisplay_Param_Event_Choice_Connected")), new Choice("disconnected", Strings.Get("Trigger_DeviceDisplay_Param_Event_Choice_Disconnected"))) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var connect = config.GetChoice("event", "connected") != "disconnected";
        void OnChange((int Old, int New) c)
        {
            if (connect ? c.New <= c.Old : c.New >= c.Old) return;
            sink.Fire(ContentValue.Number(c.New), connect ? Strings.Get("Trigger_DeviceDisplay_Fired_AMonitorWasConnected") : Strings.Get("Trigger_DeviceDisplay_Fired_AMonitorWasDisconnected"));
        }
        return TP.Watch(env.Displays, () => env.Displays.Changed += OnChange, () => env.Displays.Changed -= OnChange);
    }
}

public sealed class AudioOutputTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.audio", Title = Strings.Get("Trigger_DeviceAudio_Title"), Category = TP.CatDevices, Icon = "Speaker224",
        Summary = Strings.Get("Trigger_DeviceAudio_Summary"),
        Description = Strings.Get("Trigger_DeviceAudio_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_DeviceAudio_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", Strings.Get("Trigger_DeviceAudio_Param_Device_Label"), (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(new[] { new Choice("", Strings.Get("Trigger_DeviceAudio_Param_Device_Choice_X")) }.Concat(AudioOutputMonitor.OutputNames().Select(n => new Choice(n, n))).ToList()), ""),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var wanted = config.GetChoice("device", "").Trim();
        var monitor = new AudioOutputMonitor();
        void OnChange(string name)
        {
            if (wanted.Length > 0 && !string.Equals(wanted, name, StringComparison.OrdinalIgnoreCase)) return;
            sink.Fire(ContentValue.Text(name), Strings.Format("Trigger_Audio_Fired_SoundGoingTo", name));
        }
        var sub = monitor.Subscribe();
        monitor.OutputChanged += OnChange;
        return new DisposableAction(() => { monitor.OutputChanged -= OnChange; sub.Dispose(); monitor.Dispose(); });
    }
}

// ---- keyboard --------------------------------------------------------------------------------

public sealed class KeyShortcutTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "key.shortcut", Title = Strings.Get("Trigger_KeyShortcut_Title"), Category = TP.CatKeyboard, Icon = "Keyboard24",
        Summary = Strings.Get("Trigger_KeyShortcut_Summary"),
        Description = Strings.Get("Trigger_KeyShortcut_Description"),
        OutputKind = ContentKind.App, OutputDescription = Strings.Get("Trigger_KeyShortcut_OutputDescription"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "hotkey", Label = Strings.Get("Trigger_KeyShortcut_Param_Hotkey_Label"), Type = ParamType.Hotkey, Required = true, AllowVariables = false, AllowAskEachTime = false },
            TP.AppParam("onlyIn", Strings.Get("Trigger_KeyShortcut_Param_OnlyIn_Label"), required: false),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var hotkeys = env.Hotkeys ?? throw new InvalidOperationException(Strings.Get("Trigger_Hotkey_Error_Unavailable"));
        var hotkey = config.GetString("hotkey");
        if (string.IsNullOrWhiteSpace(hotkey)) throw new InvalidOperationException(Strings.Get("Trigger_Hotkey_Error_Choose"));
        var only = config.GetApp("onlyIn");
        if (only is not null && string.IsNullOrWhiteSpace(only.Id) && string.IsNullOrWhiteSpace(only.Name)) only = null;
        var owner = "trigger:" + Guid.NewGuid().ToString("N");
        var pretty = HotkeyParser.Normalize(hotkey) ?? hotkey;
        var result = hotkeys.Register(owner, hotkey, () =>
        {
            var front = AppInfo.Foreground();
            sink.Fire(front is null ? ContentValue.Empty : ContentValue.From(front.ToItem()), Strings.Format("Trigger_Hotkey_Fired_YouPressed", pretty));
        }, "an automation", only);
        if (!result.Success) throw new InvalidOperationException(result.Reason);
        return new DisposableAction(() => hotkeys.Unregister(owner));
    }
}

public sealed class KeyTypedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "key.typed", Title = Strings.Get("Trigger_KeyTyped_Title"), Category = TP.CatKeyboard, Icon = "TextT24",
        Summary = Strings.Get("Trigger_KeyTyped_Summary"),
        Description = Strings.Get("Trigger_KeyTyped_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_KeyTyped_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("phrase", Strings.Get("Trigger_KeyTyped_Param_Phrase_Label"), null, Strings.Get("Trigger_KeyTyped_Param_Phrase_Placeholder")) with { Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Bool("replace", Strings.Get("Trigger_KeyTyped_Param_Replace_Label")),
            ParameterDescriptor.Bool("matchCase", Strings.Get("Trigger_KeyTyped_Param_MatchCase_Label")),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var service = env.TypedPhrases ?? throw new InvalidOperationException(Strings.Get("Trigger_Typed_Error_Unavailable"));
        var phrase = config.GetString("phrase");
        if (phrase.Trim().Length < 2) throw new InvalidOperationException(Strings.Get("Trigger_Typed_Error_TooShort"));
        var replace = config.GetBool("replace");
        var matchCase = config.GetBool("matchCase");
        return service.Register(phrase, matchCase, async match =>
        {
            var description = Strings.Format("Trigger_Typed_Fired_YouTyped", phrase);
            if (replace && sink is ITriggerSinkExtras extras)
            {
                var result = await extras.FireAndWaitAsync(ContentValue.Text(match.Typed), description);
                var text = result?.AsText();
                if (!string.IsNullOrEmpty(text)) await service.ReplaceTypedAsync(match, text);
            }
            else sink.Fire(ContentValue.Text(match.Typed), description);
        });
    }
}

// ---- clipboard -------------------------------------------------------------------------------

public sealed class ClipboardChangedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "clip.changed", Title = Strings.Get("Trigger_ClipChanged_Title"), Category = TP.CatClipboard, Icon = "Clipboard24",
        Summary = Strings.Get("Trigger_ClipChanged_Summary"),
        Description = Strings.Get("Trigger_ClipChanged_Description"),
        OutputKind = ContentKind.Any, OutputDescription = Strings.Get("Trigger_ClipChanged_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("kind", Strings.Get("Trigger_ClipChanged_Param_Kind_Label"), "any", new Choice("any", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Any")), new Choice("text", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Text")), new Choice("link", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Link")),
                new Choice("image", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Image")), new Choice("files", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Files")), new Choice("contains", Strings.Get("Trigger_ClipChanged_Param_Kind_Choice_Contains"))),
            ParameterDescriptor.Text("text", Strings.Get("Trigger_ClipChanged_Param_Text_Label"), null, Strings.Get("Trigger_ClipChanged_Param_Text_Placeholder")) with { VisibleWhen = new VisibleWhen("kind", "contains"), AllowVariables = false, AllowAskEachTime = false },
        },
    };
    public override TriggerDescriptor Descriptor => D;

    internal static bool IsLink(string? text) =>
        text is not null && !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    /// <summary>Does what was copied match the chosen kind? Pure, for tests.</summary>
    internal static bool Accepts(string kind, string? contains, string? text, int fileCount, bool hasImage) => kind switch
    {
        "text" => text is not null,
        "link" => IsLink(text?.Trim()),
        "image" => hasImage,
        "files" => fileCount > 0,
        "contains" => text is not null && !string.IsNullOrEmpty(contains) && text.Contains(contains, StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var kind = config.GetChoice("kind", "any");
        var contains = config.GetString("text");
        void OnClip(ClipboardSnapshot s)
        {
            if (!Accepts(kind, contains, s.Text, s.Files.Count, s.ImagePath is not null)) return;
            if (s.Files.Count > 0) sink.Fire(ContentValue.Files(s.Files), s.Files.Count == 1 ? Strings.Get("Trigger_ClipChanged_Fired_YouCopiedAFile") : Strings.Format("Trigger_Clip_Fired_CopiedFiles", s.Files.Count));
            else if (s.ImagePath is not null) sink.Fire(ContentValue.Image(s.ImagePath), Strings.Get("Trigger_ClipChanged_Fired_YouCopiedAPicture"));
            else if (IsLink(s.Text?.Trim())) sink.Fire(ContentValue.Url(s.Text!.Trim()), Strings.Get("Trigger_ClipChanged_Fired_YouCopiedALink"));
            else sink.Fire(ContentValue.Text(s.Text ?? ""), Strings.Get("Trigger_ClipChanged_Fired_YouCopiedSomeText"));
        }
        return TP.Watch(env.Clipboard, () => env.Clipboard.Changed += OnClip, () => env.Clipboard.Changed -= OnClip);
    }
}
