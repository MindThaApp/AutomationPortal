using System.Globalization;

namespace Flowmate.Actions.Documents;

internal static class PdfParams
{
    public static async Task<IReadOnlyList<PathItem>> RequirePdfsAsync(ActionContext ctx, string key)
    {
        var files = await FileParams.RequireFilesAsync(ctx, key, false, "PDF files");
        foreach (var f in files)
            if (!Path.GetExtension(f.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ActionException($"'{f.FullName}' isn't a PDF. Choose PDF files.");
        return files;
    }
}

public sealed class MakePdfAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.make", Title = "Make PDF", Category = ActionCategories.Pdf, Icon = "DocumentPdf24",
        Keywords = new[] { "pictures", "images to pdf", "text to pdf", "print", "a4", "letter", "create" },
        Summary = "Make a PDF from {items}",
        Description = "Turns pictures, text or documents into one PDF — one page per picture, with text flowing over as many pages as it needs. PDFs you include are added page by page.",
        InputKinds = new[] { ContentKind.Image, ContentKind.File, ContentKind.Text }, OutputKind = ContentKind.File, OutputName = "PDF",
        Parameters = new[]
        {
            ParameterDescriptor.Content("items", "Pictures, text or files", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("pageSize", "Page size", "a4", new Choice("a4", "A4"), new Choice("letter", "US Letter")),
            ParameterDescriptor.ChoiceOf("imageFit", "Pictures", "fit",
                new Choice("fit", "fit on the page"), new Choice("original", "make the page as big as the picture")),
            ParameterDescriptor.Number("margin", "Margin", 10, 0, 50, "mm"),
            ParameterDescriptor.Number("fontSize", "Text size", 11, 6, 40, "points"),
            ParameterDescriptor.Text("font", "Text font", "Segoe UI") with { Hint = "Any font installed on this PC. Right-to-left and complex scripts may not be shaped correctly." },
            ParameterDescriptor.Text("name", "File name", "Document"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("items");
        if (content.IsEmpty) throw new ActionException("There is nothing to put in the PDF. Choose some pictures, text or files first.");
        var files = await content.AsFilesAsync(ctx, includeFolders: false);
        var options = new PdfMakeOptions(
            await ctx.GetChoiceAsync("pageSize", "a4"), await ctx.GetChoiceAsync("imageFit", "fit"),
            await ctx.GetNumberAsync("margin", 10), await ctx.GetNumberAsync("fontSize", 11), await ctx.GetTextAsync("font", "Segoe UI"));

        var sources = new List<PdfSource>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!f.Exists) throw new ActionException($"Couldn't find '{f.FullName}'. Check it still exists.");
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
        await Task.Run(() => PdfEngine.Make(sources, dst, options), ctx.CancellationToken);
        return ContentValue.File(dst);
    }
}

public sealed class MergePdfAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.merge", Title = "Combine PDFs", Category = ActionCategories.Pdf, Icon = "DocumentMultiple24",
        Keywords = new[] { "merge", "join", "stitch", "append" },
        Summary = "Combine {files} into one PDF",
        Description = "Joins several PDFs into one, in the order you give them.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Combined PDF",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "PDF files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("name", "File name", "Combined"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await PdfParams.RequirePdfsAsync(ctx, "files");
        if (files.Count < 2) throw new ActionException("Choose at least two PDFs to combine.");
        var name = (await ctx.GetTextAsync("name", "Combined")).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) name = "Combined";
        var target = await OutputTarget.ReadAsync(ctx);
        var dst = target.NewPath(files[0].Path, name + ".pdf");
        var paths = files.Select(f => f.Path).ToList();
        await Task.Run(() => PdfEngine.Merge(paths, dst), ctx.CancellationToken);
        return ContentValue.File(dst);
    }
}

public sealed class SplitPdfAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "pdf.split", Title = "Split PDF", Category = ActionCategories.Pdf, Icon = "DocumentSplitHint24",
        Keywords = new[] { "pages", "extract", "separate", "divide", "range" },
        Summary = "Split {files} {mode} {pages}{chunk}",
        Description = "Cuts a PDF into smaller PDFs: one per page, one per page range (like 1-3, 5), only some pages, or a set number of pages each.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Split PDFs",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "PDF files", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("mode", "Split", "each",
                new Choice("each", "into one file per page"), new Choice("ranges", "into one file per page range"),
                new Choice("extract", "keeping only the pages"), new Choice("chunks", "into files of this many pages")),
            ParameterDescriptor.Text("pages", "Pages", null, "e.g. 1-3, 5, 8-") with { VisibleWhen = new VisibleWhen("mode", "ranges", "extract") },
            ParameterDescriptor.Number("chunk", "Pages in each file", 5, 1, 1000) with { VisibleWhen = new VisibleWhen("mode", "chunks") },
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
                await Task.Run(() => PdfEngine.ExtractPages(src, pages, dst), ctx.CancellationToken);
                outputs.Add(dst);
            }
        }
        return ContentValue.Files(outputs);
    }

    public static IReadOnlyList<IReadOnlyList<int>> Plan(string mode, string pageText, int chunk, int pageCount)
    {
        switch (mode)
        {
            case "ranges":
                if (string.IsNullOrWhiteSpace(pageText)) throw new ActionException("Type the page ranges to split at, like 1-3, 5.");
                return PageRanges.ParseGroups(pageText, pageCount);
            case "extract":
                if (string.IsNullOrWhiteSpace(pageText)) throw new ActionException("Type the pages to keep, like 1-3, 5.");
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
        Id = "pdf.text", Title = "Get Text from PDF", Category = ActionCategories.Pdf, Icon = "TextDescription24",
        Keywords = new[] { "read", "extract", "copy text", "words" },
        Summary = "Get the text from {files}",
        Description = "Reads the words in a PDF. Scanned pages (pictures of text) have no text to read — use Get Text from Image for those.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "PDF Text",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "PDF files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("pages", "Only these pages", null, "e.g. 1-3, 5  (empty = all pages)"),
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
        Id = "pdf.details", Title = "Get Details of PDF", Category = ActionCategories.Pdf, Icon = "Info24",
        Keywords = new[] { "pages", "title", "author", "size", "properties", "page count" },
        Summary = "Get {detail} of {files}",
        Description = "Reads facts about a PDF: how many pages it has, its title and author, page size and file size.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Any, OutputName = "PDF Details",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "PDF files", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", "Detail", "pages",
                new Choice("pages", "the number of pages"), new Choice("title", "the title"), new Choice("author", "the author"),
                new Choice("pageSize", "the page size"), new Choice("fileSize", "the file size"), new Choice("created", "the date created"),
                new Choice("all", "everything")),
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
        Id = "pdf.toImages", Title = "Make Images from PDF", Category = ActionCategories.Pdf, Icon = "ImageMultiple24",
        Keywords = new[] { "convert", "pages", "png", "render", "snapshot" },
        Summary = "Make pictures from the pages of {files}",
        Description = "Turns each page of a PDF into a PNG picture.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Image, OutputName = "PDF Page Images",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "PDF files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("pages", "Only these pages", null, "e.g. 1-3, 5  (empty = all pages)"),
            ParameterDescriptor.Number("width", "Picture width", 1600, 100, 8000, "pixels"),
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
                ctx.CancellationToken);
            all.AddRange(results.Select(r => r.ToItem()));
        }
        return ContentValue.FromItems(all);
    }
}
