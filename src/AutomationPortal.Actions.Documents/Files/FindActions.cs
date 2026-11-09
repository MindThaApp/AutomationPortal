namespace AutomationPortal.Actions.Documents;

internal static class FileParams
{
    public static ParameterDescriptor[] Filters() => new[]
    {
        ParameterDescriptor.ChoiceOf("kind", "Kind", "any", FileKindFilter.Choices),
        ParameterDescriptor.Text("nameContains", "Name contains", null, "e.g. invoice  (use * as a wildcard: report*.pdf)"),
        ParameterDescriptor.Text("extensions", "File types", null, "e.g. jpg, png") with { Hint = "Only files ending in one of these. Separate with commas." },
        ParameterDescriptor.Number("minSizeMb", "Bigger than", null, 0, null, "MB"),
        ParameterDescriptor.Number("maxSizeMb", "Smaller than", null, 0, null, "MB"),
        ParameterDescriptor.ChoiceOf("dateField", "Date to check", "modified", new Choice("modified", "Date modified"), new Choice("created", "Date created")),
        ParameterDescriptor.Number("changedWithinDays", "Changed in the last", null, 0, null, "days"),
        ParameterDescriptor.Number("olderThanDays", "Older than", null, 0, null, "days"),
    };

    public static ParameterDescriptor[] SortAndLimit(string defaultSort = "name") => new[]
    {
        ParameterDescriptor.ChoiceOf("sortBy", "Sort by", defaultSort, FileQuery.SortChoices),
        ParameterDescriptor.ChoiceOf("order", "Order", "asc",
            new Choice("asc", "A to Z, smallest or oldest first"), new Choice("desc", "Z to A, biggest or newest first")),
        ParameterDescriptor.Number("limit", "Limit", 0, 0, null, "files") with { Hint = "Keep only this many files. 0 means no limit." },
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
        if (string.IsNullOrWhiteSpace(folder)) throw new ActionException("Choose a folder first.");
        if (!Directory.Exists(folder))
            throw new ActionException($"Couldn't find the folder '{folder}'. Check the folder still exists.");
        return folder;
    }

    public static async Task<IReadOnlyList<PathItem>> RequireFilesAsync(ActionContext ctx, string key, bool includeFolders, string what = "files")
    {
        var items = await ctx.GetFilesAsync(key, includeFolders);
        if (items.Count == 0) throw new ActionException($"There are no {what} to work with. Choose some {what} first.");
        foreach (var f in items)
            if (!f.Exists) throw new ActionException($"Couldn't find '{f.FullName}'. Check it still exists.");
        return items;
    }
}

public sealed class GetFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.get", Title = "Get File", Category = ActionCategories.FilesAndFolders, Icon = "Document24",
        Keywords = new[] { "choose", "pick", "open", "path" },
        Summary = "Get {file}",
        Description = "Picks a file or folder on your PC so the next actions can work with it.",
        InputKinds = Array.Empty<ContentKind>(), OutputKind = ContentKind.File, OutputName = "File",
        Parameters = new[] { ParameterDescriptor.FileParam("file", "File") with { Required = true } },
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
        Id = "file.getFolder", Title = "Get Folder", Category = ActionCategories.FilesAndFolders, Icon = "Folder24",
        Keywords = new[] { "desktop", "documents", "downloads", "pictures", "music", "videos", "home", "onedrive" },
        Summary = "Get the {folder} folder{chosen}",
        Description = "Gives you one of your usual folders (Desktop, Downloads, Pictures …) or any folder you choose.",
        OutputKind = ContentKind.Folder, OutputName = "Folder",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("folder", "Folder", "downloads",
                new Choice("desktop", "Desktop"), new Choice("documents", "Documents"), new Choice("downloads", "Downloads"),
                new Choice("pictures", "Pictures"), new Choice("music", "Music"), new Choice("videos", "Videos"),
                new Choice("home", "Home (your user folder)"), new Choice("onedrive", "OneDrive"), new Choice("chosen", "A folder I choose")),
            ParameterDescriptor.FolderParam("chosen", "Folder") with { VisibleWhen = new VisibleWhen("folder", "chosen") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var id = await ctx.GetChoiceAsync("folder", "downloads");
        string path;
        if (id.Equals("chosen", StringComparison.OrdinalIgnoreCase))
            path = await ctx.GetPathAsync("chosen") ?? throw new ActionException("Choose a folder first.");
        else
        {
            try { path = Folders.Resolve(id); }
            catch (ActionException) when (!Folders.IsKnownId(id))
            {
                path = Environment.ExpandEnvironmentVariables(id.Trim().Trim('"'));
            }
        }
        if (!Directory.Exists(path)) throw new ActionException($"Couldn't find the folder '{path}'. Check the folder still exists.");
        return ContentValue.Folder(path);
    }
}

public sealed class GetFolderContentsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.contents", Title = "Get Contents of Folder", Category = ActionCategories.FilesAndFolders, Icon = "FolderOpen24",
        Keywords = new[] { "list", "files", "folder", "browse" },
        Summary = "Get the {include} in {folder}",
        Description = "Lists the files (and optionally folders) inside a folder.",
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Folder Contents",
        Parameters = new[]
        {
            ParameterDescriptor.FolderParam("folder", "Folder") with { Required = true, IsInput = true },
            ParameterDescriptor.ChoiceOf("include", "Include", "files", new Choice("files", "files"), new Choice("folders", "folders"), new Choice("all", "files and folders")),
            ParameterDescriptor.Bool("subfolders", "Include subfolders", false),
            ParameterDescriptor.Bool("showHidden", "Include hidden files", false),
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
        Id = "file.find", Title = "Find Files", Category = ActionCategories.FilesAndFolders, Icon = "DocumentSearch24",
        Keywords = new[] { "search", "locate", "kind", "size", "date", "extension" },
        Summary = "Find {kind} in {folder} named {nameContains}",
        Description = "Searches a folder for files by name, type, size or date, and gives you the matches.",
        InputKinds = new[] { ContentKind.Folder }, OutputKind = ContentKind.File, OutputName = "Found Files",
        Parameters = new[]
        {
            ParameterDescriptor.FolderParam("folder", "Folder") with { Required = true, IsInput = true },
        }
        .Concat(FileParams.Filters())
        .Concat(new[]
        {
            ParameterDescriptor.Bool("subfolders", "Look inside subfolders", true),
            ParameterDescriptor.Bool("showHidden", "Include hidden files", false),
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
        Id = "file.filter", Title = "Filter Files", Category = ActionCategories.FilesAndFolders, Icon = "Filter24",
        Keywords = new[] { "keep", "only", "kind", "size", "date", "extension" },
        Summary = "Keep only {kind} from {files} named {nameContains}",
        Description = "Keeps just the files that match your rules (type, name, size or date) and drops the rest.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Filtered Files",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
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
        ("name", "Name", "Name"), ("fullName", "Name with extension", "Full Name"), ("extension", "Extension", "Extension"),
        ("size", "Size (bytes)", "Size"), ("sizeText", "Size (like 1.2 MB)", "Size Text"), ("created", "Date created", "Created"),
        ("modified", "Date modified", "Modified"), ("folder", "Folder it is in", "Parent Folder"), ("path", "Full path", "Path"),
        ("kind", "Kind", "Kind"),
    };

    private static readonly ActionDescriptor D = new()
    {
        Id = "file.details", Title = "Get Details of Files", Category = ActionCategories.FilesAndFolders, Icon = "Info24",
        Keywords = new[] { "name", "size", "date", "properties", "info" },
        Summary = "Get {detail} of {files}",
        Description = "Reads a detail such as the size, date or folder of each file. Choose \"Everything\" to get all details together.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder }, OutputKind = ContentKind.Any, OutputName = "File Details",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", "Detail", "name",
                Details.Select(d => new Choice(d.Id, d.Label)).Append(new Choice("all", "Everything")).ToArray()),
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
                   ?? throw new ActionException($"Don't know the detail '{detail}'. Pick one from the list.");
        return ContentValue.FromItems(files).Property(prop);
    }
}
