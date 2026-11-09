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
        Subfolder = Path.Combine(folder, "Sample subfolder");
        TextFile = Path.Combine(folder, "Sample file.txt");
        ImageFile = Path.Combine(folder, "Sample picture.png");
        SubfolderFile = Path.Combine(Subfolder, "Another sample file.txt");
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
        sandbox.Write(sandbox.TextFile, System.Text.Encoding.UTF8.GetBytes("This is a sample file made by Automation Portal's \"Test now\" button."));
        sandbox.Write(sandbox.ImageFile, Convert.FromBase64String(TinyPng));
        sandbox.Write(sandbox.SubfolderFile, System.Text.Encoding.UTF8.GetBytes("Another sample file made by Automation Portal's \"Test now\" button."));
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
            ? ContentValue.Text("Sample input")
            : For(provider.Descriptor, TriggerConfig.From(automation.Trigger, provider.Descriptor.Parameters), sandbox);
    }

    public static ContentValue For(TriggerDescriptor descriptor, TriggerConfig config, SampleSandbox sandbox)
    {
        switch (descriptor.Id)
        {
            case "web.rssNew":
                return ContentValue.Concat(
                    Item("Sample news item", "https://example.com/news/1", "A short summary of the first sample item."),
                    Item("Another sample item", "https://example.com/news/2", "A short summary of the second sample item."));
            case "cal.eventSoon":
                return ContentValue.Dict(
                    ("Title", ContentValue.Text("Sample meeting")), ("Start", ContentValue.Date(DateTimeOffset.Now.AddMinutes(10))),
                    ("End", ContentValue.Date(DateTimeOffset.Now.AddMinutes(40))), ("Location", ContentValue.Text("Meeting room")),
                    ("Description", ContentValue.Text("Sample description")));
            case "key.typed":
                return ContentValue.Text(config.GetString("phrase", ";sample"));
            case "clip.changed":
                return config.GetChoice("kind", "any") switch
                {
                    "link" => ContentValue.Url("https://example.com"),
                    "image" => ContentValue.Image(sandbox.ImageFile),
                    "files" => ContentValue.Files(new[] { sandbox.TextFile }),
                    "contains" => ContentValue.Text("Sample text containing " + config.GetString("text")),
                    _ => ContentValue.Text("Sample copied text"),
                };
            case "app.opened" or "app.closed" or "app.focused":
                if (config.GetApp("app") is { } app) return ContentValue.From(new AppItem(app.Id, app.Name ?? app.Id));
                break;
            case "file.changed":
                return ContentValue.Files(new[] { sandbox.TextFile });
            case "drive.connected":
                return ContentValue.Folder(sandbox.Folder);
            case "device.bluetooth" or "device.usb" or "device.audio":
                return ContentValue.Text(config.GetString("device") is { Length: > 0 } d ? d : "Sample device");
            case "net.wifiJoined" or "net.wifiLeft":
                return ContentValue.Text(config.GetString("network") is { Length: > 0 } n ? n : "Sample Wi-Fi");
        }

        return descriptor.OutputKind switch
        {
            ContentKind.Text => ContentValue.Text("Sample text"),
            ContentKind.Number => ContentValue.Number(42),
            ContentKind.Boolean => ContentValue.Bool(true),
            ContentKind.Date => ContentValue.Date(DateTimeOffset.Now),
            ContentKind.Url => ContentValue.Url("https://example.com"),
            ContentKind.File => ContentValue.Files(new[] { sandbox.TextFile }),
            ContentKind.Folder => ContentValue.Folder(sandbox.Folder),
            ContentKind.Image => ContentValue.Image(sandbox.ImageFile, 1, 1),
            ContentKind.App => ContentValue.From(new AppItem("Sample app", "Sample app")),
            ContentKind.Dictionary => ContentValue.Dict(("Name", ContentValue.Text("Sample"))),
            _ => ContentValue.Text("Sample input"),
        };
    }

    private static ContentValue Item(string title, string link, string summary) =>
        ContentValue.Dict(("Title", ContentValue.Text(title)), ("Link", ContentValue.Text(link)), ("Summary", ContentValue.Text(summary)),
            ("Published", ContentValue.Date(DateTimeOffset.Now)), ("Feed", ContentValue.Text("Sample feed")));
}
