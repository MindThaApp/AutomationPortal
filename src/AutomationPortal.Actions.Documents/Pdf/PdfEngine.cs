using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace AutomationPortal.Actions.Documents;

public abstract record PdfSource
{
    /// <summary>A picture already encoded as JPEG (see <see cref="ImageEngine.ToJpegBytes"/>).</summary>
    public sealed record Picture(byte[] Jpeg, int Width, int Height) : PdfSource;
    public sealed record Text(string Value) : PdfSource;
    public sealed record ExistingPdf(string Path) : PdfSource;
}

public sealed record PdfMakeOptions(string PageSize = "a4", string ImageFit = "fit", double MarginMm = 10, double FontSize = 11, string Font = "Segoe UI");

public static class PdfEngine
{
    private static readonly object FontLock = new();
    private static bool _fontsReady;

    /// <summary>PDFsharp's core build needs to be told to use the Windows fonts.</summary>
    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady) return;
            try { GlobalFontSettings.FontResolver ??= new WindowsFontResolver(); }
            catch (InvalidOperationException) { /* someone already chose a resolver: keep it */ }
            _fontsReady = true;
        }
    }

    private const double MmToPt = 72.0 / 25.4;

    private static PdfSharp.PageSize SizeOf(string id) => id.ToLowerInvariant() == "letter" ? PdfSharp.PageSize.Letter : PdfSharp.PageSize.A4;

    // ---- make --------------------------------------------------------------------------------

    public static void Make(IReadOnlyList<PdfSource> sources, string dst, PdfMakeOptions options)
    {
        if (sources.Count == 0) throw new ActionException("There is nothing to put in the PDF. Choose some pictures, text or files.");
        EnsureFonts();
        var margin = Math.Max(0, options.MarginMm) * MmToPt;
        using var doc = new PdfDocument();
        doc.Info.Creator = "Automation Portal";
        XFont? font = null;
        foreach (var source in sources)
        {
            switch (source)
            {
                case PdfSource.Picture pic: AddPicture(doc, pic, options, margin); break;
                case PdfSource.Text text:
                    font ??= CreateFont(options);
                    AddText(doc, text.Value, options, margin, font);
                    break;
                case PdfSource.ExistingPdf existing:
                    using (var input = OpenForImport(existing.Path))
                        foreach (var page in input.Pages) doc.AddPage(page);
                    break;
            }
        }
        if (doc.PageCount == 0) throw new ActionException("There is nothing to put in the PDF. Choose some pictures, text or files.");
        SaveDoc(doc, dst);
    }

    private static XFont CreateFont(PdfMakeOptions o) =>
        new(string.IsNullOrWhiteSpace(o.Font) ? "Segoe UI" : o.Font, Math.Clamp(o.FontSize, 4, 72), XFontStyleEx.Regular);

    private static void AddPicture(PdfDocument doc, PdfSource.Picture pic, PdfMakeOptions o, double margin)
    {
        var page = doc.AddPage();
        double boxX, boxY, boxW, boxH;
        if (o.ImageFit == "original")
        {
            page.Width = XUnit.FromPoint(pic.Width * 0.75);
            page.Height = XUnit.FromPoint(pic.Height * 0.75);
            boxX = boxY = 0; boxW = page.Width.Point; boxH = page.Height.Point;
        }
        else
        {
            page.Size = SizeOf(o.PageSize);
            page.Orientation = pic.Width > pic.Height ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
            boxX = boxY = margin;
            boxW = Math.Max(10, page.Width.Point - 2 * margin);
            boxH = Math.Max(10, page.Height.Point - 2 * margin);
        }
        var scale = Math.Min(boxW / pic.Width, boxH / pic.Height);
        var w = pic.Width * scale;
        var h = pic.Height * scale;
        using var gfx = XGraphics.FromPdfPage(page);
        using var ms = new MemoryStream(pic.Jpeg);
        using var img = XImage.FromStream(ms);
        gfx.DrawImage(img, boxX + (boxW - w) / 2, boxY + (boxH - h) / 2, w, h);
    }

    private static void AddText(PdfDocument doc, string text, PdfMakeOptions o, double margin, XFont font)
    {
        PdfPage NewPage()
        {
            var p = doc.AddPage();
            p.Size = SizeOf(o.PageSize);
            return p;
        }
        var page = NewPage();
        var gfx = XGraphics.FromPdfPage(page);
        var maxWidth = Math.Max(20, page.Width.Point - 2 * margin);
        var lineHeight = font.GetHeight() * 1.1;
        var y = margin;

        void Line(string s)
        {
            if (y + lineHeight > page.Height.Point - margin)
            {
                gfx.Dispose();
                page = NewPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
            }
            if (s.Length > 0) gfx.DrawString(s, font, XBrushes.Black, new XPoint(margin, y), XStringFormats.TopLeft);
            y += lineHeight;
        }

        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var p = paragraph.Replace("\t", "    ");
            if (p.Length == 0) { Line(""); continue; }
            foreach (var l in Wrap(gfx, font, p, maxWidth)) Line(l);
        }
        gfx.Dispose();
    }

    private static IEnumerable<string> Wrap(XGraphics gfx, XFont font, string paragraph, double maxWidth)
    {
        var current = "";
        foreach (var word in paragraph.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (gfx.MeasureString(candidate, font).Width <= maxWidth) { current = candidate; continue; }
            if (current.Length > 0) yield return current;
            current = word;
            while (gfx.MeasureString(current, font).Width > maxWidth && current.Length > 1)
            {
                var cut = current.Length - 1;
                while (cut > 1 && gfx.MeasureString(current[..cut], font).Width > maxWidth) cut--;
                yield return current[..cut];
                current = current[cut..];
            }
        }
        yield return current;
    }

    // ---- merge / split -----------------------------------------------------------------------

    private static PdfDocument OpenForImport(string path)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the PDF '{Path.GetFileName(path)}'. Check it still exists.");
        try { return PdfReader.Open(path, PdfDocumentOpenMode.Import); }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionException($"'{Path.GetFileName(path)}' is protected by a password. Remove the password first.", ex);
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException or IndexOutOfRangeException)
        {
            throw new ActionException($"Couldn't read '{Path.GetFileName(path)}'. It may be damaged or not a real PDF.", ex);
        }
        catch (IOException ex)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(path)}'. Close any program that is using it and try again.", ex);
        }
    }

    private static void SaveDoc(PdfDocument doc, string dst)
    {
        try { doc.Save(dst); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't save '{Path.GetFileName(dst)}'. Check the folder exists and you can save there.", ex);
        }
    }

    public static int Merge(IReadOnlyList<string> pdfs, string dst)
    {
        if (pdfs.Count == 0) throw new ActionException("There are no PDFs to combine. Choose some PDF files first.");
        using var output = new PdfDocument();
        output.Info.Creator = "Automation Portal";
        foreach (var path in pdfs)
        {
            using var input = OpenForImport(path);
            foreach (var page in input.Pages) output.AddPage(page);
        }
        var pages = output.PageCount;
        SaveDoc(output, dst);
        return pages;
    }

    public static int PageCount(string path)
    {
        using var input = OpenForImport(path);
        return input.PageCount;
    }

    /// <summary>Writes the given pages (1-based, in the given order) of <paramref name="src"/> into a new PDF.</summary>
    public static void ExtractPages(string src, IReadOnlyList<int> pages, string dst)
    {
        using var input = OpenForImport(src);
        using var output = new PdfDocument();
        output.Info.Creator = "Automation Portal";
        foreach (var n in pages) output.AddPage(input.Pages[n - 1]);
        SaveDoc(output, dst);
    }

    // ---- text & details ----------------------------------------------------------------------

    public static string GetText(string path, string? pageText)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the PDF '{Path.GetFileName(path)}'. Check it still exists.");
        IReadOnlyList<int>? pages = null;
        if (!string.IsNullOrWhiteSpace(pageText))
        {
            int count;
            try { using var d = PigDocument.Open(path); count = d.NumberOfPages; }
            catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException ex) { throw new ActionException($"'{Path.GetFileName(path)}' is protected by a password. Remove the password first.", ex); }
            pages = PageRanges.Parse(pageText, count);
        }
        return DocumentText.Read(path, pages);
    }

    public sealed record PdfInfo(int Pages, string Title, string Author, string Subject, string Keywords, string Creator, string Producer,
        string PageSize, double WidthPt, double HeightPt, long Bytes, DateTimeOffset? Created);

    public static PdfInfo GetInfo(string path)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the PDF '{Path.GetFileName(path)}'. Check it still exists.");
        try
        {
            using var d = PigDocument.Open(path);
            var info = d.Information;
            double w = 0, h = 0;
            if (d.NumberOfPages > 0) { var p = d.GetPage(1); w = p.Width; h = p.Height; }
            return new PdfInfo(d.NumberOfPages, info.Title ?? "", info.Author ?? "", info.Subject ?? "", info.Keywords ?? "",
                info.Creator ?? "", info.Producer ?? "", NameSize(w, h), w, h, new FileInfo(path).Length, ParsePdfDate(info.CreationDate));
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException ex)
        {
            throw new ActionException($"'{Path.GetFileName(path)}' is protected by a password. Remove the password first.", ex);
        }
        catch (Exception ex) when (ex is UglyToad.PdfPig.Core.PdfDocumentFormatException or InvalidOperationException)
        {
            throw new ActionException($"Couldn't read '{Path.GetFileName(path)}'. It may be damaged or not a real PDF.", ex);
        }
    }

    private static string NameSize(double w, double h)
    {
        var (a, b) = w <= h ? (w, h) : (h, w);
        bool Near(double x, double y) => Math.Abs(x - y) < 3;
        if (Near(a, 595) && Near(b, 842)) return "A4";
        if (Near(a, 612) && Near(b, 792)) return "Letter";
        if (Near(a, 612) && Near(b, 1008)) return "Legal";
        if (Near(a, 420) && Near(b, 595)) return "A5";
        if (Near(a, 842) && Near(b, 1191)) return "A3";
        return w > 0 ? $"{Math.Round(w / 72 * 25.4)} × {Math.Round(h / 72 * 25.4)} mm" : "";
    }

    /// <summary>"D:20240501101530+01'00'" → date.</summary>
    public static DateTimeOffset? ParsePdfDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith("D:")) t = t[2..];
        var digits = new string(t.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length < 4) return null;
        var rest = t[digits.Length..];
        digits = digits.PadRight(14, '0');
        int Part(int i, int len, int def) => int.TryParse(digits.AsSpan(i, len), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : def;
        try
        {
            var dt = new DateTime(Part(0, 4, 2000), Math.Max(1, Part(4, 2, 1)), Math.Max(1, Part(6, 2, 1)), Part(8, 2, 0), Part(10, 2, 0), Part(12, 2, 0));
            var offset = TimeSpan.Zero;
            if (rest.Length >= 3 && (rest[0] == '+' || rest[0] == '-'))
            {
                var hh = int.TryParse(rest.AsSpan(1, 2), out var hv) ? hv : 0;
                var mm = rest.Length >= 6 && int.TryParse(rest.AsSpan(4, 2), out var mv) ? mv : 0;
                offset = new TimeSpan(hh, mm, 0);
                if (rest[0] == '-') offset = -offset;
            }
            else if (!rest.StartsWith('Z')) offset = TimeZoneInfo.Local.GetUtcOffset(dt);
            return new DateTimeOffset(dt, offset);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // ---- PDF -> images -----------------------------------------------------------------------

    public static async Task<IReadOnlyList<ImageResult>> ToImagesAsync(string pdfPath, string? pageText, int widthPx,
        Func<int, int, string> destinationFor, CancellationToken ct)
    {
        if (!File.Exists(pdfPath)) throw new ActionException($"Couldn't find the PDF '{Path.GetFileName(pdfPath)}'. Check it still exists.");
        widthPx = Math.Clamp(widthPx, 100, 8000);
        Windows.Data.Pdf.PdfDocument doc;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath)).AsTask(ct).ConfigureAwait(false);
            doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or FileNotFoundException)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(pdfPath)}'. It may be damaged or protected by a password.", ex);
        }
        if (doc.IsPasswordProtected)
            throw new ActionException($"'{Path.GetFileName(pdfPath)}' is protected by a password. Remove the password first.");

        var count = (int)doc.PageCount;
        var pages = PageRanges.Parse(pageText, count);
        var results = new List<ImageResult>();
        foreach (var n in pages)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage((uint)(n - 1));
            var heightPx = (uint)Math.Max(1, Math.Round(widthPx * page.Size.Height / page.Size.Width));
            var dst = destinationFor(n, count);
            using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ras, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)widthPx, DestinationHeight = heightPx })
                .AsTask(ct).ConfigureAwait(false);
            ras.Seek(0);
            await using (var fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
                await ras.AsStreamForRead().CopyToAsync(fs, ct).ConfigureAwait(false);
            results.Add(new ImageResult(dst, widthPx, (int)heightPx));
        }
        return results;
    }
}
