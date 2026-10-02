using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Actions;

public sealed class ReadSpreadsheetAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sheet.read", Title = "Read Spreadsheet", Category = ActionCategories.Spreadsheets, Icon = "TableSimple24",
        Keywords = new[] { "excel", "xlsx", "csv", "rows", "table", "import", "data", "comma separated" },
        Summary = "Read the spreadsheet {file}",
        Description = "Reads an Excel (.xlsx) or CSV file and gives you one dictionary per row, with the column headings as keys. Numbers, dates and true/false values keep their type. CSV text from the web works too.",
        InputKinds = new[] { ContentKind.File, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = "Spreadsheet Rows",
        Parameters = new[]
        {
            ParameterDescriptor.Content("file", "File or CSV text", isInput: true, required: true),
            ParameterDescriptor.Text("sheet", "Sheet", null, "First sheet") with { Hint = "For Excel files: the sheet's name or number." },
            ParameterDescriptor.ChoiceOf("header", "First row is a heading", "auto", new Choice("auto", "Detect automatically"), new Choice("yes", "Yes"), new Choice("no", "No")),
            ParameterDescriptor.ChoiceOf("delimiter", "CSV separator", "auto",
                new Choice("auto", "Detect automatically"), new Choice("comma", "Comma"), new Choice("semicolon", "Semicolon"), new Choice("tab", "Tab"), new Choice("pipe", "Pipe |")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("file");
        if (input.IsEmpty) throw new ActionException("There is no spreadsheet to read. Choose an Excel or CSV file.");
        var sheet = (await ctx.GetTextAsync("sheet")).Trim();
        var header = await ctx.GetChoiceAsync("header", "auto");
        char? delimiter = (await ctx.GetChoiceAsync("delimiter", "auto")) switch
        {
            "comma" => ',', "semicolon" => ';', "tab" => '\t', "pipe" => '|', _ => null,
        };

        List<List<ContentItem>> rows;
        var item = input[0];
        if (item is PathItem p && p is not FolderItem) rows = await Task.Run(() => SheetIO.ReadRows(p.Path, sheet, delimiter), ctx.CancellationToken);
        else if (item is TextItem t && LooksLikeFilePath(t.Value, out var path)) rows = await Task.Run(() => SheetIO.ReadRows(path, sheet, delimiter), ctx.CancellationToken);
        else if (item is TextItem csv) rows = SheetIO.ReadCsvRows(csv.Value, delimiter);
        else throw new ActionException("Flowmate can read spreadsheets from a file or from CSV text. Choose an Excel or CSV file.");

        var records = SheetIO.ToRecords(rows, header);
        return new ContentValue(records);
    }

    private static bool LooksLikeFilePath(string text, out string path)
    {
        path = text.Trim().Trim('"');
        if (path.Contains('\n') || path.Length < 3 || path.Length > 260) return false;
        try { return Path.IsPathRooted(path) && (File.Exists(path) || Path.HasExtension(path) && !path.Contains(',') ); }
        catch { return false; }
    }
}

public sealed class AddRowToSpreadsheetAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sheet.addRow", Title = "Add Row to Spreadsheet", Category = ActionCategories.Spreadsheets, Icon = "TableAdd24",
        Keywords = new[] { "append", "log", "excel", "xlsx", "csv", "record", "new row", "table" },
        Summary = "Add {row} to the spreadsheet {file}",
        Description = "Adds a new row at the bottom of an Excel (.xlsx) or CSV file. Give it a dictionary and its keys are matched to the column headings (new keys become new columns), or give it a list of values to add in order. The file is created if it doesn't exist yet.",
        InputKinds = new[] { ContentKind.Dictionary, ContentKind.Any }, OutputKind = ContentKind.File, OutputName = "Spreadsheet",
        Parameters = new[]
        {
            ParameterDescriptor.Content("row", "Row", isInput: true, required: true),
            new ParameterDescriptor { Key = "file", Label = "Spreadsheet file", Type = ParamType.File, Required = true, Placeholder = "C:\\Users\\you\\Documents\\log.xlsx", Hint = "An .xlsx or .csv file." },
            ParameterDescriptor.Text("sheet", "Sheet", null, "First sheet") with { Hint = "For Excel files: the sheet's name. A new sheet is made if it doesn't exist." },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var row = await ctx.GetContentAsync("row");
        var path = await ctx.GetPathAsync("file");
        if (path is null) throw new ActionException("Choose the spreadsheet file to add the row to (an .xlsx or .csv file).");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length == 0) path += ext = ".xlsx";
        if (!SheetIO.IsExcel(path) && ext is not (".csv" or ".tsv" or ".txt"))
            throw new ActionException($"Flowmate can add rows to .xlsx and .csv files, not '{ext}'.");
        var sheet = (await ctx.GetTextAsync("sheet")).Trim();
        await Task.Run(() => SheetIO.AppendRow(path, row, sheet), ctx.CancellationToken);
        return ContentValue.Files(new[] { path });
    }
}

public sealed class MakeCsvAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sheet.makeCsv", Title = "Make CSV from List", Category = ActionCategories.Spreadsheets, Icon = "DocumentTable24",
        Keywords = new[] { "export", "comma separated", "csv", "table", "dictionary", "excel", "spreadsheet" },
        Summary = "Make CSV from {list}",
        Description = "Turns a list into CSV (comma separated) data. A list of dictionaries becomes a table with a heading row; a list of text becomes one column. Get it as text or as a .csv file.",
        InputKinds = new[] { ContentKind.Dictionary, ContentKind.Any }, OutputKind = ContentKind.File, OutputName = "CSV",
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", "List", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("output", "Give me", "file", new Choice("file", "a CSV file"), new Choice("text", "CSV text")),
            ParameterDescriptor.ChoiceOf("delimiter", "Separator", "comma", new Choice("comma", "Comma"), new Choice("semicolon", "Semicolon"), new Choice("tab", "Tab")),
            ParameterDescriptor.Bool("header", "Include a heading row", true),
            ParameterDescriptor.Text("fileName", "File name", "Data.csv"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        if (list.IsEmpty) throw new ActionException("There is nothing to put in the CSV. Give it a list of dictionaries or text.");
        var delimiter = (await ctx.GetChoiceAsync("delimiter", "comma")) switch { "semicolon" => ';', "tab" => '\t', _ => ',' };
        var csv = SheetIO.MakeCsv(list.Items, delimiter, await ctx.GetBoolAsync("header", true));
        if (await ctx.GetChoiceAsync("output", "file") == "text") return ContentValue.Text(csv);
        var name = (await ctx.GetTextAsync("fileName", "Data.csv")).Trim();
        if (name.Length == 0) name = "Data.csv";
        if (!Path.HasExtension(name)) name += ".csv";
        var path = ctx.NewTempPath(name);
        // UTF-8 with a byte-order mark so Excel opens accented characters correctly.
        await File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(true), ctx.CancellationToken);
        return ContentValue.Files(new[] { path });
    }
}
