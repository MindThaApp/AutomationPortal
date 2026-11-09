using AutomationPortal.Core;

namespace AutomationPortal.GalleryBuilder;

/// <summary>A suggested automation (shown as a tip in the description; validated against the trigger registry).</summary>
internal sealed record SuggestedTrigger(string TriggerId, params (string Key, ParamValue Value)[] Parameters);

internal sealed class Starter
{
    public required string Section { get; init; }
    public required Shortcut Shortcut { get; init; }
    public SuggestedTrigger? Suggested { get; init; }
    public string Slug => Starters.Slugify(Shortcut.Name);
}

internal static class Starters
{
    public const string GetStarted = "Get Started";
    public const string Files = "Files & Photos";
    public const string Productivity = "Productivity";
    public const string Device = "Device";
    public const string Ai = "AI";
    public const string Ideas = "Automation Ideas";
    public static readonly string[] SectionOrder = { GetStarted, Files, Productivity, Device, Ai, Ideas };

    public static string Slugify(string name)
    {
        var chars = name.ToLowerInvariant().Replace("'", "").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly DateTimeOffset Stamp = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ShortcutBuilder Make(string name, string icon, string color, string description,
        ContentKind[]? accepts = null, bool sendTo = false, WhenNoInput whenNoInput = WhenNoInput.ContinueWithNothing)
    {
        var b = new ShortcutBuilder(name, icon, color);
        var s = b.Shortcut;
        s.Id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("flowmate.gallery/" + name)));
        s.Description = description;
        s.AcceptsInput = (accepts ?? Array.Empty<ContentKind>()).ToList();
        s.ShowInSendTo = sendTo;
        s.WhenNoInput = whenNoInput;
        s.CreatedAt = s.ModifiedAt = Stamp;
        return b;
    }

    private static string Do(this ShortcutBuilder b, string uid, string actionId, params (string Key, ParamValue Value)[] p) =>
        b.Add(ActionInstance.Create(actionId, p).WithUid(uid));

    private static string DoSoft(this ShortcutBuilder b, string uid, string actionId, params (string Key, ParamValue Value)[] p) =>
        b.Add(ActionInstance.Create(actionId, p).WithUid(uid).OnErrorContinue());

    private static void Ask(this ShortcutBuilder b, string uid, string key, string question, ParamValue? @default = null) =>
        b.Shortcut.ImportQuestions.Add(new ImportQuestion { ActionUid = uid, ParameterKey = key, Question = question, DefaultValue = @default });

    private static Starter Done(string section, ShortcutBuilder b, SuggestedTrigger? suggested = null) =>
        new() { Section = section, Shortcut = b.Build(), Suggested = suggested };

    private static ParamValue Out(string uid, string? property = null) =>
        ParamValue.Var(property is null ? VarRef.Output(uid) : VarRef.Output(uid).WithProperty(property));

    private static VarRef Ref(string uid, string? property = null) =>
        property is null ? VarRef.Output(uid) : VarRef.Output(uid).WithProperty(property);

    private static readonly ContentKind[] Images = { ContentKind.Image };
    private static readonly ContentKind[] TextOrLink = { ContentKind.Text, ContentKind.Url };

    public static List<Starter> All() => new()
    {
        // ---- Get Started ----------------------------------------------------------------------
        GoodMorning(), WeatherNow(), QuickNote(), UnitConverter(), PasswordToClipboard(), CleanLink(), CopyAsPlainText(), WhatsPlaying(),
        // ---- Files & Photos -------------------------------------------------------------------
        OrganizeDownloads(), ResizeImages(), ImagesToPdf(), CombinePdfs(), RenamePhotosByDate(), RemovePhotoLocation(), ZipAndEmail(), TidyDesktop(),
        // ---- Productivity ---------------------------------------------------------------------
        GrabTextFromScreen(), ClipboardToQr(), FocusMode(), MeetingMode(), Pomodoro(), OpenWorkApps(), InsertDate(), InsertAddress(), SendToPhone(),
        // ---- Device ---------------------------------------------------------------------------
        DailyBingWallpaper(), KeepAwakeOneHour(), ToggleDarkMode(), ToggleMicrophone(),
        // ---- AI -------------------------------------------------------------------------------
        TranslateClipboard(), SummarizeWebPage(), FixMyWriting(),
        // ---- Automation Ideas -----------------------------------------------------------------
        BatterySaver(), PowerSaverWhenUnplugged(), HeadphonesVolume(), DarkModeAtSunset(), LightModeAtSunrise(),
    };

    // ======================================================================================================
    // Get Started
    // ======================================================================================================

    private static Starter GoodMorning()
    {
        var b = Make("Good Morning", "WeatherSunny24", "orange",
            "Tells you today's date and weather out loud, then opens your first apps. Tip: add an automation \"At a time of day\" (weekday mornings) to run this for you.");
        b.Do("weather01", "weather.current", ("location", "London"), ("units", ParamValue.Choice("auto")));
        b.Do("today0001", "date.current");
        b.Do("dayName01", "date.format", ("date", Out("today0001")), ("format", ParamValue.Choice("custom")), ("custom", "dddd, d MMMM"));
        b.Do("greeting1", "content.text", ("text", ParamValue.Text("Good morning! It's ", Ref("dayName01"), ". Right now it's ", Ref("weather01", "key:Summary"),
            ", with a high of ", Ref("weather01", "key:High"), " and a low of ", Ref("weather01", "key:Low"), ".")));
        b.Do("notify001", "ui.notify", ("title", "Good Morning"), ("body", Out("greeting1")));
        b.Do("speak0001", "media.speak", ("text", Out("greeting1")));
        b.DoSoft("openApp01", "app.open", ("app", ParamValue.App("Microsoft Edge", "Microsoft Edge")));
        b.DoSoft("openApp02", "app.open", ("app", ParamValue.App("Outlook", "Outlook")));
        b.Ask("weather01", "location", "Which city should I check the weather for?", "London");
        b.Ask("openApp01", "app", "Which app do you want to open first each morning?", ParamValue.App("Microsoft Edge", "Microsoft Edge"));
        b.Ask("openApp02", "app", "And which app second?", ParamValue.App("Outlook", "Outlook"));
        return Done(GetStarted, b, new SuggestedTrigger("time.daily", ("time", ParamValue.Time("07:30")),
            ("days", ParamValue.List(ParamValue.Choice("mon"), ParamValue.Choice("tue"), ParamValue.Choice("wed"), ParamValue.Choice("thu"), ParamValue.Choice("fri")))));
    }

    private static Starter WeatherNow()
    {
        var b = Make("Weather Now", "WeatherPartlyCloudyDay24", "cyan", "Shows the current weather for your city: temperature, what it feels like, today's high and low, and the chance of rain.");
        b.Do("weather01", "weather.current", ("location", "London"), ("units", ParamValue.Choice("auto")));
        b.Do("message01", "content.text", ("text", ParamValue.Text(Ref("weather01", "key:Summary"), ".\nFeels like ", Ref("weather01", "key:Feels Like"),
            "°. High ", Ref("weather01", "key:High"), "°, low ", Ref("weather01", "key:Low"), "°. Chance of rain: ", Ref("weather01", "key:Rain Chance"), "%.")));
        b.Do("show00001", "ui.showResult", ("content", Out("message01")));
        b.Ask("weather01", "location", "Which city should I check the weather for?", "London");
        return Done(GetStarted, b);
    }

    private static Starter QuickNote()
    {
        var b = Make("Quick Note", "Note24", "yellow",
            "Jot something down in one step. Your note is added to the top of a Quick Notes file in Documents\\Automation Portal Notes. If you run it on selected text, the text is filled in for you.",
            accepts: new[] { ContentKind.Text });
        b.Do("askNote01", "ui.ask", ("kind", ParamValue.Choice("multiline")), ("prompt", "What do you want to jot down?"), ("default", ParamValue.Text(VarRef.ShortcutInput)));
        b.Do("addNote01", "note.add", ("text", Out("askNote01")), ("name", "Quick Notes"), ("position", ParamValue.Choice("top")), ("timestamp", true));
        b.Do("notify001", "ui.notify", ("title", "Quick Note"), ("body", "Saved to your Quick Notes."));
        return Done(GetStarted, b);
    }

    private static readonly (string Label, string From, string To, string FromText, string ToText)[] Conversions =
    {
        ("Kilometres to miles", "km", "mi", "km", "miles"),
        ("Miles to kilometres", "mi", "km", "miles", "km"),
        ("Kilograms to pounds", "kg", "lb", "kg", "lb"),
        ("Pounds to kilograms", "lb", "kg", "lb", "kg"),
        ("Celsius to Fahrenheit", "°C", "°F", "°C", "°F"),
        ("Fahrenheit to Celsius", "°F", "°C", "°F", "°C"),
        ("Centimetres to inches", "cm", "in", "cm", "inches"),
        ("Inches to centimetres", "in", "cm", "inches", "cm"),
        ("Litres to US gallons", "L", "gal", "litres", "gallons"),
        ("Megabytes to gigabytes", "MB", "GB", "MB", "GB"),
    };

    private static Starter UnitConverter()
    {
        var b = Make("Unit Converter", "ArrowSwap24", "slate", "Converts a number between everyday units: distance, weight, temperature, volume and file sizes.");
        b.Do("askValue1", "ui.ask", ("kind", ParamValue.Choice("number")), ("prompt", "What number do you want to convert?"));
        b.Menu("What do you want to convert?", Conversions.Select((c, i) => (c.Label, (Action<ShortcutBuilder>)(m =>
        {
            var uid = $"convert{i + 1:00}";
            m.Do(uid, "num.convertUnits", ("value", Out("askValue1")), ("from", ParamValue.Choice(c.From)), ("to", ParamValue.Choice(c.To)));
            m.Do($"round{i + 1:00}00", "num.round", ("number", Out(uid)), ("mode", ParamValue.Choice("nearest")), ("places", 2));
            m.Do($"show{i + 1:00}000", "ui.showResult", ("content", ParamValue.Text(Ref("askValue1"), $" {c.FromText} = ", Ref($"round{i + 1:00}00"), $" {c.ToText}")));
        }))).ToArray());
        return Done(GetStarted, b);
    }

    private static Starter PasswordToClipboard()
    {
        var b = Make("Password to Clipboard", "Key24", "graphite",
            "Makes a strong random password and copies it. It's wiped from the clipboard after a minute, so it doesn't linger.");
        b.Do("password01", "text.password", ("style", ParamValue.Choice("characters")), ("length", 20), ("symbols", true), ("digits", true));
        b.Do("copy000001", "clip.set", ("content", Out("password01")), ("clearAfter", ParamValue.Duration(60)));
        b.Do("notify001", "ui.notify", ("title", "Password ready"), ("body", "Your new password is on the clipboard. It will be cleared in 60 seconds."));
        b.Ask("password01", "length", "How long should the password be?", ParamValue.Number(20));
        return Done(GetStarted, b);
    }

    private static Starter CleanLink()
    {
        var b = Make("Clean Link", "LinkEdit24", "blue",
            "Removes tracking bits (utm_, fbclid and friends) from a web link and copies the clean version. Works on a link you share to it, or the link on your clipboard.",
            accepts: TextOrLink, whenNoInput: WhenNoInput.UseClipboard);
        b.Do("clean00001", "web.cleanUrl", ("url", ParamValue.ShortcutInput()));
        b.Do("copy000001", "clip.set", ("content", Out("clean00001")));
        b.Do("notify001", "ui.notify", ("title", "Clean link copied"), ("body", ParamValue.Text(Ref("clean00001"))));
        return Done(GetStarted, b);
    }

    private static Starter CopyAsPlainText()
    {
        var b = Make("Copy as Plain Text", "TextClearFormatting24", "teal",
            "Turns whatever you copied into plain text (no fonts, colours or links) and puts it back on the clipboard. Tip: give it a keyboard shortcut and paste without formatting anywhere.");
        b.Do("getClip001", "clip.get", ("as", ParamValue.Choice("text")));
        b.Do("setClip001", "clip.set", ("content", Out("getClip001")));
        b.Do("notify001", "ui.notify", ("title", "Plain text copied"), ("body", "The clipboard now has plain text only."));
        return Done(GetStarted, b);
    }

    private static Starter WhatsPlaying()
    {
        var b = Make("What's Playing", "MusicNote224", "pink", "Shows the song that's playing right now, from Spotify, YouTube Music, your browser or any other media player.");
        b.Do("playing001", "media.nowPlaying");
        b.If(ParamValue.AllOf(ConditionRow.Of(Ref("playing001", "key:Title"), ConditionOperators.HasValue)),
            then: t => t.Do("show00001", "ui.showResult", ("content", ParamValue.Text(Ref("playing001", "key:Title"), " by ", Ref("playing001", "key:Artist"), " (", Ref("playing001", "key:App"), ")"))),
            otherwise: o => o.Do("notify001", "ui.notify", ("title", "What's Playing"), ("body", "Nothing is playing right now.")));
        return Done(GetStarted, b);
    }

    // ======================================================================================================
    // Files & Photos
    // ======================================================================================================

    private static Starter OrganizeDownloads()
    {
        var b = Make("Organize Downloads", "FolderArrowRight24", "cyan",
            "Sorts the loose files in your Downloads folder into folders by type: Images, Documents, Archives and so on. It asks before it starts.");
        b.Do("confirm001", "ui.alert", ("title", "Organize Downloads"), ("message", "This moves the files in your Downloads folder into folders by type (Images, Documents, ...). Continue?"), ("showCancel", true));
        b.Do("folder0001", "file.getFolder", ("folder", ParamValue.Choice("downloads")));
        b.Do("contents01", "file.contents", ("folder", Out("folder0001")), ("include", ParamValue.Choice("files")), ("subfolders", false));
        b.Do("moved00001", "file.move", ("files", Out("contents01")), ("folder", Out("folder0001")), ("subfolder", "{kind}"), ("ifExists", ParamValue.Choice("keep")));
        b.Do("count00001", "content.count", ("input", Out("moved00001")), ("what", ParamValue.Choice("items")));
        b.Do("notify001", "ui.notify", ("title", "Downloads organized"), ("body", ParamValue.Text("Sorted ", Ref("count00001"), " files into folders by type.")));
        return Done(Files, b);
    }

    private static Starter ResizeImages()
    {
        var b = Make("Resize Images", "ResizeImage24", "purple",
            "Makes photos smaller so they're easy to email or upload. Copies are saved next to the originals with \"-small\" added to the name. Right-click photos and choose Send to.",
            accepts: Images, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("resize0001", "img.resize", ("images", ParamValue.ShortcutInput()), ("by", ParamValue.Choice("longest")), ("longest", 1600),
            ("neverEnlarge", true), ("quality", 85), ("suffix", "-small"), ("saveTo", ParamValue.Choice("next")));
        b.Do("count00001", "content.count", ("input", Out("resize0001")), ("what", ParamValue.Choice("items")));
        b.Do("notify001", "ui.notify", ("title", "Images resized"), ("body", ParamValue.Text("Made ", Ref("count00001"), " smaller copies.")));
        b.Do("reveal0001", "app.reveal", ("files", Out("resize0001")));
        b.Ask("resize0001", "longest", "How many pixels should the longest side be? (1600 is good for email)", ParamValue.Number(1600));
        return Done(Files, b);
    }

    private static Starter ImagesToPdf()
    {
        var b = Make("Images to PDF", "DocumentPdf24", "red", "Puts the photos or pictures you choose into one PDF, one picture per page. Right-click pictures and choose Send to.",
            accepts: Images, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("makePdf001", "pdf.make", ("items", ParamValue.ShortcutInput()), ("pageSize", ParamValue.Choice("a4")), ("imageFit", ParamValue.Choice("fit")),
            ("name", "Pictures"), ("saveTo", ParamValue.Choice("next")));
        b.Do("reveal0001", "app.reveal", ("files", Out("makePdf001")));
        return Done(Files, b);
    }

    private static Starter CombinePdfs()
    {
        var b = Make("Combine PDFs", "DocumentMultiple24", "red", "Joins the PDFs you choose into one PDF, in the order you select them. Right-click PDFs and choose Send to.",
            accepts: new[] { ContentKind.File }, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("merge00001", "pdf.merge", ("files", ParamValue.ShortcutInput()), ("name", "Combined"), ("saveTo", ParamValue.Choice("next")));
        b.Do("reveal0001", "app.reveal", ("files", Out("merge00001")));
        return Done(Files, b);
    }

    private static Starter RenamePhotosByDate()
    {
        var b = Make("Rename Photos by Date", "Rename24", "orange",
            "Renames photos to the day they were taken followed by a number, like 2026-10-02 001. It asks before changing anything. Right-click photos and choose Send to.",
            accepts: new[] { ContentKind.Image, ContentKind.File }, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("confirm001", "ui.alert", ("title", "Rename Photos by Date"), ("message", "This renames the photos you chose to the date they were taken, like 2026-10-02 001. Continue?"), ("showCancel", true));
        b.Do("rename0001", "file.rename", ("files", ParamValue.ShortcutInput()), ("pattern", "{taken:yyyy-MM-dd} {n:000}"));
        b.Do("count00001", "content.count", ("input", Out("rename0001")), ("what", ParamValue.Choice("items")));
        b.Do("notify001", "ui.notify", ("title", "Photos renamed"), ("body", ParamValue.Text("Renamed ", Ref("count00001"), " photos.")));
        return Done(Files, b);
    }

    private static Starter RemovePhotoLocation()
    {
        var b = Make("Remove Photo Location Before Sharing", "LocationOff24", "green",
            "Makes clean copies of your photos without the hidden location, camera and date details, then opens the share window. Your originals are untouched.",
            accepts: Images, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("clean00001", "img.stripInfo", ("images", ParamValue.ShortcutInput()), ("suffix", "-clean"), ("saveTo", ParamValue.Choice("next")));
        b.Do("share00001", "share.share", ("items", Out("clean00001")), ("title", "Shared from Automation Portal"));
        return Done(Files, b);
    }

    private static Starter ZipAndEmail()
    {
        var b = Make("Zip & Email", "FolderZip24", "blue",
            "Packs the files or folders you choose into one zip file and starts a new email with it attached. Nothing is sent until you press Send. Right-click files and choose Send to.",
            accepts: new[] { ContentKind.File, ContentKind.Folder }, sendTo: true, whenNoInput: WhenNoInput.AskForFiles);
        b.Do("zip0000001", "file.zip", ("files", ParamValue.ShortcutInput()), ("name", "Files"), ("saveTo", ParamValue.Choice("next")));
        b.Do("mail000001", "mail.compose", ("to", "name@example.com"), ("subject", "Files for you"), ("body", "Hi,\n\nI've attached the files.\n"), ("attachments", Out("zip0000001")));
        b.Ask("mail000001", "to", "Who do you usually send files to? (email address)", "name@example.com");
        return Done(Files, b);
    }

    private static Starter TidyDesktop()
    {
        var b = Make("Tidy Desktop", "DesktopCheckmark24", "gray",
            "Moves pictures, PDFs, documents and zip files off your Desktop into a \"Tidy Desktop\" folder, sorted by type. Shortcuts and programs stay where they are. It asks first.");
        b.Do("confirm001", "ui.alert", ("title", "Tidy Desktop"), ("message", "This moves pictures, PDFs, documents and zip files from your Desktop into a Tidy Desktop folder. Continue?"), ("showCancel", true));
        b.Do("desktop001", "file.getFolder", ("folder", ParamValue.Choice("desktop")));
        b.Do("contents01", "file.contents", ("folder", Out("desktop001")), ("include", ParamValue.Choice("files")), ("subfolders", false));
        var n = 0;
        foreach (var kind in new[] { "image", "pdf", "document", "archive" })
        {
            n++;
            b.DoSoft($"filter{n:00}00", "file.filter", ("files", Out("contents01")), ("kind", ParamValue.Choice(kind)));
            b.DoSoft($"move{n:00}000", "file.move", ("files", Out($"filter{n:00}00")), ("folder", Out("desktop001")), ("subfolder", "Tidy Desktop/{kind}"), ("ifExists", ParamValue.Choice("keep")));
        }
        b.Do("notify001", "ui.notify", ("title", "Desktop tidied"), ("body", "Your files are in the Tidy Desktop folder."));
        return Done(Files, b);
    }

    // ======================================================================================================
    // Productivity
    // ======================================================================================================

    private static Starter GrabTextFromScreen()
    {
        var b = Make("Grab Text from Screen", "ScanText24", "pink",
            "Drag a box around any text on screen (a picture, a video, a PDF that won't copy) and it lands on your clipboard, ready to paste. Tip: give it a keyboard shortcut.");
        b.Do("grab000001", "screen.grabText", ("language", ParamValue.Choice("auto")));
        b.Do("copy000001", "clip.set", ("content", Out("grab000001")));
        b.Do("notify001", "ui.notify", ("title", "Text copied"), ("body", ParamValue.Text(Ref("grab000001"))));
        return Done(Productivity, b);
    }

    private static Starter ClipboardToQr()
    {
        var b = Make("Clipboard to QR Code", "QrCode24", "slate",
            "Turns a link or some text into a QR code you can scan with your phone. Uses the text you give it, or whatever is on the clipboard.",
            accepts: TextOrLink, whenNoInput: WhenNoInput.UseClipboard);
        b.Do("qr00000001", "text.qrMake", ("text", ParamValue.ShortcutInput()), ("size", 12), ("repair", ParamValue.Choice("M")));
        b.Do("preview001", "ui.preview", ("content", Out("qr00000001")));
        return Done(Productivity, b);
    }

    private static Starter FocusMode()
    {
        var b = Make("Focus Mode", "BrainCircuit24", "indigo",
            "Mutes your sound, closes two distracting apps and starts a 25-minute timer so you can concentrate.");
        b.Do("mute000001", "dev.mute", ("state", ParamValue.Choice("mute")));
        b.DoSoft("quit000001", "app.quit", ("app", ParamValue.App("Discord", "Discord")));
        b.DoSoft("quit000002", "app.quit", ("app", ParamValue.App("Slack", "Slack")));
        b.Do("timer00001", "rem.timer", ("duration", ParamValue.Duration(1500)), ("label", "Focus session finished"), ("sound", true));
        b.Do("notify001", "ui.notify", ("title", "Focus Mode"), ("body", "Sound is muted and your timer is running. See you in 25 minutes."));
        b.Ask("quit000001", "app", "Which app distracts you most?", ParamValue.App("Discord", "Discord"));
        b.Ask("quit000002", "app", "And another one to close while you focus?", ParamValue.App("Slack", "Slack"));
        return Done(Productivity, b);
    }

    private static Starter MeetingMode()
    {
        var b = Make("Meeting Mode", "VideoPerson24", "red",
            "Gets you ready for a call: microphone on, speakers at a comfortable volume, one distracting app closed and the PC kept awake for an hour.");
        b.Do("mic0000001", "dev.micMute", ("state", ParamValue.Choice("unmute")));
        b.Do("volume0001", "dev.setVolume", ("level", 60));
        b.DoSoft("quit000001", "app.quit", ("app", ParamValue.App("Spotify", "Spotify")));
        b.Do("awake00001", "dev.keepAwake", ("state", ParamValue.Choice("on")), ("duration", ParamValue.Duration(3600)), ("keepScreenOn", true));
        b.Do("notify001", "ui.notify", ("title", "Meeting Mode"), ("body", "You're set for your call."));
        b.Ask("quit000001", "app", "Which app should be closed during meetings?", ParamValue.App("Spotify", "Spotify"));
        return Done(Productivity, b, new SuggestedTrigger("call.started", ("source", ParamValue.Choice("any"))));
    }

    private static Starter Pomodoro()
    {
        var b = Make("Pomodoro", "Timer24", "red",
            "Four rounds of 25 minutes of focus followed by a 5-minute break, with a notification at each step. Leave it running in the background.");
        b.Repeat(4, r =>
        {
            r.Do("focus00001", "ui.notify", ("title", "Focus time"), ("body", ParamValue.Text("Round ", VarRef.RepeatIndex, " of 4. Focus for 25 minutes.")));
            r.Do("wait000001", "flow.wait", ("seconds", ParamValue.Duration(1500)));
            r.Do("break00001", "ui.notify", ("title", "Break time"), ("body", "Nice work. Take a 5-minute break."));
            r.Do("wait000002", "flow.wait", ("seconds", ParamValue.Duration(300)));
        });
        b.Do("notify001", "ui.notify", ("title", "Pomodoro finished"), ("body", "All four rounds are done. Time for a longer rest."));
        return Done(Productivity, b);
    }

    private static Starter OpenWorkApps()
    {
        var b = Make("Open Work Apps", "Briefcase24", "blue", "Opens the apps you use at work in one click. Choose your favourites when you add it.");
        b.DoSoft("openApp01", "app.open", ("app", ParamValue.App("Outlook", "Outlook")));
        b.DoSoft("openApp02", "app.open", ("app", ParamValue.App("Microsoft Teams", "Microsoft Teams")));
        b.DoSoft("openApp03", "app.open", ("app", ParamValue.App("Excel", "Excel")));
        b.Ask("openApp01", "app", "Which app do you open first at work?", ParamValue.App("Outlook", "Outlook"));
        b.Ask("openApp02", "app", "Which app second?", ParamValue.App("Microsoft Teams", "Microsoft Teams"));
        b.Ask("openApp03", "app", "And a third?", ParamValue.App("Excel", "Excel"));
        return Done(Productivity, b, new SuggestedTrigger("system.signIn"));
    }

    private static Starter InsertDate()
    {
        var b = Make("Text Expansion: Today's Date", "CalendarDate24", "yellow",
            "Type ;date anywhere and it turns into today's date, like 2026-10-02. Tip: add an automation \"When I type a phrase\" with the phrase ;date and turn on \"replace the phrase with this shortcut's text\".");
        b.Do("today0001", "date.current");
        b.Do("format0001", "date.format", ("date", Out("today0001")), ("format", ParamValue.Choice("isoDate")));
        b.Do("output0001", "flow.output", ("output", Out("format0001")));
        return Done(Productivity, b, new SuggestedTrigger("key.typed", ("phrase", ";date"), ("replace", true)));
    }

    private static Starter InsertAddress()
    {
        var b = Make("Text Expansion: My Address", "Home24", "teal",
            "Type ;addr anywhere and it turns into your address. Tip: add an automation \"When I type a phrase\" with the phrase ;addr and turn on \"replace the phrase with this shortcut's text\".");
        b.Do("address001", "content.text", ("text", "123 Main Street\nSpringfield"));
        b.Do("output0001", "flow.output", ("output", Out("address001")));
        b.Ask("address001", "text", "What text should ;addr type for you? (your address)", "123 Main Street\nSpringfield");
        return Done(Productivity, b, new SuggestedTrigger("key.typed", ("phrase", ";addr"), ("replace", true)));
    }

    private static Starter SendToPhone()
    {
        var b = Make("Send to My Phone", "Phone24", "green",
            "Sends text or a link to your phone as a notification, using the free ntfy app. Install ntfy on your phone and subscribe to the same topic name. Uses what you give it, or the clipboard.",
            accepts: TextOrLink, whenNoInput: WhenNoInput.UseClipboard);
        b.Do("phone00001", "share.phone", ("message", ParamValue.ShortcutInput()), ("title", "From my PC"), ("topic", "my-automationportal-topic"), ("priority", ParamValue.Choice("default")));
        b.Do("notify001", "ui.notify", ("title", "Sent"), ("body", "It's on its way to your phone."));
        b.Ask("phone00001", "topic", "What is your ntfy topic name? Pick something hard to guess.", "my-automationportal-topic");
        return Done(Productivity, b);
    }

    // ======================================================================================================
    // Device
    // ======================================================================================================

    private static Starter DailyBingWallpaper()
    {
        var b = Make("Daily Bing Wallpaper", "ImageSparkle24", "mint",
            "Downloads Bing's picture of the day into Pictures\\Bing Wallpaper and sets it as your wallpaper. Tip: add an automation \"When I sign in\" to get a fresh one every day.");
        b.Do("fetch00001", "web.fetch", ("url", "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-US"), ("method", ParamValue.Choice("GET")));
        b.Do("imagePath1", "dict.getValue", ("dictionary", Out("fetch00001")), ("mode", ParamValue.Choice("key")), ("key", "images.0.url"));
        b.Do("pictures01", "file.getFolder", ("folder", ParamValue.Choice("pictures")));
        b.Do("saveDir001", "file.newFolder", ("name", "Bing Wallpaper"), ("parent", Out("pictures01")));
        b.Do("today0001", "date.current");
        b.Do("stamp00001", "date.format", ("date", Out("today0001")), ("format", ParamValue.Choice("isoDate")));
        b.Do("download01", "web.download", ("url", ParamValue.Text("https://www.bing.com", Ref("imagePath1"))), ("folder", Out("saveDir001")),
            ("fileName", ParamValue.Text("Bing ", Ref("stamp00001"), ".jpg")));
        b.Do("wallpaper1", "dev.wallpaper", ("image", Out("download01")), ("style", ParamValue.Choice("fill")));
        return Done(Device, b, new SuggestedTrigger("system.signIn"));
    }

    private static Starter KeepAwakeOneHour()
    {
        var b = Make("Keep PC Awake 1 h", "DrinkToGo24", "orange", "Stops your PC from going to sleep or turning off the screen for the next hour. Handy for presentations, downloads and long videos.");
        b.Do("awake00001", "dev.keepAwake", ("state", ParamValue.Choice("on")), ("duration", ParamValue.Duration(3600)), ("keepScreenOn", true));
        b.Do("notify001", "ui.notify", ("title", "Keep Awake"), ("body", "Your PC will stay awake for the next hour."));
        return Done(Device, b);
    }

    private static Starter ToggleDarkMode()
    {
        var b = Make("Toggle Dark Mode", "WeatherMoon24", "indigo", "Switches Windows between light and dark mode.");
        b.Do("theme00001", "dev.theme", ("mode", ParamValue.Choice("toggle")), ("apply", ParamValue.Choice("both")));
        b.Do("notify001", "ui.notify", ("title", "Appearance"), ("body", ParamValue.Text("Switched to ", Ref("theme00001"), " mode.")));
        return Done(Device, b);
    }

    private static Starter ToggleMicrophone()
    {
        var b = Make("Toggle Microphone", "MicOff24", "red", "Mutes or unmutes your microphone with one click. Tip: give it a keyboard shortcut to use it during calls.");
        b.Do("mic0000001", "dev.micMute", ("state", ParamValue.Choice("toggle")));
        b.If(ParamValue.AllOf(ConditionRow.Of(Ref("mic0000001"), ConditionOperators.IsTrue)),
            then: t => t.Do("notify001", "ui.notify", ("title", "Microphone"), ("body", "Your microphone is muted.")),
            otherwise: o => o.Do("notify002", "ui.notify", ("title", "Microphone"), ("body", "Your microphone is on.")));
        return Done(Device, b);
    }

    // ======================================================================================================
    // AI
    // ======================================================================================================

    private static Starter TranslateClipboard()
    {
        var b = Make("Translate Clipboard", "Translate24", "indigo",
            "Translates the text you copied into another language, shows it and puts the translation on the clipboard. Uses your default AI provider (set it up in Settings).");
        b.Do("getClip001", "clip.get", ("as", ParamValue.Choice("text")));
        b.Do("translate1", "ai.translate", ("text", Out("getClip001")), ("language", ParamValue.Choice("Spanish")), ("provider", ParamValue.Choice("default")));
        b.Do("setClip001", "clip.set", ("content", Out("translate1")));
        b.Do("show00001", "ui.showResult", ("content", Out("translate1")));
        b.Ask("translate1", "language", "Which language should I translate into?", ParamValue.Choice("Spanish"));
        return Done(Ai, b);
    }

    private static Starter SummarizeWebPage()
    {
        var b = Make("Summarize Web Page", "TextBulletListSquare24", "indigo",
            "Reads a web page and gives you the main points as a short list. Uses the link you give it, or the link on your clipboard, and your default AI provider (set it up in Settings).",
            accepts: new[] { ContentKind.Url, ContentKind.Text }, whenNoInput: WhenNoInput.UseClipboard);
        b.Do("pageText01", "web.pageText", ("url", ParamValue.ShortcutInput()), ("what", ParamValue.Choice("text")));
        b.Do("summary001", "ai.summarize", ("text", Out("pageText01")), ("length", ParamValue.Choice("bullets")), ("provider", ParamValue.Choice("default")));
        b.Do("show00001", "ui.showResult", ("content", Out("summary001")));
        return Done(Ai, b);
    }

    private static Starter FixMyWriting()
    {
        var b = Make("Fix My Writing", "TextEditStyle24", "purple",
            "Fixes spelling and grammar in the text you copied, keeping your own words. The corrected text goes back on the clipboard. Uses your default AI provider (set it up in Settings).",
            accepts: new[] { ContentKind.Text }, whenNoInput: WhenNoInput.UseClipboard);
        b.Do("rewrite001", "ai.rewrite", ("text", ParamValue.ShortcutInput()), ("style", ParamValue.Choice("proofread")), ("provider", ParamValue.Choice("default")));
        b.Do("setClip001", "clip.set", ("content", Out("rewrite001")));
        b.Do("show00001", "ui.showResult", ("content", Out("rewrite001")));
        return Done(Ai, b);
    }

    // ======================================================================================================
    // Automation Ideas
    // ======================================================================================================

    private static Starter BatterySaver()
    {
        var b = Make("Battery Saver", "BatterySaver24", "green",
            "Switches to the power-saving mode and dims the screen to stretch your battery. Tip: add an automation \"When battery drops below 20%\" to run this for you.");
        b.Do("power00001", "dev.powerMode", ("mode", ParamValue.Choice("efficiency")));
        b.Do("bright0001", "dev.brightness", ("level", 40));
        b.Do("notify001", "ui.notify", ("title", "Battery Saver"), ("body", "Power saving is on and the screen is dimmer."));
        return Done(Ideas, b, new SuggestedTrigger("power.battery", ("direction", ParamValue.Choice("below")), ("percent", 20)));
    }

    private static Starter PowerSaverWhenUnplugged()
    {
        var b = Make("Power saver when unplugged", "BatterySaver24", "green",
            "Switches Windows to the Power saver plan to stretch your battery. Tip: add an automation \"When the charger is unplugged\" to run this for you.");
        b.Do("plan000001", "dev.powerPlan.switch", ("plan", ParamValue.Choice("Power saver")));
        b.Do("notify001", "ui.notify", ("title", "Power saver"), ("body", "Switched to the Power saver plan."));
        return Done(Ideas, b, new SuggestedTrigger("power.unplugged"));
    }

    private static Starter HeadphonesVolume()
    {
        var b = Make("Headphones: Volume 40%", "Headphones24", "pink",
            "Sets the volume to a comfortable 40% so music never blasts in your ears. Tip: add an automation \"When headphones connect\" to run this for you.");
        b.Do("volume0001", "dev.setVolume", ("level", 40));
        b.Do("notify001", "ui.notify", ("title", "Volume"), ("body", "Volume set to 40%."));
        return Done(Ideas, b, new SuggestedTrigger("device.audio"));
    }

    private static Starter DarkModeAtSunset()
    {
        var b = Make("Dark Mode at Sunset", "WeatherMoon24", "indigo",
            "Switches Windows to dark mode, easier on the eyes in the evening. Tip: add an automation \"At sunrise or sunset\" set to sunset to run this for you.");
        b.Do("theme00001", "dev.theme", ("mode", ParamValue.Choice("dark")), ("apply", ParamValue.Choice("both")));
        return Done(Ideas, b, new SuggestedTrigger("time.sun", ("event", ParamValue.Choice("sunset")), ("direction", ParamValue.Choice("at"))));
    }

    private static Starter LightModeAtSunrise()
    {
        var b = Make("Light Mode at Sunrise", "WeatherSunny24", "yellow",
            "Switches Windows back to light mode for the day. Tip: add an automation \"At sunrise or sunset\" set to sunrise to run this for you.");
        b.Do("theme00001", "dev.theme", ("mode", ParamValue.Choice("light")), ("apply", ParamValue.Choice("both")));
        return Done(Ideas, b, new SuggestedTrigger("time.sun", ("event", ParamValue.Choice("sunrise")), ("direction", ParamValue.Choice("at"))));
    }
}
