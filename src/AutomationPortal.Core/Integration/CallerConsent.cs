namespace AutomationPortal.Core;

/// <summary>Caller ids: the short name another app gives when it asks to run a shortcut (<c>ap run --from filesportal</c>).</summary>
public static class Callers
{
    public const int MaxIdLength = 32;

    /// <summary>1 to 32 characters, each a lower-case letter a-z, a digit or a dash.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 1 and <= MaxIdLength } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

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
    public string CallerName => Callers.FriendlyName(CallerId);

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

    /// <summary>Asks unless the caller is always allowed. Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled (the caller left).</summary>
    public async Task<CallerVerdict> RequestAsync(CallerPrompt prompt, CancellationToken ct)
    {
        if (IsAlwaysAllowed(prompt.CallerId)) return CallerVerdict.Allowed;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        try { await _oneAtATime.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return CallerVerdict.TimedOut; }

        try
        {
            // The answer to an earlier prompt may have been Always allow for this caller.
            if (IsAlwaysAllowed(prompt.CallerId)) return CallerVerdict.Allowed;

            CallerDecision decision;
            try { decision = await _ask(prompt, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return CallerVerdict.TimedOut; }

            switch (decision)
            {
                case CallerDecision.AlwaysAllow:
                    Allow(prompt.CallerId);
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

    /// <summary>Takes the caller off the always-allowed list.</summary>
    public static void Forget(ISettingsStore settings, string callerId) =>
        settings.Update(s => s.AllowedCallers?.RemoveAll(x => string.Equals(x, callerId, StringComparison.Ordinal)));

    /// <summary>The reply when the shortcut didn't run.</summary>
    public PipeResponse NotRun(CallerVerdict verdict) => PipeResponse.Error(CliExit.Declined, verdict == CallerVerdict.TimedOut
        ? $"Not run: nobody answered in {(int)Math.Round(Timeout.TotalSeconds)} seconds"
        : "Not run: you chose Don't run");
}
