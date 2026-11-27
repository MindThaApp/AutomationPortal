using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>
/// The CLI's commands (list, run, import, export), shared by the running app (answering over the pipe) and the
/// in-process <c>ap</c>, so both behave and print the same.
/// </summary>
public sealed class ShortcutCommands
{
    public delegate Task<RunResult?> RunHandler(Shortcut shortcut, ContentValue input, RunSource source, CancellationToken ct);

    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IShortcutLibrary _library;
    private readonly RunHandler _run;

    public ShortcutCommands(IShortcutLibrary library, RunHandler run)
    {
        _library = library;
        _run = run;
    }

    /// <summary>
    /// Set by the app when it has a window. Every pipe import is then shown to the user, who decides whether it is added, whatever the
    /// request's <see cref="PipeRequest.Source"/> says. Gets the .autoportal text and the request; returns the import, or null when the
    /// user said no. When not set (the in-process ap, run by the user at the terminal) imports are added straight away.
    /// </summary>
    public Func<string, PipeRequest, Task<ImportResult?>>? ReviewImport { get; init; }

    /// <summary>
    /// Set by the app: gets every accepted "event" request, which only feeds triggers. It must return quickly (never wait for an
    /// automation to run). When not set (the in-process ap) events are accepted and dropped.
    /// </summary>
    public Action<AppEvent>? PublishEvent { get; init; }

    /// <summary>
    /// Set by the app: asks the user before a run whose request has <see cref="PipeRequest.Ask"/> set. When not set (the in-process ap)
    /// such a run is refused, since nobody could be asked.
    /// </summary>
    public CallerConsent? Consent { get; init; }

    /// <summary>Whether a shortcut has steps that change files, for the caller prompt. The app knows which steps do.</summary>
    public Func<Shortcut, bool>? ChangesFiles { get; init; }

    /// <summary>The file changes a run recorded, by run id (the undo journal). Null or empty means the reply lists no files.</summary>
    public Func<string, IReadOnlyList<FileChange>?>? RunChanges { get; init; }

    /// <summary>Whether a shortcut has steps that click and type in other apps, for the caller prompt.</summary>
    public Func<Shortcut, bool>? ControlsApps { get; init; }

    /// <summary>
    /// Set by the app: opens the editor on a new, unsaved shortcut with the files to try it on, and completes once the editor is shown.
    /// When not set (the in-process ap) "new" is refused, since there is no editor.
    /// </summary>
    public Func<NewShortcutRequest, CancellationToken, Task>? OpenNewShortcut { get; init; }

    /// <summary>The actions, for what each shortcut does to files (list, tools, preview and when a run is asked first). Without them only action ids are used.</summary>
    public ActionRegistry? Actions { get; init; }

    /// <summary>Set by the app: one step as a sentence ("Move files to Documents") for previews. Without it the step's title is used.</summary>
    public Func<ActionInstance, string?>? DescribeStep { get; init; }

    /// <summary>Set by the app: gets every run request from another app and how it ended, for the app's log.</summary>
    public Action<RunRequestLog>? LogRequest { get; init; }

    /// <summary>The pipe contract version reported by ping and capabilities. See "Pipe contract v1" in docs/CLI.md.</summary>
    public const int ContractVersion = 1;

    /// <summary>The contract's minor version, reported as <c>contractVersion</c>; 1.1 added list details, preview, tools and caller.</summary>
    public const string ContractVersionText = "1.1";

    /// <summary>The kinds other apps may rely on. "launch" and "export" also work but are internal.</summary>
    public static IReadOnlyList<string> ContractKinds { get; } = new[] { "ping", "capabilities", "list", "run", "import", "event", "new", "preview", "tools" };

    public Task<PipeResponse> ExecuteAsync(PipeRequest request, CancellationToken ct) => request.Kind switch
    {
        "ping" or "capabilities" => Task.FromResult(PipeResponse.Done(Hello())),
        "list" => Task.FromResult(List(request.Json)),
        "run" => RunAsync(request, ct),
        "preview" => Task.FromResult(Preview(request)),
        "tools" => Task.FromResult(Tools()),
        "import" => ImportAsync(request, ct),
        "export" => Task.FromResult(Export(request)),
        "event" => Task.FromResult(Event(request)),
        "new" => NewAsync(request, ct),
        _ => Task.FromResult(PipeResponse.Error(CliExit.Usage, $"Automation Portal doesn't know the command \"{request.Kind}\".")),
    };

    // ---- ping / capabilities -----------------------------------------------------------------

    private string Hello()
    {
        // caller-prompt: ap.exe starts the app when it has to ask, so it is always there.
        var features = new List<string>
        {
            "list.json", "run.wait", "run.input", "run.files", "run.json", "import.json", "cli.files-from", "caller-prompt", "new-shortcut",
            "list.details", "preview", "tools", "caller", "run.request-sheet", "run.files-list",
        };
        if (ReviewImport is not null) features.Add("import.review");
        return JsonSerializer.Serialize(new
        {
            app = "AutomationPortal", version = Version, contract = ContractVersion, contractVersion = ContractVersionText,
            edition = PackageIdentity.IsPackaged ? "store" : "unpackaged", kinds = ContractKinds, features,
        }, JsonOut);
    }

    private static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(ShortcutCommands).Assembly;
        var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : info[..plus];   // drop the source revision the SDK appends
    }

    // ---- finding a shortcut ------------------------------------------------------------------

    /// <summary>Id, then exact name, then alias, then tool name, then a name that starts with the text (only when exactly one does).</summary>
    public Shortcut? Find(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id) && _library.GetById(id) is { } byId) return byId;
        if (_library.FindByName(nameOrId) is { } byName) return byName;
        var all = _library.All;
        var alias = all.Where(s => string.Equals(s.Alias, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (alias.Count == 1) return alias[0];
        if (ShortcutTools.ToolNames(all).FirstOrDefault(p => p.Value == nameOrId) is { Value: not null } tool && _library.GetById(tool.Key) is { } byTool) return byTool;
        var prefix = all.Where(s => s.Name.StartsWith(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return prefix.Count == 1 ? prefix[0] : null;
    }

    private PipeResponse NotFound(string nameOrId)
    {
        var near = _library.All.Where(s => s.Name.Contains(nameOrId, StringComparison.OrdinalIgnoreCase)).Take(5).Select(s => $"\"{s.Name}\"").ToList();
        var hint = near.Count > 0 ? " Did you mean " + string.Join(", ", near) + "?" : " Run \"ap list\" to see your shortcuts.";
        return PipeResponse.Error(CliExit.NotFound, $"Couldn't find a shortcut called \"{nameOrId}\".{hint}");
    }

    // ---- list --------------------------------------------------------------------------------

    private PipeResponse List(bool json)
    {
        var folders = _library.Folders.ToDictionary(f => f.Id, f => f.Name);
        var all = _library.All;
        var tools = ShortcutTools.ToolNames(all);
        var items = all.Select(s => new
        {
            id = s.Id, name = s.Name, folder = s.FolderId is { } f && folders.TryGetValue(f, out var n) ? n : null,
            alias = s.Alias, hotkey = s.Hotkey, pinned = s.Pinned, description = s.Description,
            effects = ShortcutEffects.WireName(ShortcutEffects.Of(s, Actions)), inputs = ShortcutTools.InputSchema(s), toolName = tools[s.Id],
        }).ToList();

        if (json) return PipeResponse.Done(JsonSerializer.Serialize(items, JsonOut));
        if (items.Count == 0) return PipeResponse.Done("", "You don't have any shortcuts yet.");

        var width = Math.Min(40, items.Max(i => i.name.Length));
        var sb = new StringBuilder();
        foreach (var i in items)
            sb.AppendLine($"{Trim(i.name, width).PadRight(width)}  {i.id:D}{(i.alias is { Length: > 0 } a ? "  (" + a + ")" : "")}");
        return PipeResponse.Done(sb.ToString().TrimEnd());
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---- when a run from another app is asked first ------------------------------------------

    /// <summary>
    /// Every pipe and ap run comes from outside Automation Portal's own window, tray, hotkeys and triggers. It is asked first when the
    /// request says so (<see cref="PipeRequest.Ask"/>), when the caller says it is an AI, when the shortcut makes or changes files or
    /// clicks and types in other apps, and when it runs programs, downloads or sends data. Only a shortcut that does none of that runs
    /// straight away for an app that isn't an AI.
    /// </summary>
    public bool MustAsk(PipeRequest r, Shortcut shortcut) =>
        r.Ask || r.Caller?.IsAi == true
        || ShortcutEffects.Of(shortcut, Actions) != ShortcutEffect.None
        || ShortcutEffects.Risks(shortcut, Actions) != ImportRisk.None;

    private static string CallerNameOf(PipeRequest r) =>
        r.Caller?.SafeAppName ?? (Callers.IsValidId(r.Source) ? Callers.FriendlyName(r.Source!) : "Another app");

    /// <summary>One waiting request per caller: by the name it gives, else its caller id; requests that give neither share one.</summary>
    private static string RateKeyOf(PipeRequest r) =>
        r.Caller?.SafeAppName is { } app ? "app:" + app.ToLowerInvariant() : Callers.IsValidId(r.Source) ? "id:" + r.Source : "unnamed";

    /// <summary>True when this run request names a shortcut that must be asked first: only the app can do that.</summary>
    public bool NeedsConsent(PipeRequest r) =>
        r.Kind == "run" && !string.IsNullOrWhiteSpace(r.Name) && Find(r.Name) is { } shortcut && MustAsk(r, shortcut);

    // ---- preview / tools (contract 1.1) ------------------------------------------------------

    /// <summary>What the shortcut would do with this input, worked out from its steps. Never runs, reads or opens anything.</summary>
    public ShortcutDryRun DryRun(Shortcut shortcut, string? text, IReadOnlyList<string> files) =>
        ShortcutTools.DryRun(shortcut, text, files, Actions, DescribeStep);

    private PipeResponse Preview(PipeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to preview, for example: ap preview \"Good Morning\".");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);
        if (HiddenCharacterProblem(r.Files) is { } bad) return PipeResponse.Error(CliExit.Usage, bad);
        var plan = DryRun(shortcut, r.Input, r.Files);
        if (!r.Json) return PipeResponse.Done(plan.Describe(int.MaxValue));
        var tools = ShortcutTools.ToolNames(_library.All);
        return PipeResponse.Done(JsonSerializer.Serialize(new
        {
            shortcut = shortcut.Name, id = shortcut.Id, toolName = tools[shortcut.Id], effects = ShortcutEffects.WireName(plan.Effect),
            input = new { text = r.Input, files = r.Files }, asksUser = plan.AsksUser, asksFirst = MustAsk(r, shortcut),
            steps = plan.Steps.Select(s => new { number = s.Number, title = s.Title, text = s.Text, effect = ShortcutEffects.WireName(s.Effect) }),
            summary = plan.Describe(),
        }, JsonOut));
    }

    /// <summary>Every shortcut as a function tool: <c>{type: "function", function: {name, description, parameters}}</c>.</summary>
    private PipeResponse Tools()
    {
        var all = _library.All;
        var names = ShortcutTools.ToolNames(all);
        var tools = all.Select(s => new
        {
            type = "function",
            function = new
            {
                name = names[s.Id],
                description = ShortcutTools.ToolDescription(s, ShortcutEffects.Of(s, Actions), asksFirst: true),
                parameters = ShortcutTools.InputSchema(s),
            },
        }).ToList();
        return PipeResponse.Done(JsonSerializer.Serialize(tools, JsonOut));
    }

    // ---- run ---------------------------------------------------------------------------------

    private async Task<PipeResponse> RunAsync(PipeRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to run, for example: ap run \"Good Morning\".");
        var shortcut = Find(r.Name);
        if (shortcut is null) { Log(r, null, "pipe", "notFound"); return NotFound(r.Name); }
        if (await GateAsync(r, shortcut, "pipe", ct).ConfigureAwait(false) is { } notRun) return notRun;

        var input = r.Files.Length > 0
            ? (string.IsNullOrEmpty(r.Input) ? ContentValue.Files(r.Files) : ContentValue.Concat(ContentValue.Text(r.Input), ContentValue.Files(r.Files)))
            : string.IsNullOrEmpty(r.Input) ? ContentValue.Empty : ContentValue.Text(r.Input);
        return await RunNowAsync(r, shortcut, input, ct).ConfigureAwait(false);
    }

    // Everything from the request goes through SafeLogText, so a name with line breaks can't forge log lines.
    private void Log(PipeRequest r, Shortcut? shortcut, string via, string outcome) =>
        LogRequest?.Invoke(new RunRequestLog(DateTimeOffset.Now, CallerNameOf(r), r.Caller?.IsAi == true ? "ai" : CallerInfo.Sanitize(r.Caller?.Kind, 16),
            SafeLogText(r.Source), SafeLogText(shortcut?.Name ?? r.Name) ?? "", shortcut is null ? "-" : ShortcutEffects.WireName(ShortcutEffects.Of(shortcut, Actions)), outcome, r.Files.Length, !string.IsNullOrEmpty(r.Input))
        { Via = via });

    /// <summary>
    /// A <c>--run</c> from a command line (Send To, a desktop or Start menu link, a right-click entry, or any program that starts
    /// Automation Portal or ap.exe). With a valid <see cref="LaunchTokens"/> token it is an entry Automation Portal made and runs as
    /// before; without one it is asked like another app's request (shown as "Another program") whenever <see cref="MustAsk"/> says
    /// so. Null when it may run; otherwise why it didn't.
    /// </summary>
    public Task<PipeResponse?> GateLaunchAsync(Shortcut shortcut, string? input, IReadOnlyList<string> files, bool validToken, CancellationToken ct)
    {
        var r = new PipeRequest { Kind = "run", Name = shortcut.Id.ToString("D"), Input = input, Files = files.ToArray(), Caller = new CallerInfo { App = AnotherProgram } };
        if (!validToken) return GateAsync(r, shortcut, "launch", ct);
        Log(r, shortcut, "shell entry", "ran");
        return Task.FromResult<PipeResponse?>(null);
    }

    public const string AnotherProgram = "Another program";

    private static bool IsHidden(char c) => char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format;

    /// <summary>
    /// A path with a control or format character (a line break, or U+202E that shows "exe.txt" as "txt.exe") could look like a
    /// different file on the request sheet. Such requests are refused; null when every path is plain.
    /// </summary>
    public static string? HiddenCharacterProblem(IReadOnlyList<string> files)
    {
        for (var i = 0; i < files.Count; i++)
            if (files[i].Any(IsHidden))
                return $"Item {i + 1} of the files has a hidden or control character in its path (such as a line break or a direction mark), so Automation Portal won't use it. Nothing ran.";
        return null;
    }

    /// <summary>The input text as shown on the sheet: format characters (direction marks, zero-width characters) taken out; null when empty.</summary>
    public static string? WithoutFormatCharacters(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var shown = new string(text.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
        return shown.Length == 0 ? null : shown;
    }

    /// <summary>The program file of a pipe client (by process id), or null. Replaceable for tests.</summary>
    public Func<int, string?> ClientImagePath { get; init; } = PipeClients.ImagePathOf;

    /// <summary>
    /// True when the request came through ap.exe: its source is "cli" (what ap sends unless told otherwise), or the pipe client's
    /// program is called ap.exe. Any program can start ap.exe with any --from, so earlier "Don't ask again" answers don't count.
    /// </summary>
    private bool CameThroughAp(PipeRequest r) =>
        r.Source == "cli"
        || r.ClientProcessId > 0 && string.Equals(Path.GetFileName(ClientImagePath(r.ClientProcessId)), "ap.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text from a request made safe for the log (see <see cref="CallerInfo.Sanitize"/>), at most 200 characters.</summary>
    public static string? SafeLogText(string? text) => CallerInfo.Sanitize(text, 200);

    /// <summary>Asks first when <see cref="MustAsk"/> says so, and logs how the request ended. Null when it may run.</summary>
    private async Task<PipeResponse?> GateAsync(PipeRequest r, Shortcut shortcut, string via, CancellationToken ct)
    {
        var effect = ShortcutEffects.Of(shortcut, Actions);
        void Log(string outcome) => this.Log(r, shortcut, via, outcome);

        if (HiddenCharacterProblem(r.Files) is { } bad) { Log("blocked"); return PipeResponse.Error(CliExit.Usage, bad); }
        if (MustAsk(r, shortcut))
        {
            if (r.Ask && !Callers.IsValidId(r.Source)) { Log("blocked"); return PipeResponse.Error(CliExit.Usage, "To ask first, say which app is asking. " + Callers.InvalidIdMessage); }
            if (Consent is null) { Log("blocked"); return PipeResponse.Error(CliExit.Problem, "Automation Portal needs to be open to ask you first. Open it and try again."); }
            var isAi = r.Caller?.IsAi == true;
            var validId = Callers.IsValidId(r.Source);
            var viaAp = CameThroughAp(r);
            var prompt = new CallerPrompt(validId ? r.Source! : "", shortcut.Name, r.Files, !string.IsNullOrEmpty(r.Input))
            {
                ShortcutId = shortcut.Id, StepsHash = LinkPolicy.StepsHash(shortcut), ChangesFiles = ChangesFiles?.Invoke(shortcut) == true, ControlsApps = ControlsApps?.Invoke(shortcut) == true,
                DisplayName = r.Caller?.SafeAppName, IsAi = isAi, Effect = effect,
                // "Don't ask again" only for a caller id whose request doesn't name some other app, and never through ap.exe.
                CanRemember = validId && !isAi && !viaAp && Callers.NameMatches(r.Source, r.Caller?.SafeAppName),
                IgnoreAlwaysAllow = viaAp,
                PreviewText = DryRun(shortcut, r.Input, r.Files).Describe(), InputText = WithoutFormatCharacters(r.Input),
                RateKey = RateKeyOf(r),
            };
            var verdict = await Consent.RequestAsync(prompt, ct).ConfigureAwait(false);
            var status = verdict switch
            {
                CallerVerdict.Allowed => "allowed", CallerVerdict.TimedOut => "timedOut", CallerVerdict.Busy => "rateLimited", CallerVerdict.Full => "busy", _ => "declined",
            };
            Log(status);
            if (verdict != CallerVerdict.Allowed)
            {
                var notRun = Consent.NotRun(verdict, prompt.CallerName);
                // Contract 1.1 callers (they send "caller") get the reason as JSON too; v1 replies stay as they were.
                if (r.Json && r.Caller is not null)
                    notRun.Output = JsonSerializer.Serialize(new { shortcut = shortcut.Name, id = shortcut.Id, status, error = notRun.Message }, JsonOut);
                return notRun;
            }
        }
        else Log("ran");
        return null;
    }

    private async Task<PipeResponse> RunNowAsync(PipeRequest r, Shortcut shortcut, ContentValue input, CancellationToken ct)
    {
        if (!r.Wait)
        {
            _ = _run(shortcut, input, RunSource.Cli, CancellationToken.None).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return PipeResponse.Done(r.Json ? JsonSerializer.Serialize(new { shortcut = shortcut.Name, status = "started" }, JsonOut) : null, $"Started \"{shortcut.Name}\".");
        }

        var result = await _run(shortcut, input, RunSource.Cli, ct).ConfigureAwait(false);
        if (result is null) return PipeResponse.Error(CliExit.Failed, $"\"{shortcut.Name}\" is already running.");

        var text = result.Output.AsText();
        var code = result.Status switch { RunStatus.Succeeded => CliExit.Ok, RunStatus.Failed => CliExit.Failed, _ => CliExit.Cancelled };
        var message = result.Status switch
        {
            RunStatus.Failed => result.ErrorMessage ?? "The shortcut didn't finish.",
            RunStatus.Cancelled => "The shortcut was cancelled.",
            RunStatus.Stopped => "The shortcut stopped before it finished.",
            _ => null,
        };
        var output = r.Json
            ? JsonSerializer.Serialize(new
            {
                shortcut = result.ShortcutName, id = result.ShortcutId, status = result.Status.ToString().ToLowerInvariant(),
                output = text, error = result.ErrorMessage, durationMs = (long)result.Duration.TotalMilliseconds, runId = result.RunId,
                files = FilesOf(result.RunId),
            }, JsonOut)
            : text;
        return new PipeResponse { ExitCode = code, Output = output, Message = r.Json ? null : message };
    }

    /// <summary>The files a run made or changed, from what it recorded: kind "new", "copy" (a copy it made) or "changed". Nothing is scanned.</summary>
    private object[] FilesOf(string runId)
    {
        IReadOnlyList<FileChange>? changes = null;
        try { if (RunChanges is not null && !string.IsNullOrEmpty(runId)) changes = RunChanges(runId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (changes is null) return Array.Empty<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<object>();
        foreach (var c in changes)
        {
            string? kind = c.Kind switch
            {
                FileChangeKind.Created => string.IsNullOrEmpty(c.From) ? "new" : "copy",
                FileChangeKind.Replaced or FileChangeKind.Changed or FileChangeKind.Moved or FileChangeKind.Renamed => "changed",
                _ => null,
            };
            if (kind is null || !seen.Add(c.Path)) continue;
            files.Add(new { path = c.Path, kind });
        }
        return files.ToArray();
    }

    // ---- new ---------------------------------------------------------------------------------

    /// <summary>
    /// Opens the editor on a new, unsaved shortcut with the files as its test input. Runs nothing, saves nothing and never opens or
    /// changes the files; files that aren't there are allowed and noted. Never asks first, since nothing can happen without the user.
    /// </summary>
    private async Task<PipeResponse> NewAsync(PipeRequest r, CancellationToken ct)
    {
        if (r.Files.Length == 0) return PipeResponse.Error(CliExit.Usage, "Say which files the new shortcut is for, for example: ap new --file \"C:\\Photos\\a.jpg\".");
        var (paths, problem) = LaunchArguments.CheckNewShortcutPaths(r.Files);
        if (problem is not null) return PipeResponse.Error(CliExit.Usage, problem);
        if (OpenNewShortcut is null) return PipeResponse.Error(CliExit.Problem, "Automation Portal needs to be open to create a shortcut. Open it and try again.");

        var missing = LaunchArguments.MissingNewShortcutFiles(paths);
        var caller = Callers.IsValidId(r.Source) && r.Source != "cli" ? r.Source : null;
        await OpenNewShortcut(new NewShortcutRequest(paths, missing, caller), ct).ConfigureAwait(false);

        var files = paths.Count == 1 ? "1 file" : $"{paths.Count} files";
        var note = missing.Count == 0 ? "" : missing.Count == 1 ? " 1 of them couldn't be found." : $" {missing.Count} of them couldn't be found.";
        var output = r.Json ? JsonSerializer.Serialize(new { status = "opened", files = paths.Count, missing = missing.Count }, JsonOut) : null;
        return PipeResponse.Done(output, $"Opened a new shortcut in Automation Portal to try on {files}. Nothing has run.{note}");
    }

    // ---- event -------------------------------------------------------------------------------

    /// <summary>Set by the app: one line per event request and how it ended, for the app's log.</summary>
    public Action<string>? LogEvent { get; init; }

    /// <summary>The clock for the event rate limit. Replaceable for tests.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.Now;

    /// <summary>Each source may send a burst of this many events, then one more per second.</summary>
    public const int EventBurst = 5;
    public const double EventsPerSecond = 1;
    /// <summary>How many sources the event limit keeps track of at once.</summary>
    public const int EventSourcesTracked = 1000;

    private readonly RateBuckets _eventBuckets = new(EventBurst, EventsPerSecond, EventSourcesTracked);
    private int _eventsInFlight;

    /// <summary>
    /// Fire-and-forget whatever <see cref="PipeRequest.Wait"/> says: the reply never waits for an automation. At most
    /// <see cref="CallerConsent.MaxWaiting"/> events are handed over at once, and each source has a token bucket (a burst of
    /// <see cref="EventBurst"/>, refilled at <see cref="EventsPerSecond"/>). Nothing is shown; every request is logged.
    /// </summary>
    private PipeResponse Event(PipeRequest r)
    {
        var source = SafeLogText(r.Source) ?? "unnamed";
        var name = SafeLogText(r.Name) ?? "";
        void Log(string outcome) => LogEvent?.Invoke($"Event request from {source}: \"{name}\": {outcome}");

        if (!AppEvents.TryParse(r, Clock(), out var ev, out var problem)) { Log("invalid"); return problem!; }
        if (!_eventBuckets.TryTake(source, Clock()))
        {
            Log("rateLimited");
            return PipeResponse.Error(CliExit.Declined, $"Not accepted: {source} sent too many events at once. After a burst of {EventBurst}, send at most one a second.");
        }
        if (Interlocked.Increment(ref _eventsInFlight) > CallerConsent.MaxWaiting)
        {
            Interlocked.Decrement(ref _eventsInFlight);
            Log("busy");
            return PipeResponse.Error(CliExit.Declined, "Not accepted: Automation Portal is still handling other events. Try again in a moment.");
        }
        try { PublishEvent?.Invoke(ev!); }
        catch (Exception) { /* a listener must never turn an accepted event into an error for the sender */ }
        finally { Interlocked.Decrement(ref _eventsInFlight); }
        Log("accepted");
        return PipeResponse.Done();
    }

    // ---- import / export ---------------------------------------------------------------------

    /// <summary>
    /// Imports never replace a shortcut: the library gives every import a new id and a free name. With <see cref="ReviewImport"/> set,
    /// nothing is added until the user agrees; without wait the reply comes straight away, while the review is still open.
    /// </summary>
    private async Task<PipeResponse> ImportAsync(PipeRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Input)) return PipeResponse.Error(CliExit.Usage, "There was nothing to import.");
        if (ReviewImport is null) return Import(r);

        Shortcut preview;
        try { preview = ShortcutPackage.Parse(r.Input); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }

        var review = ReviewImport(r.Input, r);
        if (!r.Wait)
        {
            _ = review.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return PipeResponse.Done(r.Json ? JsonSerializer.Serialize(new { name = preview.Name, status = "review" }, JsonOut) : null,
                $"Automation Portal is showing \"{preview.Name}\" so you can decide whether to add it.");
        }

        ImportResult? result;
        try { result = await review.WaitAsync(ct).ConfigureAwait(false); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
        if (result is null)
            return new PipeResponse
            {
                ExitCode = CliExit.Cancelled, Output = r.Json ? JsonSerializer.Serialize(new { name = preview.Name, status = "declined" }, JsonOut) : null,
                Message = $"\"{preview.Name}\" wasn't added.",
            };
        return Imported(result, r.Json);
    }

    private PipeResponse Import(PipeRequest r)
    {
        try { return Imported(_library.ImportJson(r.Input!), r.Json); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }

    private static PipeResponse Imported(ImportResult result, bool json)
    {
        var note = result.RenamedFrom is null ? "" : $" You already had \"{result.RenamedFrom}\", so it was renamed.";
        var warn = result.Warnings.Count == 0 ? ""
            : " Heads up: it includes " + string.Join(", ", result.Warnings.Select(w => w.Title).Distinct()) + ".";
        var extras = new List<string>();
        if (result.PendingHotkey is { Length: > 0 } hk) extras.Add($"keyboard shortcut {hk}");
        if (result.PendingAlias is { Length: > 0 } al) extras.Add($"Quick Run name \"{al}\"");
        if (result.PendingShowInSendTo && PackageCapabilities.UnvirtualizedWritesAllowed) extras.Add("Send to entry");
        if (extras.Count > 0) warn += " Left off for safety: " + string.Join(", ", extras) + ". Turn them on in Automation Portal if you trust this shortcut.";
        var output = json ? JsonSerializer.Serialize(new { id = result.Shortcut.Id, name = result.Shortcut.Name, status = "added" }, JsonOut) : null;
        return PipeResponse.Done(output, $"Added \"{result.Shortcut.Name}\" to your shortcuts.{note}{warn}");
    }

    private PipeResponse Export(PipeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to export, for example: ap export \"Good Morning\" file.autoportal.");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);
        try { return PipeResponse.Done(_library.Export(shortcut.Id)); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }
}

/// <summary>
/// One run request from another app and how it ended. Outcome: "ran" (nothing to ask), "allowed", "declined", "timedOut",
/// "rateLimited" (that app already had a request waiting), "blocked" (refused before asking) or "notFound".
/// </summary>
public sealed record RunRequestLog(DateTimeOffset At, string Caller, string? CallerKind, string? Source, string Shortcut, string Effects, string Outcome, int Files, bool HasText)
{
    /// <summary>How it arrived: "pipe" (ap or another app), "launch" (a command line without a valid token) or "shell entry".</summary>
    public string Via { get; init; } = "pipe";

    /// <summary>One line for the app's log.</summary>
    public string Describe() =>
        $"Run request ({Via}) from {Caller}{(CallerKind is { Length: > 0 } k ? $" ({k})" : "")}{(Source is { Length: > 0 } s ? $" [source {s}]" : "")}: \"{Shortcut}\" " +
        $"effects {Effects}, {Files} file(s){(HasText ? ", text" : "")}: {Outcome}";
}

/// <summary>A "new" request for the app: the files to try the new shortcut on, those that couldn't be found, and the app that sent them (null for ap or a link).</summary>
public sealed record NewShortcutRequest(IReadOnlyList<string> Files, IReadOnlyList<string> Missing, string? CallerId);
