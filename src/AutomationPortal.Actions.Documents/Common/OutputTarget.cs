namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Where an action that creates new files puts them. "Next to the original" keeps results with their source
/// (the default for image, PDF and zip actions); the temporary folder is deleted when the shortcut ends.
/// </summary>
public sealed class OutputTarget
{
    private readonly string _mode;
    private readonly string? _folder;
    private readonly ActionContext _ctx;

    private OutputTarget(ActionContext ctx, string mode, string? folder) { _ctx = ctx; _mode = mode; _folder = folder; }

    public static IReadOnlyList<ParameterDescriptor> Parameters(string defaultMode = "next") => new[]
    {
        ParameterDescriptor.ChoiceOf("saveTo", "Save result", defaultMode,
            new Choice("next", "Next to the original"),
            new Choice("temp", "In a temporary folder"),
            new Choice("folder", "In a folder I choose")) with
        {
            Hint = "The temporary folder is cleared when the shortcut finishes, so pick another place if you want to keep the result.",
        },
        ParameterDescriptor.FolderParam("saveFolder", "Save in folder") with { VisibleWhen = new VisibleWhen("saveTo", "folder") },
    };

    public static async Task<OutputTarget> ReadAsync(ActionContext ctx)
    {
        var mode = (await ctx.GetChoiceAsync("saveTo", "next")).ToLowerInvariant();
        string? folder = null;
        if (mode == "folder")
        {
            folder = await ctx.GetPathAsync("saveFolder");
            if (string.IsNullOrWhiteSpace(folder))
                throw new ActionException("Choose a folder to save the results in.");
        }
        return new OutputTarget(ctx, mode, folder);
    }

    /// <summary>The folder results go into (created if needed).</summary>
    public string FolderFor(string? sourcePath)
    {
        var dir = _mode == "temp" || (_mode == "next" && string.IsNullOrEmpty(sourcePath)) ? _ctx.TempFolder
            : _mode == "folder" ? _folder!
            : Path.GetDirectoryName(sourcePath!.TrimEnd('\\', '/'))!;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A path that doesn't exist yet: "name.ext", then "name (2).ext" …</summary>
    public string NewPath(string? sourcePath, string fileName)
    {
        fileName = ActionContext.SanitizeFileName(fileName);
        if (_mode == "temp" || (_mode == "next" && string.IsNullOrEmpty(sourcePath)))
            return _ctx.NewTempPath(fileName);
        var dir = _mode == "folder" ? _folder! : Path.GetDirectoryName(sourcePath!)!;
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't use the folder '{dir}'. Check that it exists and you can save there.", ex);
        }
        var path = UniquePath(dir, fileName);
        // Recorded before the action writes it; if nothing ends up there, undo finds nothing to remove.
        _ctx.Journal.Created(path);
        return path;
    }

    public static string UniquePath(string dir, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var path = Path.Combine(dir, fileName);
        for (var n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(dir, $"{stem} ({n}){ext}");
        return path;
    }

    /// <summary>
    /// Preview: the path <see cref="NewPath"/> would pick, without creating the folder or recording anything. Null when the file
    /// would go to the run's temporary folder (not one of the user's folders).
    /// </summary>
    public string? PlanPath(FileChangePlan plan, string? sourcePath, string fileName)
    {
        fileName = ActionContext.SanitizeFileName(fileName);
        if (_mode == "temp" || (_mode == "next" && string.IsNullOrEmpty(sourcePath))) return null;
        var dir = _mode == "folder" ? _folder! : Path.GetDirectoryName(sourcePath!)!;
        return plan.UniquePath(dir, fileName);
    }
}
