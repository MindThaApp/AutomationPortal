namespace AutomationPortal.Actions.Documents;

public sealed class CompressMediaAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "img.compress", Title = Strings.Get("CompressMediaAction_Title"), Category = ActionCategories.Images, Icon = "ArrowMinimize24",
        Keywords = new[] { "shrink", "smaller", "reduce", "size", "optimize", "jpeg", "png", "photo", "picture" },
        Summary = Strings.Get("CompressMediaAction_Summary"),
        Description = Strings.Get("CompressMediaAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Image, OutputName = Strings.Get("CompressMediaAction_OutputName"),
        Parameters = new[]
        {
            ImageParams.Images(),
            ParameterDescriptor.Number("quality", Strings.Get("CompressMediaAction_Param_quality_Label"), 75, 1, 100, "%") with
            {
                Hint = Strings.Get("CompressMediaAction_Param_quality_Hint"),
            },
            ParameterDescriptor.Number("maxWidth", Strings.Get("CompressMediaAction_Param_maxWidth_Label"), null, 1, 30000, "pixels") with
            {
                Hint = Strings.Get("CompressMediaAction_Param_maxWidth_Hint"),
            },
        }.Concat(ImageParams.SaveParams()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    internal const string NameSuffix = " (compressed)";

    internal static string ExtFor(string path) =>
        ImageFormats.FromExtension(Path.GetExtension(path)) == ImageFormat.Jpeg ? ".jpg" : ".png";

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("CompressMediaAction_Error_ThereAreNoPictures"));
        var quality = ImageParams.QualityOf(await ctx.GetNumberAsync("quality", 75));
        var maxWidth = await ImageParams.Opt(ctx, "maxWidth");
        var spec = maxWidth is > 0
            ? new ResizeSpec("width", maxWidth.Value, 0, 0, 0, true)
            : new ResizeSpec("percent", 0, 0, 0, 100, true);
        var target = await OutputTarget.ReadAsync(ctx);

        var results = new List<ImageItem>();
        long before = 0, after = 0;
        foreach (var img in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var src = img.Path;
            if (!File.Exists(src)) throw new ActionException(Strings.Format("CompressMediaAction_Error_CouldnTFindThe", Path.GetFileName(src)));
            var ext = ExtFor(src);
            // NewPath picks a name that doesn't exist yet ("name (compressed).jpg", then "name (compressed) (2).jpg"); the file really
            // written (the next free name if that one was taken meanwhile) is recorded for Undo.
            var dst = target.NewPath(src, Path.GetFileNameWithoutExtension(src) + NameSuffix + ext);
            var r = await StaWorker.Imaging.RunAsync(() => ImageEngine.Resize(src, dst, spec, quality), ctx.CancellationToken);
            target.Record(r.Path);
            before += new FileInfo(src).Length;
            after += new FileInfo(r.Path).Length;
            ctx.ReportProgress($"Saved {Path.GetFileName(r.Path)}");
            results.Add(r.ToItem());
        }
        ctx.ReportProgress(Summary(results.Count, before, after));
        return ContentValue.FromItems(results);
    }

    internal static string Summary(int count, long before, long after)
    {
        var what = count == 1 ? "1 picture" : $"{count} pictures";
        var change = before > 0 && after < before ? $" ({100 - (int)Math.Round(after * 100.0 / before)}% smaller)" : "";
        return $"Compressed {what}: {ContentFormat.Size(before)} before, {ContentFormat.Size(after)} after{change}";
    }
}
