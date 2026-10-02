using Flowmate.Core;

namespace Flowmate.App.DesignTime;

/// <summary>A trigger that never fires. Lets the Automations UI be built and used before the real providers exist.</summary>
public sealed class SampleTrigger : TriggerProvider
{
    private readonly TriggerDescriptor _descriptor;
    public SampleTrigger(TriggerDescriptor descriptor) { _descriptor = descriptor; }
    public override TriggerDescriptor Descriptor => _descriptor;
    public override IDisposable Start(TriggerConfig config, ITriggerSink sink) => new Nothing();
    private sealed class Nothing : IDisposable { public void Dispose() { } }
}

public sealed class SampleCondition : ConditionProvider
{
    private readonly ConditionDescriptor _descriptor;
    public SampleCondition(ConditionDescriptor descriptor) { _descriptor = descriptor; }
    public override ConditionDescriptor Descriptor => _descriptor;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// A few sample triggers and conditions per category, covering every literal parameter type. Registered only when
/// the real registries are empty. INTEGRATION(E): once Flowmate.Triggers is registered this does nothing.
/// </summary>
public static class SampleProviders
{
    private static readonly Choice[] Days =
    {
        new("mon", "Monday"), new("tue", "Tuesday"), new("wed", "Wednesday"), new("thu", "Thursday"),
        new("fri", "Friday"), new("sat", "Saturday"), new("sun", "Sunday"),
    };

    private static ParameterDescriptor DaysParam(string key = "days") => new()
    {
        Key = key, Label = "On these days", Type = ParamType.List, Choices = Days,
        Default = ParamValue.List("mon", "tue", "wed", "thu", "fri", "sat", "sun"),
    };

    public static void RegisterIfEmpty(TriggerRegistry triggers, ConditionRegistry conditions)
    {
        if (triggers.All.Count == 0)
            foreach (var d in Triggers()) triggers.Register(new SampleTrigger(d));
        if (conditions.All.Count == 0)
            foreach (var d in Conditions()) conditions.Register(new SampleCondition(d));
    }

    private static TriggerDescriptor T(string id, string title, string category, string icon, string summary, string description,
        ContentKind output = ContentKind.Any, string? outputDescription = null, params ParameterDescriptor[] parameters) =>
        new() { Id = id, Title = title, Category = category, Icon = icon, Summary = summary, Description = description, OutputKind = output, OutputDescription = outputDescription, Parameters = parameters };

    public static IReadOnlyList<TriggerDescriptor> Triggers() => new[]
    {
        // Time
        T("time.daily", "At a time of day", "Time", "Clock24", "At {time} on {days}", "Runs at the time you choose, on the days you pick.",
            ContentKind.Date, "The date and time it ran",
            new ParameterDescriptor { Key = "time", Label = "Time", Type = ParamType.Time, Required = true, Default = ParamValue.Time("07:30") }, DaysParam()),
        T("time.interval", "Every few minutes or hours", "Time", "ArrowRepeatAll24", "Every {every}", "Runs again and again with a pause in between.",
            parameters: new[]
            {
                ParameterDescriptor.Duration("every", "Repeat every", 3600) with { Required = true },
                ParameterDescriptor.Bool("onlyActive", "Only during certain hours"),
                new ParameterDescriptor { Key = "from", Label = "From", Type = ParamType.Time, Default = ParamValue.Time("09:00"), VisibleWhen = new VisibleWhen("onlyActive", "true") },
                new ParameterDescriptor { Key = "to", Label = "Until", Type = ParamType.Time, Default = ParamValue.Time("17:00"), VisibleWhen = new VisibleWhen("onlyActive", "true") },
            }),
        T("time.once", "On a date and time", "Time", "CalendarClock24", "On {when}", "Runs once, at the date and time you choose.",
            parameters: new ParameterDescriptor { Key = "when", Label = "Date and time", Type = ParamType.DateTime, Required = true }),
        T("time.yearly", "Every year on a date", "Time", "Calendar24", "Every year on {date} at {at}", "Good for birthdays and anniversaries.",
            parameters: new[]
            {
                new ParameterDescriptor { Key = "date", Label = "Date", Type = ParamType.Date, Required = true },
                new ParameterDescriptor { Key = "at", Label = "Time", Type = ParamType.Time, Default = ParamValue.Time("09:00") },
            }),

        // Calls & Screen
        T("call.started", "When I'm in a call", "Calls & Screen", "Call24", "When I start a call", "Runs when your microphone or camera starts being used."),
        T("call.ended", "When a call ends", "Calls & Screen", "CallEnd24", "When a call ends", "Runs when your microphone and camera stop being used."),
        T("screen.screenshotTaken", "When I take a screenshot", "Calls & Screen", "Screenshot24", "When I take a screenshot", "Runs after you take a screenshot.",
            ContentKind.Image, "The screenshot"),

        // Web & Weather
        T("web.rssNew", "New item in a news feed", "Web & Weather", "Rss24", "When {feed} has something new", "Checks a feed and runs for every new item.",
            ContentKind.Text, "The new item",
            new ParameterDescriptor { Key = "feed", Label = "Feed address", Type = ParamType.Url, Required = true, Placeholder = "https://example.com/feed.xml" }),
        T("weather.alert", "Weather alert", "Web & Weather", "WeatherRain24", "When {when} is expected", "Runs when the forecast matches.",
            parameters: new[]
            {
                ParameterDescriptor.ChoiceOf("when", "Weather", "rain", new Choice("rain", "rain"), new Choice("snow", "snow"), new Choice("hot", "a hot day"), new Choice("cold", "a cold day")),
                ParameterDescriptor.Number("degrees", "Temperature", 30, -40, 60, "°C") with { VisibleWhen = new VisibleWhen("when", "hot", "cold") },
            }),

        // System
        T("system.signIn", "When I sign in", "System", "Person24", "When I sign in or Flowmate starts", "Runs when you sign in to Windows."),
        T("system.wake", "When the PC wakes up", "System", "WeatherSunny24", "When the PC wakes up", "Runs after the PC wakes from sleep."),
        T("system.lock", "When the PC is locked", "System", "LockClosed24", "When the PC is locked", "Runs when you lock the screen."),
        T("system.idle", "When I've been away", "System", "ClockAlarm24", "When I've been away for {away}", "Runs when you haven't touched the keyboard or mouse for a while.",
            parameters: ParameterDescriptor.Duration("away", "Away for", 300) with { Required = true }),

        // Apps
        T("app.opened", "An app opens", "Apps", "Apps24", "When {app} opens", "Runs when the app you choose starts.",
            ContentKind.App, "The app that opened",
            new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true, Placeholder = "Spotify" }),
        T("app.closed", "An app closes", "Apps", "AppsList24", "When {app} closes", "Runs when the app you choose is closed.",
            ContentKind.App, "The app that closed",
            new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true, Placeholder = "Spotify" }),
        T("app.focused", "An app comes to the front", "Apps", "WindowApps24", "When {app} comes to the front", "Runs when you switch to the app.",
            parameters: new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true }),

        // Files & Drives
        T("file.changed", "Files change in a folder", "Files & Drives", "FolderArrowRight24", "When files are {what} in {folder}", "Runs when files show up or change in a folder. Half-downloaded files are ignored.",
            ContentKind.File, "The files that changed",
            ParameterDescriptor.ChoiceOf("what", "What happens", "added", new Choice("added", "added"), new Choice("changed", "changed"), new Choice("removed", "removed")),
            ParameterDescriptor.FolderParam("folder", "Folder") with { Required = true },
            ParameterDescriptor.ChoiceOf("kind", "Only these files", "any", new Choice("any", "Any files"), new Choice("pictures", "Pictures"), new Choice("documents", "Documents"), new Choice("music", "Music")),
            ParameterDescriptor.Text("types", "Only file types", null, ".pdf, .docx"),
            ParameterDescriptor.Bool("subfolders", "Include subfolders")),
        T("file.fileChanged", "One file changes", "Files & Drives", "DocumentEdit24", "When {file} changes", "Runs when the file you choose is saved.",
            ContentKind.File, "The file",
            ParameterDescriptor.FileParam("file", "File") with { Required = true }),
        T("drive.connected", "A USB drive is plugged in", "Files & Drives", "UsbStick24", "When a USB drive is {change}", "Runs when a USB drive is plugged in or removed.",
            parameters: ParameterDescriptor.ChoiceOf("change", "When", "plugged in", new Choice("plugged in", "plugged in"), new Choice("removed", "removed"))),

        // Network
        T("net.wifiJoined", "I join Wi-Fi", "Network", "Wifi124", "When I join {network}", "Runs when your PC connects to a Wi-Fi network.",
            parameters: ParameterDescriptor.DynamicChoice("network", "Network", SampleNetworks, "any")),
        T("net.online", "Internet comes back", "Network", "GlobeDesktop24", "When the internet comes back", "Runs when your PC gets online again."),
        T("net.offline", "Internet drops", "Network", "GlobeProhibited24", "When the internet drops", "Runs when your PC loses its connection."),

        // Power
        T("power.pluggedIn", "Plugged in", "Power", "PlugConnected24", "When I plug in the charger", "Runs when your laptop starts charging."),
        T("power.unplugged", "Unplugged", "Power", "PlugDisconnected24", "When I unplug the charger", "Runs when your laptop switches to battery."),
        T("power.battery", "Battery level", "Power", "Battery624", "When the battery goes {direction} {level}", "Runs when the battery crosses a level.",
            parameters: new[]
            {
                ParameterDescriptor.ChoiceOf("direction", "Goes", "below", new Choice("below", "below"), new Choice("above", "above")),
                ParameterDescriptor.Number("level", "Level", 20, 1, 100, "%") with { Required = true },
            }),

        // Devices
        T("device.bluetooth", "A Bluetooth device connects", "Devices", "Bluetooth24", "When a Bluetooth device {change}", "Runs when headphones, speakers or other devices connect.",
            parameters: ParameterDescriptor.ChoiceOf("change", "When it", "connects", new Choice("connects", "connects"), new Choice("disconnects", "disconnects"))),
        T("device.display", "A monitor is connected", "Devices", "Desktop24", "When a monitor is {change}", "Runs when you plug in or unplug a screen.",
            parameters: ParameterDescriptor.ChoiceOf("change", "When it is", "connected", new Choice("connected", "connected"), new Choice("disconnected", "disconnected"))),

        // Keyboard
        T("key.shortcut", "A keyboard shortcut", "Keyboard", "Keyboard24", "When I press {keys}", "Runs when you press the keys you choose.",
            parameters: new[]
            {
                new ParameterDescriptor { Key = "keys", Label = "Keyboard shortcut", Type = ParamType.Hotkey, Required = true },
                new ParameterDescriptor { Key = "app", Label = "Only when this app is in front", Type = ParamType.App },
            }),
        T("key.typed", "I type a phrase", "Keyboard", "TextT24", "When I type {phrase}", "Runs when you type the phrase anywhere.",
            ContentKind.Text, "The phrase you typed",
            ParameterDescriptor.Text("phrase", "Phrase", null, ";date") with { Required = true },
            ParameterDescriptor.Bool("replace", "Replace the phrase with the shortcut's text")),

        // Clipboard
        T("clip.changed", "I copy something", "Clipboard", "ClipboardPaste24", "When I copy {kind}", "Runs whenever you copy.",
            ContentKind.Any, "What you copied",
            ParameterDescriptor.ChoiceOf("kind", "Copy type", "anything", new Choice("anything", "anything"), new Choice("text", "text"), new Choice("link", "a link"), new Choice("image", "a picture"), new Choice("files", "files")),
            ParameterDescriptor.Text("contains", "Only if it contains", null, "any text")),
    };

    private static async Task<IReadOnlyList<Choice>> SampleNetworks(FlowmateServices services, CancellationToken ct)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);
        return new Choice[] { new("any", "Any network"), new("home", "Home Wi-Fi"), new("office", "Office Wi-Fi"), new("cafe", "Coffee shop") };
    }

    private static ConditionDescriptor C(string id, string title, string icon, string summary, string description, params ParameterDescriptor[] parameters) =>
        new() { Id = id, Title = title, Category = "Conditions", Icon = icon, Summary = summary, Description = description, Parameters = parameters };

    public static IReadOnlyList<ConditionDescriptor> Conditions() => new[]
    {
        C("cond.timeBetween", "Time of day", "Clock24", "it's between {from} and {to}", "Only run during certain hours.",
            new ParameterDescriptor { Key = "from", Label = "From", Type = ParamType.Time, Required = true, Default = ParamValue.Time("09:00") },
            new ParameterDescriptor { Key = "to", Label = "Until", Type = ParamType.Time, Required = true, Default = ParamValue.Time("17:00") }),
        C("cond.days", "Day of the week", "CalendarWeekNumbers24", "it's {days}", "Only run on certain days.", DaysParam()),
        C("cond.power", "Power source", "PlugConnected24", "I'm {state}", "Only when on battery or plugged in.",
            ParameterDescriptor.ChoiceOf("state", "State", "battery", new Choice("battery", "on battery"), new Choice("plugged", "plugged in"))),
        C("cond.battery", "Battery level", "Battery624", "the battery is {direction} {level}", "Only when the battery is low or high.",
            ParameterDescriptor.ChoiceOf("direction", "Is", "below", new Choice("below", "below"), new Choice("above", "above")),
            ParameterDescriptor.Number("level", "Level", 30, 1, 100, "%")),
        C("cond.wifi", "Wi-Fi network", "Wifi124", "I'm connected to {network}", "Only when connected to a network.",
            ParameterDescriptor.DynamicChoice("network", "Network", SampleNetworks, "home")),
        C("cond.appRunning", "App is running", "Apps24", "{app} is running", "Only if an app is open.",
            new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true }),
        C("cond.appFront", "App is in front", "WindowApps24", "{app} is in front", "Only if an app is the window you're using.",
            new ParameterDescriptor { Key = "app", Label = "App", Type = ParamType.App, Required = true }),
        C("cond.notFullscreen", "Not in full screen", "FullScreenMaximize24", "I'm not in a full-screen app", "Skips games, videos and presentations."),
        C("cond.online", "Internet", "GlobeDesktop24", "I'm {state}", "Only when online or offline.",
            ParameterDescriptor.ChoiceOf("state", "State", "online", new Choice("online", "online"), new Choice("offline", "offline"))),
        C("cond.inCall", "In a call", "Call24", "I'm {state}", "Only when in a call or not.",
            ParameterDescriptor.ChoiceOf("state", "State", "not in a call", new Choice("in a call", "in a call"), new Choice("not in a call", "not in a call"))),
    };
}
