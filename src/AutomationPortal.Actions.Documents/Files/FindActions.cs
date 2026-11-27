namespace AutomationPortal.Actions.Documents;

internal static class FileParams
{
    public static ParameterDescriptor[] Filters() => new[]
    {
        ParameterDescriptor.ChoiceOf("kind", Strings.Get("FileParams_Param_kind_Label"), "any", FileKindFilter.Choices),
        ParameterDescriptor.Text("nameContains", Strings.Get("FileParams_Param_nameContains_Label"), null, Strings.Get("FileParams_Param_nameContains_Placeholder")),
        ParameterDescriptor.Text("extensions", Strings.Get("FileParams_Param_extensions_Label"), null, Strings.Get("FileParams_Param_extensions_Placeholder")) with { Hint = Strings.Get("FileParams_Param_extensions_Hint") },
        ParameterDescriptor.Number("minSizeMb", Strings.Get("FileParams_Param_minSizeMb_Label"), null, 0, null, "MB"),
        ParameterDescriptor.Number("maxSizeMb", Strings.Get("FileParams_Param_maxSizeMb_Label"), null, 0, null, "MB"),
        ParameterDescriptor.ChoiceOf("dateField", Strings.Get("FileParams_Param_dateField_Label"), "modified", new Choice("modified", Strings.Get("FileParams_Param_dateField_Choice_modified")), new Choice("created", Strings.Get("FileParams_Param_dateField_Choice_created"))),
        ParameterDescriptor.Number("changedWithinDays", Strings.Get("FileParams_Param_changedWithinDays_Label"), null, 0, null, "days"),
        ParameterDescriptor.Number("olderThanDays", Strings.Get("FileParams_Param_olderThanDays_Label"), null, 0, null, "days"),
        ParameterDescriptor.Bool("skipPartial", Strings.Get("FileParams_Param_skipPartial_Label"), false) with
        {
            Hint = Strings.Get("FileParams_Param_skipPartial_Hint"),
        },
    };

    public static ParameterDescriptor[] SortAndLimit(string defaultSort = "name") => new[]
    {
        ParameterDescriptor.ChoiceOf("sortBy", Strings.Get("FileParams_Param_sortBy_Label"), defaultSort, FileQuery.SortChoices),
        ParameterDescriptor.ChoiceOf("order", Strings.Get("FileParams_Param_order_Label"), "asc",
            new Choice("asc", Strings.Get("FileParams_Param_order_Choice_asc")), new Choice("desc", Strings.Get("FileParams_Param_order_Choice_desc"))),
        ParameterDescriptor.Number("limit", Strings.Get("FileParams_Param_limit_Label"), 0, 0, null, "files") with { Hint = Strings.Get("FileParams_Param_limit_Hint") },
    };

    public static async Task<FileQuery> ReadQueryAsync(ActionContext ctx)
    {
        double? Positive(double? v) => v is > 0 ? v : null;
        var min = Positive(await ctx.GetNumberOrNullAsync("minSizeMb"));
        var max = Positive(await ctx.GetNumberOrNullAsync("maxSizeMb"));
        return new FileQuery
        {
            Kind = await ctx.GetChoiceAsync("kind", "any"),
            NameContains = (await ctx.GetTextAsync("nameContains")).Trim(),
            Extensions = FileQuery.ParseExtensions(await ctx.GetTextAsync("extensions")),
            MinBytes = min is { } a ? (long)(a * 1024 * 1024) : null,
            MaxBytes = max is { } b ? (long)(b * 1024 * 1024) : null,
            DateField = await ctx.GetChoiceAsync("dateField", "modified"),
            ChangedWithinDays = Positive(await ctx.GetNumberOrNullAsync("changedWithinDays")),
            OlderThanDays = Positive(await ctx.GetNumberOrNullAsync("olderThanDays")),
            SkipPartialDownloads = await ctx.GetBoolAsync("skipPartial"),
        };
    }

    public static async Task<IReadOnlyList<string>> SortAndLimitAsync(ActionContext ctx, IEnumerable<string> paths)
    {
        var sorted = FileQuery.Sort(paths, await ctx.GetChoiceAsync("sortBy", "name"), (await ctx.GetChoiceAsync("order", "asc")) == "desc");
        var limit = (int)(await ctx.GetNumberAsync("limit", 0));
        if (limit > 0) sorted = sorted.Take(limit);
        return sorted.ToList();
    }

    public static async Task<string> RequireFolderAsync(ActionContext ctx, string key = "folder")
    {
        var folder = await ctx.GetPathAsync(key);
        if (string.IsNullOrWhiteSpace(folder)) throw new ActionException(Strings.Get("FileParams_Error_ChooseAFolderFirst"));
        if (!Directory.Exists(folder))
            throw new ActionException(Strings.Format("FileParams_Error_CouldnTFindThe", folder));
        return folder;
    }

    public static async Task<IReadOnlyList<PathItem>> RequireFilesAsync(ActionContext ctx, string key, bool includeFolders, string what = "files")
    {
        var items = await ctx.GetFilesAsync(key, includeFolders);
        if (items.Count == 0) throw new ActionException(Strings.Format("FileParams_Error_ThereAreNoTo", what));
        foreach (var f in items)
            if (!f.Exists) throw new ActionException(Strings.Format("AiInput_Error_CouldnTFindCheck", f.FullName));
        return items;
    }
}

public sealed class GetFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.get", Title = Strings.Get("GetFileAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Document24",
        Keywords = new[] { "choose", "pick", "open", "path" },
        Summary = Strings.Get("GetFileAction_Summary"),
        Description = Strings.Get("GetFileAction_Description"),
        InputKinds = Array.Empty<ContentKind>(), OutputKind = ContentKind.File, OutputName = Strings.Get("GetFileAction_OutputName"),
        Parameters = new[] { ParameterDescriptor.FileParam("file", Strings.Get("GetFileAction_Param_file_Label")) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = await FileParams.RequireFilesAsync(ctx, "file", true, "files");
        return ContentValue.FromItems(items);
    }
}

public sealed class GetFolderAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.getFolder", Title = Strings.Get("GetFolderAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Folder24",
        Keywords = new[] { "desktop", "documents", "downloads", "pictures", "music", "videos", "home", "onedrive" },
        Summary = Strings.Get("GetFolderAction_Summary"),
        Description = Strings.Get("GetFolderAction_Description"),
        OutputKind = ContentKind.Folder, OutputName = Strings.Get("GetFolderAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("folder", Strings.Get("GetFolderAction_Param_folder_Label"), "downloads",
                new Choice("desktop", Strings.Get("GetFolderAction_Param_folder_Choice_desktop")), new Choice("documents", Strings.Get("GetFolderAction_Param_folder_Choice_documents")), new Choice("downloads", Strings.Get("GetFolderAction_Param_folder_Choice_downloads")),
                new Choice("pictures", Strings.Get("GetFolderAction_Param_folder_Choice_pictures")), new Choice("music", Strings.Get("GetFolderAction_Param_folder_Choice_music")), new Choice("videos", Strings.Get("GetFolderAction_Param_folder_Choice_videos")),
                new Choice("home", Strings.Get("GetFolderAction_Param_folder_Choice_home")), new Choice("onedrive", Strings.Get("GetFolderAction_Param_folder_Choice_onedrive")), new Choice("chosen", Strings.Get("GetFolderAction_Param_folder_Choice_chosen"))),
            ParameterDescriptor.FolderParam("chosen", Strings.Get("GetFolderAction_Param_chosen_Label")) with { VisibleWhen = new VisibleWhen("folder", "chosen") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var id = await ctx.GetChoiceAsync("folder", "downloads");
        string path;
        if (id.Equals("chosen", StringComparison.OrdinalIgnoreCase))
            path = await ctx.GetPathAsync("chosen") ?? throw new ActionException(Strings.Get("FileParams_Error_ChooseAFolderFirst"));
        else
        {
            try { path = Folders.Resolve(id); }
            catch (ActionException) when (!Folders.IsKnownId(id))
            {
                path = Environment.ExpandEnvironmentVariables(id.Trim().Trim('"'));
            }
        }
        if (!Directory.Exists(path)) throw new ActionException(Strings.Format("FileParams_Error_CouldnTFindThe", path));
        return ContentValue.Folder(path);
    }
}

public sealed class GetFolderContentsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.contents", Title = Strings.Get("GetFolderContentsAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "FolderOpen24",
        Keywords = new[] { "list", "files", "folder", "browse" },
        Summary = Strings.Get("GetFolderContentsAction_Summary"),
        Description = Strings.Get("GetFolderContentsAction_Description"),
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("GetFolderContentsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FolderParam("folder", Strings.Get("GetFolderContentsAction_Param_folder_Label")) with { Required = true, IsInput = true },
            ParameterDescriptor.ChoiceOf("include", Strings.Get("GetFolderContentsAction_Param_include_Label"), "files", new Choice("files", Strings.Get("GetFolderContentsAction_Param_include_Choice_files")), new Choice("folders", Strings.Get("GetFolderContentsAction_Param_include_Choice_folders")), new Choice("all", Strings.Get("GetFolderContentsAction_Param_include_Choice_all"))),
            ParameterDescriptor.Bool("subfolders", Strings.Get("GetFolderContentsAction_Param_subfolders_Label"), false),
            ParameterDescriptor.Bool("showHidden", Strings.Get("GetFolderContentsAction_Param_showHidden_Label"), false),
        }.Concat(FileParams.SortAndLimit()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var folder = await FileParams.RequireFolderAsync(ctx);
        var include = await ctx.GetChoiceAsync("include", "files");
        var sub = await ctx.GetBoolAsync("subfolders");
        var hidden = await ctx.GetBoolAsync("showHidden");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = sub, IgnoreInaccessible = true,
            AttributesToSkip = hidden ? FileAttributes.None : FileAttributes.Hidden | FileAttributes.System,
        };
        var paths = new List<string>();
        if (include is "files" or "all") paths.AddRange(Directory.EnumerateFiles(folder, "*", options));
        if (include is "folders" or "all") paths.AddRange(Directory.EnumerateDirectories(folder, "*", options));
        return ContentValue.Files(await FileParams.SortAndLimitAsync(ctx, paths));
    }
}

public sealed class FindFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.find", Title = Strings.Get("FindFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "DocumentSearch24",
        Keywords = new[] { "search", "locate", "kind", "size", "date", "extension" },
        Summary = Strings.Get("FindFilesAction_Summary"),
        Description = Strings.Get("FindFilesAction_Description"),
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = Strings.Get("FindFilesAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FolderParam("folder", Strings.Get("FindFilesAction_Param_folder_Label")) with { Required = true, IsInput = true },
        }
        .Concat(FileParams.Filters())
        .Concat(new[]
        {
            ParameterDescriptor.Bool("subfolders", Strings.Get("FindFilesAction_Param_subfolders_Label"), true),
            ParameterDescriptor.Bool("showHidden", Strings.Get("FindFilesAction_Param_showHidden_Label"), false),
        })
        .Concat(FileParams.SortAndLimit()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var folder = await FileParams.RequireFolderAsync(ctx);
        var query = await FileParams.ReadQueryAsync(ctx);
        var sub = await ctx.GetBoolAsync("subfolders", true);
        var hidden = await ctx.GetBoolAsync("showHidden");
        var now = DateTime.Now;
        var found = await Task.Run(() => FileQuery.Enumerate(folder, sub, hidden)
            .TakeWhile(_ => !ctx.CancellationToken.IsCancellationRequested)
            .Where(p => query.Matches(p, now)).ToList(), ctx.CancellationToken);
        ctx.CancellationToken.ThrowIfCancellationRequested();
        return ContentValue.Files(await FileParams.SortAndLimitAsync(ctx, found));
    }
}

public sealed class FilterFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.filter", Title = Strings.Get("FilterFilesAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Filter24",
        Keywords = new[] { "keep", "only", "kind", "size", "date", "extension" },
        Summary = Strings.Get("FilterFilesAction_Summary"),
        Description = Strings.Get("FilterFilesAction_Description"),
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = Strings.Get("FilterFilesAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("FilterFilesAction_Param_files_Label"), isInput: true) with { Required = true },
        }
        .Concat(FileParams.Filters())
        .Concat(FileParams.SortAndLimit()).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await ctx.GetFilesAsync("files");
        var query = await FileParams.ReadQueryAsync(ctx);
        var now = DateTime.Now;
        var kept = files.Where(f => f.Exists && query.Matches(f.Path, now)).Select(f => f.Path).ToList();
        return ContentValue.Files(await FileParams.SortAndLimitAsync(ctx, kept));
    }
}

public sealed class GetFileDetailsAction : ShortcutAction
{
    private static readonly (string Id, string Label, string Property)[] Details =
    {
        ("name", Strings.Get("GetFileDetailsAction_Label_Name"), "Name"), ("fullName", Strings.Get("GetFileDetailsAction_Label_NameWithExtension"), "Full Name"), ("extension", Strings.Get("GetFileDetailsAction_Label_Extension"), "Extension"),
        ("size", Strings.Get("GetFileDetailsAction_Label_SizeBytes"), "Size"), ("sizeText", Strings.Get("GetFileDetailsAction_Label_SizeLike12"), "Size Text"), ("created", Strings.Get("GetFileDetailsAction_Label_DateCreated"), "Created"),
        ("modified", Strings.Get("GetFileDetailsAction_Label_DateModified"), "Modified"), ("folder", Strings.Get("GetFileDetailsAction_Label_FolderItIsIn"), "Parent Folder"), ("path", Strings.Get("GetFileDetailsAction_Label_FullPath"), "Path"),
        ("kind", Strings.Get("GetFileDetailsAction_Label_Kind"), "Kind"),
    };

    private static readonly ActionDescriptor D = new()
    {
        Id = "file.details", Title = Strings.Get("GetFileDetailsAction_Title"), Category = ActionCategories.FilesAndFolders, Icon = "Info24",
        Keywords = new[] { "name", "size", "date", "properties", "info" },
        Summary = Strings.Get("GetFileDetailsAction_Summary"),
        Description = Strings.Get("GetFileDetailsAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.Any, OutputName = Strings.Get("GetFileDetailsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("GetFileDetailsAction_Param_files_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", Strings.Get("GetFileDetailsAction_Param_detail_Label"), "name",
                Details.Select(d => new Choice(d.Id, d.Label)).Append(new Choice("all", Strings.Get("GetFileDetailsAction_Param_detail_Choice_all"))).ToArray()),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", true);
        var detail = await ctx.GetChoiceAsync("detail", "name");
        if (detail == "all")
        {
            return ContentValue.FromItems(files.Select(f => (ContentItem)new DictionaryItem(
                Details.Select(d => new KeyValuePair<string, ContentValue>(d.Label, f.GetProperty(d.Property) ?? ContentValue.Empty)).ToList())));
        }
        var prop = Details.FirstOrDefault(d => d.Id.Equals(detail, StringComparison.OrdinalIgnoreCase)).Property
                   ?? throw new ActionException(Strings.Format("GetFileDetailsAction_Error_DonTKnowThe", detail));
        return ContentValue.FromItems(files).Property(prop);
    }
}
