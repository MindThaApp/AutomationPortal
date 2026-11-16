using System.Globalization;

namespace AutomationPortal.Core;

/// <summary>
/// Passed to <see cref="ShortcutAction.RunAsync"/>. Every <c>Get…Async(key)</c> resolves the parameter the user
/// configured: inline variables, magic variables, Ask Each Time (prompts via <see cref="IUserInteraction"/>),
/// the descriptor default, and implicit input for <c>IsInput</c> parameters — then converts it forgivingly.
/// </summary>
public sealed class ActionContext
{
    private readonly RunScope _scope;

    public ActionContext(RunScope scope, ActionInstance step, ActionDescriptor descriptor, ContentValue input)
    {
        _scope = scope;
        Step = step;
        Descriptor = descriptor;
        Input = input;
    }

    public ActionInstance Step { get; }
    public ActionDescriptor Descriptor { get; }
    public RunScope Scope => _scope;

    /// <summary>Output of the previous action (empty for the first one: then it is the shortcut input).</summary>
    public ContentValue Input { get; }
    public ContentValue ShortcutInput => _scope.ShortcutInput;
    public string ShortcutName => _scope.ShortcutName;
    public Guid ShortcutId => _scope.ShortcutId;
    public string RunId => _scope.RunId;
    public RunSource Source => _scope.Options.Source;

    /// <summary>The plan being filled when the shortcut runs in preview ("Show what it would do"); null in a real run.</summary>
    public FileChangePlan? Preview => _scope.Options.Preview;

    /// <summary>Nobody is watching: the run was started by a schedule, a trigger or a reminder.</summary>
    public bool Unattended => Source is RunSource.Automation or RunSource.Reminder;
    /// <summary>0 for a top-level run, +1 for every Run Shortcut level.</summary>
    public int Depth => _scope.Options.Depth;
    public CancellationToken CancellationToken => _scope.Token;
    public AutomationPortalServices Services => _scope.Services;
    /// <summary>Per-run temp folder, created on first use and deleted when the run ends. Put generated files here.</summary>
    public string TempFolder => _scope.TempFolder;
    /// <summary>Record every file this step creates, moves, renames, replaces or deletes here, so the run can be undone.</summary>
    public RunFileJournal Journal => _scope.Journal;

    public void Log(string message) => _scope.Observer?.OnLog(message);
    public void ReportProgress(string message) => _scope.Observer?.OnProgress(Step.Uid, message);

    // ---- variables ---------------------------------------------------------------------------

    public ContentValue GetVariable(string name) => _scope.GetVariable(name);
    public void SetVariable(string name, ContentValue value) => _scope.SetVariable(name, value);
    public Task<ContentValue> ResolveAsync(VarRef reference) => _scope.ResolveAsync(reference);
    /// <summary>True when <paramref name="reference"/> is the output of a step whose output is private (a generated password, the clipboard, typed input).</summary>
    public bool IsSensitive(VarRef reference) => reference.Kind == VarKind.Output && reference.Uid is not null && _scope.SensitiveOutputs.ContainsKey(reference.Uid);

    // ---- raw parameter access ----------------------------------------------------------------

    /// <summary>True when the user (or an import) stored a value for this parameter.</summary>
    public bool HasParameter(string key) => Step.Parameters.ContainsKey(key);
    public ParamValue? GetParameter(string key) => Step.Parameters.TryGetValue(key, out var v) ? v : null;
    public ParameterDescriptor? FindParameter(string key) => Descriptor.FindParameter(key);

    /// <summary>The text of a Choice/Text parameter without variable resolution or prompts (for VisibleWhen-style decisions).</summary>
    public string? PeekLiteral(string key) =>
        (GetParameter(key) ?? FindParameter(key)?.Default)?.ToLiteralText();

    // ---- typed getters -----------------------------------------------------------------------

    /// <summary>The parameter as content (no conversion). Missing and no default → implicit input if <c>IsInput</c>, else empty.</summary>
    public async Task<ContentValue> GetContentAsync(string key)
    {
        var pd = FindParameter(key);
        var pv = GetParameter(key);
        if (pv is null || (pv is TextValue { IsBlank: true } && pd?.IsInput == true))
        {
            if (pd?.IsInput == true && !Input.IsEmpty) return Input;
            pv = pd?.Default;
        }
        if (pv is null) return ContentValue.Empty;
        return await ResolveAsync(pv, pd).ConfigureAwait(false);
    }

    public async Task<string> GetTextAsync(string key, string fallback = "")
    {
        var c = await GetContentAsync(key).ConfigureAwait(false);
        return c.IsEmpty ? fallback : c.AsText();
    }

    public async Task<double> GetNumberAsync(string key, double fallback = 0) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsNumber() ?? fallback;

    /// <summary>Like <see cref="GetNumberAsync"/> but null when empty or not a number.</summary>
    public async Task<double?> GetNumberOrNullAsync(string key) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsNumber();

    public async Task<bool> GetBoolAsync(string key, bool fallback = false) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsBool(fallback);

    /// <summary>The id of the chosen option (or the text if a variable supplied it).</summary>
    public async Task<string> GetChoiceAsync(string key, string fallback = "")
    {
        var c = await GetContentAsync(key).ConfigureAwait(false);
        return c.IsEmpty ? fallback : c.AsText();
    }

    public async Task<DateTimeOffset?> GetDateAsync(string key, DateTimeOffset? fallback = null) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsDate() ?? fallback;

    public async Task<TimeSpan> GetDurationAsync(string key, TimeSpan fallback = default)
    {
        var c = await GetContentAsync(key).ConfigureAwait(false);
        if (c.IsEmpty) return fallback;
        var text = c.AsText().Trim();
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts) && text.Contains(':')) return ts;
        return c.AsNumber() is { } seconds ? TimeSpan.FromSeconds(seconds) : fallback;
    }

    /// <summary>Time of day from a Time parameter ("07:30") or text.</summary>
    public async Task<TimeOnly?> GetTimeAsync(string key)
    {
        var c = await GetContentAsync(key).ConfigureAwait(false);
        if (c.IsEmpty) return null;
        if (TimeOnly.TryParse(c.AsText(), CultureInfo.CurrentCulture, out var t) || TimeOnly.TryParse(c.AsText(), CultureInfo.InvariantCulture, out t)) return t;
        return c.AsDate() is { } d ? TimeOnly.FromDateTime(d.LocalDateTime) : null;
    }

    /// <summary>Files (and images) from the parameter. Folders are skipped unless includeFolders.</summary>
    public async Task<IReadOnlyList<PathItem>> GetFilesAsync(string key, bool includeFolders = false) =>
        await (await GetContentAsync(key).ConfigureAwait(false)).AsFilesAsync(this, includeFolders).ConfigureAwait(false);

    public async Task<IReadOnlyList<ImageItem>> GetImagesAsync(string key) =>
        await (await GetContentAsync(key).ConfigureAwait(false)).AsImagesAsync(this).ConfigureAwait(false);

    /// <summary>A single file or folder path from a File/Folder/SaveFile parameter (null when empty).</summary>
    public async Task<string?> GetPathAsync(string key)
    {
        var c = await GetContentAsync(key).ConfigureAwait(false);
        if (c.IsEmpty) return null;
        if (c[0] is PathItem p) return p.Path;
        var t = c.AsText().Trim().Trim('"');
        return t.Length == 0 ? null : Environment.ExpandEnvironmentVariables(t);
    }

    public async Task<IReadOnlyList<Uri>> GetUrlsAsync(string key) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsUrls();

    public async Task<IReadOnlyList<ContentItem>> GetListAsync(string key) =>
        (await GetContentAsync(key).ConfigureAwait(false)).Items;

    public async Task<DictionaryItem?> GetDictionaryAsync(string key) =>
        (await GetContentAsync(key).ConfigureAwait(false)).AsDictionary();

    public async Task<ConditionSet> GetConditionsAsync(string key)
    {
        if (GetParameter(key) is not ConditionsValue cv) return new ConditionSet(ConditionMatch.All, Array.Empty<ResolvedCondition>());
        var rows = new List<ResolvedCondition>();
        foreach (var r in cv.Items)
        {
            CancellationToken.ThrowIfCancellationRequested();
            rows.Add(new ResolvedCondition(
                await ResolveAsync(r.Left, null).ConfigureAwait(false),
                r.Op,
                r.Right is null ? ContentValue.Empty : await ResolveAsync(r.Right, null).ConfigureAwait(false),
                r.Right2 is null ? ContentValue.Empty : await ResolveAsync(r.Right2, null).ConfigureAwait(false)));
        }
        return new ConditionSet(cv.Match, rows);
    }

    // ---- temp files --------------------------------------------------------------------------

    /// <summary>A new, unused path in the run's temp folder, keeping the file name's base and extension ("Text.txt", "Text 2.txt", …).</summary>
    public string NewTempPath(string fileName)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "File" : SanitizeFileName(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var path = Path.Combine(TempFolder, fileName);
        for (var n = 2; File.Exists(path) || Directory.Exists(path); n++) path = Path.Combine(TempFolder, $"{stem} {n}{ext}");
        return path;
    }

    public FileItem WriteTempFile(string fileName, string text)
    {
        var path = NewTempPath(fileName);
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        return new FileItem(path);
    }

    /// <summary>Downloads a URL into the run's temp folder using the shared HttpClient.</summary>
    public async Task<PathItem> DownloadAsync(Uri url)
    {
        // A preview never reaches the web.
        if (Preview is not null) throw new ActionException($"Can't preview {url}: it is a web item and isn't downloaded in a preview.");
        try
        {
            using var response = await Services.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, CancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(url.AbsolutePath);
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            if (!Path.HasExtension(name) && response.Content.Headers.ContentType?.MediaType is { } mt)
                name += mt switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", "application/pdf" => ".pdf", "text/html" => ".html", "text/plain" => ".txt", "application/json" => ".json", _ => "" };
            var path = NewTempPath(name);
            await using (var fs = File.Create(path))
                await response.Content.CopyToAsync(fs, CancellationToken).ConfigureAwait(false);
            return ContentValue.ItemForPath(path) as PathItem ?? new FileItem(path);
        }
        catch (HttpRequestException ex)
        {
            throw new ActionException($"Couldn't download {url.Host}. Check your internet connection and the link. ({ex.Message})", ex);
        }
    }

    public static string SanitizeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => bad.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }

    // ---- resolution --------------------------------------------------------------------------

    /// <summary>Resolves an arbitrary <see cref="ParamValue"/> (e.g. one element of a custom list) to content.</summary>
    public Task<ContentValue> ResolveAsync(ParamValue value) => ResolveAsync(value, null);

    private async Task<ContentValue> ResolveAsync(ParamValue value, ParameterDescriptor? pd)
    {
        CancellationToken.ThrowIfCancellationRequested();
        switch (value)
        {
            case TextValue t:
                {
                    if (t.Parts.Count == 1 && t.Parts[0].Var is null) return ContentValue.Text(t.Parts[0].Literal ?? "");
                    var pieces = new List<(string Text, VarRef? Var)>();
                    foreach (var part in t.Parts)
                    {
                        if (part.Var is { } v) pieces.Add(((await _scope.ResolveAsync(v).ConfigureAwait(false)).AsText(), v));
                        else pieces.Add((part.Literal ?? "", null));
                    }
                    var result = string.Concat(pieces.Select(p => p.Text));
                    if (!string.IsNullOrWhiteSpace(result) && pd?.Type is ParamType.File or ParamType.Folder or ParamType.SaveFile)
                        CheckPathVariables(pieces, pd, result);
                    return ContentValue.Text(result);
                }
            case NumberValue n: return ContentValue.Number(n.Value);
            case BoolValue b: return ContentValue.Bool(b.Value);
            case ChoiceValue c: return ContentValue.Text(c.Value);
            case VarValue v: return await _scope.ResolveAsync(v.Ref).ConfigureAwait(false);
            case ListValue l:
                {
                    var items = new List<ContentItem>();
                    foreach (var e in l.Items) items.AddRange(await ResolveAsync(e, null).ConfigureAwait(false));
                    return new ContentValue(items);
                }
            case DictValue d:
                {
                    var entries = new List<KeyValuePair<string, ContentValue>>();
                    foreach (var e in d.Items)
                    {
                        var k = (await ResolveAsync(e.Key, null).ConfigureAwait(false)).AsText();
                        entries.Add(new(k, await ResolveAsync(e.Value, null).ConfigureAwait(false)));
                    }
                    return ContentValue.Dict(entries);
                }
            case AskValue: return await AskAsync(pd).ConfigureAwait(false);
            case DateValue dt: return ContentValue.Date(dt.Value);
            case TimeValue tm: return ContentValue.Text(tm.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
            case DurationValue du: return ContentValue.Number(du.Seconds);
            case HotkeyValue hk: return ContentValue.Text(hk.Keys);
            case FileValue f: return ContentValue.FileOrFolder(Environment.ExpandEnvironmentVariables(f.Path));
            case AppValue a: return ContentValue.From(new AppItem(a.Id, a.Name ?? a.Id));
            case ConditionsValue: throw new ActionException("Conditions can only be used where a comparison is expected.");
            default: return ContentValue.Empty;
        }
    }

    /// <summary>
    /// A path built from text and variables must not silently lose a part or climb out of its folder: "C:\Pictures\{Album}"
    /// with an empty Album, or with Album "..", would otherwise point at the whole Pictures folder or above it. A variable that
    /// fills a whole path segment may not be empty or only dots and slashes; one next to other text in the same segment
    /// ("{Prefix}report.txt") may be empty, but may not bring in a slash and dots. A segment counts as empty when nothing but quotes,
    /// spaces and dots is left in it, because the path is unquoted and trimmed later and Windows drops trailing dots and spaces:
    /// "D:\Photos\{Album}" in quotes, or followed by a space or a dot, is still the whole Photos folder. A variable may never bring
    /// in a ".." segment ("..\Other").
    /// </summary>
    private static void CheckPathVariables(List<(string Text, VarRef? Var)> pieces, ParameterDescriptor pd, string result)
    {
        static bool IsSeparator(char c) => c is '\\' or '/';
        static bool EmptySegment(string segment) => segment.All(c => c is '.' or '"' || char.IsWhiteSpace(c));
        var before = 0;
        for (var i = 0; i < pieces.Count; i++)
        {
            var (text, v) = pieces[i];
            if (v is not null)
            {
                var blank = string.IsNullOrWhiteSpace(text);
                var onlyDotsAndSlashes = !blank && text.All(c => c is '.' or ' ' or '"' || IsSeparator(c));
                // The segment of the finished path that holds the variable (meaningful when the value has no separator of its own).
                var start = before;
                while (start > 0 && !IsSeparator(result[start - 1])) start--;
                var end = before + text.Length;
                while (end < result.Length && !IsSeparator(result[end])) end++;
                var ownSegmentEmpty = !text.Any(IsSeparator) && EmptySegment(result[start..end]);
                if (blank && ownSegmentEmpty)
                    throw new ActionException($"{DescribeVariable(v)} is empty, so '{pd.Label}' isn't a complete path. Automation Portal stopped instead of using '{result.Trim()}'.");
                if (onlyDotsAndSlashes && (ownSegmentEmpty || text.Any(IsSeparator))
                    || text.Split('\\', '/').Any(s => s.Trim().Trim('"').Trim() == ".."))
                    throw new ActionException($"{DescribeVariable(v)} is '{text}', so '{pd.Label}' would point to a different folder. Automation Portal stopped instead of using '{result.Trim()}'.");
            }
            before += text.Length;
        }
    }

    /// <summary>How a variable is named in messages, e.g. "The variable 'Album'".</summary>
    public static string DescribeVariable(VarRef v) => v.Kind switch
    {
        VarKind.Named when !string.IsNullOrWhiteSpace(v.Name) => $"The variable '{v.Name}'",
        VarKind.Output => "The output of an earlier step",
        VarKind.Input => "The variable Shortcut Input",
        VarKind.RepeatItem => "The variable Repeat Item",
        VarKind.RepeatIndex => "The variable Repeat Index",
        VarKind.MenuResult => "The variable Menu Result",
        _ => $"The variable {v.Kind}",
    } + (v.Property is null ? "" : $" ({v.Property})");

    private async Task<ContentValue> AskAsync(ParameterDescriptor? pd)
    {
        var ui = Services.UserInteraction;
        var label = pd?.Label ?? "Enter a value";
        var prompt = label.EndsWith('?') || label.EndsWith(':') ? label : $"{label}:";
        var def = pd?.Default?.ToLiteralText();
        var ct = CancellationToken;
        switch (pd?.Type ?? ParamType.Text)
        {
            case ParamType.Number: return await ui.AskForInputAsync(prompt, InputKind.Number, def, ct).ConfigureAwait(false);
            case ParamType.Duration: return await ui.AskForInputAsync($"{label} (seconds):", InputKind.Number, def, ct).ConfigureAwait(false);
            case ParamType.Date:
            case ParamType.DateTime: return await ui.AskForInputAsync(prompt, InputKind.Date, def, ct).ConfigureAwait(false);
            case ParamType.Time: return await ui.AskForInputAsync(prompt, InputKind.Time, def, ct).ConfigureAwait(false);
            case ParamType.Url: return await ui.AskForInputAsync(prompt, InputKind.Url, def, ct).ConfigureAwait(false);
            case ParamType.MultilineText:
            case ParamType.Content:
            case ParamType.List:
            case ParamType.Dictionary:
                return await ui.AskForInputAsync(prompt, InputKind.Multiline, def, ct).ConfigureAwait(false);
            case ParamType.Boolean:
                {
                    var r = await ui.ChooseFromListAsync(prompt, new ContentItem[] { new TextItem("Yes"), new TextItem("No") }, false, ct).ConfigureAwait(false);
                    return ContentValue.Bool(r.AsBool());
                }
            case ParamType.Choice:
                {
                    var choices = pd!.Choices ?? (pd.ChoicesProvider is { } p ? await p(Services, ct).ConfigureAwait(false) : Array.Empty<Choice>());
                    var r = await ui.ChooseFromListAsync(prompt, choices.Select(c => (ContentItem)new TextItem(c.Label)).ToList(), false, ct).ConfigureAwait(false);
                    var label2 = r.AsText();
                    return ContentValue.Text(choices.FirstOrDefault(c => c.Label == label2)?.Id ?? label2);
                }
            case ParamType.File: return await ui.PickFilesAsync(prompt, false, null, ct).ConfigureAwait(false);
            case ParamType.Folder: return await ui.PickFolderAsync(prompt, ct).ConfigureAwait(false);
            case ParamType.SaveFile: return ContentValue.Text(await ui.PickSaveFileAsync(prompt, null, null, ct).ConfigureAwait(false));
            default: return await ui.AskForInputAsync(prompt, InputKind.Text, def, ct).ConfigureAwait(false);
        }
    }
}
