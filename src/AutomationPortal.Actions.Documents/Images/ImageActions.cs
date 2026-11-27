using System.Globalization;

namespace AutomationPortal.Actions.Documents;

internal static class ImageParams
{
    public static ParameterDescriptor Images() =>
        ParameterDescriptor.FileParam("images", Strings.Get("ImageParams_Param_images_Label"), isInput: true) with { Required = true };

    public static ParameterDescriptor Quality(string? visibleWhenKey = null, params string[] values) =>
        ParameterDescriptor.Number("quality", Strings.Get("ImageParams_Param_quality_Label"), 90, 1, 100, "%") with
        {
            Hint = Strings.Get("ImageParams_Param_quality_Hint"),
            VisibleWhen = visibleWhenKey is null ? null : new VisibleWhen(visibleWhenKey, values),
        };

    public static ParameterDescriptor Suffix(string def) =>
        ParameterDescriptor.Text("suffix", Strings.Get("ImageParams_Param_suffix_Label"), def) with
        {
            Hint = Strings.Get("ImageParams_Param_suffix_Hint"),
        };

    public static ParameterDescriptor[] SaveParams() => OutputTarget.Parameters().ToArray();

    public static int QualityOf(double q) => (int)Math.Clamp(Math.Round(q), 1, 100);

    /// <summary>Runs <paramref name="op"/> on every image on the imaging thread and returns the new pictures.</summary>
    public static async Task<ContentValue> RunBatchAsync(ActionContext ctx, string suffix, Func<string, string> extFor,
        Func<string, string, ImageResult> op, bool replaceSuffixWithNothing = false)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("CompressMediaAction_Error_ThereAreNoPictures"));
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
            ctx.ReportCount(results.Count, images.Count);
        }
        return ContentValue.FromItems(results);
    }

    /// <summary>Preview of <see cref="RunBatchAsync"/>: the new file each picture would get. Reads nothing but names; changes nothing.</summary>
    public static async Task<ContentValue> PlanBatchAsync(ActionContext ctx, FileChangePlan plan, string suffix, Func<string, string> extFor)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("CompressMediaAction_Error_ThereAreNoPictures"));
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
        Id = "img.resize", Title = Strings.Get("ResizeImagesAction_Title"), Category = ActionCategories.Images, Icon = "ResizeLarge24",
        Keywords = new[] { "scale", "smaller", "shrink", "width", "height", "photo", "size" },
        Summary = Strings.Get("ResizeImagesAction_Summary"),
        Description = Strings.Get("ResizeImagesAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("ResizeImagesAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("by", Strings.Get("ResizeImagesAction_Param_by_Label"), "width",
                new Choice("width", Strings.Get("ResizeImagesAction_Param_by_Choice_width")), new Choice("height", Strings.Get("ResizeImagesAction_Param_by_Choice_height")),
                new Choice("longest", Strings.Get("ResizeImagesAction_Param_by_Choice_longest")), new Choice("percent", Strings.Get("ResizeImagesAction_Param_by_Choice_percent"))),
            ParameterDescriptor.Number("width", Strings.Get("ResizeImagesAction_Param_width_Label"), 1280, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "width") },
            ParameterDescriptor.Number("height", Strings.Get("ResizeImagesAction_Param_height_Label"), 720, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "height") },
            ParameterDescriptor.Number("longest", Strings.Get("ResizeImagesAction_Param_longest_Label"), 1600, 1, 30000, "pixels") with { VisibleWhen = new VisibleWhen("by", "longest") },
            ParameterDescriptor.Number("percent", Strings.Get("ResizeImagesAction_Param_percent_Label"), 50, 1, 1000, "%") with { VisibleWhen = new VisibleWhen("by", "percent") },
            ParameterDescriptor.Bool("neverEnlarge", Strings.Get("ResizeImagesAction_Param_neverEnlarge_Label"), true),
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
        Id = "img.convert", Title = Strings.Get("ConvertImagesAction_Title"), Category = ActionCategories.Images, Icon = "ImageArrowBack24",
        Keywords = new[] { "jpeg", "jpg", "png", "bmp", "gif", "tiff", "format", "heic", "webp" },
        Summary = Strings.Get("ConvertImagesAction_Summary"),
        Description = Strings.Get("ConvertImagesAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("ConvertImagesAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("format", Strings.Get("ConvertImagesAction_Param_format_Label"), "jpeg",
                new Choice("jpeg", Strings.Get("ConvertImagesAction_Param_format_Choice_jpeg")), new Choice("png", Strings.Get("ConvertImagesAction_Param_format_Choice_png")), new Choice("bmp", Strings.Get("ConvertImagesAction_Param_format_Choice_bmp")), new Choice("gif", Strings.Get("ConvertImagesAction_Param_format_Choice_gif")), new Choice("tiff", Strings.Get("ConvertImagesAction_Param_format_Choice_tiff"))),
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
        Id = "img.crop", Title = Strings.Get("CropImagesAction_Title"), Category = ActionCategories.Images, Icon = "Crop24",
        Keywords = new[] { "trim", "cut", "square", "ratio", "16:9", "4:3", "1:1" },
        Summary = Strings.Get("CropImagesAction_Summary"),
        Description = Strings.Get("CropImagesAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("CropImagesAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("shape", Strings.Get("CropImagesAction_Param_shape_Label"), "1:1",
                new Choice("1:1", Strings.Get("CropImagesAction_Param_shape_Choice_11")), new Choice("16:9", Strings.Get("CropImagesAction_Param_shape_Choice_169")), new Choice("4:3", "4:3"), new Choice("3:2", "3:2"),
                new Choice("9:16", Strings.Get("CropImagesAction_Param_shape_Choice_916")), new Choice("3:4", Strings.Get("CropImagesAction_Param_shape_Choice_34")), new Choice("box", Strings.Get("CropImagesAction_Param_shape_Choice_box"))),
            ParameterDescriptor.ChoiceOf("anchor", Strings.Get("CropImagesAction_Param_anchor_Label"), "center",
                new Choice("center", Strings.Get("CropImagesAction_Param_anchor_Choice_center")), new Choice("top", Strings.Get("CropImagesAction_Param_anchor_Choice_top")), new Choice("bottom", Strings.Get("CropImagesAction_Param_anchor_Choice_bottom")),
                new Choice("left", Strings.Get("CropImagesAction_Param_anchor_Choice_left")), new Choice("right", Strings.Get("CropImagesAction_Param_anchor_Choice_right")), new Choice("topLeft", Strings.Get("CropImagesAction_Param_anchor_Choice_topLeft")),
                new Choice("topRight", Strings.Get("CropImagesAction_Param_anchor_Choice_topRight")), new Choice("bottomLeft", Strings.Get("CropImagesAction_Param_anchor_Choice_bottomLeft")), new Choice("bottomRight", Strings.Get("CropImagesAction_Param_anchor_Choice_bottomRight"))) with
            {
                VisibleWhen = new VisibleWhen("shape", "1:1", "16:9", "4:3", "3:2", "9:16", "3:4"),
            },
            ParameterDescriptor.Number("x", Strings.Get("CropImagesAction_Param_x_Label"), 0, 0, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("y", Strings.Get("CropImagesAction_Param_y_Label"), 0, 0, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("width", Strings.Get("CropImagesAction_Param_width_Label"), 1000, 1, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
            ParameterDescriptor.Number("height", Strings.Get("CropImagesAction_Param_height_Label"), 1000, 1, null, "pixels") with { VisibleWhen = new VisibleWhen("shape", "box") },
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
        Id = "img.rotate", Title = Strings.Get("RotateImagesAction_Title"), Category = ActionCategories.Images, Icon = "RotateRight24",
        Keywords = new[] { "turn", "mirror", "flip", "upright", "orientation" },
        Summary = Strings.Get("RotateImagesAction_Summary"),
        Description = Strings.Get("RotateImagesAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("RotateImagesAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("action", Strings.Get("RotateImagesAction_Param_action_Label"), "right",
                new Choice("right", Strings.Get("RotateImagesAction_Param_action_Choice_right")), new Choice("left", Strings.Get("RotateImagesAction_Param_action_Choice_left")), new Choice("180", Strings.Get("RotateImagesAction_Param_action_Choice_180")),
                new Choice("flipH", Strings.Get("RotateImagesAction_Param_action_Choice_flipH")), new Choice("flipV", Strings.Get("RotateImagesAction_Param_action_Choice_flipV")), new Choice("upright", Strings.Get("RotateImagesAction_Param_action_Choice_upright"))),
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
        Id = "img.combine", Title = Strings.Get("CombineImagesAction_Title"), Category = ActionCategories.Images, Icon = "Image24",
        Keywords = new[] { "merge", "collage", "stitch", "grid", "side by side", "stack" },
        Summary = Strings.Get("CombineImagesAction_Summary"),
        Description = Strings.Get("CombineImagesAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("CombineImagesAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("layout", Strings.Get("CombineImagesAction_Param_layout_Label"), "horizontal",
                new Choice("horizontal", Strings.Get("CombineImagesAction_Param_layout_Choice_horizontal")), new Choice("vertical", Strings.Get("CombineImagesAction_Param_layout_Choice_vertical")), new Choice("grid", Strings.Get("CombineImagesAction_Param_layout_Choice_grid"))),
            ParameterDescriptor.Number("columns", Strings.Get("CombineImagesAction_Param_columns_Label"), 2, 1, 20) with { VisibleWhen = new VisibleWhen("layout", "grid") },
            ParameterDescriptor.Number("spacing", Strings.Get("CombineImagesAction_Param_spacing_Label"), 0, 0, 500, "pixels"),
            ParameterDescriptor.Of("background", Strings.Get("CombineImagesAction_Param_background_Label"), ParamType.Color, "#FFFFFF"),
            ParameterDescriptor.Bool("matchSize", Strings.Get("CombineImagesAction_Param_matchSize_Label"), true),
            ParameterDescriptor.ChoiceOf("format", Strings.Get("CombineImagesAction_Param_format_Label"), "png", new Choice("png", Strings.Get("CombineImagesAction_Param_format_Choice_png")), new Choice("jpeg", Strings.Get("CombineImagesAction_Param_format_Choice_jpeg"))),
            ImageParams.Quality("format", "jpeg"),
            ParameterDescriptor.Text("name", Strings.Get("CombineImagesAction_Param_name_Label"), "Combined"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("CombineImagesAction_Error_ThereAreNoPictures"));
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
        Id = "img.details", Title = Strings.Get("GetImageDetailsAction_Title"), Category = ActionCategories.Images, Icon = "ImageSearch24",
        Keywords = new[] { "exif", "size", "date taken", "camera", "gps", "location", "metadata", "dimensions" },
        Summary = Strings.Get("GetImageDetailsAction_Summary"),
        Description = Strings.Get("GetImageDetailsAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Any, OutputName = Strings.Get("GetImageDetailsAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.ChoiceOf("detail", Strings.Get("GetImageDetailsAction_Param_detail_Label"), "dimensions",
                new Choice("dimensions", Strings.Get("GetImageDetailsAction_Param_detail_Choice_dimensions")), new Choice("width", Strings.Get("GetImageDetailsAction_Param_detail_Choice_width")), new Choice("height", Strings.Get("GetImageDetailsAction_Param_detail_Choice_height")),
                new Choice("dateTaken", Strings.Get("GetImageDetailsAction_Param_detail_Choice_dateTaken")), new Choice("camera", Strings.Get("GetImageDetailsAction_Param_detail_Choice_camera")), new Choice("location", Strings.Get("GetImageDetailsAction_Param_detail_Choice_location")),
                new Choice("all", Strings.Get("GetImageDetailsAction_Param_detail_Choice_all"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("GetImageDetailsAction_Error_ThereAreNoPictures"));
        var detail = await ctx.GetChoiceAsync("detail", "dimensions");
        var paths = images.Select(i => i.Path).ToList();
        var infos = await StaWorker.Imaging.RunAsync(() => paths.Select(p => (Path: p, Info: ReadInfo(p))).ToList(), ctx.CancellationToken);
        return Shape(infos, detail);
    }

    private static ImageInfo ReadInfo(string path)
    {
        if (!File.Exists(path)) throw new ActionException(Strings.Format("GetImageDetailsAction_Error_CouldnTFindThe", Path.GetFileName(path)));
        try { return ImageMetadataReader.Read(path); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            throw new ActionException(Strings.Format("GetImageDetailsAction_Error_CouldnTReadAs", Path.GetFileName(path)), ex);
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
        Id = "img.stripInfo", Title = Strings.Get("RemovePhotoInfoAction_Title"), Category = ActionCategories.Images, Icon = "LocationOff24",
        Keywords = new[] { "exif", "gps", "location", "privacy", "metadata", "strip", "clean" },
        Summary = Strings.Get("RemovePhotoInfoAction_Summary"),
        Description = Strings.Get("RemovePhotoInfoAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("RemovePhotoInfoAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.Number("quality", Strings.Get("RemovePhotoInfoAction_Param_quality_Label"), 95, 1, 100, "%"),
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
        Id = "img.addText", Title = Strings.Get("AddTextToImageAction_Title"), Category = ActionCategories.Images, Icon = "TextT24",
        Keywords = new[] { "caption", "watermark", "label", "write", "overlay", "meme" },
        Summary = Strings.Get("AddTextToImageAction_Summary"),
        Description = Strings.Get("AddTextToImageAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("AddTextToImageAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.MultilineText("text", Strings.Get("AddTextToImageAction_Param_text_Label"), null, Strings.Get("AddTextToImageAction_Param_text_Placeholder")) with { Required = true },
            ParameterDescriptor.ChoiceOf("position", Strings.Get("AddTextToImageAction_Param_position_Label"), "bottom",
                new Choice("topLeft", Strings.Get("AddTextToImageAction_Param_position_Choice_topLeft")), new Choice("top", Strings.Get("AddTextToImageAction_Param_position_Choice_top")), new Choice("topRight", Strings.Get("AddTextToImageAction_Param_position_Choice_topRight")),
                new Choice("left", Strings.Get("AddTextToImageAction_Param_position_Choice_left")), new Choice("center", Strings.Get("AddTextToImageAction_Param_position_Choice_center")), new Choice("right", Strings.Get("AddTextToImageAction_Param_position_Choice_right")),
                new Choice("bottomLeft", Strings.Get("AddTextToImageAction_Param_position_Choice_bottomLeft")), new Choice("bottom", Strings.Get("AddTextToImageAction_Param_position_Choice_bottom")), new Choice("bottomRight", Strings.Get("AddTextToImageAction_Param_position_Choice_bottomRight"))),
            ParameterDescriptor.Number("size", Strings.Get("AddTextToImageAction_Param_size_Label"), 5, 1, 100, Strings.Get("AddTextToImageAction_Param_size_Unit")) with { Hint = Strings.Get("AddTextToImageAction_Param_size_Hint") },
            ParameterDescriptor.Of("color", Strings.Get("AddTextToImageAction_Param_color_Label"), ParamType.Color, "#FFFFFF"),
            ParameterDescriptor.Bool("shadow", Strings.Get("AddTextToImageAction_Param_shadow_Label"), true),
            ParameterDescriptor.Bool("bold", Strings.Get("AddTextToImageAction_Param_bold_Label"), false),
            ParameterDescriptor.Text("font", Strings.Get("AddTextToImageAction_Param_font_Label"), "Segoe UI"),
            ImageParams.Quality(),
            ImageParams.Suffix("-text"),
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException(Strings.Get("AddTextToImageAction_Error_TypeTheTextTo"));
        var spec = new TextSpec(text, await ctx.GetChoiceAsync("position", "bottom"), await ctx.GetNumberAsync("size", 5),
            await ctx.GetTextAsync("color", "#FFFFFF"), await ctx.GetBoolAsync("shadow", true), await ctx.GetBoolAsync("bold"),
            await ctx.GetTextAsync("font", "Segoe UI"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 90));
        var suffix = await ctx.GetTextAsync("suffix", "-text");
        return await ImageParams.RunBatchAsync(ctx, suffix, ImageParams.SameExt, (s, d) => ImageEngine.AddText(s, d, spec, quality));
    }
}
