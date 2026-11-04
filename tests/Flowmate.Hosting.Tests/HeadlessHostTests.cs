using System.IO.Compression;
using Flowmate.Core;
using Xunit;

namespace Flowmate.Hosting.Tests;

/// <summary>An action the host app adds next to Flowmate's own (like Files Portal's "Add tag").</summary>
public sealed class AddTagTestAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "test.portal.addTag", Title = "Add Tag", Category = ActionCategories.FilesAndFolders, Icon = "Tag24",
        Summary = "Tag {files} with {tag}", Description = "Test-only host action.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "Tagged",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("tag", "Tag"),
            ParameterDescriptor.Text("pane", "Pane"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await ctx.GetFilesAsync("files");
        var tag = await ctx.GetTextAsync("tag");
        var pane = await ctx.GetTextAsync("pane");
        return ContentValue.Text($"{pane}|{tag}|{string.Join(",", files.Select(f => f.FullName))}");
    }
}

public sealed class HeadlessHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-hosting-tests", Guid.NewGuid().ToString("N")[..8]);
    private readonly string _work;

    public HeadlessHostTests()
    {
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private FlowmateEngine BuildEngine() => FlowmateEngine.CreateBuilder()
        .UsePaths(FlowmatePaths.ForRoot(Path.Combine(_root, "data")))
        .AddActions(typeof(Flowmate.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
        .AddActions(typeof(Flowmate.Actions.Documents.AiService).Assembly)
        .AddActions(typeof(Flowmate.Actions.Windows.Apps.InstalledApps).Assembly)
        .AddActionsFrom<AddTagTestAction>()
        .Build();

    [Fact]
    public async Task Runs_a_file_shortcut_with_no_WPF_services_after_a_save_and_load_round_trip()
    {
        var engine = BuildEngine();

        var b = new ShortcutBuilder("Pack a note");
        var folder = b.Add("file.newFolder", ("name", "Out"), ("parent", ParamValue.Named("CurrentFolder")));
        var saved = b.Add("file.save", ("content", "hello from a host"), ("folder", ParamValue.Output(folder)), ("name", "note.txt"));
        var renamed = b.Add("file.rename", ("files", ParamValue.Output(saved)), ("pattern", "renamed {name}"));
        b.Add("file.zip", ("files", ParamValue.Output(renamed)), ("name", "Bundle"));
        var shortcut = b.Build();
        Assert.Empty(engine.Validate(shortcut).Where(i => i.Severity == IssueSeverity.Error));

        var file = Path.Combine(_root, "Pack a note.flowmate");
        engine.SaveShortcut(shortcut, file);
        var loaded = engine.LoadShortcut(file);

        var result = await engine.RunAsync(loaded, ContentValue.Empty, new RunOptions
        {
            SeedVariables = new() { ["CurrentFolder"] = ContentValue.Files(new[] { _work }) },
        });

        Assert.True(result.Status == RunStatus.Succeeded, result.ErrorMessage);
        var zip = Path.Combine(_work, "Out", "Bundle.zip");
        Assert.True(File.Exists(zip));
        using var archive = ZipFile.OpenRead(zip);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("renamed note.txt", entry.Name);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("hello from a host", reader.ReadToEnd());
    }

    [Fact]
    public async Task Host_actions_get_the_selection_as_input_and_the_seed_variables()
    {
        var engine = BuildEngine();
        Assert.NotNull(engine.Actions.Get("test.portal.addTag"));
        var a = Path.Combine(_work, "a.txt");
        var c = Path.Combine(_work, "c.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(c, "c");

        var b = new ShortcutBuilder("Tag selection");
        b.Add("test.portal.addTag", ("tag", ParamValue.Text("Work ", VarRef.Named("SelectedCount"))), ("pane", ParamValue.Named("ActivePaneId")));
        var result = await engine.RunAsync(b.Build(), ContentValue.Files(new[] { a, c }), new RunOptions
        {
            SeedVariables = new()
            {
                ["CurrentFolder"] = ContentValue.Files(new[] { _work }),
                ["ActivePaneId"] = ContentValue.Text("left"),
                ["ActiveTabId"] = ContentValue.Text("tab-1"),
                ["SelectedCount"] = ContentValue.Number(2),
            },
        });

        Assert.True(result.Status == RunStatus.Succeeded, result.ErrorMessage);
        Assert.Equal("left|Work 2|a.txt,c.txt", result.Output.AsText());
    }

    [Fact]
    public async Task Resizes_and_converts_a_picture_with_no_WPF_anywhere()
    {
        var engine = BuildEngine();
        var picture = Path.Combine(_work, "p.png");
        File.WriteAllBytes(picture, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var b = new ShortcutBuilder("Shrink and convert");
        var resized = b.Add("img.resize", ("by", ParamValue.Choice("width")), ("width", 40), ("neverEnlarge", false));
        b.Add("img.convert", ("images", ParamValue.Output(resized)), ("format", ParamValue.Choice("jpeg")));
        var result = await engine.RunAsync(b.Build(), ContentValue.Files(new[] { picture }));

        Assert.True(result.Status == RunStatus.Succeeded, result.ErrorMessage);
        var made = Assert.IsType<ImageItem>(result.Output[0]);
        Assert.EndsWith(".jpg", made.Path);
        Assert.Equal((40, 40), (made.Width, made.Height));
        var bytes = File.ReadAllBytes(made.Path);
        Assert.True(bytes[0] == 0xFF && bytes[1] == 0xD8);
        Assert.Equal(40, Flowmate.Actions.Documents.ImageEngine.ReadSize(made.Path).Width);
        Assert.IsType<Flowmate.Actions.Documents.WinRtImageBackend>(Flowmate.Actions.Documents.ImageEngine.Backend);
    }

    [Theory]
    [InlineData("ui.alert", "can't show dialogs here")]
    public async Task Services_the_host_left_out_fail_with_a_clear_message(string actionId, string expected)
    {
        var engine = BuildEngine();
        Assert.NotNull(engine.Actions.Get(actionId));
        var picture = Path.Combine(_work, "p.png");
        File.WriteAllBytes(picture, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var b = new ShortcutBuilder("Needs a service");
        b.Add(actionId);
        var result = await engine.RunAsync(b.Build(), ContentValue.Files(new[] { picture }));

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains(expected, result.ErrorMessage);
    }
}
