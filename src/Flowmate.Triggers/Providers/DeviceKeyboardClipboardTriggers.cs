using Flowmate.Core;

namespace Flowmate.Triggers;

public sealed class BluetoothTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.bluetooth", Title = "When a Bluetooth device connects", Category = TP.CatDevices, Icon = "Bluetooth24",
        Summary = "When {device} {event}",
        Description = "Runs when a Bluetooth device such as headphones, a speaker or a mouse connects or disconnects. Only devices you have already paired with this PC appear in the list.",
        OutputKind = ContentKind.Text, OutputDescription = "The device name",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", "Device", async (_, ct) =>
                new[] { new Choice("", "any Bluetooth device") }.Concat((await DeviceWatch.PairedBluetoothNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
            ParameterDescriptor.ChoiceOf("event", "Event", "connected", new Choice("connected", "connects"), new Choice("disconnected", "disconnects")),
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
        Id = "device.usb", Title = "When a USB device connects", Category = TP.CatDevices, Icon = "UsbPlug24",
        Summary = "When {device} {event}",
        Description = "Runs when a USB device such as a keyboard, camera, headset or game controller is plugged in or unplugged.",
        OutputKind = ContentKind.Text, OutputDescription = "The device name",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", "Device", async (_, ct) =>
                new[] { new Choice("", "any USB device") }.Concat((await DeviceWatch.UsbNamesAsync(ct)).Select(n => new Choice(n, n))).ToList(), ""),
            ParameterDescriptor.ChoiceOf("event", "Event", "connected", new Choice("connected", "is plugged in"), new Choice("disconnected", "is unplugged")),
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
            sink.Fire(ContentValue.Text(e.Name), connect ? $"{e.Name} was plugged in" : $"{e.Name} was unplugged");
        };
        watch.Start();
        return watch;
    }
}

public sealed class DisplayTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.display", Title = "When a monitor is connected", Category = TP.CatDevices, Icon = "Desktop24",
        Summary = "When a monitor is {event}",
        Description = "Runs when you plug in or unplug a monitor, projector or TV.",
        OutputKind = ContentKind.Number, OutputDescription = "How many monitors are connected now",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("event", "Event", "connected", new Choice("connected", "connected"), new Choice("disconnected", "disconnected")) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var connect = config.GetChoice("event", "connected") != "disconnected";
        void OnChange((int Old, int New) c)
        {
            if (connect ? c.New <= c.Old : c.New >= c.Old) return;
            sink.Fire(ContentValue.Number(c.New), connect ? "A monitor was connected" : "A monitor was disconnected");
        }
        return TP.Watch(env.Displays, () => env.Displays.Changed += OnChange, () => env.Displays.Changed -= OnChange);
    }
}

public sealed class AudioOutputTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "device.audio", Title = "When the sound output changes", Category = TP.CatDevices, Icon = "Speaker224",
        Summary = "When the sound output changes to {device}",
        Description = "Runs when Windows switches to another speaker or headphones, or when you plug headphones in.",
        OutputKind = ContentKind.Text, OutputDescription = "The name of the sound output",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("device", "Output", (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(new[] { new Choice("", "any output") }.Concat(AudioOutputMonitor.OutputNames().Select(n => new Choice(n, n))).ToList()), ""),
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
            sink.Fire(ContentValue.Text(name), $"Sound is now going to {name}");
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
        Id = "key.shortcut", Title = "A keyboard shortcut", Category = TP.CatKeyboard, Icon = "Keyboard24",
        Summary = "When I press {hotkey}",
        Description = "Runs when you press a key combination anywhere in Windows. You can limit it to one app. While an app is limited, the keys still work normally in other apps.",
        OutputKind = ContentKind.App, OutputDescription = "The app that was in front when you pressed the keys",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "hotkey", Label = "Keyboard shortcut", Type = ParamType.Hotkey, Required = true, AllowVariables = false, AllowAskEachTime = false },
            TP.AppParam("onlyIn", "Only when this app is in front", required: false),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var hotkeys = env.Hotkeys ?? throw new InvalidOperationException("Keyboard shortcuts aren't available right now.");
        var hotkey = config.GetString("hotkey");
        if (string.IsNullOrWhiteSpace(hotkey)) throw new InvalidOperationException("Choose a keyboard shortcut for this trigger.");
        var only = config.GetApp("onlyIn");
        if (only is not null && string.IsNullOrWhiteSpace(only.Id) && string.IsNullOrWhiteSpace(only.Name)) only = null;
        var owner = "trigger:" + Guid.NewGuid().ToString("N");
        var pretty = HotkeyParser.Normalize(hotkey) ?? hotkey;
        var result = hotkeys.Register(owner, hotkey, () =>
        {
            var front = AppInfo.Foreground();
            sink.Fire(front is null ? ContentValue.Empty : ContentValue.From(front.ToItem()), $"You pressed {pretty}");
        }, "an automation", only);
        if (!result.Success) throw new InvalidOperationException(result.Reason);
        return new DisposableAction(() => hotkeys.Unregister(owner));
    }
}

public sealed class KeyTypedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "key.typed", Title = "When I type a phrase", Category = TP.CatKeyboard, Icon = "TextT24",
        Summary = "When I type {phrase}",
        Description = "Runs when you type a phrase in any app (for example \";sig\"). Turn on \"Replace\" to swap the phrase for the text the shortcut gives back, which is text expansion with a shortcut. Nothing is saved, and it never works in password boxes.",
        OutputKind = ContentKind.Text, OutputDescription = "The phrase you typed",
        Parameters = new[]
        {
            ParameterDescriptor.Text("phrase", "Phrase", null, ";sig") with { Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Bool("replace", "Replace the phrase with the shortcut's text result"),
            ParameterDescriptor.Bool("matchCase", "Match upper and lower case exactly"),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var service = env.TypedPhrases ?? throw new InvalidOperationException("Typed phrases aren't available right now.");
        var phrase = config.GetString("phrase");
        if (phrase.Trim().Length < 2) throw new InvalidOperationException("Type a phrase of at least 2 characters, like \";sig\".");
        var replace = config.GetBool("replace");
        var matchCase = config.GetBool("matchCase");
        return service.Register(phrase, matchCase, async match =>
        {
            var description = $"You typed {phrase}";
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
        Id = "clip.changed", Title = "When I copy something", Category = TP.CatClipboard, Icon = "Clipboard24",
        Summary = "When I copy {kind}",
        Description = "Runs when you copy something. Choose what kind of thing, or words it must contain. Copies made by Flow Mate itself, and things copied from password managers, are ignored.",
        OutputKind = ContentKind.Any, OutputDescription = "What you copied (text, a link, a picture or files)",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("kind", "Anything", "any", new Choice("any", "anything"), new Choice("text", "text"), new Choice("link", "a link"),
                new Choice("image", "a picture"), new Choice("files", "files"), new Choice("contains", "text containing…")),
            ParameterDescriptor.Text("text", "Containing", null, "Words to look for") with { VisibleWhen = new VisibleWhen("kind", "contains"), AllowVariables = false, AllowAskEachTime = false },
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
            if (s.Files.Count > 0) sink.Fire(ContentValue.Files(s.Files), s.Files.Count == 1 ? "You copied a file" : $"You copied {s.Files.Count} files");
            else if (s.ImagePath is not null) sink.Fire(ContentValue.Image(s.ImagePath), "You copied a picture");
            else if (IsLink(s.Text?.Trim())) sink.Fire(ContentValue.Url(s.Text!.Trim()), "You copied a link");
            else sink.Fire(ContentValue.Text(s.Text ?? ""), "You copied some text");
        }
        return TP.Watch(env.Clipboard, () => env.Clipboard.Changed += OnClip, () => env.Clipboard.Changed -= OnClip);
    }
}
