using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace AutomationPortal.Actions.Documents;

public sealed class RenameFilesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.rename", Title = Strings.Get("RenameFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Rename24",
        Keywords = new[] { "name", "batch", "number", "date", "prefix", "suffix", "replace" },
        Summary = Strings.Get("RenameFilesAction_Summary"),
        Description = Strings.Get("RenameFilesAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("RenameFilesAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("RenameFilesAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Text("pattern", Strings.Get("RenameFilesAction_Param_pattern_Label"), "{name}", Strings.Get("RenameFilesAction_Param_pattern_Placeholder")) with
            {
                Hint = Strings.Get("RenameFilesAction_Param_pattern_Hint"),
            },
            ParameterDescriptor.Text("find", Strings.Get("RenameFilesAction_Param_find_Label"), null, Strings.Get("RenameFilesAction_Param_find_Placeholder")),
            ParameterDescriptor.Text("replaceWith", Strings.Get("RenameFilesAction_Param_replaceWith_Label"), null, Strings.Get("RenameFilesAction_Param_replaceWith_Placeholder")),
            ParameterDescriptor.Bool("matchCase", Strings.Get("RenameFilesAction_Param_matchCase_Label"), false),
            ParameterDescriptor.Text("prefix", Strings.Get("RenameFilesAction_Param_prefix_Label")),
            ParameterDescriptor.Text("suffix", Strings.Get("RenameFilesAction_Param_suffix_Label")),
            ParameterDescriptor.ChoiceOf("caseStyle", Strings.Get("RenameFilesAction_Param_caseStyle_Label"), "keep",
                new Choice("keep", Strings.Get("RenameFilesAction_Param_caseStyle_Choice_keep")), new Choice("lower", Strings.Get("RenameFilesAction_Param_caseStyle_Choice_lower")), new Choice("upper", Strings.Get("RenameFilesAction_Param_caseStyle_Choice_upper")),
                new Choice("title", Strings.Get("RenameFilesAction_Param_caseStyle_Choice_title")), new Choice("sentence", Strings.Get("RenameFilesAction_Param_caseStyle_Choice_sentence"))),
            ParameterDescriptor.Number("startAt", Strings.Get("RenameFilesAction_Param_startAt_Label"), 1, 0, null),
            ParameterDescriptor.Bool("previewOnly", Strings.Get("RenameFilesAction_Param_previewOnly_Label"), false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (plan, preview) = await BuildPlanAsync(ctx);
        if (preview)
            return ContentValue.Texts(plan.Select(s => $"{Path.GetFileName(s.Source)} → {Path.GetFileName(s.Target)}"));

        try { RenamePlanner.Execute(plan, ctx.CancellationToken); }
        catch (IOException ex) { throw new ActionException(Strings.Format("RenameFilesAction_Error_CouldnTRenameThe", ex.Message), ex); }
        catch (UnauthorizedAccessException ex) { throw new ActionException(Strings.Get("RenameFilesAction_Error_AutomationPortalIsnT"), ex); }
        foreach (var s in JournalOrder(plan))
        {
            ctx.Journal.Renamed(s.Source, s.Target);
            ctx.ReportProgress($"Renamed {Path.GetFileName(s.Source)} to {Path.GetFileName(s.Target)}");
        }
        return ContentValue.Files(plan.Select(s => s.Target));
    }

    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan changes)
    {
        var (plan, _) = await BuildPlanAsync(ctx);
        foreach (var s in plan)
            changes.Add(string.Equals(s.Source, s.Target, StringComparison.Ordinal)
                ? new PlannedChange(PlannedChangeKind.Skip, D.Title, s.Source, Note: Strings.Get("BulkRenameAction_Note_TheNameStaysThe"))
                : new PlannedChange(PlannedChangeKind.Rename, D.Title, s.Source, s.Target));
        return ContentValue.Files(plan.Select(s => s.Target));
    }

    /// <summary>Reads the parameters and works out every new name. Changes nothing.</summary>
    private static async Task<(IReadOnlyList<RenameStep> Plan, bool PreviewOnly)> BuildPlanAsync(ActionContext ctx)
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

        return (RenamePlanner.Plan(wanted), preview);
    }

    /// <summary>
    /// The renames in the order they could run one at a time: one that takes another's old name comes after that one. Undo goes
    /// newest first, so each name is free again when it is needed. Swaps have no such order and stay as planned.
    /// </summary>
    internal static IReadOnlyList<RenameStep> JournalOrder(IReadOnlyList<RenameStep> plan)
    {
        var changes = plan.Where(s => !string.Equals(s.Source, s.Target, StringComparison.Ordinal)).ToList();
        var bySource = new Dictionary<string, RenameStep>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in changes) bySource.TryAdd(s.Source, s);
        var done = new HashSet<RenameStep>(ReferenceEqualityComparer.Instance);
        var result = new List<RenameStep>();
        foreach (var start in changes)
        {
            var chain = new List<RenameStep>();
            for (RenameStep? s = start; s is not null && done.Add(s); s = bySource.GetValueOrDefault(s.Target)) chain.Add(s);
            chain.Reverse();
            result.AddRange(chain);
        }
        return result;
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
        ParameterDescriptor.FileParam("files", Strings.Get("TransferParams_Param_files_Label"), isInput: true) with { Required = true },
        ParameterDescriptor.FolderParam("folder", Strings.Get("TransferParams_Param_folder_Label")) with { Required = true },
        ParameterDescriptor.Text("subfolder", Strings.Get("TransferParams_Param_subfolder_Label"), null, Strings.Get("TransferParams_Param_subfolder_Placeholder")) with
        {
            Hint = Strings.Get("TransferParams_Param_subfolder_Hint"),
        },
        ParameterDescriptor.ChoiceOf("ifExists", Strings.Get("TransferParams_Param_ifExists_Label"), "keep",
            new Choice("keep", Strings.Get("TransferParams_Param_ifExists_Choice_keep")), new Choice("replace", Strings.Get("TransferParams_Param_ifExists_Choice_replace")), new Choice("skip", Strings.Get("TransferParams_Param_ifExists_Choice_skip"))),
        ParameterDescriptor.Bool("previewOnly", Strings.Get("TransferParams_Param_previewOnly_Label"), false),
    };

    public static async Task<ContentValue> RunAsync(ActionContext ctx, bool move)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var dest = await ctx.GetPathAsync("folder");
        if (string.IsNullOrWhiteSpace(dest)) throw new ActionException(Strings.Get("TransferParams_Error_ChooseTheFolderTo"));
        var sub = await ctx.GetTextAsync("subfolder");
        var mode = Transfer.ParseMode(await ctx.GetChoiceAsync("ifExists", "keep"));
        if (await ctx.GetBoolAsync("previewOnly"))
        {
            var planned = new List<string>();
            Transfer.Run(files.Select(f => f.Path), dest, sub, mode, move, ctx.CancellationToken, preview: planned);
            return ContentValue.Texts(planned);
        }
        TransferResult result;
        // With nobody watching, a replaced file on a drive without a Recycle Bin is kept as "name (replaced)" instead of the shell asking.
        try { result = Transfer.Run(files.Select(f => f.Path), dest, sub, mode, move, ctx.CancellationToken, ctx.Journal, recycle: ctx.Unattended ? p => RecycleBin.TrySendUnattended(p, ctx.CancellationToken) : null); }
        catch (IOException ex) { throw new ActionException(Strings.Format("TransferParams_Error_CouldnTUseThe", dest, ex.Message), ex); }
        if (result.Skipped > 0) ctx.ReportProgress(Strings.Format("TransferParams_Progress_SkippedThatAlreadyExisted", result.Skipped));
        foreach (var note in result.Notes)
        {
            ctx.Log(note);
            ctx.ReportProgress(note);
        }
        return ContentValue.Files(result.Paths);
    }

    /// <summary>Preview: where each file would go, with the same naming rules as a real run. Changes nothing.</summary>
    public static async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan, bool move)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var dest = await ctx.GetPathAsync("folder");
        if (string.IsNullOrWhiteSpace(dest)) throw new ActionException(Strings.Get("TransferParams_Error_ChooseTheFolderTo"));
        var sub = await ctx.GetTextAsync("subfolder");
        var mode = Transfer.ParseMode(await ctx.GetChoiceAsync("ifExists", "keep"));
        var result = Transfer.Run(files.Select(f => f.Path), dest, sub, mode, move, ctx.CancellationToken, plan: plan, step: ctx.Descriptor.Title);
        return ContentValue.Files(result.Paths);
    }
}

public sealed class MoveFilesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.move", Title = Strings.Get("MoveFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "FolderArrowRight24",
        Keywords = new[] { "organize", "sort", "tidy", "folder", "year", "month" },
        Summary = Strings.Get("MoveFilesAction_Summary"),
        Description = Strings.Get("MoveFilesAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("MoveFilesAction_OutputName"),
        DestructiveWhenChoice = ("ifExists", "replace"),
        Parameters = TransferParams.Common(),
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => TransferParams.RunAsync(ctx, move: true);
    public Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan) => TransferParams.PlanAsync(ctx, plan, move: true);
}

public sealed class CopyFilesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.copy", Title = Strings.Get("CopyFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Copy24",
        Keywords = new[] { "duplicate", "backup", "folder" },
        Summary = Strings.Get("CopyFilesAction_Summary"),
        Description = Strings.Get("CopyFilesAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("CopyFilesAction_OutputName"),
        DestructiveWhenChoice = ("ifExists", "replace"),
        Parameters = TransferParams.Common(),
    };
    public override ActionDescriptor Descriptor => D;
    public override Task<ContentValue> RunAsync(ActionContext ctx) => TransferParams.RunAsync(ctx, move: false);
    public Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan) => TransferParams.PlanAsync(ctx, plan, move: false);
}

public sealed class DeleteFilesAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.delete", Title = Strings.Get("DeleteFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Delete24",
        Keywords = new[] { "remove", "trash", "recycle", "bin" },
        Summary = Strings.Get("DeleteFilesAction_Summary"),
        Description = Strings.Get("DeleteFilesAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, HasOutput = false, OutputName = Strings.Get("DeleteFilesAction_OutputName"),
        IsDestructive = true,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("DeleteFilesAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Bool("permanently", Strings.Get("DeleteFilesAction_Param_permanently_Label"), false) with
            {
                Hint = Strings.Get("DeleteFilesAction_Param_permanently_Hint"),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var permanently = await ctx.GetBoolAsync("permanently");
        foreach (var f in files) ProtectedPaths.Ensure(f.Path, "delete");
        var handled = 0;
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (f is FolderItem)
                {
                    if (permanently) Directory.Delete(f.Path, true);
                    else RecycleBin.Send(f.Path, ctx.Unattended, ctx.CancellationToken);
                }
                else
                {
                    if (permanently) File.Delete(f.Path);
                    else RecycleBin.Send(f.Path, ctx.Unattended, ctx.CancellationToken);
                }
                if (permanently) ctx.Journal.Deleted(f.Path); else ctx.Journal.Recycled(f.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                throw new ActionException(Strings.Format("DeleteFilesAction_Error_CouldnTDeleteIt", f.FullName), ex);
            }
            ctx.ReportProgress($"Deleted {f.FullName}");
            ctx.ReportCount(++handled, files.Count);
        }
        return ContentValue.Empty;
    }

    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var permanently = await ctx.GetBoolAsync("permanently");
        foreach (var f in files) ProtectedPaths.Ensure(f.Path, "delete");
        foreach (var f in files)
            plan.Add(permanently ? new PlannedChange(PlannedChangeKind.Delete, D.Title, f.Path)
                : RecycleBin.DriveHasBin(f.Path) ? new PlannedChange(PlannedChangeKind.Recycle, D.Title, f.Path)
                : new PlannedChange(PlannedChangeKind.Delete, D.Title, f.Path, Note: Transfer.NoRecycleBinNote));
        return ContentValue.Empty;
    }
}

public sealed class CreateFolderAction : ShortcutAction, IPlansFileChanges
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.newFolder", Title = Strings.Get("CreateFolderAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "FolderAdd24",
        Keywords = new[] { "new", "make", "directory" },
        Summary = Strings.Get("CreateFolderAction_Summary"),
        Description = Strings.Get("CreateFolderAction_Description"),
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.Folder, OutputName = Strings.Get("CreateFolderAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", Strings.Get("CreateFolderAction_Param_name_Label"), null, Strings.Get("CreateFolderAction_Param_name_Placeholder")) with { Required = true },
            ParameterDescriptor.FolderParam("parent", Strings.Get("CreateFolderAction_Param_parent_Label")) with { Required = true, IsInput = true },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public async Task<ContentValue> PlanAsync(ActionContext ctx, FileChangePlan plan)
    {
        var path = await PathAsync(ctx);
        if (!plan.IsTaken(path)) plan.Add(new PlannedChange(PlannedChangeKind.NewFolder, D.Title, Target: path));
        return ContentValue.Folder(path);
    }

    private static async Task<string> PathAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("CreateFolderAction_Error_TypeANameFor"));
        var parent = await ctx.GetPathAsync("parent") ?? throw new ActionException(Strings.Get("CreateFolderAction_Error_ChooseTheFolderTo"));
        var segments = name.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(NameTokens.SafeName).Where(s => s.Length > 0 && s != "..");
        return Path.Combine(new[] { parent }.Concat(segments).ToArray());
    }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var path = await PathAsync(ctx);
        string? firstNew = Path.GetFullPath(path);
        if (Directory.Exists(firstNew) || File.Exists(firstNew)) firstNew = null;
        else
            while (Path.GetDirectoryName(firstNew) is { } up && !Directory.Exists(up)) firstNew = up;
        try
        {
            TestRootGuard.Check(path);
            Directory.CreateDirectory(path);
            if (firstNew is not null) ctx.Journal.Created(firstNew);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException(Strings.Format("CreateFolderAction_Error_CouldnTCreateThe", path), ex);
        }
        return ContentValue.Folder(path);
    }
}

public sealed class SaveFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.save", Title = Strings.Get("SaveFileAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Save24",
        Keywords = new[] { "write", "store", "export", "keep" },
        Summary = Strings.Get("SaveFileAction_Summary"),
        Description = Strings.Get("SaveFileAction_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.File, OutputName = Strings.Get("SaveFileAction_OutputName"),
        DestructiveWhenChoice = ("ifExists", "replace"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("content", Strings.Get("SaveFileAction_Param_content_Label"), isInput: true, required: true),
            ParameterDescriptor.FolderParam("folder", Strings.Get("SaveFileAction_Param_folder_Label")) with { Hint = Strings.Get("SaveFileAction_Param_folder_Hint") },
            ParameterDescriptor.Text("name", Strings.Get("SaveFileAction_Param_name_Label"), null, Strings.Get("SaveFileAction_Param_name_Placeholder")) with
            {
                Hint = Strings.Get("SaveFileAction_Param_name_Hint"),
            },
            ParameterDescriptor.ChoiceOf("ifExists", Strings.Get("SaveFileAction_Param_ifExists_Label"), "keep",
                new Choice("keep", Strings.Get("SaveFileAction_Param_ifExists_Choice_keep")), new Choice("replace", Strings.Get("SaveFileAction_Param_ifExists_Choice_replace"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("content");
        if (content.IsEmpty) throw new ActionException(Strings.Get("SaveFileAction_Error_ThereIsNothingTo"));
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
                var target = await ui.PickSaveFileAsync(Strings.Get("SaveFileAction_Prompt_SaveAs"), suggested, null, ctx.CancellationToken);
                CopyTo(files[0].Path, target, true, ctx.Journal);
                saved.Add(target);
                return ContentValue.Files(saved);
            }
            var chosen = await ui.PickFolderAsync(Strings.Get("SaveFileAction_Prompt_ChooseWhereToSave"), ctx.CancellationToken);
            folder = chosen.FirstItem is PathItem p ? p.Path : chosen.AsText();
        }
        TestRootGuard.Check(folder);
        Directory.CreateDirectory(folder);
        for (var i = 0; i < files.Count; i++)
        {
            var src = files[i];
            var fileName = nameWanted.Length == 0 ? src.FullName
                : WithExtension(files.Count == 1 ? nameWanted : $"{nameWanted} {i + 1}", src.Path);
            fileName = ActionContext.SanitizeFileName(fileName);
            var target = replace ? Path.Combine(folder, fileName) : OutputTarget.UniquePath(folder, fileName);
            CopyTo(src.Path, target, replace, ctx.Journal, ctx.Unattended, ctx.CancellationToken);
            saved.Add(target);
        }
        return ContentValue.Files(saved);
    }

    private static string WithExtension(string name, string sourcePath) =>
        Path.HasExtension(name) ? name : name + Path.GetExtension(sourcePath);

    /// <summary>Copies src to target. With <paramref name="replace"/> an existing file there goes to the Recycle Bin first; a folder is never replaced.</summary>
    internal static void CopyTo(string src, string target, bool replace, RunFileJournal? journal = null, bool unattended = false, CancellationToken ct = default)
    {
        try
        {
            TestRootGuard.Check(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(target))
                throw new ActionException(Strings.Format("SaveFileAction_Error_ThereIsAFolder", Path.GetFileName(target)));
            if (replace && File.Exists(target))
            {
                ProtectedPaths.Ensure(target, "replace");
                var backup = journal?.BackUp(target);
                Transfer.ReplaceWithCopy(src, target, unattended ? p => RecycleBin.TrySendUnattended(p, ct) : null);
                journal?.Replaced(target, backup);
            }
            else
            {
                File.Copy(src, target);
                journal?.Created(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException(Strings.Format("SaveFileAction_Error_CouldnTSaveCheck", Path.GetFileName(target)), ex);
        }
    }
}

public sealed class AppendToFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.append", Title = Strings.Get("AppendToFileAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "DocumentAdd24",
        Keywords = new[] { "add", "log", "write", "text" },
        Summary = Strings.Get("AppendToFileAction_Summary"),
        Description = Strings.Get("AppendToFileAction_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.File, OutputName = Strings.Get("AppendToFileAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", Strings.Get("AppendToFileAction_Param_text_Label"), isInput: true, required: true),
            ParameterDescriptor.Of("file", Strings.Get("AppendToFileAction_Param_file_Label"), ParamType.SaveFile) with { Required = true },
            ParameterDescriptor.Bool("newLine", Strings.Get("AppendToFileAction_Param_newLine_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        var path = await ctx.GetPathAsync("file") ?? throw new ActionException(Strings.Get("AppendToFileAction_Error_ChooseTheTextFile"));
        var newLine = await ctx.GetBoolAsync("newLine", true);
        try
        {
            TestRootGuard.Check(path);
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
            var existed = File.Exists(path);
            var known = ctx.Journal.Recorded(path);
            var backup = existed && !known ? ctx.Journal.BackUp(path) : null;
            File.AppendAllText(path, prefix + text + (newLine ? Environment.NewLine : ""), encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding);
            if (!known)
            {
                if (!existed) ctx.Journal.Created(path);
                else if (backup is not null) ctx.Journal.Replaced(path, backup);
                else ctx.Journal.Changed(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException(Strings.Format("AppendToFileAction_Error_CouldnTWriteTo", Path.GetFileName(path)), ex);
        }
        return ContentValue.File(path);
    }
}

public sealed class ReadTextFromDocumentAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.readText", Title = Strings.Get("ReadTextFromDocumentAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "DocumentText24",
        Keywords = new[] { "read", "open", "docx", "word", "pdf", "txt", "markdown", "csv", "json", "html" },
        Summary = Strings.Get("ReadTextFromDocumentAction_Summary"),
        Description = Strings.Get("ReadTextFromDocumentAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = Strings.Get("ReadTextFromDocumentAction_OutputName"),
        Parameters = new[] { ParameterDescriptor.FileParam("files", Strings.Get("ReadTextFromDocumentAction_Param_files_Label"), isInput: true) with { Required = true } },
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
        Id = "file.zip", Title = Strings.Get("MakeZipAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "FolderZip24",
        Keywords = new[] { "compress", "archive", "pack" },
        Summary = Strings.Get("MakeZipAction_Summary"),
        Description = Strings.Get("MakeZipAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("MakeZipAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("MakeZipAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Text("name", Strings.Get("MakeZipAction_Param_name_Label"), "Archive", Strings.Get("MakeZipAction_Param_name_Placeholder")),
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
            throw new ActionException(Strings.Format("MakeZipAction_Error_CouldnTMakeThe", ex.Message), ex);
        }
        return ContentValue.File(target.Record(zip));   // made with CreateNew, so this run owns it
    }
}

public sealed class ExtractZipAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.unzip", Title = Strings.Get("ExtractZipAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "FolderZip24",
        Keywords = new[] { "unzip", "decompress", "unpack", "archive" },
        Summary = Strings.Get("ExtractZipAction_Summary"),
        Description = Strings.Get("ExtractZipAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = Strings.Get("ExtractZipAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("ExtractZipAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.Bool("intoFolder", Strings.Get("ExtractZipAction_Param_intoFolder_Label"), true),
        }.Concat(OutputTarget.Parameters()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var zips = await FileParams.RequireFilesAsync(ctx, Strings.Get("ExtractZipAction_Noun_Files"), false, Strings.Get("ExtractZipAction_Noun_ZipFiles"));
        var intoFolder = await ctx.GetBoolAsync("intoFolder", true);
        var target = await OutputTarget.ReadAsync(ctx);
        var all = new List<string>();
        foreach (var z in zips)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var dest = intoFolder ? target.NewPath(z.Path, Path.GetFileNameWithoutExtension(z.FullName)) : target.FolderFor(z.Path);
            try
            {
                var newFolder = !Directory.Exists(dest);
                var extracted = await Task.Run(() => ZipTools.Extract(z.Path, dest, ctx.CancellationToken), ctx.CancellationToken);
                if (newFolder) { if (!ctx.Journal.Recorded(dest)) ctx.Journal.Created(dest); }
                else foreach (var f in extracted) ctx.Journal.Created(f);
                all.AddRange(extracted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ActionException(Strings.Format("ExtractZipAction_Error_CouldnTExtractCheck", z.FullName, ex.Message), ex);
            }
        }
        return ContentValue.Files(all);
    }
}

public sealed class GetSelectedFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.selected", Title = Strings.Get("GetSelectedFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "CursorClick24",
        Keywords = new[] { "explorer", "selection", "highlighted", "current" },
        Summary = Strings.Get("GetSelectedFilesAction_Summary"),
        Description = Strings.Get("GetSelectedFilesAction_Description"),
        OutputKind = ContentKind.File, OutputName = Strings.Get("GetSelectedFilesAction_OutputName"),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        IReadOnlyList<string> paths;
        try { paths = await ExplorerSelection.GetSelectedPathsAsync(ctx.CancellationToken); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            throw new ActionException(Strings.Get("GetSelectedFilesAction_Error_CouldnTReadFile"), ex);
        }
        if (paths.Count == 0)
            throw new ActionException(Strings.Get("GetSelectedFilesAction_Error_NoFilesAreSelected"));
        return ContentValue.Files(paths);
    }
}
