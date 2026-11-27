using System.Globalization;

namespace AutomationPortal.Actions.Documents;

internal static class PdfParams
{
    /// <summary>Preview: the one new PDF a step would save, named as a real run would name it. Changes nothing.</summary>
    public static async Task<ContentValue> PlanOneAsync(ActionContext ctx, FileChangePlan plan, string? sourcePath, string defaultName)
    {
        var name = (await ctx.GetTextAsync("name", defaultName)).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) name = defaultName;
        var target = await OutputTarget.ReadAsync(ctx);
        if (target.PlanPath(plan, sourcePath, name + ".pdf") is not { } dst) return ContentValue.Empty;   // into the run's temp folder
        plan.Add(new PlannedChange(PlannedChangeKind.NewFile, ctx.Descriptor.Title, sourcePath, dst));
        return ContentValue.Files(new[] { dst });
    }

    public static async Task<IReadOnlyList<PathItem>> RequirePdfsAsync(ActionContext ctx, string key)
    {
        var files = await FileParams.RequireFilesAsync(ctx, key, false, Strings.Get("PdfParams_Noun_PDFFiles"));
        foreach (var f in files)
            if (!Path.GetExtension(f.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ActionException(Strings.Format("PdfParams_Error_IsnTAPDF", f.FullName));
        return files;
    }
}

public sealed class MakePdfAction : ShortcutAction, IPlansFileChanges
{
    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan)
    {
        var content = await ctx.GetContentAsync("items");
        if (content.IsEmpty) throw new ActionException(Strings.Get("MakePdfAction_Error_ThereIsNothingTo"));
        // A real run saves next to the first item; text becomes a temporary file first, so then the PDF goes to the temp folder.
        var first = content.FirstOrDefault(i => i is not FolderItem) as PathItem;
        return await PdfParams.PlanOneAsync(ctx, plan, first?.Path, "Document");
    }

    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.make", Title = Strings.Get("MakePdfAction_Title"), Category = ActionCategories.Pdf, Icon = "DocumentPdf24",
        Keywords = new[] { "pictures", "images to pdf", "text to pdf", "print", "a4", "letter", "create" },
        Summary = Strings.Get("MakePdfAction_Summary"),
        Description = Strings.Get("MakePdfAction_Description"),
        InputKinds = new[] { ContentKind.Image, ContentKind.File, ContentKind.Text }, OutputKind = ContentKind.File, OutputName = Strings.Get("MakePdfAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("items", Strings.Get("MakePdfAction_Param_items_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("pageSize", Strings.Get("MakePdfAction_Param_pageSize_Label"), "a4", new Choice("a4", "A4"), new Choice("letter", Strings.Get("MakePdfAction_Param_pageSize_Choice_letter"))),
            ParameterDescriptor.ChoiceOf("imageFit", Strings.Get("MakePdfAction_Param_imageFit_Label"), "fit",
                new Choice("fit", Strings.Get("MakePdfAction_Param_imageFit_Choice_fit")), new Choice("original", Strings.Get("MakePdfAction_Param_imageFit_Choice_original"))),
            ParameterDescriptor.Number("margin", Strings.Get("MakePdfAction_Param_margin_Label"), 10, 0, 50, "mm"),
            ParameterDescriptor.Number("fontSize", Strings.Get("MakePdfAction_Param_fontSize_Label"), 11, 6, 40, "points"),
            ParameterDescriptor.Text("font", Strings.Get("MakePdfAction_Param_font_Label"), "Segoe UI") with { Hint = Strings.Get("MakePdfAction_Param_font_Hint") },
            ParameterDescriptor.Text("name", Strings.Get("MakePdfAction_Param_name_Label"), "Document"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("items");
        if (content.IsEmpty) throw new ActionException(Strings.Get("MakePdfAction_Error_ThereIsNothingTo"));
        var files = await content.AsFilesAsync(ctx, includeFolders: false);
        var options = new PdfMakeOptions(
            await ctx.GetChoiceAsync("pageSize", "a4"), await ctx.GetChoiceAsync("imageFit", "fit"),
            await ctx.GetNumberAsync("margin", 10), await ctx.GetNumberAsync("fontSize", 11), await ctx.GetTextAsync("font", "Segoe UI"));

        var sources = new List<PdfSource>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!f.Exists) throw new ActionException(Strings.Format("AiInput_Error_CouldnTFindCheck", f.FullName));
            var ext = Path.GetExtension(f.Path);
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) sources.Add(new PdfSource.ExistingPdf(f.Path));
            else if (FileKinds.IsImage(f.Path))
            {
                var path = f.Path;
                var (jpeg, w, h) = await StaWorker.Imaging.RunAsync(() => ImageEngine.ToJpegBytes(path), ctx.CancellationToken);
                sources.Add(new PdfSource.Picture(jpeg, w, h));
            }
            else sources.Add(new PdfSource.Text(await Task.Run(() => DocumentText.Read(f.Path), ctx.CancellationToken)));
        }

        var name = (await ctx.GetTextAsync("name", "Document")).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) name = "Document";
        var target = await OutputTarget.ReadAsync(ctx);
        var dst = target.NewPath(files[0].Path, name + ".pdf");
        var written = await Task.Run(() => PdfEngine.Make(sources, dst, options), ctx.CancellationToken);
        return ContentValue.File(target.Record(written));
    }
}

public sealed class MergePdfAction : ShortcutAction, IPlansFileChanges
{
    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        if (files.Count < 2) throw new ActionException(Strings.Get("MergePdfAction_Error_ChooseAtLeastTwo"));
        return await PdfParams.PlanOneAsync(ctx, plan, files[0].Path, "Combined");
    }

    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.merge", Title = Strings.Get("MergePdfAction_Title"), Category = ActionCategories.Pdf, Icon = "DocumentMultiple24",
        Keywords = new[] { "merge", "join", "stitch", "append" },
        Summary = Strings.Get("MergePdfAction_Summary"),
        Description = Strings.Get("MergePdfAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = Strings.Get("MergePdfAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("MergePdfAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Text("name", Strings.Get("MergePdfAction_Param_name_Label"), "Combined"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        if (files.Count < 2) throw new ActionException(Strings.Get("MergePdfAction_Error_ChooseAtLeastTwo"));
        var name = (await ctx.GetTextAsync("name", "Combined")).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) name = "Combined";
        var target = await OutputTarget.ReadAsync(ctx);
        var dst = target.NewPath(files[0].Path, name + ".pdf");
        var paths = files.Select(f => f.Path).ToList();
        var written = await Task.Run(() => { PdfEngine.Merge(paths, dst, out var w); return w; }, ctx.CancellationToken);
        return ContentValue.File(target.Record(written));
    }
}

public sealed class SplitPdfAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.split", Title = Strings.Get("SplitPdfAction_Title"), Category = ActionCategories.Pdf, Icon = "DocumentSplitHint24",
        Keywords = new[] { "pages", "extract", "separate", "divide", "range" },
        Summary = Strings.Get("SplitPdfAction_Summary"),
        Description = Strings.Get("SplitPdfAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = Strings.Get("SplitPdfAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("SplitPdfAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("SplitPdfAction_Param_mode_Label"), "each",
                new Choice("each", Strings.Get("SplitPdfAction_Param_mode_Choice_each")), new Choice("ranges", Strings.Get("SplitPdfAction_Param_mode_Choice_ranges")),
                new Choice("extract", Strings.Get("SplitPdfAction_Param_mode_Choice_extract")), new Choice("chunks", Strings.Get("SplitPdfAction_Param_mode_Choice_chunks"))),
            ParameterDescriptor.Text("pages", Strings.Get("SplitPdfAction_Param_pages_Label"), null, "e.g. 1-3, 5, 8-") with { VisibleWhen = new VisibleWhen("mode", "ranges", "extract") },
            ParameterDescriptor.Number("chunk", Strings.Get("SplitPdfAction_Param_chunk_Label"), 5, 1, 1000) with { VisibleWhen = new VisibleWhen("mode", "chunks") },
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        var mode = await ctx.GetChoiceAsync("mode", "each");
        var pageText = await ctx.GetTextAsync("pages");
        var chunk = (int)await ctx.GetNumberAsync("chunk", 5);
        var target = await OutputTarget.ReadAsync(ctx);
        var outputs = new List<string>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var src = f.Path;
            var stem = Path.GetFileNameWithoutExtension(src);
            var count = await Task.Run(() => PdfEngine.PageCount(src), ctx.CancellationToken);
            var groups = Plan(mode, pageText, chunk, count);
            foreach (var group in groups)
            {
                var label = mode == "each" ? $"page {group[0].ToString("D" + count.ToString(CultureInfo.InvariantCulture).Length, CultureInfo.InvariantCulture)}" : PageRanges.Describe(group);
                var dst = target.NewPath(src, $"{stem} - {label}.pdf");
                var pages = group;
                var written = await Task.Run(() => PdfEngine.ExtractPages(src, pages, dst), ctx.CancellationToken);
                outputs.Add(target.Record(written));
            }
        }
        return ContentValue.Files(outputs);
    }

    public static IReadOnlyList<IReadOnlyList<int>> Plan(string mode, string pageText, int chunk, int pageCount)
    {
        switch (mode)
        {
            case "ranges":
                if (string.IsNullOrWhiteSpace(pageText)) throw new ActionException(Strings.Get("SplitPdfAction_Error_TypeThePageRanges"));
                return PageRanges.ParseGroups(pageText, pageCount);
            case "extract":
                if (string.IsNullOrWhiteSpace(pageText)) throw new ActionException(Strings.Get("SplitPdfAction_Error_TypeThePagesTo"));
                return new[] { PageRanges.Parse(pageText, pageCount) };
            case "chunks":
                {
                    chunk = Math.Max(1, chunk);
                    var list = new List<IReadOnlyList<int>>();
                    for (var start = 1; start <= pageCount; start += chunk)
                        list.Add(Enumerable.Range(start, Math.Min(chunk, pageCount - start + 1)).ToList());
                    return list;
                }
            default:
                return Enumerable.Range(1, pageCount).Select(n => (IReadOnlyList<int>)new[] { n }).ToList();
        }
    }
}

public sealed class GetPdfTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.text", Title = Strings.Get("GetPdfTextAction_Title"), Category = ActionCategories.Pdf, Icon = "TextDescription24",
        Keywords = new[] { "read", "extract", "copy text", "words" },
        Summary = Strings.Get("GetPdfTextAction_Summary"),
        Description = Strings.Get("GetPdfTextAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = Strings.Get("GetPdfTextAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("GetPdfTextAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Text("pages", Strings.Get("GetPdfTextAction_Param_pages_Label"), null, Strings.Get("GetPdfTextAction_Param_pages_Placeholder")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        var pages = await ctx.GetTextAsync("pages");
        var texts = new List<string>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var path = f.Path;
            texts.Add(await Task.Run(() => PdfEngine.GetText(path, pages), ctx.CancellationToken));
        }
        return ContentValue.Texts(texts);
    }
}

public sealed class GetPdfDetailsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.details", Title = Strings.Get("GetPdfDetailsAction_Title"), Category = ActionCategories.Pdf, Icon = "Info24",
        Keywords = new[] { "pages", "title", "author", "size", "properties", "page count" },
        Summary = Strings.Get("GetPdfDetailsAction_Summary"),
        Description = Strings.Get("GetPdfDetailsAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Any, OutputName = Strings.Get("GetPdfDetailsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("GetPdfDetailsAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", Strings.Get("GetPdfDetailsAction_Param_detail_Label"), "pages",
                new Choice("pages", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_pages")), new Choice("title", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_title")), new Choice("author", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_author")),
                new Choice("pageSize", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_pageSize")), new Choice("fileSize", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_fileSize")), new Choice("created", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_created")),
                new Choice("all", Strings.Get("GetPdfDetailsAction_Param_detail_Choice_all"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        var detail = await ctx.GetChoiceAsync("detail", "pages");
        var paths = files.Select(f => f.Path).ToList();
        var infos = await Task.Run(() => paths.Select(p => (Path: p, Info: PdfEngine.GetInfo(p))).ToList(), ctx.CancellationToken);
        return Shape(infos, detail);
    }

    public static ContentValue Shape(IReadOnlyList<(string Path, PdfEngine.PdfInfo Info)> infos, string detail)
    {
        var items = new List<ContentItem>();
        foreach (var (path, i) in infos)
        {
            switch (detail)
            {
                case "title": if (i.Title.Length > 0) items.Add(new TextItem(i.Title)); break;
                case "author": if (i.Author.Length > 0) items.Add(new TextItem(i.Author)); break;
                case "pageSize": items.Add(new TextItem(i.PageSize)); break;
                case "fileSize": items.Add(new TextItem(ContentFormat.Size(i.Bytes))); break;
                case "created": if (i.Created is { } c) items.Add(new DateItem(c)); break;
                case "all":
                    items.Add(new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
                    {
                        new("Name", ContentValue.Text(Path.GetFileName(path))),
                        new("Pages", ContentValue.Number(i.Pages)),
                        new("Title", ContentValue.Text(i.Title)),
                        new("Author", ContentValue.Text(i.Author)),
                        new("Subject", ContentValue.Text(i.Subject)),
                        new("Page Size", ContentValue.Text(i.PageSize)),
                        new("File Size", ContentValue.Text(ContentFormat.Size(i.Bytes))),
                        new("Created", i.Created is { } cd ? ContentValue.Date(cd) : ContentValue.Empty),
                        new("Made With", ContentValue.Text(string.Join(" / ", new[] { i.Creator, i.Producer }.Where(s => s.Length > 0)))),
                    }));
                    break;
                default: items.Add(new NumberItem(i.Pages)); break;
            }
        }
        return ContentValue.FromItems(items);
    }
}

public sealed class PdfToImagesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.toImages", Title = Strings.Get("PdfToImagesAction_Title"), Category = ActionCategories.Pdf, Icon = "ImageMultiple24",
        Keywords = new[] { "convert", "pages", "png", "render", "snapshot" },
        Summary = Strings.Get("PdfToImagesAction_Summary"),
        Description = Strings.Get("PdfToImagesAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Image, OutputName = Strings.Get("PdfToImagesAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("PdfToImagesAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Text("pages", Strings.Get("PdfToImagesAction_Param_pages_Label"), null, Strings.Get("PdfToImagesAction_Param_pages_Placeholder")),
            ParameterDescriptor.Number("width", Strings.Get("PdfToImagesAction_Param_width_Label"), 1600, 100, 8000, "pixels"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        var pages = await ctx.GetTextAsync("pages");
        var width = (int)await ctx.GetNumberAsync("width", 1600);
        var target = await OutputTarget.ReadAsync(ctx);
        var all = new List<ImageItem>();
        foreach (var f in files)
        {
            var stem = Path.GetFileNameWithoutExtension(f.Path);
            var src = f.Path;
            var results = await PdfEngine.ToImagesAsync(src, pages, width,
                (n, count) => target.NewPath(src, $"{stem} page {n.ToString("D" + count.ToString(CultureInfo.InvariantCulture).Length, CultureInfo.InvariantCulture)}.png"),
                ctx.CancellationToken, created: written => target.Record(written));
            all.AddRange(results.Select(r => r.ToItem()));
        }
        return ContentValue.FromItems(all);
    }
}
