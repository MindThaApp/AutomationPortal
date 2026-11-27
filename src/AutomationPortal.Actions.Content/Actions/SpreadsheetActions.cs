using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class ReadSpreadsheetAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sheet.read", Title = Strings.Get("SheetRead_Title"), Category = ActionCategories.Spreadsheets, Icon = "TableSimple24",
        Keywords = new[] { "excel", "xlsx", "csv", "rows", "table", "import", "data", "comma separated" },
        Summary = Strings.Get("SheetRead_Summary"),
        Description = Strings.Get("SheetRead_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("SheetRead_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("file", Strings.Get("SheetRead_Param_File_Label"), isInput: true, required: true),
            ParameterDescriptor.Text("sheet", Strings.Get("SheetRead_Param_Sheet_Label"), null, Strings.Get("SheetRead_Param_Sheet_Placeholder")) with { Hint = Strings.Get("SheetRead_Param_Sheet_Hint") },
            ParameterDescriptor.ChoiceOf("header", Strings.Get("SheetRead_Param_Header_Label"), "auto", new Choice("auto", Strings.Get("SheetRead_Choice_Auto")), new Choice("yes", Strings.Get("SheetRead_Choice_Yes")), new Choice("no", Strings.Get("SheetRead_Choice_No"))),
            ParameterDescriptor.ChoiceOf("delimiter", Strings.Get("SheetRead_Param_Delimiter_Label"), "auto",
                new Choice("auto", Strings.Get("SheetRead_Choice_Auto")), new Choice("comma", Strings.Get("SheetRead_Choice_Comma")), new Choice("semicolon", Strings.Get("SheetRead_Choice_Semicolon")), new Choice("tab", Strings.Get("SheetRead_Choice_Tab")), new Choice("pipe", Strings.Get("SheetRead_Choice_Pipe"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("file");
        if (input.IsEmpty) throw new ActionException(Strings.Get("SheetRead_Error_ThereIsNoSpreadsheet"));
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
        else throw new ActionException(Strings.Get("SheetRead_Error_AutomationPortalCanRead"));

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
        Id = "sheet.addRow", Title = Strings.Get("SheetAddRow_Title"), Category = ActionCategories.Spreadsheets, Icon = "TableAdd24",
        Keywords = new[] { "append", "log", "excel", "xlsx", "csv", "record", "new row", "table" },
        Summary = Strings.Get("SheetAddRow_Summary"),
        Description = Strings.Get("SheetAddRow_Description"),
        InputKinds = new[] { ContentKind.Dictionary, ContentKind.Any }, OutputKind = ContentKind.File, OutputName = Strings.Get("SheetAddRow_OutputName"),
        IsDestructive = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("row", Strings.Get("SheetAddRow_Param_Row_Label"), isInput: true, required: true),
            new ParameterDescriptor { Key = "file", Label = Strings.Get("SheetAddRow_Param_File_Label"), Type = ParamType.File, Required = true, Placeholder = Strings.Get("SheetAddRow_Param_File_Placeholder"), Hint = Strings.Get("SheetAddRow_Param_File_Hint") },
            ParameterDescriptor.Text("sheet", Strings.Get("SheetAddRow_Param_Sheet_Label"), null, Strings.Get("SheetAddRow_Param_Sheet_Placeholder")) with { Hint = Strings.Get("SheetAddRow_Param_Sheet_Hint") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var row = await ctx.GetContentAsync("row");
        var path = await ctx.GetPathAsync("file");
        if (path is null) throw new ActionException(Strings.Get("SheetAddRow_Error_ChooseTheSpreadsheetFile"));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length == 0) path += ext = ".xlsx";
        if (!SheetIO.IsExcel(path) && ext is not (".csv" or ".tsv" or ".txt"))
            throw new ActionException(Strings.Format("SheetAddRow_Error_AutomationPortalCanAdd", ext));
        var sheet = (await ctx.GetTextAsync("sheet")).Trim();
        await Task.Run(() => SheetIO.AppendRow(path, row, sheet), ctx.CancellationToken);
        return ContentValue.Files(new[] { path });
    }
}

public sealed class MakeCsvAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sheet.makeCsv", Title = Strings.Get("SheetMakeCsv_Title"), Category = ActionCategories.Spreadsheets, Icon = "DocumentTable24",
        Keywords = new[] { "export", "comma separated", "csv", "table", "dictionary", "excel", "spreadsheet" },
        Summary = Strings.Get("SheetMakeCsv_Summary"),
        Description = Strings.Get("SheetMakeCsv_Description"),
        InputKinds = new[] { ContentKind.Dictionary, ContentKind.Any }, OutputKind = ContentKind.File, OutputName = Strings.Get("SheetMakeCsv_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", Strings.Get("SheetMakeCsv_Param_List_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("output", Strings.Get("SheetMakeCsv_Param_Output_Label"), "file", new Choice("file", Strings.Get("SheetMakeCsv_Choice_File")), new Choice("text", Strings.Get("SheetMakeCsv_Choice_Text"))),
            ParameterDescriptor.ChoiceOf("delimiter", Strings.Get("SheetMakeCsv_Param_Delimiter_Label"), "comma", new Choice("comma", Strings.Get("SheetMakeCsv_Choice_Comma")), new Choice("semicolon", Strings.Get("SheetMakeCsv_Choice_Semicolon")), new Choice("tab", Strings.Get("SheetMakeCsv_Choice_Tab"))),
            ParameterDescriptor.Bool("header", Strings.Get("SheetMakeCsv_Param_Header_Label"), true),
            ParameterDescriptor.Text("fileName", Strings.Get("SheetMakeCsv_Param_FileName_Label"), "Data.csv"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        if (list.IsEmpty) throw new ActionException(Strings.Get("SheetMakeCsv_Error_ThereIsNothingTo"));
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
