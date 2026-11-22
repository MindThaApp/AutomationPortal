namespace AutomationPortal.Core;

/// <summary>Caller ids: the short name another app gives when it asks to run a shortcut (<c>ap run --from filesportal</c>).</summary>
public static class Callers
{
    public const int MaxIdLength = 32;

    /// <summary>1 to 32 characters, each a lower-case letter a-z, a digit or a dash.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 1 and <= MaxIdLength } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>True when there is no name, no id, or the name is the id's known name (or the id itself), ignoring case.</summary>
    public static bool NameMatches(string? callerId, string? displayName) =>
        string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(callerId)
        || string.Equals(FriendlyName(callerId), displayName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(callerId, displayName, StringComparison.OrdinalIgnoreCase);

    public const string InvalidIdMessage = "The caller name must be 1 to 32 lower-case letters, digits or dashes, for example: filesportal.";

    /// <summary>The name shown to the user: a known app's name, otherwise the id as given.</summary>
    public static string FriendlyName(string id) => id switch
    {
        "filesportal" => "Files Portal",
        "deskpal" => "Desk Pal",
        "powerplanmanager" or "ppm" => "Power Plan Manager",
        "automationportal" => "Automation Portal",
        "cli" => "The ap command",
        _ => id,
    };
}

/// <summary>What the user is asked when another app wants to run a shortcut.</summary>
public sealed record CallerPrompt(string CallerId, string ShortcutName, IReadOnlyList<string> Files, bool HasText)
{
    /// <summary>The name the request gave itself (<see cref="CallerInfo.App"/>), shown instead of the caller id's name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The name shown. A name the request gave that isn't the known name of its caller id is shown with the id and marked unverified:
    /// "Files Portal (unverified), id x".
    /// </summary>
    public string CallerName => DisplayName is { Length: > 0 } d
        ? CallerId.Length == 0 || NameMatchesId ? d : $"{d} (unverified), id {CallerId}"
        : CallerId.Length > 0 ? Callers.FriendlyName(CallerId) : "Another app";

    /// <summary>True unless the request gave a name that differs from the known name of its caller id.</summary>
    public bool NameMatchesId => Callers.NameMatches(CallerId, DisplayName);

    /// <summary>The caller says it is an AI: it is always asked, and "Don't ask again" is never offered or kept.</summary>
    public bool IsAi { get; init; }

    /// <summary>False when "Don't ask again" must not be offered (an AI, or a caller without a valid id).</summary>
    public bool CanRemember { get; init; } = true;

    /// <summary>The request came through ap.exe, which any program can start with any caller id: "Don't ask again" given earlier doesn't count.</summary>
    public bool IgnoreAlwaysAllow { get; init; }

    /// <summary>What the shortcut does to files, for the badge.</summary>
    public ShortcutEffect Effect { get; init; }

    /// <summary>What the shortcut would do (<see cref="ShortcutDryRun.Describe"/>), or null.</summary>
    public string? PreviewText { get; init; }

    /// <summary>The text the caller hands to the shortcut, or null.</summary>
    public string? InputText { get; init; }

    /// <summary>Who counts as the same caller for "one waiting request per caller". Defaults to the caller id.</summary>
    public string? RateKey { get; init; }

    /// <summary>The line shown for an AI caller, else null.</summary>
    public string? AiLine => IsAi ? $"{CallerName} says it is an AI assistant. Check what it would do before you run it." : null;

    /// <summary>The shortcut asked for; "Don't ask again" then covers this caller with this shortcut only.</summary>
    public Guid? ShortcutId { get; init; }

    /// <summary><see cref="LinkPolicy.StepsHash"/> of the shortcut now; "Don't ask again" covers these steps only.</summary>
    public string? StepsHash { get; init; }

    /// <summary>True when the shortcut has steps that change files.</summary>
    public bool ChangesFiles { get; init; }

    /// <summary>True when the shortcut has steps that click and type in other apps.</summary>
    public bool ControlsApps { get; init; }

    /// <summary>The prompt's headline: "Files Portal wants to run "Resize Images" on 3 files".</summary>
    public string Question
    {
        get
        {
            var on = Files.Count switch
            {
                0 => HasText ? " with some text" : "",
                1 => " on 1 file",
                var n => $" on {n} files",
            };
            return $"{CallerName} wants to run \"{ShortcutName}\"{on}";
        }
    }

    /// <summary>One line under the headline when it matters, else null.</summary>
    public string? ChangesFilesLine => (ChangesFiles, ControlsApps) switch
    {
        (true, true) => "This shortcut changes files and clicks and types in other apps.",
        (true, false) => "This shortcut changes files.",
        (false, true) => "This shortcut clicks and types in other apps.",
        _ => null,
    };

    /// <summary>The tick box that stands for "Always allow".</summary>
    public string DontAskAgainText => ShortcutId is null
        ? $"Don't ask again when {CallerName} runs a shortcut"
        : $"Don't ask again when {CallerName} runs this shortcut";

    /// <summary>"Don't run (25)": the button that is also what happens when time is up.</summary>
    public static string DontRunLabel(int secondsLeft) => $"Don't run ({Math.Max(0, secondsLeft)})";

    /// <summary>"Files Portal wants to run "Tidy" with 3 files".</summary>
    public string Headline
    {
        get
        {
            var with = Files.Count switch
            {
                0 => HasText ? " with some text" : "",
                1 => " with 1 file",
                var n => $" with {n} files",
            };
            return $"{CallerName} wants to run \"{ShortcutName}\"{with}";
        }
    }
}

/// <summary>The user's answer to a <see cref="CallerPrompt"/>.</summary>
public enum CallerDecision
{
    AllowOnce,
    AlwaysAllow,
    DontRun,
}

/// <summary>How a consent request ended.</summary>
public enum CallerVerdict
{
    /// <summary>The caller is on the always-allowed list, or the user allowed it.</summary>
    Allowed,
    /// <summary>The user chose Don't run.</summary>
    Declined,
    /// <summary>Nobody answered in time.</summary>
    TimedOut,
    /// <summary>The same caller already had a request waiting for an answer; this one was turned away without asking.</summary>
    Busy,
    /// <summary><see cref="CallerConsent.MaxWaiting"/> requests were already waiting; this one was turned away without asking.</summary>
    Full,
}

/// <summary>
/// The consent step for other apps that ask to run a shortcut. Callers on the always-allowed list (in settings) go ahead; otherwise
/// the user is asked, one prompt at a time. No answer within <see cref="Timeout"/> (counted from when the request arrived, so a
/// queued request never waits longer) means no. The caller id is whatever the other app says it is: this is a consent step, not a
/// security boundary.
/// </summary>
public sealed class CallerConsent
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly ISettingsStore _settings;
    private readonly Func<CallerPrompt, CancellationToken, Task<CallerDecision>> _ask;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <param name="ask">Shows the prompt. The token is cancelled when time is up or the caller left: close the prompt then.</param>
    public CallerConsent(ISettingsStore settings, Func<CallerPrompt, CancellationToken, Task<CallerDecision>> ask, TimeSpan? timeout = null)
    {
        _settings = settings;
        _ask = ask;
        Timeout = timeout ?? DefaultTimeout;
    }

    public TimeSpan Timeout { get; }

    public bool IsAlwaysAllowed(string callerId) => _settings.Current.AllowedCallers?.Contains(callerId, StringComparer.Ordinal) == true;

    /// <summary>
    /// True when the caller may run any shortcut, or this one, without asking. "Don't ask again" for one shortcut only holds while
    /// its steps are the ones it was given for; after an edit the user is asked again.
    /// </summary>
    public bool IsAlwaysAllowed(CallerPrompt prompt) =>
        !prompt.IsAi && !prompt.IgnoreAlwaysAllow && (IsAlwaysAllowed(prompt.CallerId)
        || prompt.ShortcutId is { } id && _settings.Current.AllowedCallerShortcuts?.Contains(PairKey(prompt.CallerId, id), StringComparer.Ordinal) == true
           && LinkPolicy.SameSteps(_settings.Current, PairKey(prompt.CallerId, id), prompt.StepsHash));

    /// <summary>
    /// Earlier versions could allow a caller for every shortcut, including ones made later. Turns each such caller into one
    /// "Don't ask again" pair per shortcut there is now (for its steps now), and empties the old list. Saved through the settings
    /// store. Returns how many callers were turned over.
    /// </summary>
    public static int MigrateWholeCallers(ISettingsStore settings, IEnumerable<Shortcut> shortcuts)
    {
        var callers = settings.Current.AllowedCallers?.Where(Callers.IsValidId).Distinct(StringComparer.Ordinal).ToList();
        if (settings.Current.AllowedCallers is not { Count: > 0 }) return 0;
        var all = shortcuts.Select(s => (s.Id, Hash: LinkPolicy.StepsHash(s))).ToList();
        settings.Update(s =>
        {
            s.AllowedCallerShortcuts ??= new();
            s.TrustedStepHashes ??= new();
            foreach (var caller in callers ?? new())
                foreach (var (id, hash) in all)
                {
                    var key = PairKey(caller, id);
                    if (!s.AllowedCallerShortcuts.Contains(key, StringComparer.Ordinal)) s.AllowedCallerShortcuts.Add(key);
                    s.TrustedStepHashes[key] = hash;
                }
            s.AllowedCallers!.Clear();
        });
        return callers?.Count ?? 0;
    }

    /// <summary>How a caller and shortcut pair is kept in settings: "filesportal/0b6c...".</summary>
    public static string PairKey(string callerId, Guid shortcutId) => callerId + "/" + shortcutId.ToString("D");

    /// <summary>Splits a <see cref="PairKey"/>; false when it is damaged.</summary>
    public static bool TryParsePair(string key, out string callerId, out Guid shortcutId)
    {
        callerId = "";
        shortcutId = Guid.Empty;
        var slash = key.IndexOf('/');
        if (slash <= 0 || !Guid.TryParse(key[(slash + 1)..], out shortcutId)) return false;
        callerId = key[..slash];
        return Callers.IsValidId(callerId);
    }

    /// <summary>Asks unless the caller is always allowed. Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled (the caller left).</summary>
    public async Task<CallerVerdict> RequestAsync(CallerPrompt prompt, CancellationToken ct)
    {
        if (IsAlwaysAllowed(prompt)) return CallerVerdict.Allowed;

        var key = prompt.RateKey ?? prompt.CallerId;
        lock (_pending)
        {
            if (_pending.Contains(key)) return CallerVerdict.Busy;
            if (_pending.Count >= MaxWaiting) return CallerVerdict.Full;
            _pending.Add(key);
        }
        try { return await AskOnceAsync(prompt, ct).ConfigureAwait(false); }
        finally { lock (_pending) _pending.Remove(key); }
    }

    /// <summary>At most this many requests wait for the user at once, from all callers together.</summary>
    public const int MaxWaiting = 2;

    /// <summary>Callers with a request waiting for the user; each may have only one.</summary>
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    private async Task<CallerVerdict> AskOnceAsync(CallerPrompt prompt, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        try { await _oneAtATime.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return CallerVerdict.TimedOut; }

        try
        {
            // The answer to an earlier prompt may have been Always allow for this caller.
            if (IsAlwaysAllowed(prompt)) return CallerVerdict.Allowed;

            CallerDecision decision;
            try { decision = await _ask(prompt, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return CallerVerdict.TimedOut; }

            switch (decision)
            {
                case CallerDecision.AlwaysAllow when prompt.IsAi || !prompt.CanRemember:
                    return CallerVerdict.Allowed;
                case CallerDecision.AlwaysAllow:
                    if (prompt.ShortcutId is { } sid) Allow(prompt.CallerId, sid, prompt.StepsHash);
                    else Allow(prompt.CallerId);
                    return CallerVerdict.Allowed;
                case CallerDecision.AllowOnce:
                    return CallerVerdict.Allowed;
                default:
                    return CallerVerdict.Declined;
            }
        }
        finally { _oneAtATime.Release(); }
    }

    /// <summary>Adds the caller to the always-allowed list (once).</summary>
    public void Allow(string callerId)
    {
        if (!Callers.IsValidId(callerId)) return;
        _settings.Update(s => { s.AllowedCallers ??= new(); if (!s.AllowedCallers.Contains(callerId, StringComparer.Ordinal)) s.AllowedCallers.Add(callerId); });
    }

    /// <summary>Lets the caller run this one shortcut, with these steps, without asking (once in the list).</summary>
    public void Allow(string callerId, Guid shortcutId, string? stepsHash = null)
    {
        if (!Callers.IsValidId(callerId)) return;
        var key = PairKey(callerId, shortcutId);
        _settings.Update(s =>
        {
            s.AllowedCallerShortcuts ??= new();
            if (!s.AllowedCallerShortcuts.Contains(key, StringComparer.Ordinal)) s.AllowedCallerShortcuts.Add(key);
            s.TrustedStepHashes ??= new();
            if (stepsHash is null) s.TrustedStepHashes.Remove(key); else s.TrustedStepHashes[key] = stepsHash;
        });
    }

    /// <summary>Takes a caller and shortcut pair off the list.</summary>
    public static void ForgetPair(ISettingsStore settings, string key) =>
        settings.Update(s =>
        {
            s.AllowedCallerShortcuts?.RemoveAll(x => string.Equals(x, key, StringComparison.Ordinal));
            s.TrustedStepHashes?.Remove(key);
        });

    /// <summary>Takes the caller off the always-allowed list.</summary>
    public static void Forget(ISettingsStore settings, string callerId) =>
        settings.Update(s => s.AllowedCallers?.RemoveAll(x => string.Equals(x, callerId, StringComparison.Ordinal)));

    /// <summary>The reply when the shortcut didn't run.</summary>
    public PipeResponse NotRun(CallerVerdict verdict, string? callerName = null) => PipeResponse.Error(CliExit.Declined, verdict switch
    {
        CallerVerdict.TimedOut => $"Not run: nobody answered in {(int)Math.Round(Timeout.TotalSeconds)} seconds",
        CallerVerdict.Busy => $"Not run: {callerName ?? "this app"} already has a request waiting for your answer. Wait for it, then ask again.",
        CallerVerdict.Full => $"Not run: {MaxWaiting} requests are already waiting for your answer. Try again in a moment.",
        _ => "Not run: you chose Don't run",
    });
}
