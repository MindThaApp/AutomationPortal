using System.Globalization;

namespace AutomationPortal.Actions.Documents;

internal static class ImageParams
{
    public static ParameterDescriptor Images() =>
        ParameterDescriptor.FileParam("images", "Images", isInput: true) with { Required = true };

    public static ParameterDescriptor Quality(string? visibleWhenKey = null, params string[] values) =>
        ParameterDescriptor.Number("quality", "JPEG quality", 90, 1, 100, "%") with
        {
            Hint = "Higher keeps more detail but makes bigger files. Only used for JPEG pictures.",
            VisibleWhen = visibleWhenKey is null ? null : new VisibleWhen(visibleWhenKey, values),
        };

    public static ParameterDescriptor Suffix(string def) =>
        ParameterDescriptor.Text("suffix", "Add to the file name", def) with
        {
            Hint = "Added to each new file's name, like photo-resized.jpg. The original is never changed.",
        };

    public static ParameterDescriptor[] SaveParams() => OutputTarget.Parameters().ToArray();

    public static int QualityOf(double q) => (int)Math.Clamp(Math.Round(q), 1, 100);

    /// <summary>Runs <paramref name="op"/> on every image on the imaging thread and returns the new pictures.</summary>
    public static async Task<ContentValue> RunBatchAsync(ActionContext ctx, string suffix, Func<string, string> extFor,
        Func<string, string, ImageResult> op, bool replaceSuffixWithNothing = false)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException("There are no pictures to work with. Choose some pictures first.");
        var target = await OutputTarget.ReadAsync(ctx);
        var results = new List<ImageItem>();
        foreach (var img in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(img.Path);
            var dst = target.NewPath(img.Path, stem + suffix + extFor(img.Path));
            var src = img.Path;
            var r = await StaWorker.Imaging.RunAsync(() => op(src, dst), ctx.CancellationToken);
            target.Record(r.Path);   // the file really written ("name (2)" when the name was taken meanwhile)
            ctx.ReportProgress($"Saved {Path.GetFileName(r.Path)}");
            results.Add(r.ToItem());
        }
        return ContentValue.FromItems(results);
    }

    /// <summary>Preview of <see cref="RunBatchAsync"/>: the new file each picture would get. Reads nothing but names; changes nothing.</summary>
    public static async Task<ContentValue> PlanBatchAsync(ActionContext ctx, FileChangePlan plan, string suffix, Func<string, string> extFor)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException("There are no pictures to work with. Choose some pictures first.");
        var target = await OutputTarget.ReadAsync(ctx);
        var results = new List<string>();
        foreach (var img in images)
        {
            var stem = Path.GetFileNameWithoutExtension(img.Path);
            if (target.PlanPath(plan, img.Path, stem + suffix + extFor(img.Path)) is not { } dst) continue;   // into the run's temp folder
            plan.Add(new PlannedChange(PlannedChangeKind.NewFile, ctx.Descriptor.Title, img.Path, dst));
            results.Add(dst);
        }
        return ContentValue.Files(results);
    }

    /// <summary>Keep the picture's own file type when Windows can write it; otherwise use PNG.</summary>
    public static string SameExt(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageFormats.FromExtension(ext) is not null ? ext : ".png";
    }

    public static async Task<double?> Opt(ActionContext ctx, string key) => await ctx.GetNumberOrNullAsync(key);
}

public sealed class ResizeImagesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.resize", Title = "Resize Images", Category = ActionCategories.Images, Icon = "ResizeLarge24",
        Keywords = new[] { "scale", "smaller", "shrink", "width", "height", "photo", "size" },
        Summary = "Resize {images} {by} {width}{height}{longest}{percent}",
        Description = "Makes pictures bigger or smaller and keeps their shape. By default the new copies are saved next to the originals; the originals are never changed.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Resized Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("by", "Resize", "width",
                new Choice("width", "to a width of"), new Choice("height", "to a height of"),
                new Choice("longest", "so the longest side is"), new Choice("percent", "to a size of")),
            ParameterDescriptor.Number("width", "Width", 1280, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "width") },
            ParameterDescriptor.Number("height", "Height", 720, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "height") },
            ParameterDescriptor.Number("longest", "Longest side", 1600, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "longest") },
            ParameterDescriptor.Number("percent", "Percent", 50, 1, 1000, "%") with { VisibleWhen = new VisibleWhen("by", "percent") },
            ParameterDescriptor.Bool("neverEnlarge", "Never make pictures bigger", true),
            ImageParams.Quality(),
            ImageParams.Suffix("-resized"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var spec = new ResizeSpec(
            await ctx.GetChoiceAsync("by", "width"),
            await ctx.GetNumberAsync("width", 1280), await ctx.GetNumberAsync("height", 720),
            await ctx.GetNumberAsync("longest", 1600), await ctx.GetNumberAsync("percent", 50),
            await ctx.GetBoolAsync("neverEnlarge", true));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix", "-resized");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.Resize(s, d, spec, quality));
    }

    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan) =>
        await ImageParams.PlanBatchAsync(ctx, plan, await ctx.GetTextAsync("suffix", "-resized"), ImageParams.SameExt);
}

public sealed class ConvertImagesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.convert", Title = "Convert Images", Category = ActionCategories.Images, Icon = "ImageArrowBack24",
        Keywords = new[] { "jpeg", "jpg", "png", "bmp", "gif", "tiff", "format", "heic", "webp" },
        Summary = "Convert {images} to {format}",
        Description = "Saves pictures in another file type: JPEG, PNG, BMP, GIF or TIFF. Transparent areas become white for JPEG, BMP and GIF.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Converted Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("format", "Convert to", "jpeg",
                new Choice("jpeg", "JPEG"), new Choice("png", "PNG"), new Choice("bmp", "BMP"), new Choice("gif", "GIF"), new Choice("tiff", "TIFF")),
            ImageParams.Quality("format", "jpeg"),
            ImageParams.Suffix(""),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var format = ImageFormats.FromChoice(await ctx.GetChoiceAsync("format", "jpeg"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix");
        return await ImageParams.RunBatchAsync(ctx, suffix, _ => ImageFormats.Extension(format), (s, d) => ImageEngine.Convert(s, d, format, quality));
    }

    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan)
    {
        var ext = ImageFormats.Extension(ImageFormats.FromChoice(await ctx.GetChoiceAsync("format", "jpeg")));
        return await ImageParams.PlanBatchAsync(ctx, plan, await ctx.GetTextAsync("suffix"), _ => ext);
    }
}

public sealed class CropImagesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.crop", Title = "Crop Images", Category = ActionCategories.Images, Icon = "Crop24",
        Keywords = new[] { "trim", "cut", "square", "ratio", "16:9", "4:3", "1:1" },
        Summary = "Crop {images} to {shape}",
        Description = "Cuts pictures down to a shape such as square (1:1), widescreen (16:9) or 4:3 — or to an exact area. The biggest possible piece is kept.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Cropped Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("shape", "Shape", "1:1",
                new Choice("1:1", "square (1:1)"), new Choice("16:9", "widescreen (16:9)"), new Choice("4:3", "4:3"), new Choice("3:2", "3:2"),
                new Choice("9:16", "tall phone screen (9:16)"), new Choice("3:4", "tall 3:4"), new Choice("box", "an exact area")),
            ParameterDescriptor.ChoiceOf("anchor", "Keep the part at", "center",
                new Choice("center", "the center"), new Choice("top", "the top"), new Choice("bottom", "the bottom"),
                new Choice("left", "the left"), new Choice("right", "the right"), new Choice("topLeft", "the top left"),
                new Choice("topRight", "the top right"), new Choice("bottomLeft", "the bottom left"), new Choice("bottomRight", "the bottom right")) with
            {
                VisibleWhen = new VisibleWhen("shape", "1:1", "16:9", "4:3", "3:2", "9:16", "3:4"),
            },
            ParameterDescriptor.Number("x", "Left", 0, 0, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("y", "Top", 0, 0, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("width", "Width", 1000, 1, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("height", "Height", 1000, 1, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ImageParams.Quality(),
            ImageParams.Suffix("-cropped"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var shape = await ctx.GetChoiceAsync("shape", "1:1");
        var spec = shape == "box"
            ? new CropSpec("box", await ctx.GetNumberAsync("x"), await ctx.GetNumberAsync("y"), await ctx.GetNumberAsync("width", 1000), await ctx.GetNumberAsync("height", 1000), "", "center")
            : new CropSpec("ratio", 0, 0, 0, 0, shape, await ctx.GetChoiceAsync("anchor", "center"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix", "-cropped");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.Crop(s, d, spec, quality));
    }
}

public sealed class RotateImagesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.rotate", Title = "Rotate / Flip Images", Category = ActionCategories.Images, Icon = "RotateRight24",
        Keywords = new[] { "turn", "mirror", "flip", "upright", "orientation" },
        Summary = "{action} {images}",
        Description = "Turns pictures or mirrors them. The camera's own rotation note is respected, so photos always start the right way up.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Rotated Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("action", "Action", "right",
                new Choice("right", "Rotate right"), new Choice("left", "Rotate left"), new Choice("180", "Rotate 180°"),
                new Choice("flipH", "Flip left-to-right"), new Choice("flipV", "Flip top-to-bottom"), new Choice("upright", "Make upright using camera info")),
            ImageParams.Quality(),
            ImageParams.Suffix("-rotated"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var op = await ctx.GetChoiceAsync("action", "right");
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix", "-rotated");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.Rotate(s, d, op, quality));
    }
}

public sealed class CombineImagesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.combine", Title = "Combine Images", Category = ActionCategories.Images, Icon = "Image24",
        Keywords = new[] { "merge", "collage", "stitch", "grid", "side by side", "stack" },
        Summary = "Combine {images} {layout}",
        Description = "Joins several pictures into one: side by side, stacked, or in a grid.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Combined Image",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("layout", "Layout", "horizontal",
                new Choice("horizontal", "side by side"), new Choice("vertical", "stacked"), new Choice("grid", "in a grid")),
            ParameterDescriptor.Number("columns", "Pictures per row", 2, 1, 20) with { VisibleWhen = new VisibleWhen("layout", "grid") },
            ParameterDescriptor.Number("spacing", "Space between", 0, 0, 500, "pixels"),
            ParameterDescriptor.Of("background", "Background color", ParamType.Color, "#FFFFFF"),
            ParameterDescriptor.Bool("matchSize", "Make all the same size", true),
            ParameterDescriptor.ChoiceOf("format", "Save as", "png", new Choice("png", "PNG"), new Choice("jpeg", "JPEG")),
            ImageParams.Quality("format", "jpeg"),
            ParameterDescriptor.Text("name", "File name", "Combined"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException("There are no pictures to combine. Choose some pictures first.");
        var spec = new CombineSpec(
            await ctx.GetChoiceAsync("layout", "horizontal"), (int)await ctx.GetNumberAsync("columns", 2),
            await ctx.GetNumberAsync("spacing"), await ctx.GetTextAsync("background", "#FFFFFF"), await ctx.GetBoolAsync("matchSize", true));
        var format = ImageFormats.FromChoice(await ctx.GetChoiceAsync("format", "png"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var name = (await ctx.GetTextAsync("name", "Combined")).Trim();
        if (name.Length == 0) name = "Combined";
        var target = await OutputTarget.ReadAsync(ctx);
        var dst = target.NewPath(images[0].Path, name + ImageFormats.Extension(format));
        var sources = images.Select(i => i.Path).ToList();
        var r = await StaWorker.Imaging.RunAsync(() => ImageEngine.Combine(sources, dst, spec, quality), ctx.CancellationToken);
        target.Record(r.Path);
        return ContentValue.From(r.ToItem());
    }
}

public sealed class GetImageDetailsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.details", Title = "Get Details of Images", Category = ActionCategories.Images, Icon = "ImageSearch24",
        Keywords = new[] { "exif", "size", "date taken", "camera", "gps", "location", "metadata", "dimensions" },
        Summary = "Get {detail} of {images}",
        Description = "Reads facts about a picture: its size, when the photo was taken, the camera, and where it was taken (if the photo has a location).",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Any, OutputName = "Image Details",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("detail", "Detail", "dimensions",
                new Choice("dimensions", "the size (width × height)"), new Choice("width", "the width"), new Choice("height", "the height"),
                new Choice("dateTaken", "the date taken"), new Choice("camera", "the camera"), new Choice("location", "the location"),
                new Choice("all", "everything")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException("There are no pictures to look at. Choose some pictures first.");
        var detail = await ctx.GetChoiceAsync("detail", "dimensions");
        var paths = images.Select(i => i.Path).ToList();
        var infos = await StaWorker.Imaging.RunAsync(() => paths.Select(p => (Path: p, Info: ReadInfo(p))).ToList(), ctx.CancellationToken);
        return Shape(infos, detail);
    }

    private static ImageInfo ReadInfo(string path)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the picture '{Path.GetFileName(path)}'. Check it still exists.");
        try { return ImageMetadataReader.Read(path); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            throw new ActionException($"Couldn't read '{Path.GetFileName(path)}' as a picture.", ex);
        }
    }

    public static string CameraText(ImageInfo i) =>
        string.Join(" ", new[] { i.CameraMake, i.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)))
            is { Length: > 0 } t ? DeduplicateMake(i.CameraMake, i.CameraModel, t) : "";

    private static string DeduplicateMake(string? make, string? model, string joined) =>
        !string.IsNullOrWhiteSpace(make) && !string.IsNullOrWhiteSpace(model) && model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : joined;

    public static ContentValue Shape(IReadOnlyList<(string Path, ImageInfo Info)> infos, string detail)
    {
        var items = new List<ContentItem>();
        foreach (var (path, i) in infos)
        {
            switch (detail)
            {
                case "width": items.Add(new NumberItem(i.Width)); break;
                case "height": items.Add(new NumberItem(i.Height)); break;
                case "dateTaken": if (i.DateTaken is { } d) items.Add(new DateItem(new DateTimeOffset(d, TimeZoneInfo.Local.GetUtcOffset(d)))); break;
                case "camera": if (CameraText(i) is { Length: > 0 } c) items.Add(new TextItem(c)); break;
                case "location": if (i.Latitude is { } la && i.Longitude is { } lo) items.Add(new LocationItem(la, lo)); break;
                case "all":
                    {
                        var entries = new List<KeyValuePair<string, ContentValue>>
                        {
                            new("Name", ContentValue.Text(Path.GetFileName(path))),
                            new("Width", ContentValue.Number(i.Width)),
                            new("Height", ContentValue.Number(i.Height)),
                            new("Size", ContentValue.Text(ContentFormat.Size(new FileInfo(path).Length))),
                            new("Date Taken", i.DateTaken is { } dt ? ContentValue.Date(new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))) : ContentValue.Empty),
                            new("Camera", ContentValue.Text(CameraText(i))),
                            new("Location", i.Latitude is { } a && i.Longitude is { } b ? ContentValue.From(new LocationItem(a, b)) : ContentValue.Empty),
                        };
                        items.Add(new DictionaryItem(entries));
                        break;
                    }
                default: items.Add(new TextItem($"{i.Width} × {i.Height}")); break;
            }
        }
        return ContentValue.FromItems(items);
    }
}

public sealed class RemovePhotoInfoAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.stripInfo", Title = "Remove Photo Info", Category = ActionCategories.Images, Icon = "LocationOff24",
        Keywords = new[] { "exif", "gps", "location", "privacy", "metadata", "strip", "clean" },
        Summary = "Remove location and camera info from {images}",
        Description = "Saves a copy of each picture without its hidden photo information — where it was taken, the camera and the date — so it's safer to share. The picture itself looks the same.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Cleaned Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.Number("quality", "JPEG quality", 95, 1, 100, "%"),
            ImageParams.Suffix("-clean"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 95));
        var suffix = await ctx.GetTextAsync("suffix", "-clean");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.Strip(s, d, quality));
    }
}

public sealed class AddTextToImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.addText", Title = "Add Text to Image", Category = ActionCategories.Images, Icon = "TextT24",
        Keywords = new[] { "caption", "watermark", "label", "write", "overlay", "meme" },
        Summary = "Add {text} to {images} at the {position}",
        Description = "Writes a caption or watermark on top of pictures. Long text wraps to fit.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = "Captioned Images",
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.MultilineText("text", "Text", null, "e.g. Summer 2026") with { Required = true },
            ParameterDescriptor.ChoiceOf("position", "Position", "bottom",
                new Choice("topLeft", "top left"), new Choice("top", "top"), new Choice("topRight", "top right"),
                new Choice("left", "left"), new Choice("center", "center"), new Choice("right", "right"),
                new Choice("bottomLeft", "bottom left"), new Choice("bottom", "bottom"), new Choice("bottomRight", "bottom right")),
            ParameterDescriptor.Number("size", "Text size", 5, 1, 100, "% of the picture") with { Hint = "Relative to the shorter side of the picture, so it looks the same on big and small photos." },
            ParameterDescriptor.Of("color", "Text color", ParamType.Color, "#FFFFFF"),
            ParameterDescriptor.Bool("shadow", "Add a shadow so it's easy to read", true),
            ParameterDescriptor.Bool("bold", "Bold", false),
            ParameterDescriptor.Text("font", "Font", "Segoe UI"),
            ImageParams.Quality(),
            ImageParams.Suffix("-text"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException("Type the text to put on the pictures.");
        var spec = new TextSpec(text, await ctx.GetChoiceAsync("position", "bottom"), await ctx.GetNumberAsync("size", 5),
            await ctx.GetTextAsync("color", "#FFFFFF"), await ctx.GetBoolAsync("shadow", true), await ctx.GetBoolAsync("bold"),
            await ctx.GetTextAsync("font", "Segoe UI"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix", "-text");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.AddText(s, d, spec, quality));
    }
}
