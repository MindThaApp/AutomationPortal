using System.Diagnostics;
using System.Globalization;

namespace AutomationPortal.Actions.Documents;

/// <summary>Notes are plain Markdown files in Documents\Automation Portal Notes\&lt;name&gt;.md.</summary>
public static class NoteStore
{
    public static string Root(string? custom) =>
        string.IsNullOrWhiteSpace(custom) ? Folders.NotesFolder() : custom!;

    public static string PathFor(string? root, string name)
    {
        var clean = ActionContext.SanitizeFileName((name ?? "").Trim());
        if (clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) clean = clean[..^3];
        clean = clean.Trim().TrimEnd('.');
        if (clean.Length == 0) clean = "Notes";
        return Path.Combine(Root(root), clean + ".md");
    }

    public static string FormatEntry(string text, bool timestamp, DateTime now)
    {
        var body = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        var entry = timestamp ? $"**{now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}**\n{body}" : body;
        return entry.Replace("\n", Environment.NewLine);
    }

    public static string Combine(string existing, string entry, bool atTop)
    {
        var nl = Environment.NewLine;
        if (string.IsNullOrWhiteSpace(existing)) return entry + nl;
        return atTop
            ? entry + nl + nl + existing.TrimStart('\r', '\n')
            : existing.TrimEnd('\r', '\n', ' ', '\t') + nl + nl + entry + nl;
    }

    public static string Add(string? root, string name, string text, bool atTop, bool timestamp, DateTime now)
    {
        var path = PathFor(root, name);
        try
        {
            TestRootGuard.Check(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var exists = File.Exists(path);
            var existing = exists ? File.ReadAllText(path) : "";
            var updated = Combine(existing, FormatEntry(text, timestamp, now), atTop);
            // Never rewrite the note in place: the new version is written next to it and swapped in, and the previous
            // version stays as "name.md.autoportal-bak" (a name only Automation Portal uses, so no file of the user's is replaced).
            PathSafety.WriteViaTemp(path, tmp => File.WriteAllText(tmp, updated, new System.Text.UTF8Encoding(false)),
                exists ? BackupPathFor(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException(Strings.Format("NoteStore_Error_CouldnTSaveThe", Path.GetFileName(path)), ex);
        }
        return path;
    }

    /// <summary>Where Add to Note keeps the previous version of a note.</summary>
    public static string BackupPathFor(string notePath) => notePath + ".autoportal-bak";

    public static string? Latest(string? root)
    {
        var dir = Root(root);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.md").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    public static string Resolve(string? root, string name)
    {
        var path = string.IsNullOrWhiteSpace(name) ? Latest(root) : PathFor(root, name);
        if (path is null || !File.Exists(path))
            throw new ActionException(string.IsNullOrWhiteSpace(name)
                ? Strings.Get("NoteStore_Error_YouDonTHave")
                : Strings.Format("NoteStore_Error_CouldnTFindA", name.Trim()));
        return path;
    }
}

internal static class NoteParams
{
    public static ParameterDescriptor Folder() =>
        ParameterDescriptor.FolderParam("folder", Strings.Get("NoteParams_Param_folder_Label")) with { Hint = Strings.Get("NoteParams_Param_folder_Hint") };
}

public sealed class AddToNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.add", Title = Strings.Get("AddToNoteAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "NoteAdd24",
        Keywords = new[] { "note", "jot", "markdown", "write", "journal", "quick note" },
        Summary = Strings.Get("AddToNoteAction_Summary"),
        Description = Strings.Get("AddToNoteAction_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.File, OutputName = Strings.Get("AddToNoteAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", Strings.Get("AddToNoteAction_Param_text_Label"), isInput: true, required: true),
            ParameterDescriptor.Text("name", Strings.Get("AddToNoteAction_Param_name_Label"), "Quick Notes", Strings.Get("AddToNoteAction_Param_name_Placeholder")),
            ParameterDescriptor.ChoiceOf("position", Strings.Get("AddToNoteAction_Param_position_Label"), "bottom", new Choice("bottom", Strings.Get("AddToNoteAction_Param_position_Choice_bottom")), new Choice("top", Strings.Get("AddToNoteAction_Param_position_Choice_top"))),
            ParameterDescriptor.Bool("timestamp", Strings.Get("AddToNoteAction_Param_timestamp_Label"), false),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException(Strings.Get("AddToNoteAction_Error_ThereIsNothingTo"));
        var path = NoteStore.Add(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name", "Quick Notes"), text,
            (await ctx.GetChoiceAsync("position", "bottom")) == "top", await ctx.GetBoolAsync("timestamp"), DateTime.Now);
        return ContentValue.File(path);
    }
}

public sealed class GetNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.get", Title = Strings.Get("GetNoteAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "Note24",
        Keywords = new[] { "read", "note", "open", "markdown" },
        Summary = Strings.Get("GetNoteAction_Summary"),
        Description = Strings.Get("GetNoteAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("GetNoteAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", Strings.Get("GetNoteAction_Param_name_Label"), null, Strings.Get("GetNoteAction_Param_name_Placeholder")),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var path = NoteStore.Resolve(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name"));
        try { return ContentValue.Text(await File.ReadAllTextAsync(path, ctx.CancellationToken)); }
        catch (IOException ex) { throw new ActionException(Strings.Format("GetNoteAction_Error_CouldnTReadThe", Path.GetFileName(path)), ex); }
    }
}

public sealed class OpenNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.open", Title = Strings.Get("OpenNoteAction_Title"), Category = ActionCategories.NotesAndReminders, Icon = "NoteEdit24",
        Keywords = new[] { "show", "edit", "note" },
        Summary = Strings.Get("OpenNoteAction_Summary"),
        Description = Strings.Get("OpenNoteAction_Description"),
        OutputKind = ContentKind.File, OutputName = Strings.Get("OpenNoteAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", Strings.Get("OpenNoteAction_Param_name_Label"), null, Strings.Get("OpenNoteAction_Param_name_Placeholder")),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        TestRootGuard.BlockSystemChange("opening notes");
        var path = NoteStore.Resolve(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name"));
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No program is set for .md: fall back to Notepad.
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex2) { throw new ActionException(Strings.Get("OpenNoteAction_Error_CouldnTOpenThe"), ex2); }
        }
        return ContentValue.File(path);
    }
}
