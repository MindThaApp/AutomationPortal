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
            throw new ActionException($"Couldn't save the note '{Path.GetFileName(path)}'. Check the notes folder exists and the note isn't open elsewhere.", ex);
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
                ? "You don't have any notes yet. Use Add to Note first."
                : $"Couldn't find a note called '{name.Trim()}'. Check the name, or use Add to Note to start it.");
        return path;
    }
}

internal static class NoteParams
{
    public static ParameterDescriptor Folder() =>
        ParameterDescriptor.FolderParam("folder", "Notes folder") with { Hint = "Leave empty to use Documents\\Automation Portal Notes." };
}

public sealed class AddToNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.add", Title = "Add to Note", Category = ActionCategories.NotesAndReminders, Icon = "NoteAdd24",
        Keywords = new[] { "note", "jot", "markdown", "write", "journal", "quick note" },
        Summary = "Add {text} to the note {name}",
        Description = "Adds text to a note kept as a Markdown file in Documents\\Automation Portal Notes. The note is created if it doesn't exist.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.File, OutputName = "Note",
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", "Text", isInput: true, required: true),
            ParameterDescriptor.Text("name", "Note name", "Quick Notes", "e.g. Ideas"),
            ParameterDescriptor.ChoiceOf("position", "Add it at the", "bottom", new Choice("bottom", "bottom"), new Choice("top", "top")),
            ParameterDescriptor.Bool("timestamp", "Add the date and time", false),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException("There is nothing to add to the note. Type some text first.");
        var path = NoteStore.Add(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name", "Quick Notes"), text,
            (await ctx.GetChoiceAsync("position", "bottom")) == "top", await ctx.GetBoolAsync("timestamp"), DateTime.Now);
        return ContentValue.File(path);
    }
}

public sealed class GetNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.get", Title = "Get Note", Category = ActionCategories.NotesAndReminders, Icon = "Note24",
        Keywords = new[] { "read", "note", "open", "markdown" },
        Summary = "Get the text of the note {name}",
        Description = "Reads a note from Documents\\Automation Portal Notes. Leave the name empty to get the note you changed last.",
        OutputKind = ContentKind.Text, OutputName = "Note Text",
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", "Note name", null, "empty = the latest note"),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var path = NoteStore.Resolve(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name"));
        try { return ContentValue.Text(await File.ReadAllTextAsync(path, ctx.CancellationToken)); }
        catch (IOException ex) { throw new ActionException($"Couldn't read the note '{Path.GetFileName(path)}'. Close any program that is using it and try again.", ex); }
    }
}

public sealed class OpenNoteAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "note.open", Title = "Open Note", Category = ActionCategories.NotesAndReminders, Icon = "NoteEdit24",
        Keywords = new[] { "show", "edit", "note" },
        Summary = "Open the note {name}",
        Description = "Opens a note in the program Windows uses for Markdown files. Leave the name empty to open the note you changed last.",
        OutputKind = ContentKind.File, OutputName = "Note",
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", "Note name", null, "empty = the latest note"),
            NoteParams.Folder(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var path = NoteStore.Resolve(await ctx.GetPathAsync("folder"), await ctx.GetTextAsync("name"));
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No program is set for .md: fall back to Notepad.
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex2) { throw new ActionException("Couldn't open the note. Choose a program for .md files in Windows Settings.", ex2); }
        }
        return ContentValue.File(path);
    }
}
