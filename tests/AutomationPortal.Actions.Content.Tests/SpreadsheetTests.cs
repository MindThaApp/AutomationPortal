using System.Text;
using ClosedXML.Excel;
using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

public class CsvParsingTests
{
    [Fact]
    public void Parses_plain_rows_and_blank_lines_and_missing_final_newline()
    {
        var rows = SheetIO.ParseCsv("a,b,c\n1,2,3\n\n4,5,6", null, out var d);
        Assert.Equal(',', d);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "4", "5", "6" }, rows[2]);
        Assert.Equal(2, SheetIO.ParseCsv("a,b\r\n1,2\r\n", null, out _).Count);
        Assert.Equal(2, SheetIO.ParseCsv("a,b\r1,2\r", null, out _).Count);
    }

    [Fact]
    public void Handles_quotes_embedded_delimiters_doubled_quotes_and_line_breaks()
    {
        var rows = SheetIO.ParseCsv("name,note\r\n\"Smith, Jo\",\"said \"\"hi\"\"\"\r\n\"multi\nline\",\"\"\r\nplain,\"  spaced  \"", null, out _);
        Assert.Equal(new[] { "Smith, Jo", "said \"hi\"" }, rows[1]);
        Assert.Equal(new[] { "multi\nline", "" }, rows[2]);
        Assert.Equal("  spaced  ", rows[3][1]);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void Keeps_empty_fields_and_trailing_delimiters()
    {
        var rows = SheetIO.ParseCsv("a,,c\n,,\nx,y,", null, out _);
        Assert.Equal(new[] { "a", "", "c" }, rows[0]);
        Assert.Equal(new[] { "", "", "" }, rows[1]);
        Assert.Equal(new[] { "x", "y", "" }, rows[2]);
    }

    [Theory]
    [InlineData("a;b;c\n1,5;2;3\n4;5;6", ';')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a|b|c\n1|2|3", '|')]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("\"a,b\";c;d\n1;2;3", ';')]
    [InlineData("just one column\nsecond line", ',')]
    [InlineData("", ',')]
    [InlineData("name,city\n\"Doe; Jane\",Rome\n\"Roe; Rick\",Oslo", ',')]
    public void Detects_the_delimiter(string text, char expected)
    {
        SheetIO.ParseCsv(text, null, out var used);
        Assert.Equal(expected, used);
    }

    [Fact]
    public void An_explicit_delimiter_wins_and_a_bom_is_ignored()
    {
        var rows = SheetIO.ParseCsv("﻿a;b,c\n1;2,3", ',', out var d);
        Assert.Equal(',', d);
        Assert.Equal(new[] { "a;b", "c" }, rows[0]);
        Assert.Equal("a", SheetIO.ParseCsv("﻿a,b", null, out _)[0][0]);
    }

    [Theory]
    [InlineData("12", 12.0)] [InlineData("-3.5", -3.5)] [InlineData("+5", 5.0)] [InlineData(".5", 0.5)] [InlineData("1e3", 1000.0)] [InlineData("0", 0.0)] [InlineData("0.25", 0.25)]
    public void Infers_numbers(string raw, double expected) => Assert.Equal(expected, Assert.IsType<NumberItem>(SheetIO.InferValue(raw)).Value);

    [Theory]
    [InlineData("007")] [InlineData("1,000")] [InlineData("12abc")] [InlineData("12345678901234567890")] [InlineData("hello")] [InlineData("1.2.3")] [InlineData("+44 20 7946")] [InlineData("")]
    public void Keeps_other_things_as_text(string raw) => Assert.IsType<TextItem>(SheetIO.InferValue(raw));

    [Fact]
    public void Infers_booleans_and_dates()
    {
        Assert.True(Assert.IsType<BooleanItem>(SheetIO.InferValue("TRUE")).Value);
        Assert.False(Assert.IsType<BooleanItem>(SheetIO.InferValue("false")).Value);
        Assert.Equal(new DateTime(2026, 10, 2), Assert.IsType<DateItem>(SheetIO.InferValue("2026-10-02")).Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 2, 9, 30, 0), Assert.IsType<DateItem>(SheetIO.InferValue("2026-10-02 09:30")).Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 2, 9, 30, 5), Assert.IsType<DateItem>(SheetIO.InferValue("2026-10-02T09:30:05")).Value.DateTime);
        Assert.IsType<TextItem>(SheetIO.InferValue("2026-13-45"));
        Assert.IsType<TextItem>(SheetIO.InferValue("02/10/2026"));
        Assert.Equal(" padded ", SheetIO.InferValue(" padded ").DisplayText);
    }

    private static List<List<ContentItem>> Rows(params object[][] rows) =>
        rows.Select(r => r.Select(c => c is ContentItem i ? i : c is double d ? new NumberItem(d) : new TextItem((string)c)).ToList()).ToList();

    [Fact]
    public void Header_detection()
    {
        var withHeader = SheetIO.ToRecords(Rows(new object[] { "Name", "Age" }, new object[] { "Sam", 7.0 }, new object[] { "Al", 9.0 }));
        Assert.Equal(2, withHeader.Count);
        Assert.Equal(new[] { "Name", "Age" }, withHeader[0].Keys);
        Assert.Equal(7, withHeader[0].Get("Age").AsNumber());

        var numbersOnly = SheetIO.ToRecords(Rows(new object[] { 1.0, 2.0 }, new object[] { 3.0, 4.0 }));
        Assert.Equal(2, numbersOnly.Count);
        Assert.Equal(new[] { "Column 1", "Column 2" }, numbersOnly[0].Keys);

        Assert.Single(SheetIO.ToRecords(Rows(new object[] { "a", "b" })));
        Assert.Equal(new[] { "Column 1", "Column 2" }, SheetIO.ToRecords(Rows(new object[] { "a", "a" }, new object[] { "1", "2" }))[0].Keys);
    }

    [Fact]
    public void Header_overrides_duplicates_blanks_ragged_and_empty_rows()
    {
        var records = SheetIO.ToRecords(Rows(new object[] { "a", "a", "" }, new object[] { "1", "2" }, new object[] { "", "" }, new object[] { "3", "4", "5", "6" }), "yes");
        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { "a", "a 2", "Column 3", "Column 4" }, records[0].Keys);
        Assert.Equal("", records[0].Get("Column 3").AsText());
        Assert.Equal("5", records[1].Get("Column 3").AsText());

        var none = SheetIO.ToRecords(Rows(new object[] { "Name", "Age" }, new object[] { "Sam", 7.0 }), "no");
        Assert.Equal(2, none.Count);
        Assert.Equal("Name", none[0].Get("Column 1").AsText());
        Assert.Empty(SheetIO.ToRecords(new List<List<ContentItem>>()));
        Assert.Empty(SheetIO.ToRecords(Rows(new object[] { "", "" })));
    }
}

public class SheetFileTests : IDisposable
{
    private readonly TestHost _host = new();
    private string Dir { get { Directory.CreateDirectory(_host.Root); return _host.Root; } }
    private string P(string name) => Path.Combine(Dir, name);
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Reads_a_csv_file_into_typed_dictionaries()
    {
        await File.WriteAllTextAsync(P("people.csv"), "Name,Age,Joined,Active,Zip\r\nSam,7,2026-01-05,TRUE,007\r\n\"Doe, Jane\",31,2025-12-24 18:00,false,00100\r\n", new UTF8Encoding(true));
        var rows = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("people.csv")));
        Assert.Equal(2, rows.Count);
        var sam = (DictionaryItem)rows[0];
        Assert.IsType<NumberItem>(sam.Get("Age")[0]);
        Assert.IsType<DateItem>(sam.Get("Joined")[0]);
        Assert.IsType<BooleanItem>(sam.Get("Active")[0]);
        Assert.Equal("007", sam.Get("Zip").AsText());
        Assert.IsType<TextItem>(sam.Get("Zip")[0]);
        Assert.Equal("Doe, Jane", ((DictionaryItem)rows[1]).Get("Name").AsText());
        Assert.Equal(new DateTime(2025, 12, 24, 18, 0, 0), ((DictionaryItem)rows[1]).Get("Joined").AsDate()!.Value.DateTime);
    }

    [Fact]
    public async Task Reads_semicolon_latin1_and_utf16_files()
    {
        await File.WriteAllBytesAsync(P("latin.csv"), Encoding.Latin1.GetBytes("name;city\nJosé;Zürich\n"));
        var latin = (DictionaryItem)(await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("latin.csv"))))[0];
        Assert.Equal("José", latin.Get("name").AsText());
        Assert.Equal("Zürich", latin.Get("city").AsText());

        await File.WriteAllTextAsync(P("u16.csv"), "a,b\n1,2\n", Encoding.Unicode);
        Assert.Equal(1, (await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("u16.csv"))))[0] is DictionaryItem d ? d.Get("a").AsNumber() : null);

        await File.WriteAllTextAsync(P("data.tsv"), "x\ty\n1\t2\n");
        Assert.Equal(2, ((DictionaryItem)(await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("data.tsv"))))[0]).Get("y").AsNumber());
    }

    [Fact]
    public async Task Reads_csv_text_and_a_path_given_as_text()
    {
        var fromText = await Run(_host, new ReadSpreadsheetAction(), ContentValue.Text("a,b\n1,2\n3,4"));
        Assert.Equal(2, fromText.Count);
        Assert.Equal(4, ((DictionaryItem)fromText[1]).Get("b").AsNumber());

        await File.WriteAllTextAsync(P("t.csv"), "k,v\nx,1");
        var fromPath = await Run(_host, new ReadSpreadsheetAction(), ContentValue.Text(P("t.csv")));
        Assert.Equal("x", ((DictionaryItem)fromPath[0]).Get("k").AsText());
    }

    [Fact]
    public async Task Reader_options_header_and_delimiter()
    {
        var noHeader = await Run(_host, new ReadSpreadsheetAction(), ContentValue.Text("Name,Age\nSam,7"), ("header", C("no")));
        Assert.Equal(2, noHeader.Count);
        var picked = await Run(_host, new ReadSpreadsheetAction(), ContentValue.Text("a;b,c\n1;2,3"), ("delimiter", C("semicolon")));
        Assert.Equal("a", ((DictionaryItem)picked[0]).Keys.First());
        Assert.Equal("2,3", ((DictionaryItem)picked[0]).Get("b,c").AsText().Replace("2,3", "2,3"));
    }

    [Fact]
    public async Task Reader_errors_are_friendly()
    {
        var missing = await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("nope.csv"))));
        Assert.Contains("Couldn't find the file 'nope.csv'", missing.Message);
        await File.WriteAllBytesAsync(P("old.xls"), new byte[] { 1, 2, 3 });
        var xls = await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("old.xls"))));
        Assert.Contains(".xlsx", xls.Message);
        await File.WriteAllTextAsync(P("broken.xlsx"), "this is not a zip");
        var broken = await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("broken.xlsx"))));
        Assert.Contains("valid .xlsx", broken.Message);
        await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.Empty));
        await File.WriteAllTextAsync(P("locked.csv"), "a,b\n1,2");
        using (new FileStream(P("locked.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("locked.csv"))));
            Assert.Contains("another app", locked.Message);
        }
    }

    [Fact]
    public async Task Reads_an_excel_workbook_made_elsewhere_including_dates_formulas_sheets_and_gaps()
    {
        var path = P("book.xlsx");
        using (var wb = new XLWorkbook())
        {
            var s1 = wb.Worksheets.Add("Data");
            s1.Cell("A1").Value = "Item"; s1.Cell("B1").Value = "Qty"; s1.Cell("C1").Value = "Price"; s1.Cell("D1").Value = "Total"; s1.Cell("E1").Value = "When"; s1.Cell("F1").Value = "OK";
            s1.Cell("A2").Value = "Apple"; s1.Cell("B2").Value = 3; s1.Cell("C2").Value = 1.5; s1.Cell("D2").FormulaA1 = "B2*C2"; s1.Cell("E2").Value = new DateTime(2026, 3, 4, 12, 30, 0); s1.Cell("F2").Value = true;
            s1.Cell("A4").Value = "Pear"; s1.Cell("B4").Value = 2; s1.Cell("C4").Value = "7";
            var s2 = wb.Worksheets.Add("Other");
            s2.Cell("A1").Value = "X"; s2.Cell("A2").Value = 99;
            wb.SaveAs(path);
        }
        var rows = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path));
        Assert.Equal(2, rows.Count);                                 // the blank row 3 is skipped
        var apple = (DictionaryItem)rows[0];
        Assert.Equal(new[] { "Item", "Qty", "Price", "Total", "When", "OK" }, apple.Keys);
        Assert.Equal(3, apple.Get("Qty").AsNumber());
        Assert.Equal(4.5, apple.Get("Total").AsNumber());
        Assert.Equal(new DateTime(2026, 3, 4, 12, 30, 0), apple.Get("When").AsDate()!.Value.DateTime);
        Assert.True(apple.Get("OK").AsBool());
        var pear = (DictionaryItem)rows[1];
        Assert.IsType<TextItem>(pear.Get("Price")[0]);                // text stays text even if it looks numeric
        Assert.Equal("", pear.Get("When").AsText());

        var other = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path), ("sheet", "other"));
        Assert.Equal(99, ((DictionaryItem)other[0]).Get("X").AsNumber());
        var byNumber = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path), ("sheet", "2"));
        Assert.Equal(99, ((DictionaryItem)byNumber[0]).Get("X").AsNumber());
        var missing = await Fails(Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path), ("sheet", "Nope")));
        Assert.Contains("Data, Other", missing.Message);

        // still readable while "open in Excel" (shared read)
        using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.Equal(2, (await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path))).Count);
    }

    [Fact]
    public async Task Xlsx_round_trip_through_add_row()
    {
        var path = P("log.xlsx");
        var joined = L(2026, 1, 5);
        var first = ContentValue.Dict(("Name", "Sam"), ("Age", 7), ("Joined", ContentValue.Date(joined)), ("Active", true), ("Zip", "007"));
        var created = await Run(_host, new AddRowToSpreadsheetAction(), first, ("file", ParamValue.File(path)));
        Assert.Equal(path, ((PathItem)created[0]).Path);

        var again = ContentValue.Dict(("Name", "Al"), ("Age", 9), ("City", "Rome"));
        await Run(_host, new AddRowToSpreadsheetAction(), again, ("file", ParamValue.File(path)));
        await Run(_host, new AddRowToSpreadsheetAction(), Texts("Cy", "11"), ("file", ParamValue.File(path)));

        var rows = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path));
        Assert.Equal(3, rows.Count);
        var sam = (DictionaryItem)rows[0];
        Assert.Equal(new[] { "Name", "Age", "Joined", "Active", "Zip", "City" }, sam.Keys);
        Assert.Equal(7, sam.Get("Age").AsNumber());
        Assert.IsType<NumberItem>(sam.Get("Age")[0]);
        Assert.Equal(joined.DateTime, sam.Get("Joined").AsDate()!.Value.DateTime);
        Assert.True(sam.Get("Active").AsBool());
        Assert.Equal("007", sam.Get("Zip").AsText());
        Assert.IsType<TextItem>(sam.Get("Zip")[0]);
        Assert.Equal("", sam.Get("City").AsText());
        var al = (DictionaryItem)rows[1];
        Assert.Equal("Rome", al.Get("City").AsText());
        Assert.Equal(9, al.Get("Age").AsNumber());
        Assert.Equal("", al.Get("Zip").AsText());
        Assert.Equal("Cy", ((DictionaryItem)rows[2]).Get("Name").AsText());      // positional values land under the first columns
        Assert.Equal(11, ((DictionaryItem)rows[2]).Get("Age").AsNumber());
    }

    [Fact]
    public async Task Add_row_keeps_one_backup_of_the_original_workbook_and_is_flagged_destructive()
    {
        Assert.True(new AddRowToSpreadsheetAction().Descriptor.IsDestructive);
        var path = P("budget.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.Worksheets.Add("Sheet1").Cell(1, 1).Value = "Item";
            wb.Worksheet(1).Cell(2, 1).Value = "Rent";
            wb.SaveAs(path);
        }
        var original = await File.ReadAllBytesAsync(path);

        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Item", "Food")), ("file", ParamValue.File(path)));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Item", "Fuel")), ("file", ParamValue.File(path)));

        var backup = P("budget.bak.xlsx");
        Assert.Equal(original, await File.ReadAllBytesAsync(backup));      // the untouched original, not the first rewrite
        Assert.Equal(3, (await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path))).Count);
        Assert.Equal(new[] { "budget.bak.xlsx", "budget.xlsx" }, Directory.GetFiles(Dir, "*budget*").Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public async Task Csv_rewrite_for_a_new_column_keeps_the_original_once()
    {
        var path = P("people.csv");
        await File.WriteAllTextAsync(path, "Name\r\n\"Sam\"\r\n");
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Name", "Al"), ("City", "Rome")), ("file", ParamValue.File(path)));
        Assert.Equal("Name\r\n\"Sam\"\r\n", await File.ReadAllTextAsync(P("people.bak.csv")));
        Assert.Equal("Name,City\r\nSam,\r\nAl,Rome\r\n", await File.ReadAllTextAsync(path));
        Assert.Equal(new[] { "people.bak.csv", "people.csv" }, Directory.GetFiles(Dir, "*people*").Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public async Task Add_row_to_a_named_sheet_creates_it_and_leaves_other_sheets_alone()
    {
        var path = P("multi.xlsx");
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("A", 1)), ("file", ParamValue.File(path)), ("sheet", "Log"));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("B", 2)), ("file", ParamValue.File(path)), ("sheet", "Second"));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("A", 3)), ("file", ParamValue.File(path)), ("sheet", "log"));
        var log = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path), ("sheet", "Log"));
        Assert.Equal(new double[] { 1, 3 }, log.Select(r => ((DictionaryItem)r).Get("A").AsNumber()!.Value));
        var second = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path), ("sheet", "Second"));
        Assert.Equal(2, ((DictionaryItem)second[0]).Get("B").AsNumber());
    }

    [Fact]
    public async Task Add_row_to_a_locked_workbook_explains()
    {
        var path = P("busy.xlsx");
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("A", 1)), ("file", ParamValue.File(path)));
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = await Fails(Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("A", 2)), ("file", ParamValue.File(path))));
            Assert.Contains("another app", ex.Message);
        }
        Assert.Single(await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path)));
    }

    [Fact]
    public async Task Csv_add_row_creates_appends_and_matches_the_header()
    {
        var path = P("log.csv");
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Name", "Sam"), ("Age", 7)), ("file", ParamValue.File(path)));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Age", 9), ("Name", "Al")), ("file", ParamValue.File(path)));
        await Run(_host, new AddRowToSpreadsheetAction(), Texts("Cy", "11"), ("file", ParamValue.File(path)));
        var text = await File.ReadAllTextAsync(path);
        Assert.Equal("Name,Age\r\nSam,7\r\nAl,9\r\nCy,11\r\n", text);

        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("Name", "Di"), ("City", "Oslo")), ("file", ParamValue.File(path)));
        Assert.Equal("Name,Age,City\r\nSam,7,\r\nAl,9,\r\nCy,11,\r\nDi,,Oslo\r\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Csv_add_row_respects_existing_style_newlines_delimiter_and_encoding()
    {
        await File.WriteAllTextAsync(P("nonl.csv"), "a,b\n1,2");
        await Run(_host, new AddRowToSpreadsheetAction(), Texts("3", "4"), ("file", ParamValue.File(P("nonl.csv"))));
        Assert.Equal("a,b\n1,2\n3,4\n", await File.ReadAllTextAsync(P("nonl.csv")));

        await File.WriteAllBytesAsync(P("semi.csv"), Encoding.Latin1.GetBytes("name;city\r\nJosé;Zürich\r\n"));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("name", "Zoë"), ("city", "A;B")), ("file", ParamValue.File(P("semi.csv"))));
        Assert.Equal("name;city\r\nJosé;Zürich\r\nZoë;\"A;B\"\r\n", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(P("semi.csv"))));
        var rows = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(P("semi.csv")));
        Assert.Equal("A;B", ((DictionaryItem)rows[1]).Get("city").AsText());
    }

    [Fact]
    public async Task Csv_add_row_round_trips_awkward_text()
    {
        var path = P("awkward.csv");
        var note = "a, \"b\"\nc";
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("note", note), ("n", 1)), ("file", ParamValue.File(path)));
        await Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Dict(("note", " lead and trail "), ("n", 2)), ("file", ParamValue.File(path)));
        var rows = await Run(_host, new ReadSpreadsheetAction(), ContentValue.File(path));
        Assert.Equal(note, ((DictionaryItem)rows[0]).Get("note").AsText());
        Assert.Equal(" lead and trail ", ((DictionaryItem)rows[1]).Get("note").AsText());
    }

    [Fact]
    public async Task Add_row_errors_and_file_extension_rules()
    {
        var empty = await Fails(Run(_host, new AddRowToSpreadsheetAction(), ContentValue.Empty, ("file", ParamValue.File(P("x.csv")))));
        Assert.Contains("nothing to add", empty.Message);
        var bad = await Fails(Run(_host, new AddRowToSpreadsheetAction(), Texts("a"), ("file", ParamValue.File(P("x.docx")))));
        Assert.Contains(".docx", bad.Message);
        await Fails(Run(_host, new AddRowToSpreadsheetAction(), Texts("a")));
        var noExt = await Run(_host, new AddRowToSpreadsheetAction(), Texts("a"), ("file", ParamValue.File(P("plain"))));
        Assert.EndsWith("plain.xlsx", ((PathItem)noExt[0]).Path);
        Assert.True(File.Exists(P("plain.xlsx")));
        var nested = await Run(_host, new AddRowToSpreadsheetAction(), Texts("a"), ("file", ParamValue.File(P(Path.Combine("sub", "deeper", "n.csv")))));
        Assert.True(File.Exists(((PathItem)nested[0]).Path));
    }

    [Fact]
    public async Task Make_csv_from_dictionaries()
    {
        var list = new ContentValue(new[]
        {
            (ContentItem)new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("name", "Sam"), new KeyValuePair<string, ContentValue>("age", 7) }),
            new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("name", "Doe, Jane"), new KeyValuePair<string, ContentValue>("city", "Rome") }),
        });
        var text = (await Run(_host, new MakeCsvAction(), list, ("output", C("text")))).AsText();
        Assert.Equal("name,age,city\r\nSam,7,\r\n\"Doe, Jane\",,Rome\r\n", text);
        var semi = (await Run(_host, new MakeCsvAction(), list, ("output", C("text")), ("delimiter", C("semicolon")), ("header", false))).AsText();
        Assert.Equal("Sam;7;\r\nDoe, Jane;;Rome\r\n", semi);
    }

    [Fact]
    public async Task Make_csv_formats_dates_booleans_nested_values_and_plain_lists()
    {
        var row = ContentValue.Dict(("when", ContentValue.Date(L(2026, 10, 5))), ("at", ContentValue.Date(L(2026, 10, 5, 9, 30))), ("ok", true),
            ("tags", Texts("a", "b")), ("n", 1.5), ("nested", ContentValue.Dict(("x", 1))));
        var text = (await Run(_host, new MakeCsvAction(), row, ("output", C("text")))).AsText();
        var lines = text.Split("\r\n");
        Assert.Equal("when,at,ok,tags,n,nested", lines[0]);
        Assert.Equal("2026-10-05,2026-10-05 09:30,true,\"a, b\",1.5,\"{\"\"x\"\":1}\"", lines[1]);

        var plain = (await Run(_host, new MakeCsvAction(), Texts("one", "two, three"), ("output", C("text")))).AsText();
        Assert.Equal("one\r\n\"two, three\"\r\n", plain);
    }

    [Fact]
    public async Task Make_csv_file_round_trips_through_read()
    {
        var people = new ContentValue(new[]
        {
            (ContentItem)new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("Name", "Zoë"), new KeyValuePair<string, ContentValue>("Score", 9.5), new KeyValuePair<string, ContentValue>("Note", "line1\nline2, \"quoted\"") }),
            new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("Name", "Al"), new KeyValuePair<string, ContentValue>("Score", -3), new KeyValuePair<string, ContentValue>("Note", "") }),
        });
        var file = await Run(_host, new MakeCsvAction(), people, ("fileName", "people"));
        var path = ((PathItem)file[0]).Path;
        Assert.EndsWith("people.csv", path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(path)).Take(3));
        var back = await Run(_host, new ReadSpreadsheetAction(), file);
        Assert.Equal(2, back.Count);
        Assert.Equal("Zoë", ((DictionaryItem)back[0]).Get("Name").AsText());
        Assert.Equal(9.5, ((DictionaryItem)back[0]).Get("Score").AsNumber());
        Assert.Equal("line1\nline2, \"quoted\"", ((DictionaryItem)back[0]).Get("Note").AsText());
        Assert.Equal(-3, ((DictionaryItem)back[1]).Get("Score").AsNumber());
    }

    [Fact]
    public async Task Make_csv_needs_something_to_write_and_default_name()
    {
        await Fails(Run(_host, new MakeCsvAction(), ContentValue.Empty));
        var file = await Run(_host, new MakeCsvAction(), Texts("x"));
        Assert.EndsWith("Data.csv", ((PathItem)file[0]).Path);
    }
}
