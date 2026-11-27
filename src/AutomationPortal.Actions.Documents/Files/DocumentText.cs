using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AutomationPortal.Actions.Documents;

/// <summary>Reads the text of .txt .md .csv .json .html .docx .pdf (and other plain-text files).</summary>
public static class DocumentText
{
    public static string Read(string path, IReadOnlyCollection<int>? pdfPages = null)
    {
        if (!File.Exists(path))
            throw new ActionException(Strings.Format("DocumentText_Error_CouldnTFindThe", Path.GetFileName(path)));
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        try
        {
            return ext switch
            {
                "docx" or "docm" or "dotx" => ReadDocx(path),
                "pdf" => ReadPdf(path, pdfPages),
                "html" or "htm" or "xhtml" => HtmlToText(File.ReadAllText(path)),
                "doc" or "xls" or "xlsx" or "ppt" or "pptx" or "zip" or "exe" or "dll" or "7z" or "rar" or "odt" or "pages" =>
                    throw new ActionException(Strings.Format("DocumentText_Error_CanTReadText", ext)),
                _ => ReadPlain(path),
            };
        }
        catch (ActionException) { throw; }
        catch (IOException ex)
        {
            throw new ActionException(Strings.Format("DocumentText_Error_CouldnTOpenClose", Path.GetFileName(path)), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ActionException(Strings.Format("DocumentText_Error_AutomationPortalIsnT", Path.GetFileName(path)), ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileFormatException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            throw new ActionException(Strings.Format("DocumentText_Error_LooksDamagedOrIsn", Path.GetFileName(path), ext), ex);
        }
    }

    private static string ReadPlain(string path)
    {
        using (var fs = File.OpenRead(path))
        {
            var buf = new byte[Math.Min(8192, fs.Length)];
            var n = fs.Read(buf, 0, buf.Length);
            if (Array.IndexOf(buf, (byte)0, 0, n) >= 0 && !(n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))))
                throw new ActionException(Strings.Format("DocumentText_Error_IsnTAText", Path.GetFileName(path)));
        }
        return File.ReadAllText(path);
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";
        var sb = new StringBuilder();
        foreach (var p in body.Descendants<Paragraph>())
        {
            foreach (var el in p.Descendants())
            {
                switch (el)
                {
                    case Text t: sb.Append(t.Text); break;
                    case TabChar: sb.Append('\t'); break;
                    case Break or CarriageReturn: sb.Append('\n'); break;
                }
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string ReadPdf(string path, IReadOnlyCollection<int>? pages)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
            {
                if (pages is { Count: > 0 } && !pages.Contains(page.Number)) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(ContentOrderTextExtractor.GetText(page).TrimEnd());
            }
            return sb.ToString();
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException ex)
        {
            throw new ActionException(Strings.Format("DocumentText_Error_IsProtectedByA", Path.GetFileName(path)), ex);
        }
    }

    private static readonly Regex Scripts = new(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlockTags = new(@"</?(p|div|br|li|tr|h[1-6]|ul|ol|table|section|article|header|footer|blockquote|pre)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);

    public static string HtmlToText(string html)
    {
        var t = Comments.Replace(Scripts.Replace(html, ""), "");
        t = BlockTags.Replace(t, "\n");
        t = AnyTag.Replace(t, "");
        t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"[ \t\f\v ]+", " ");
        t = Regex.Replace(t, @" ?\n ?", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }
}
