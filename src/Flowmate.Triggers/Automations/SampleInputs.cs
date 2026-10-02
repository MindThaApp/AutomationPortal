using Flowmate.Core;

namespace Flowmate.Triggers;

/// <summary>Made-up trigger input for the "Test now" button, shaped like what the real trigger would give the shortcut.</summary>
public static class SampleInputs
{
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public static ContentValue For(TriggerDescriptor descriptor, TriggerConfig config, FlowmatePaths paths)
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
                    "image" => ContentValue.Image(SampleImage(paths)),
                    "files" => ContentValue.Files(new[] { SampleFile(paths) }),
                    "contains" => ContentValue.Text("Sample text containing " + config.GetString("text")),
                    _ => ContentValue.Text("Sample copied text"),
                };
            case "app.opened" or "app.closed" or "app.focused":
                if (config.GetApp("app") is { } app) return ContentValue.From(new AppItem(app.Id, app.Name ?? app.Id));
                break;
            case "file.changed":
                return ContentValue.Files(new[] { SampleFile(paths) });
            case "drive.connected":
                return ContentValue.Folder(Path.GetPathRoot(paths.TempRoot) ?? paths.TempRoot);
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
            ContentKind.File => ContentValue.Files(new[] { SampleFile(paths) }),
            ContentKind.Folder => ContentValue.Folder(paths.TempRoot),
            ContentKind.Image => ContentValue.Image(SampleImage(paths), 1, 1),
            ContentKind.App => ContentValue.From(new AppItem("Sample app", "Sample app")),
            ContentKind.Dictionary => ContentValue.Dict(("Name", ContentValue.Text("Sample"))),
            _ => ContentValue.Text("Sample input"),
        };
    }

    private static ContentValue Item(string title, string link, string summary) =>
        ContentValue.Dict(("Title", ContentValue.Text(title)), ("Link", ContentValue.Text(link)), ("Summary", ContentValue.Text(summary)),
            ("Published", ContentValue.Date(DateTimeOffset.Now)), ("Feed", ContentValue.Text("Sample feed")));

    private static string SampleFolder(FlowmatePaths paths)
    {
        var dir = Path.Combine(paths.TempRoot, "trigger-samples");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SampleFile(FlowmatePaths paths)
    {
        var path = Path.Combine(SampleFolder(paths), "Sample file.txt");
        if (!File.Exists(path)) File.WriteAllText(path, "This is a sample file made by Flowmate's \"Test now\" button.");
        return path;
    }

    private static string SampleImage(FlowmatePaths paths)
    {
        var path = Path.Combine(SampleFolder(paths), "Sample picture.png");
        if (!File.Exists(path)) File.WriteAllBytes(path, Convert.FromBase64String(TinyPng));
        return path;
    }
}
