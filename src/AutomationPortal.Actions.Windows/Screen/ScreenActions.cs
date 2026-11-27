using System.Drawing;
using System.Drawing.Imaging;
using AutomationPortal.Actions.Windows.Apps;

namespace AutomationPortal.Actions.Windows.Screen;

public sealed class TakeScreenshotAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.capture", Title = Strings.Get("TakeScreenshotAction_Title"), Category = ActionCategories.Screen, Icon = "ScreenshotRecord24",
        Keywords = new[] { "capture", "screen grab", "snip", "picture of screen", "window", "region" },
        Summary = Strings.Get("TakeScreenshotAction_Summary"),
        Description = Strings.Get("TakeScreenshotAction_Description"),
        OutputKind = ContentKind.Image, OutputName = Strings.Get("TakeScreenshotAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("area", Strings.Get("TakeScreenshotAction_Param_Area_Label"), "screen",
                new Choice("screen", Strings.Get("TakeScreenshotAction_Param_Area_Choice_Screen")), new Choice("all", Strings.Get("TakeScreenshotAction_Param_Area_Choice_All")), new Choice("window", Strings.Get("TakeScreenshotAction_Param_Area_Choice_Window")), new Choice("select", Strings.Get("TakeScreenshotAction_Param_Area_Choice_Select"))),
            ParameterDescriptor.Duration("delay", Strings.Get("TakeScreenshotAction_Param_Delay_Label"), 0),
            ParameterDescriptor.Bool("saveCopy", Strings.Get("TakeScreenshotAction_Param_SaveCopy_Label"), false),
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
                "window" => ScreenCapture.CaptureWindow((WindowService.GetFront() ?? throw new ActionException(Strings.Get("TakeScreenshotAction_Error_There_S_No_Window_Open_To"))).Handle),
                "select" => ScreenCapture.CaptureRect(region!.Value.X, region.Value.Y, region.Value.Width, region.Value.Height),
                _ => ScreenCapture.CapturePrimaryScreen(),
            };
            bmp.Save(path, ImageFormat.Png);
            return (bmp.Width, bmp.Height);
        }, ct).ConfigureAwait(false);

        if (saveCopy)
        {
            var folder = ScreenCapture.ScreenshotsFolder()!;
            TestRootGuard.Check(folder);
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(path));
            for (var n = 2; File.Exists(copy); n++) copy = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)} {n}.png");
            File.Copy(path, copy);
            ctx.Journal.Created(copy);
        }
        return ContentValue.Image(path, size.Item1, size.Item2);
    }
}

public sealed class GetTextFromImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.ocr", Title = Strings.Get("GetTextFromImageAction_Title"), Category = ActionCategories.Screen, Icon = "TextDescription24",
        Keywords = new[] { "ocr", "read text", "recognize", "scan", "picture to text", "image to text" },
        Summary = Strings.Get("GetTextFromImageAction_Summary"),
        Description = Strings.Get("GetTextFromImageAction_Description"),
        InputKinds = new[] { ContentKind.Image, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = Strings.Get("GetTextFromImageAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", Strings.Get("GetTextFromImageAction_Param_Image_Label"), isInput: true) with { Required = true },
            OcrService.LanguageParam(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("image");
        if (images.Count == 0) throw new ActionException(Strings.Get("GetTextFromImageAction_Error_There_S_No_Picture_To_Read"));
        var language = await ctx.GetChoiceAsync("language", "auto");
        var parts = new List<string>();
        foreach (var image in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!image.Exists) throw new ActionException(Strings.Format("GetTextFromImageAction_Error_Couldn_T_Find_The_Picture_0", image.FullName));
            parts.Add(await OcrService.RecognizeFileAsync(image.Path, language, ctx.CancellationToken).ConfigureAwait(false));
        }
        return ContentValue.Text(string.Join("\n\n", parts));
    }
}

public sealed class GrabTextFromScreenAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.grabText", Title = Strings.Get("GrabTextFromScreenAction_Title"), Category = ActionCategories.Screen, Icon = "TextGrammarSettings24",
        Keywords = new[] { "ocr", "copy text", "select area", "capture text", "read screen", "text from picture" },
        Summary = Strings.Get("GrabTextFromScreenAction_Summary"),
        Description = Strings.Get("GrabTextFromScreenAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("GrabTextFromScreenAction_OutputName"),
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
            throw new ActionException(Strings.Get("GrabTextFromScreenAction_Error_Couldn_T_Find_Any_Text_In"));
        return ContentValue.Text(text);
    }
}

public sealed class PickColorAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "screen.pickColor", Title = Strings.Get("PickColorAction_Title"), Category = ActionCategories.Screen, Icon = "Eyedropper24",
        Keywords = new[] { "colour", "eyedropper", "hex", "rgb", "pixel" },
        Summary = Strings.Get("PickColorAction_Summary"),
        Description = Strings.Get("PickColorAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("PickColorAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("format", Strings.Get("PickColorAction_Param_Format_Label"), "hex", new Choice("hex", Strings.Get("PickColorAction_Param_Format_Choice_Hex")), new Choice("rgb", Strings.Get("PickColorAction_Param_Format_Choice_Rgb"))),
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
