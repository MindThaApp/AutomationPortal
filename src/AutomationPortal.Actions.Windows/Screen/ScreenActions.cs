using System.Drawing;
using System.Drawing.Imaging;
using AutomationPortal.Actions.Windows.Apps;

namespace AutomationPortal.Actions.Windows.Screen;

public sealed class TakeScreenshotAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.capture", Title = "Take Screenshot", Category = ActionCategories.Screen, Icon = "ScreenshotRecord24",
        Keywords = new[] { "capture", "screen grab", "snip", "picture of screen", "window", "region" },
        Summary = "Take a screenshot of {area}",
        Description = "Takes a picture of your screen, all your screens, the front window, or an area you select. The picture is kept for the rest of the shortcut; you can also save a copy in Pictures > Screenshots.",
        OutputKind = ContentKind.Image, OutputName = "Screenshot",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("area", "Capture", "screen",
                new Choice("screen", "the main screen"), new Choice("all", "all screens"), new Choice("window", "the front window"), new Choice("select", "an area I select")),
            ParameterDescriptor.Duration("delay", "Wait first", 0),
            ParameterDescriptor.Bool("saveCopy", "Also save a copy in Pictures > Screenshots", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var area = await ctx.GetChoiceAsync("area", "screen");
        var delay = await ctx.GetDurationAsync("delay", TimeSpan.Zero);
        var saveCopy = await ctx.GetBoolAsync("saveCopy", false);
        var ct = ctx.CancellationToken;

        ScreenRect? region = null;
        if (area == "select")
        {
            region = await ctx.Services.UserInteraction.SelectScreenRegionAsync(ct).ConfigureAwait(false) ?? throw new UserCancelledException();
        }
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);

        var path = ctx.NewTempPath($"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        var size = await Task.Run(() =>
        {
            using var bmp = area switch
            {
                "all" => ScreenCapture.CaptureAllScreens(),
                "window" => ScreenCapture.CaptureWindow((WindowService.GetFront() ?? throw new ActionException("There's no window open to take a picture of.")).Handle),
                "select" => ScreenCapture.CaptureRect(region!.Value.X, region.Value.Y, region.Value.Width, region.Value.Height),
                _ => ScreenCapture.CapturePrimaryScreen(),
            };
            bmp.Save(path, ImageFormat.Png);
            return (bmp.Width, bmp.Height);
        }, ct).ConfigureAwait(false);

        if (saveCopy)
        {
            var folder = ScreenCapture.ScreenshotsFolder()!;
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(path));
            for (var n = 2; File.Exists(copy); n++) copy = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)} {n}.png");
            File.Copy(path, copy);
        }
        return ContentValue.Image(path, size.Item1, size.Item2);
    }
}

public sealed class GetTextFromImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.ocr", Title = "Get Text from Image", Category = ActionCategories.Screen, Icon = "TextDescription24",
        Keywords = new[] { "ocr", "read text", "recognize", "scan", "picture to text", "image to text" },
        Summary = "Get the text from {image}",
        Description = "Reads the words in a picture (a screenshot, photo or scan) using the text recognition built into Windows. It all happens on your PC.",
        InputKinds = new[] { ContentKind.Image, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "Text from Image",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", "Image", isInput: true) with { Required = true },
            OcrService.LanguageParam(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("image");
        if (images.Count == 0) throw new ActionException("There's no picture to read. Choose an image first.");
        var language = await ctx.GetChoiceAsync("language", "auto");
        var parts = new List<string>();
        foreach (var image in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!image.Exists) throw new ActionException($"Couldn't find the picture '{image.FullName}'. Check it still exists.");
            parts.Add(await OcrService.RecognizeFileAsync(image.Path, language, ctx.CancellationToken).ConfigureAwait(false));
        }
        return ContentValue.Text(string.Join("\n\n", parts));
    }
}

public sealed class GrabTextFromScreenAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.grabText", Title = "Grab Text from Screen", Category = ActionCategories.Screen, Icon = "TextGrammarSettings24",
        Keywords = new[] { "ocr", "copy text", "select area", "capture text", "read screen", "text from picture" },
        Summary = "Select an area of the screen and get its text",
        Description = "Lets you drag a box around part of the screen, then reads the words inside it (even in pictures, videos or apps that don't let you copy). Combine with Copy to Clipboard to paste it anywhere.",
        OutputKind = ContentKind.Text, OutputName = "Grabbed Text",
        Parameters = new[] { OcrService.LanguageParam() },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var region = await ctx.Services.UserInteraction.SelectScreenRegionAsync(ct).ConfigureAwait(false) ?? throw new UserCancelledException();
        var language = await ctx.GetChoiceAsync("language", "auto");
        var path = ctx.NewTempPath("Grabbed area.png");
        await Task.Run(() =>
        {
            using var bmp = ScreenCapture.CaptureRect(region.X, region.Y, region.Width, region.Height);
            bmp.Save(path, ImageFormat.Png);
        }, ct).ConfigureAwait(false);
        var text = await OcrService.RecognizeFileAsync(path, language, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            throw new ActionException("Couldn't find any text in that area. Try again with a slightly bigger area around the words.");
        return ContentValue.Text(text);
    }
}

public sealed class PickColorAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.pickColor", Title = "Pick Color from Screen", Category = ActionCategories.Screen, Icon = "Eyedropper24",
        Keywords = new[] { "colour", "eyedropper", "hex", "rgb", "pixel" },
        Summary = "Pick a color from the screen as {format}",
        Description = "Lets you click anywhere on the screen and gives back that spot's color.",
        OutputKind = ContentKind.Text, OutputName = "Color",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("format", "Format", "hex", new Choice("hex", "hex code (#RRGGBB)"), new Choice("rgb", "red, green, blue numbers")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var point = await ctx.Services.UserInteraction.SelectScreenPointAsync(ctx.CancellationToken).ConfigureAwait(false) ?? throw new UserCancelledException();
        var rgb = await Task.Run(() => ScreenCapture.PixelAt(point.X, point.Y), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Text(await ctx.GetChoiceAsync("format", "hex") == "rgb" ? ColorFormat.Rgb(rgb.R, rgb.G, rgb.B) : ColorFormat.Hex(rgb.R, rgb.G, rgb.B));
    }
}
