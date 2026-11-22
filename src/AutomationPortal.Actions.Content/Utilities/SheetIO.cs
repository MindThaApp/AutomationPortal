using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

/// <summary>Reads and writes CSV and Excel (.xlsx) files with typed cells (numbers, dates, true/false).</summary>
public static class SheetIO
{
    public static readonly char[] Delimiters = { ',', ';', '\t', '|' };

    public static bool IsExcel(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xltx" or ".xltm";

    // ---- CSV parsing -------------------------------------------------------------------------

    /// <summary>Parses CSV text (quotes, doubled quotes, line breaks inside quotes, any of , ; tab |). Delimiter is detected when not given.</summary>
    public static List<List<string>> ParseCsv(string text, char? delimiter, out char usedDelimiter)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        usedDelimiter = delimiter ?? DetectDelimiter(text);
        return ParseCsvCore(text, usedDelimiter, int.MaxValue);
    }

    private static List<List<string>> ParseCsvCore(string text, char delim, int maxRows)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;

        void EndField() { row.Add(fieldWasQuoted ? field.ToString() : field.ToString()); field.Clear(); fieldWasQuoted = false; }
        void EndRow()
        {
            EndField();
            // a completely empty line is not a row
            if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row);
            row = new List<string>();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) { inQuotes = true; fieldWasQuoted = true; }
            else if (c == delim) EndField();
            else if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); if (rows.Count >= maxRows) return rows; }
            else if (c == '\n') { EndRow(); if (rows.Count >= maxRows) return rows; }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0 || fieldWasQuoted) EndRow();
        return rows;
    }

    public static char DetectDelimiter(string text)
    {
        var sample = text.Length > 20000 ? text[..20000] : text;
        var best = ',';
        double bestScore = 0;
        foreach (var d in Delimiters)
        {
            var rows = ParseCsvCore(sample, d, 25);
            if (rows.Count == 0) continue;
            var counts = rows.Select(r => r.Count).ToList();
            var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            if (mode.Key < 2) continue;
            var score = (double)mode.Count() / rows.Count * mode.Key;
            if (score > bestScore + 1e-9) { bestScore = score; best = d; }
        }
        return best;
    }

    public static (string Text, Encoding Encoding) ReadTextWithEncoding(string path)
    {
        byte[] bytes;
        try { bytes = ReadAllBytesShared(path); }
        catch (FileNotFoundException) { throw new ActionException($"Couldn't find the file '{Path.GetFileName(path)}'. Check it still exists."); }
        catch (DirectoryNotFoundException) { throw new ActionException($"Couldn't find the folder for '{Path.GetFileName(path)}'. Check it still exists."); }
        catch (IOException ex) { throw new ActionException($"Couldn't open '{Path.GetFileName(path)}'. If it is open in another app, close it and try again. ({ex.Message})", ex); }
        catch (UnauthorizedAccessException) { throw new ActionException($"Automation Portal isn't allowed to open '{Path.GetFileName(path)}'. Check the file's permissions."); }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
        try { return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false)); }
        catch (DecoderFallbackException) { return (Encoding.Latin1.GetString(bytes), Encoding.Latin1); }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    // ---- typing ------------------------------------------------------------------------------

    private static readonly Regex NumberRx = new(@"^[-+]?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][-+]?\d+)?$|^[-+]?\.\d+$", RegexOptions.Compiled);
    private static readonly Regex IsoDateRx = new(@"^\d{4}-\d{2}-\d{2}(?:[T ]\d{1,2}:\d{2}(?::\d{2})?(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?$", RegexOptions.Compiled);

    /// <summary>"12.5" → number, "TRUE" → true, "2026-10-02" → date, everything else (including "007") stays text.</summary>
    public static ContentItem InferValue(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) return new TextItem("");
        if (NumberRx.IsMatch(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && s.Length < 16) return new NumberItem(d);
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return new BooleanItem(true);
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return new BooleanItem(false);
        if (IsoDateRx.IsMatch(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return new DateItem(dt);
        return new TextItem(raw);
    }

    // ---- reading -----------------------------------------------------------------------------

    /// <summary>Reads a .csv/.tsv/.txt or .xlsx file into rows of typed cells.</summary>
    public static List<List<ContentItem>> ReadRows(string path, string? sheet = null, char? delimiter = null)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the file '{Path.GetFileName(path)}'. Check it still exists.");
        if (IsExcel(path)) return ReadXlsx(path, sheet);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".xls" or ".ods" or ".numbers")
            throw new ActionException($"Automation Portal can read .xlsx and .csv files, but not '{ext}'. Open it in your spreadsheet app and save it as .xlsx or .csv first.");
        var (text, _) = ReadTextWithEncoding(path);
        return ReadCsvRows(text, delimiter ?? (ext == ".tsv" ? '\t' : null));
    }

    public static List<List<ContentItem>> ReadCsvRows(string text, char? delimiter = null) =>
        ParseCsv(text, delimiter, out _).Select(r => r.Select(InferValue).ToList()).ToList();

    private static List<List<ContentItem>> ReadXlsx(string path, string? sheet)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var wb = new XLWorkbook(fs);
            var ws = PickSheet(wb, sheet);
            var rows = new List<List<ContentItem>>();
            var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
            if (used is null) return rows;
            var firstRow = used.RangeAddress.FirstAddress.RowNumber;
            var lastRow = used.RangeAddress.LastAddress.RowNumber;
            var firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
            var lastCol = used.RangeAddress.LastAddress.ColumnNumber;
            for (var r = firstRow; r <= lastRow; r++)
            {
                var row = new List<ContentItem>(lastCol - firstCol + 1);
                for (var c = firstCol; c <= lastCol; c++) row.Add(CellToItem(ws.Cell(r, c)));
                rows.Add(row);
            }
            return rows;
        }
        catch (ActionException) { throw; }
        catch (IOException ex) { throw new ActionException($"Couldn't open '{Path.GetFileName(path)}'. If it is open in another app, close it and try again. ({ex.Message})", ex); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ActionException($"Couldn't read '{Path.GetFileName(path)}'. Make sure it is a valid .xlsx file. ({ex.Message})", ex);
        }
    }

    private static IXLWorksheet PickSheet(XLWorkbook wb, string? sheet)
    {
        if (wb.Worksheets.Count == 0) throw new ActionException("That workbook has no sheets.");
        if (string.IsNullOrWhiteSpace(sheet)) return wb.Worksheets.FirstOrDefault(w => w.Visibility == XLWorksheetVisibility.Visible) ?? wb.Worksheets.First();
        var name = sheet.Trim();
        var byName = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;
        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= wb.Worksheets.Count) return wb.Worksheet(n);
        throw new ActionException($"Couldn't find a sheet called '{name}'. The sheets in this file are: {string.Join(", ", wb.Worksheets.Select(w => w.Name))}.");
    }

    private static ContentItem CellToItem(IXLCell cell)
    {
        XLCellValue v;
        try { v = cell.Value; }
        catch { v = cell.CachedValue; }
        if (v.IsBlank) return new TextItem("");
        if (v.IsBoolean) return new BooleanItem(v.GetBoolean());
        if (v.IsDateTime) return new DateItem(new DateTimeOffset(DateTime.SpecifyKind(v.GetDateTime(), DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(v.GetDateTime())));
        if (v.IsTimeSpan) return new TextItem(v.GetTimeSpan().ToString(@"hh\:mm\:ss"));
        if (v.IsNumber) return new NumberItem(v.GetNumber());
        if (v.IsError) return new TextItem(v.GetError().ToString());
        return new TextItem(v.GetText());
    }

    /// <summary>
    /// Turns rows into one dictionary per data row. headerMode: "auto" (first row is a header when it is all text and distinct),
    /// "yes", "no" (columns are named Column 1, Column 2 …).
    /// </summary>
    public static List<DictionaryItem> ToRecords(List<List<ContentItem>> rows, string headerMode = "auto")
    {
        rows = rows.Where(r => r.Any(c => c.DisplayText.Length > 0)).ToList();
        var result = new List<DictionaryItem>();
        if (rows.Count == 0) return result;
        var width = rows.Max(r => r.Count);
        var useHeader = headerMode switch { "yes" => true, "no" => false, _ => LooksLikeHeader(rows) };

        var names = new List<string>();
        for (var c = 0; c < width; c++)
        {
            var name = useHeader && c < rows[0].Count ? rows[0][c].DisplayText.Trim() : "";
            if (name.Length == 0) name = $"Column {c + 1}";
            var unique = name;
            for (var n = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); n++) unique = $"{name} {n}";
            names.Add(unique);
        }

        foreach (var row in rows.Skip(useHeader ? 1 : 0))
        {
            var entries = new List<KeyValuePair<string, ContentValue>>(width);
            for (var c = 0; c < width; c++)
                entries.Add(new(names[c], ContentValue.From(c < row.Count ? row[c] : new TextItem(""))));
            result.Add(new DictionaryItem(entries, string.Join(", ", entries.Select(e => e.Value.AsText()).Where(s => s.Length > 0))));
        }
        return result;
    }

    private static bool LooksLikeHeader(List<List<ContentItem>> rows)
    {
        if (rows.Count < 2) return false;
        var first = rows[0].Where(c => c.DisplayText.Trim().Length > 0).ToList();
        if (first.Count == 0 || first.Any(c => c is not TextItem)) return false;
        var texts = first.Select(c => c.DisplayText.Trim()).ToList();
        if (texts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != texts.Count) return false;
        // If every following row is text-only too, still accept: it is the usual shape of a contact or product list.
        return true;
    }

    // ---- writing -----------------------------------------------------------------------------

    /// <summary>A cell as it is written to a CSV file.</summary>
    public static string ToCell(ContentValue value)
    {
        if (value.IsEmpty) return "";
        if (value.Count == 1)
        {
            return value[0] switch
            {
                NumberItem n => ContentFormat.Number(n.Value),
                BooleanItem b => b.Value ? "true" : "false",
                DateItem d => FormatDate(d.Value),
                DictionaryItem di => value.ToJson(),
                var other => other.DisplayText,
            };
        }
        return string.Join(", ", value.Select(i => i is DateItem d ? FormatDate(d.Value) : i.DisplayText));
    }

    private static string FormatDate(DateTimeOffset d)
    {
        var local = d.DateTime;
        return d.TimeOfDay == TimeSpan.Zero ? local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : local.Second == 0 ? local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string EscapeField(string value, char delimiter)
    {
        if (value.Length == 0) return "";
        var needs = value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value[0] == ' ' || value[^1] == ' ';
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string FormatLine(IEnumerable<string> fields, char delimiter) =>
        string.Join(delimiter, fields.Select(f => EscapeField(f, delimiter)));

    /// <summary>The columns of a list: every key in order of first appearance for dictionaries; null when the items aren't dictionaries.</summary>
    public static List<string>? ColumnsOf(IEnumerable<ContentItem> items)
    {
        var cols = new List<string>();
        var any = false;
        foreach (var d in items.OfType<DictionaryItem>())
        {
            any = true;
            foreach (var k in d.Keys) if (!cols.Contains(k, StringComparer.OrdinalIgnoreCase)) cols.Add(k);
        }
        return any ? cols : null;
    }

    /// <summary>Builds CSV text from dictionaries (header + rows) or from plain items (one column).</summary>
    public static string MakeCsv(IReadOnlyList<ContentItem> items, char delimiter, bool includeHeader)
    {
        var sb = new StringBuilder();
        var cols = ColumnsOf(items);
        if (cols is not null)
        {
            if (includeHeader) sb.Append(FormatLine(cols, delimiter)).Append("\r\n");
            foreach (var item in items)
            {
                if (item is DictionaryItem d) sb.Append(FormatLine(cols.Select(c => ToCell(d.Get(c))), delimiter));
                else sb.Append(FormatLine(new[] { item.DisplayText }, delimiter));
                sb.Append("\r\n");
            }
        }
        else
        {
            foreach (var item in items) sb.Append(FormatLine(new[] { ToCell(ContentValue.From(item)) }, delimiter)).Append("\r\n");
        }
        return sb.ToString();
    }

    // ---- appending a row ---------------------------------------------------------------------

    /// <summary>
    /// Adds one row to a .csv or .xlsx file (creating it when missing). A dictionary is matched to the file's header by key
    /// (new keys become new columns); a list of values is added in order.
    /// </summary>
    public static void AppendRow(string path, ContentValue row, string? sheet = null)
    {
        if (row.IsEmpty) throw new ActionException("There is nothing to add. Choose the values for the new row.");
        try
        {
            TestRootGuard.Check(path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (IsExcel(path)) AppendXlsx(path, row, sheet); else AppendCsv(path, row);
        }
        catch (IOException ex)
        {
            throw new ActionException($"Couldn't write to '{Path.GetFileName(path)}'. If it is open in another app, close it and try again. ({ex.Message})", ex);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ActionException($"Automation Portal isn't allowed to change '{Path.GetFileName(path)}'. Check the file isn't read-only.");
        }
    }

    private static DictionaryItem? RowDictionary(ContentValue row) =>
        row.Count == 1 && row[0] is DictionaryItem d ? d : null;

    private static List<ContentValue> RowValues(ContentValue row) => row.Select(ContentValue.From).ToList();

    private static void AppendCsv(string path, ContentValue row)
    {
        var dict = RowDictionary(row);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            var sb = new StringBuilder();
            if (dict is not null) sb.Append(FormatLine(dict.Keys, ',')).Append("\r\n");
            var values = dict is not null ? dict.Entries.Select(e => ToCell(e.Value)) : RowValues(row).Select(ToCell);
            sb.Append(FormatLine(values, ',')).Append("\r\n");
            var fresh = sb.ToString();
            PathSafety.WriteViaTemp(path, tmp => File.WriteAllText(tmp, fresh, new UTF8Encoding(true)));
            return;
        }

        var (text, enc) = ReadTextWithEncoding(path);
        var rows = ParseCsv(text, null, out var delim);
        var newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        List<string> line;

        if (dict is not null)
        {
            var header = rows.Count > 0 ? rows[0] : new List<string>();
            var added = false;
            foreach (var k in dict.Keys)
                if (!header.Contains(k, StringComparer.OrdinalIgnoreCase)) { header.Add(k); added = true; }
            line = header.Select(h => dict.TryGet(h, out var v) ? ToCell(v) : "").ToList();
            if (added)
            {
                var sb = new StringBuilder();
                foreach (var r in rows) sb.Append(FormatLine(Pad(r, header.Count), delim)).Append(newline);
                sb.Append(FormatLine(line, delim)).Append(newline);
                // A new column means rewriting the whole file: keep the original once, then swap the new version in.
                var rewritten = sb.ToString();
                PathSafety.KeepFirstBackup(path);
                PathSafety.WriteViaTemp(path, tmp => File.WriteAllText(tmp, rewritten, enc));
                return;
            }
        }
        else line = RowValues(row).Select(ToCell).ToList();

        var append = (text.EndsWith('\n') || text.EndsWith('\r') ? "" : newline) + FormatLine(line, delim) + newline;
        TestRootGuard.Check(path);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = enc.GetBytes(append);
        fs.Write(bytes, 0, bytes.Length);
    }

    private static IEnumerable<string> Pad(List<string> r, int width) => r.Concat(Enumerable.Repeat("", Math.Max(0, width - r.Count)));

    private static void SetCell(IXLCell cell, ContentValue value)
    {
        if (value.IsEmpty) { cell.Clear(XLClearOptions.Contents); return; }
        if (value.Count == 1)
        {
            switch (value[0])
            {
                case NumberItem n: cell.Value = n.Value; return;
                case BooleanItem b: cell.Value = b.Value; return;
                case DateItem d: cell.Value = d.Value.DateTime; return;
            }
        }
        cell.Value = ToCell(value);
    }

    private static void AppendXlsx(string path, ContentValue row, string? sheet)
    {
        var dict = RowDictionary(row);
        XLWorkbook wb;
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            // ClosedXML keeps reading from the stream while it works, so give it a private in-memory copy.
            wb = new XLWorkbook(new MemoryStream(ReadAllBytesShared(path)));
        }
        else wb = new XLWorkbook();

        using (wb)
        {
            IXLWorksheet ws;
            if (wb.Worksheets.Count == 0) ws = wb.Worksheets.Add(string.IsNullOrWhiteSpace(sheet) ? "Sheet1" : sheet.Trim());
            else if (string.IsNullOrWhiteSpace(sheet)) ws = wb.Worksheets.First();
            else ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(sheet.Trim(), StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheets.Add(sheet.Trim());

            var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
            if (dict is not null)
            {
                var header = new List<string>();
                if (lastRow > 0)
                {
                    var lastCol = ws.Row(1).LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
                    for (var c = 1; c <= lastCol; c++) header.Add(ws.Cell(1, c).GetString().Trim());
                }
                else lastRow = 0;
                foreach (var k in dict.Keys)
                {
                    if (header.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;
                    header.Add(k);
                    ws.Cell(1, header.Count).Value = k;
                    ws.Cell(1, header.Count).Style.Font.Bold = true;
                    if (lastRow == 0) lastRow = 1;
                }
                if (lastRow == 0) lastRow = 1;
                var target = lastRow + 1;
                for (var c = 0; c < header.Count; c++)
                    if (dict.TryGet(header[c], out var v)) SetCell(ws.Cell(target, c + 1), v);
            }
            else
            {
                var target = lastRow + 1;
                var values = RowValues(row);
                for (var c = 0; c < values.Count; c++) SetCell(ws.Cell(target, c + 1), values[c]);
            }

            // ClosedXML may not keep everything a workbook holds (charts, pivot tables, macros), so keep the original once
            // as "name.bak.xlsx" before the first rewrite, then swap the new version in.
            PathSafety.KeepFirstBackup(path);
            PathSafety.WriteViaTemp(path, tmp => wb.SaveAs(tmp));
        }
    }
}
