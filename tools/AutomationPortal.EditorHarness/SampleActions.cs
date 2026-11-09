using System.IO;
using Flowmate.Core;

namespace Flowmate.EditorHarness;

/// <summary>Harness-only actions: one that shows every parameter type, and a few with different outputs / behaviours.</summary>
public sealed class SampleEverythingAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.everything", Title = "Sample: Every Kind of Option", Category = ActionCategories.Scripting, Icon = "Beaker24",
        Keywords = new[] { "demo", "sample", "test" },
        Summary = "Try {text}, {number} and {choice}",
        Description = "A harness-only action that has one option of every type, so the editor can be tried out.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Sample Result",
        Parameters = new[]
        {
            ParameterDescriptor.Text("text", "Text", "Hello", "Type some text"),
            ParameterDescriptor.MultilineText("multiline", "Long text", null, "Write several lines"),
            ParameterDescriptor.Number("number", "Amount", 5, 0, 100, "items") with { Step = 5 },
            ParameterDescriptor.Bool("flag", "Turn it on", true),
            ParameterDescriptor.ChoiceOf("choice", "Mode", "a", new Choice("a", "Alpha"), new Choice("b", "Beta"), new Choice("c", "Gamma")),
            ParameterDescriptor.Text("extra", "Extra (only for Beta)") with { VisibleWhen = new VisibleWhen("choice", "b") },
            ParameterDescriptor.DynamicChoice("dynamic", "Pick from a loaded list", async (_, ct) =>
            {
                await Task.Delay(400, ct);
                return Enumerable.Range(1, 14).Select(i => new Choice($"item{i}", $"Loaded item {i}", i % 3 == 0 ? "Star24" : null)).ToList();
            }),
            ParameterDescriptor.Of("date", "Date", ParamType.Date),
            ParameterDescriptor.Of("time", "Time", ParamType.Time, ParamValue.Time("07:30")),
            ParameterDescriptor.Of("datetime", "Date and time", ParamType.DateTime),
            ParameterDescriptor.Duration("duration", "How long", 90),
            ParameterDescriptor.FileParam("file", "File"),
            ParameterDescriptor.FolderParam("folder", "Folder"),
            ParameterDescriptor.Of("savefile", "Save as", ParamType.SaveFile, null, "Where to save it"),
            ParameterDescriptor.Of("app", "App", ParamType.App),
            ParameterDescriptor.Of("hotkey", "Keyboard shortcut", ParamType.Hotkey),
            ParameterDescriptor.Of("color", "Colour", ParamType.Color, ParamValue.Text("#2B5DD8")),
            ParameterDescriptor.Of("url", "Link", ParamType.Url, null, "https://"),
            ParameterDescriptor.Content("content", "Anything"),
            ParameterDescriptor.ListParam("list", "A list"),
            new ParameterDescriptor { Key = "dict", Label = "A dictionary", Type = ParamType.Dictionary },
            new ParameterDescriptor { Key = "conds", Label = "Conditions", Type = ParamType.Conditions, AllowAskEachTime = false },
            new ParameterDescriptor { Key = "varname", Label = "Variable name", Type = ParamType.VariableName, AllowVariables = false },
            new ParameterDescriptor
            {
                Key = "shortcut", Label = "Shortcut", Type = ParamType.Shortcut,
                ChoicesProvider = (s, _) => Task.FromResult<IReadOnlyList<Choice>>(s.Library.All.Select(x => new Choice(x.Id.ToString("D"), x.Name, x.Icon)).ToList()),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Text($"{await ctx.GetTextAsync("text")} x {await ctx.GetNumberAsync("number", 0)}");
}

public sealed class SampleFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.files", Title = "Sample: Get Some Files", Category = ActionCategories.FilesAndFolders, Icon = "Folder24",
        Summary = "Get {count} files from Windows",
        Description = "Returns a few files from the Windows folder.",
        OutputKind = ContentKind.File, OutputName = "Some Files",
        Parameters = new[] { ParameterDescriptor.Number("count", "How many", 3, 1, 10) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var n = (int)await ctx.GetNumberAsync("count", 3);
        return ContentValue.Files(Directory.GetFiles(Environment.SystemDirectory).Take(n));
    }
}

public sealed class SampleImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.image", Title = "Sample: Make a Picture", Category = ActionCategories.Images, Icon = "Image24",
        Summary = "Make a {size} pixel picture",
        Description = "Draws a small gradient picture.",
        OutputKind = ContentKind.Image, OutputName = "Picture",
        Parameters = new[] { ParameterDescriptor.Number("size", "Size", 96, 8, 512, "px") },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var size = (int)await ctx.GetNumberAsync("size", 96);
        var path = ctx.NewTempPath("sample.bmp");
        var rowSize = (size * 3 + 3) / 4 * 4;
        var data = new byte[54 + rowSize * size];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(size).CopyTo(data, 18);
        BitConverter.GetBytes(size).CopyTo(data, 22);
        data[26] = 1; data[28] = 24;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var o = 54 + y * rowSize + x * 3;
                data[o] = (byte)(255 * x / size); data[o + 1] = (byte)(255 * y / size); data[o + 2] = 200;
            }
        await File.WriteAllBytesAsync(path, data);
        return ContentValue.Image(path, size, size);
    }
}

public sealed class SampleSlowAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.slow", Title = "Sample: Take a Moment", Category = ActionCategories.Scripting, Icon = "Hourglass24",
        Summary = "Take {seconds} to finish",
        Description = "Waits a little so you can watch the progress feedback.",
        OutputKind = ContentKind.Text, OutputName = "Slow Result",
        Parameters = new[] { ParameterDescriptor.Duration("seconds", "Time", 2) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var t = await ctx.GetDurationAsync("seconds", TimeSpan.FromSeconds(2));
        var steps = 5;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(t / steps, ctx.CancellationToken);
            ctx.ReportProgress($"Step {i} of {steps}");
        }
        return ContentValue.Text("Done waiting");
    }
}

public sealed class SampleFailAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.fail", Title = "Sample: Always Fails", Category = ActionCategories.Scripting, Icon = "DismissCircle24",
        Summary = "Fail with {message}",
        Description = "Always fails, to show how errors look.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Text("message", "Message", "Couldn't find the file 'report.pdf'. Check the folder still exists.") },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => throw new ActionException(await ctx.GetTextAsync("message"));
}

public sealed class SampleDangerAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.danger", Title = "Sample: Delete Everything", Category = ActionCategories.FilesAndFolders, Icon = "Delete24",
        Summary = "Delete {files}", IsDestructive = true, HasOutput = false,
        Description = "Pretends to delete files (it doesn't).", InputKinds = new[] { ContentKind.File },
        Parameters = new[] { ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
}

public sealed class SampleDictAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "sample.person", Title = "Sample: Get a Person", Category = ActionCategories.ListsAndDictionaries, Icon = "Person24",
        Summary = "Get a person", Description = "Returns a dictionary with a name, an age and a city.",
        OutputKind = ContentKind.Dictionary, OutputName = "Person",
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) =>
        Task.FromResult(ContentValue.Dict(("name", ContentValue.Text("Sam")), ("age", ContentValue.Number(34)), ("city", ContentValue.Text("Lisbon"))));
}
