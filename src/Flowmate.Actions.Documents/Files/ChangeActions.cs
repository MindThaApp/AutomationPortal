using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Flowmate.Actions.Documents;

public sealed class RenameFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.rename", Title = "Rename Files", Category = ActionCategories.FilesAndFolders, Icon = "Rename24",
        Keywords = new[] { "name", "batch", "number", "date", "prefix", "suffix", "replace" },
        Summary = "Rename {files} to {pattern}",
        Description = "Gives files new names. Build the name from {name}, {ext}, {n} (a counter), {date:yyyy-MM-dd}, {created}, {modified} and {taken} (the date a photo was taken). Existing files are never overwritten.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Renamed Files",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("pattern", "New name", "{name}", "e.g. Holiday {n:000}  or  {taken:yyyy-MM-dd} {name}") with
            {
                Hint = "The file type (like .jpg) is kept unless you write {ext} yourself. {n:000} counts 001, 002 …",
            },
            ParameterDescriptor.Text("find", "Replace this text", null, "text to find in the name"),
            ParameterDescriptor.Text("replaceWith", "With", null, "leave empty to remove it"),
            ParameterDescriptor.Bool("matchCase", "Match capital letters when replacing", false),
            ParameterDescriptor.Text("prefix", "Add before the name"),
            ParameterDescriptor.Text("suffix", "Add after the name (before the file type)"),
            ParameterDescriptor.ChoiceOf("caseStyle", "Capital letters", "keep",
                new Choice("keep", "Leave as they are"), new Choice("lower", "all lowercase"), new Choice("upper", "ALL UPPERCASE"),
                new Choice("title", "Every Word Capitalised"), new Choice("sentence", "First letter only")),
            ParameterDescriptor.Number("startAt", "Start counting at", 1, 0, null),
            ParameterDescriptor.Bool("previewOnly", "Only show the new names (don't rename)", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var pattern = await ctx.GetTextAsync("pattern", "{name}");
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "{name}";
        var find = await ctx.GetTextAsync("find");
        var replaceWith = await ctx.GetTextAsync("replaceWith");
        var matchCase = await ctx.GetBoolAsync("matchCase");
        var prefix = await ctx.GetTextAsync("prefix");
        var suffix = await ctx.GetTextAsync("suffix");
        var caseStyle = await ctx.GetChoiceAsync("caseStyle", "keep");
        var start = (int)await ctx.GetNumberAsync("startAt", 1);
        var preview = await ctx.GetBoolAsync("previewOnly");

        var needsTaken = new[] { pattern, prefix, suffix }.Any(t => NameTokens.Uses(t, "taken"));
        var taken = needsTaken
            ? await StaWorker.Imaging.RunAsync(() => files.ToDictionary(f => f.Path, f => ImageMetadataReader.TryReadDateTaken(f.Path), StringComparer.OrdinalIgnoreCase), ctx.CancellationToken)
            : new Dictionary<string, DateTime?>();
        var now = DateTime.Now;

        var wanted = new List<(string, string)>();
        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var isFolder = f is FolderItem;
            var c = new RenameContext(f.Path, start + i, now, p => taken.TryGetValue(p, out var d) ? d : null);
            wanted.Add((f.Path, BuildName(f.Path, isFolder, pattern, find, replaceWith, matchCase, prefix, suffix, caseStyle, c)));
        }

        var plan = RenamePlanner.Plan(wanted);
        if (preview)
            return ContentValue.Texts(plan.Select(s => $"{Path.GetFileName(s.Source)} → {Path.GetFileName(s.Target)}"));

        try { RenamePlanner.Execute(plan, ctx.CancellationToken); }
        catch (IOException ex) { throw new ActionException($"Couldn't rename the files. One may be open in another program. ({ex.Message})", ex); }
        catch (UnauthorizedAccessException ex) { throw new ActionException("Flow Mate isn't allowed to rename these files.", ex); }
        foreach (var s in plan.Where(s => s.Source != s.Target)) ctx.ReportProgress($"Renamed {Path.GetFileName(s.Source)} to {Path.GetFileName(s.Target)}");
        return ContentValue.Files(plan.Select(s => s.Target));
    }

    /// <summary>The wanted new file name (with extension) for one file.</summary>
    public static string BuildName(string path, bool isFolder, string pattern, string find, string replaceWith, bool matchCase,
        string prefix, string suffix, string caseStyle, RenameContext c)
    {
        var expanded = NameTokens.Expand(pattern, c);
        string stem, ext;
        if (NameTokens.Uses(pattern, "ext") && !isFolder)
        {
            stem = Path.GetFileNameWithoutExtension(expanded);
            ext = Path.GetExtension(expanded);
        }
        else
        {
            stem = expanded;
            ext = isFolder ? "" : Path.GetExtension(path);
        }
        if (find.Length > 0)
            stem = stem.Replace(find, replaceWith, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        var name = NameTokens.SafeName(ApplyCase(NameTokens.Expand(prefix, c) + stem + NameTokens.Expand(suffix, c), caseStyle));
        if (name.Length == 0) name = isFolder ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path);
        return name + ext;
    }

    public static string ApplyCase(string s, string style) => style switch
    {
        "lower" => s.ToLower(CultureInfo.CurrentCulture),
        "upper" => s.ToUpper(CultureInfo.CurrentCulture),
        "title" => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower(CultureInfo.CurrentCulture)),
        "sentence" => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..].ToLower(CultureInfo.CurrentCulture),
        _ => s,
    };
}

internal static class TransferParams
{
    public static ParameterDescriptor[] Common() => new[]
    {
        ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
        ParameterDescriptor.FolderParam("folder", "Destination folder") with { Required = true },
        ParameterDescriptor.Text("subfolder", "Sort into subfolders", null, "e.g. {kind}  or  {year}/{month}") with
        {
            Hint = "Optional. Use {kind} (Pictures, Documents …), {year}, {month}, {monthName}, {day} or {ext}. Folders are created for you.",
        },
        ParameterDescriptor.ChoiceOf("ifExists", "If a file with that name is already there", "keep",
            new Choice("keep", "Keep both"), new Choice("replace", "Replace it"), new Choice("skip", "Skip it")),
    };

    public static async Task<ContentValue> RunAsync(ActionContext ctx, bool move)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var dest = await ctx.GetPathAsync("folder");
        if (string.IsNullOrWhiteSpace(dest)) throw new ActionException("Choose the folder to put the files in.");
        var sub = await ctx.GetTextAsync("subfolder");
        var mode = Transfer.ParseMode(await ctx.GetChoiceAsync("ifExists", "keep"));
        TransferResult result;
        try { result = Transfer.Run(files.Select(f => f.Path), dest, sub, mode, move, ctx.CancellationToken); }
        catch (IOException ex) { throw new ActionException($"Couldn't use the folder '{dest}'. Check that it exists and you can save there. ({ex.Message})", ex); }
        if (result.Skipped > 0) ctx.ReportProgress($"Skipped {result.Skipped} that already existed");
        return ContentValue.Files(result.Paths);
    }
}

public sealed class MoveFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.move", Title = "Move Files", Category = ActionCategories.FilesAndFolders, Icon = "FolderArrowRight24",
        Keywords = new[] { "organize", "sort", "tidy", "folder", "year", "month" },
        Summary = "Move {files} to {folder}",
        Description = "Moves files into a folder, optionally sorting them into subfolders by kind, year or month. You choose what happens when a file with the same name is already there.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Moved Files",
        DestructiveWhenChoice = ("ifExists", "replace"),
        Parameters = TransferParams.Common(),
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => TransferParams.RunAsync(ctx, move: true);
}

public sealed class CopyFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.copy", Title = "Copy Files", Category = ActionCategories.FilesAndFolders, Icon = "Copy24",
        Keywords = new[] { "duplicate", "backup", "folder" },
        Summary = "Copy {files} to {folder}",
        Description = "Copies files into a folder, optionally sorting them into subfolders by kind, year or month. The originals stay where they are.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Copied Files",
        DestructiveWhenChoice = ("ifExists", "replace"),
        Parameters = TransferParams.Common(),
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => TransferParams.RunAsync(ctx, move: false);
}

public sealed class DeleteFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.delete", Title = "Delete Files", Category = ActionCategories.FilesAndFolders, Icon = "Delete24",
        Keywords = new[] { "remove", "trash", "recycle", "bin" },
        Summary = "Delete {files}",
        Description = "Sends files or folders to the Recycle Bin, where you can still get them back. Turn on \"Delete permanently\" to skip the Recycle Bin.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, HasOutput = false, OutputName = "Deleted Files",
        IsDestructive = true,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.Bool("permanently", "Delete permanently (skip the Recycle Bin)", false) with
            {
                Hint = "Permanently deleted files can't be restored.",
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var permanently = await ctx.GetBoolAsync("permanently");
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (f is FolderItem)
                {
                    if (permanently) Directory.Delete(f.Path, true);
                    else RecycleBin.Send(f.Path);
                }
                else
                {
                    if (permanently) File.Delete(f.Path);
                    else RecycleBin.Send(f.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                throw new ActionException($"Couldn't delete '{f.FullName}'. It may be open in another program, or you may not have permission.", ex);
            }
            ctx.ReportProgress($"Deleted {f.FullName}");
        }
        return ContentValue.Empty;
    }
}

public sealed class CreateFolderAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.newFolder", Title = "Create Folder", Category = ActionCategories.FilesAndFolders, Icon = "FolderAdd24",
        Keywords = new[] { "new", "make", "directory" },
        Summary = "Create folder {name} in {parent}",
        Description = "Makes a new folder. If it already exists, it is simply used again.",
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.Folder, OutputName = "New Folder",
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", "Name", null, "e.g. Invoices 2026") with { Required = true },
            ParameterDescriptor.FolderParam("parent", "In folder") with { Required = true, IsInput = true },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException("Type a name for the new folder.");
        var parent = await ctx.GetPathAsync("parent") ?? throw new ActionException("Choose the folder to create it in.");
        var segments = name.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(NameTokens.SafeName).Where(s => s.Length > 0 && s != "..");
        var path = Path.Combine(new[] { parent }.Concat(segments).ToArray());
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't create the folder '{path}'. Check the location exists and you can save there.", ex);
        }
        return ContentValue.Folder(path);
    }
}

public sealed class SaveFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.save", Title = "Save File", Category = ActionCategories.FilesAndFolders, Icon = "Save24",
        Keywords = new[] { "write", "store", "export", "keep" },
        Summary = "Save {content} in {folder}",
        Description = "Saves files, text or pictures to a folder. Text is saved as a .txt file. Leave the folder empty to be asked where to save each time.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.File, OutputName = "Saved File",
        Parameters = new[]
        {
            ParameterDescriptor.Content("content", "What to save", isInput: true, required: true),
            ParameterDescriptor.FolderParam("folder", "Save in folder") with { Hint = "Leave empty to be asked where to save." },
            ParameterDescriptor.Text("name", "File name", null, "leave empty to keep the original name") with
            {
                Hint = "If you leave out the file type (like .txt), the original one is used.",
            },
            ParameterDescriptor.ChoiceOf("ifExists", "If a file with that name is already there", "keep",
                new Choice("keep", "Keep both"), new Choice("replace", "Replace it")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("content");
        if (content.IsEmpty) throw new ActionException("There is nothing to save. Add something to save first.");
        var files = await content.AsFilesAsync(ctx, includeFolders: false);
        var nameWanted = (await ctx.GetTextAsync("name")).Trim();
        var replace = (await ctx.GetChoiceAsync("ifExists", "keep")) == "replace";
        var folder = await ctx.GetPathAsync("folder");

        var saved = new List<string>();
        if (string.IsNullOrWhiteSpace(folder))
        {
            var ui = ctx.Services.UserInteraction;
            if (files.Count == 1)
            {
                var suggested = nameWanted.Length > 0 ? WithExtension(nameWanted, files[0].Path) : files[0].FullName;
                var target = await ui.PickSaveFileAsync("Save as", suggested, null, ctx.CancellationToken);
                CopyTo(files[0].Path, target, true);
                saved.Add(target);
                return ContentValue.Files(saved);
            }
            var chosen = await ui.PickFolderAsync("Choose where to save the files", ctx.CancellationToken);
            folder = chosen.FirstItem is PathItem p ? p.Path : chosen.AsText();
        }
        Directory.CreateDirectory(folder);
        for (var i = 0; i < files.Count; i++)
        {
            var src = files[i];
            var fileName = nameWanted.Length == 0 ? src.FullName
                : WithExtension(files.Count == 1 ? nameWanted : $"{nameWanted} {i + 1}", src.Path);
            fileName = ActionContext.SanitizeFileName(fileName);
            var target = replace ? Path.Combine(folder, fileName) : OutputTarget.UniquePath(folder, fileName);
            CopyTo(src.Path, target, replace);
            saved.Add(target);
        }
        return ContentValue.Files(saved);
    }

    private static string WithExtension(string name, string sourcePath) =>
        Path.HasExtension(name) ? name : name + Path.GetExtension(sourcePath);

    private static void CopyTo(string src, string target, bool overwrite)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(src, target, overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't save '{Path.GetFileName(target)}'. Check the folder exists and the file isn't open elsewhere.", ex);
        }
    }
}

public sealed class AppendToFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.append", Title = "Append to Text File", Category = ActionCategories.FilesAndFolders, Icon = "DocumentAdd24",
        Keywords = new[] { "add", "log", "write", "text" },
        Summary = "Add {text} to the end of {file}",
        Description = "Adds text to the end of a text file. The file is created if it doesn't exist yet.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.File, OutputName = "Text File",
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", "Text", isInput: true, required: true),
            ParameterDescriptor.Of("file", "File", ParamType.SaveFile) with { Required = true },
            ParameterDescriptor.Bool("newLine", "Put it on its own line", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        var path = await ctx.GetPathAsync("file") ?? throw new ActionException("Choose the text file to add to.");
        var newLine = await ctx.GetBoolAsync("newLine", true);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var encoding = Encoding.UTF8;
            var prefix = "";
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                using var fs = File.OpenRead(path);
                var head = new byte[2];
                var read = fs.Read(head, 0, 2);
                if (read == 2 && head[0] == 0xFF && head[1] == 0xFE) encoding = Encoding.Unicode;
                else if (read == 2 && head[0] == 0xFE && head[1] == 0xFF) encoding = Encoding.BigEndianUnicode;
                if (newLine)
                {
                    var tail = new byte[Math.Min(4, fs.Length)];
                    fs.Seek(-tail.Length, SeekOrigin.End);
                    fs.ReadExactly(tail);
                    var endsWithNewline = encoding == Encoding.UTF8 ? tail[^1] == (byte)'\n'
                        : encoding == Encoding.Unicode ? tail.Length >= 2 && tail[^2] == (byte)'\n' && tail[^1] == 0
                        : tail.Length >= 2 && tail[^1] == (byte)'\n' && tail[^2] == 0;
                    if (!endsWithNewline) prefix = Environment.NewLine;
                }
            }
            File.AppendAllText(path, prefix + text + (newLine ? Environment.NewLine : ""), encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't write to '{Path.GetFileName(path)}'. Check the folder exists and the file isn't open elsewhere.", ex);
        }
        return ContentValue.File(path);
    }
}

public sealed class ReadTextFromDocumentAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.readText", Title = "Get Text from Document", Category = ActionCategories.FilesAndFolders, Icon = "DocumentText24",
        Keywords = new[] { "read", "open", "docx", "word", "pdf", "txt", "markdown", "csv", "json", "html" },
        Summary = "Get the text from {files}",
        Description = "Reads the words out of text files, Markdown, CSV, JSON, web pages (.html), Word documents (.docx) and PDFs.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "Document Text",
        Parameters = new[] { ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", false);
        var texts = new List<string>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            texts.Add(await Task.Run(() => DocumentText.Read(f.Path), ctx.CancellationToken));
        }
        return ContentValue.Texts(texts);
    }
}

public sealed class MakeZipAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.zip", Title = "Make Zip", Category = ActionCategories.FilesAndFolders, Icon = "FolderZip24",
        Keywords = new[] { "compress", "archive", "pack" },
        Summary = "Zip {files} as {name}",
        Description = "Packs files and folders into one .zip file.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Zip File",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("name", "Zip name", "Archive", "e.g. Photos"),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) name = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0].FullName) : "Archive";
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var target = await OutputTarget.ReadAsync(ctx);
        var zip = target.NewPath(files[0].Path, name + ".zip");
        try { await Task.Run(() => ZipTools.Create(files.Select(f => f.Path).ToList(), zip, ctx.CancellationToken), ctx.CancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't make the zip file. Check the location and that the files aren't open elsewhere. ({ex.Message})", ex);
        }
        return ContentValue.File(zip);
    }
}

public sealed class ExtractZipAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.unzip", Title = "Extract Zip", Category = ActionCategories.FilesAndFolders, Icon = "FolderZip24",
        Keywords = new[] { "unzip", "decompress", "unpack", "archive" },
        Summary = "Extract {files}",
        Description = "Unpacks .zip files. Nothing is overwritten, and files are never written outside the destination.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Extracted Files",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Zip files", isInput: true) with { Required = true },
            ParameterDescriptor.Bool("intoFolder", "Put everything in a new folder named after the zip", true),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var zips = await FileParams.RequireFilesAsync(ctx, "files", false, "zip files");
        var intoFolder = await ctx.GetBoolAsync("intoFolder", true);
        var target = await OutputTarget.ReadAsync(ctx);
        var all = new List<string>();
        foreach (var z in zips)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var dest = intoFolder ? target.NewPath(z.Path, Path.GetFileNameWithoutExtension(z.FullName)) : target.FolderFor(z.Path);
            try { all.AddRange(await Task.Run(() => ZipTools.Extract(z.Path, dest, ctx.CancellationToken), ctx.CancellationToken)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ActionException($"Couldn't extract '{z.FullName}'. Check the location and that the file isn't open elsewhere. ({ex.Message})", ex);
            }
        }
        return ContentValue.Files(all);
    }
}

public sealed class GetSelectedFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.selected", Title = "Get Selected Files in File Explorer", Category = ActionCategories.FilesAndFolders, Icon = "CursorClick24",
        Keywords = new[] { "explorer", "selection", "highlighted", "current" },
        Summary = "Get the files selected in File Explorer",
        Description = "Gives you the files and folders currently selected in the File Explorer window you were using (or on the desktop). Run it with a keyboard shortcut so Explorer stays in front.",
        OutputKind = ContentKind.File, OutputName = "Selected Files",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        IReadOnlyList<string> paths;
        try { paths = await ExplorerSelection.GetSelectedPathsAsync(ctx.CancellationToken); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            throw new ActionException("Couldn't read File Explorer's selection. Try again with Explorer in front.", ex);
        }
        if (paths.Count == 0)
            throw new ActionException("No files are selected in File Explorer. Select some files, then run the shortcut again (a keyboard shortcut works best).");
        return ContentValue.Files(paths);
    }
}
