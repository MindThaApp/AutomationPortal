using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>
/// A fresh throwaway folder for one "Test now" run, with a few harmless sample files: a small text file, a tiny picture and a
/// subfolder with one file. "Test now" hands the shortcut these instead of real places such as Documents or a drive.
/// Disposing it removes only the files it made, and the folders once they are empty: anything else that ended up inside is kept.
/// </summary>
public sealed class SampleSandbox : IDisposable
{
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private readonly Dictionary<string, byte[]> _made = new(StringComparer.OrdinalIgnoreCase);
    private bool _cleaned;

    private SampleSandbox(string folder)
    {
        Folder = folder;
        Subfolder = Path.Combine(folder, Strings.Get("Sample_Subfolder"));
        TextFile = Path.Combine(folder, Strings.Get("Sample_TextFile"));
        ImageFile = Path.Combine(folder, Strings.Get("Sample_Picture"));
        SubfolderFile = Path.Combine(Subfolder, Strings.Get("Sample_SubfolderFile"));
    }

    /// <summary>The sandbox folder (stands in for a drive or a watched folder).</summary>
    public string Folder { get; }
    public string Subfolder { get; }
    public string TextFile { get; }
    public string ImageFile { get; }
    public string SubfolderFile { get; }

    /// <summary>After <see cref="Dispose"/>: true when something the sandbox didn't make was left in it, so the folder was kept.</summary>
    public bool HasLeftovers { get; private set; }

    /// <summary>Makes a new, empty sandbox under <c>TempRoot\trigger-samples</c> and fills it with the sample files.</summary>
    public static SampleSandbox Create(AutomationPortalPaths paths)
    {
        var root = Path.Combine(paths.TempRoot, "trigger-samples");
        Directory.CreateDirectory(root);
        string folder;
        do folder = Path.Combine(root, "Test " + Guid.NewGuid().ToString("N")[..12]);
        while (Directory.Exists(folder) || File.Exists(folder));

        var sandbox = new SampleSandbox(folder);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(sandbox.Subfolder);
        sandbox.Write(sandbox.TextFile, System.Text.Encoding.UTF8.GetBytes(Strings.Get("Sample_TextFile_Content")));
        sandbox.Write(sandbox.ImageFile, Convert.FromBase64String(TinyPng));
        sandbox.Write(sandbox.SubfolderFile, System.Text.Encoding.UTF8.GetBytes(Strings.Get("Sample_SubfolderFile_Content")));
        return sandbox;
    }

    private void Write(string path, byte[] bytes)
    {
        // CreateNew: the folder is brand new, so this never replaces anything.
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) fs.Write(bytes);
        _made[path] = bytes;
    }

    /// <summary>Removes the sample files that are still exactly as made, then the sandbox folders if they are empty.</summary>
    public void Dispose()
    {
        if (_cleaned) return;
        _cleaned = true;
        foreach (var (path, bytes) in _made)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != bytes.Length) continue;
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TriggerLog.Write("Couldn't remove a test sample file: " + ex.Message);
            }
        }
        foreach (var dir in new[] { Subfolder, Folder })
        {
            try
            {
                // Not recursive: a folder that still holds anything is left alone.
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TriggerLog.Write("Couldn't remove a test sample folder: " + ex.Message);
            }
        }
        HasLeftovers = Directory.Exists(Folder);
        if (HasLeftovers) TriggerLog.Write($"\"Test now\" left files in {Folder}; they were kept.");
    }
}

/// <summary>Made-up trigger input for the "Test now" button, shaped like what the real trigger would give the shortcut.</summary>
public static class SampleInputs
{
    /// <summary>Sample input for testing <paramref name="automation"/>. Files, pictures and folders all point into <paramref name="sandbox"/>.</summary>
    public static ContentValue For(Automation automation, TriggerRegistry triggers, SampleSandbox sandbox)
    {
        var provider = triggers.Get(automation.Trigger.TriggerId);
        return provider is null
            ? ContentValue.Text(Strings.Get("Sample_Input"))
            : For(provider.Descriptor, TriggerConfig.From(automation.Trigger, provider.Descriptor.Parameters), sandbox);
    }

    public static ContentValue For(TriggerDescriptor descriptor, TriggerConfig config, SampleSandbox sandbox)
    {
        switch (descriptor.Id)
        {
            case "web.rssNew":
                return ContentValue.Concat(
                    Item(Strings.Get("Sample_News1_Title"), "https://example.com/news/1", Strings.Get("Sample_News1_Summary")),
                    Item(Strings.Get("Sample_News2_Title"), "https://example.com/news/2", Strings.Get("Sample_News2_Summary")));
            case "cal.eventSoon":
                return ContentValue.Dict(
                    ("Title", ContentValue.Text(Strings.Get("Sample_Meeting_Title"))), ("Start", ContentValue.Date(DateTimeOffset.Now.AddMinutes(10))),
                    ("End", ContentValue.Date(DateTimeOffset.Now.AddMinutes(40))), ("Location", ContentValue.Text(Strings.Get("Sample_Meeting_Location"))),
                    ("Description", ContentValue.Text(Strings.Get("Sample_Meeting_Description"))));
            case "key.typed":
                return ContentValue.Text(config.GetString("phrase", ";sample"));
            case "clip.changed":
                return config.GetChoice("kind", "any") switch
                {
                    "link" => ContentValue.Url("https://example.com"),
                    "image" => ContentValue.Image(sandbox.ImageFile),
                    "files" => ContentValue.Files(new[] { sandbox.TextFile }),
                    "contains" => ContentValue.Text(Strings.Format("Sample_Clip_Contains", config.GetString("text"))),
                    _ => ContentValue.Text(Strings.Get("Sample_Clip_Text")),
                };
            case "notif.arrived":
                return ContentValue.Dict(
                    ("Title", ContentValue.Text(Strings.Get("Sample_Notif_Title"))), ("Text", ContentValue.Text(Strings.Get("Sample_Notif_Text"))), ("App", ContentValue.Text(Strings.Get("Sample_App"))));
            case "app.event":
                return AppEventSample(config);
            case "app.opened" or "app.closed" or "app.focused":
                if (config.GetApp("app") is { } app) return ContentValue.From(new AppItem(app.Id, app.Name ?? app.Id));
                break;
            case "file.changed":
                return ContentValue.Files(new[] { sandbox.TextFile });
            case "drive.connected":
                return ContentValue.Folder(sandbox.Folder);
            case "device.bluetooth" or "device.usb" or "device.audio":
                return ContentValue.Text(config.GetString("device") is { Length: > 0 } d ? d : Strings.Get("Sample_Device"));
            case "net.wifiJoined" or "net.wifiLeft":
                return ContentValue.Text(config.GetString("network") is { Length: > 0 } n ? n : Strings.Get("Sample_Wifi"));
        }

        return descriptor.OutputKind switch
        {
            ContentKind.Text => ContentValue.Text(Strings.Get("Sample_Text")),
            ContentKind.Number => ContentValue.Number(42),
            ContentKind.Boolean => ContentValue.Bool(true),
            ContentKind.Date => ContentValue.Date(DateTimeOffset.Now),
            ContentKind.Url => ContentValue.Url("https://example.com"),
            ContentKind.File => ContentValue.Files(new[] { sandbox.TextFile }),
            ContentKind.Folder => ContentValue.Folder(sandbox.Folder),
            ContentKind.Image => ContentValue.Image(sandbox.ImageFile, 1, 1),
            ContentKind.App => ContentValue.From(new AppItem(Strings.Get("Sample_App"), Strings.Get("Sample_App"))),
            ContentKind.Dictionary => ContentValue.Dict(("Name", ContentValue.Text(Strings.Get("Sample_Name")))),
            _ => ContentValue.Text(Strings.Get("Sample_Input")),
        };
    }

    private static ContentValue AppEventSample(TriggerConfig config)
    {
        var app = config.GetChoice("app", "deskpal");
        var ev = config.GetChoice("event", "break.started");
        var kind = config.GetChoice("breakKind", "any") is { } k && k != "any" ? k : "stretch";
        var now = DateTimeOffset.Now;
        var fields = new Dictionary<string, object>
        {
            ["event"] = app + "." + ev, ["at"] = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture), ["cause"] = "user",
        };
        switch (ev)
        {
            case "break.started" or "break.skipped": fields["kind"] = kind; break;
            case "break.done": fields["kind"] = kind; fields["minutes"] = 4; break;
            case "focus.on": fields["minutes"] = 25; break;
            case "focus.off": fields["reason"] = "ended"; break;
            case "pause.on": fields["until"] = now.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture); break;
            case "goal.reached": fields["goal"] = "stretches"; break;
            case "day.summary": fields["counts"] = new Dictionary<string, int> { ["stretches"] = 6, ["eyeBreaks"] = 11, ["walks"] = 2 }; break;
        }
        return AppEventMatcher.ToContent(new AppEvent
        {
            Name = app + "." + ev, Payload = System.Text.Json.JsonSerializer.Serialize(fields), Source = app, ReceivedAt = now,
        });
    }

    private static ContentValue Item(string title, string link, string summary) =>
        ContentValue.Dict(("Title", ContentValue.Text(title)), ("Link", ContentValue.Text(link)), ("Summary", ContentValue.Text(summary)),
            ("Published", ContentValue.Date(DateTimeOffset.Now)), ("Feed", ContentValue.Text(Strings.Get("Sample_Feed"))));
}
