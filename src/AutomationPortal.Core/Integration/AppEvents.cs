using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>
/// Something that happened in another app, sent over the pipe with kind "event" (for example <c>deskpal.focus.on</c>).
/// Events only feed "When another app sends an event" triggers: they never run a shortcut directly and never show anything.
/// </summary>
public sealed class AppEvent
{
    /// <summary>The full event name, <c>&lt;app&gt;.&lt;event&gt;</c>, for example <c>deskpal.break.started</c>.</summary>
    public required string Name { get; init; }
    /// <summary>The validated payload (a JSON object as text), or null when the event came without one.</summary>
    public string? Payload { get; init; }
    /// <summary>Who says they sent it. Advisory only, like <see cref="PipeRequest.Source"/>.</summary>
    public string? Source { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>The part of <see cref="Name"/> before the first dot: <c>deskpal</c>.</summary>
    public string App => Name[..Name.IndexOf('.')];
    /// <summary>The part of <see cref="Name"/> after the first dot: <c>break.started</c>.</summary>
    public string Event => Name[(Name.IndexOf('.') + 1)..];
}

/// <summary>Checks pipe requests of kind "event" (see "Pipe contract v1" in docs/CLI.md).</summary>
public static class AppEvents
{
    /// <summary>The longest event name accepted.</summary>
    public const int MaxNameLength = 100;
    /// <summary>The largest payload accepted, in characters.</summary>
    public const int MaxPayloadChars = 64 * 1024;
    private const int MaxDepth = 16;

    /// <summary>
    /// True when <paramref name="name"/> is <c>&lt;app&gt;.&lt;event&gt;</c>: lower-case letters, digits, '.', '_' and '-', at most
    /// <see cref="MaxNameLength"/> characters, with no empty parts.
    /// </summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        foreach (var c in name)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')) return false;
        var parts = name.Split('.');
        return parts.Length >= 2 && parts.All(p => p.Length > 0);
    }

    /// <summary>Checks the request and builds the event. On failure, <paramref name="problem"/> is the reply to send (exit code and message).</summary>
    public static bool TryParse(PipeRequest request, DateTimeOffset now, out AppEvent? appEvent, out PipeResponse? problem)
    {
        appEvent = null;
        problem = null;
        var name = request.Name?.Trim();
        if (!IsValidName(name))
        {
            problem = PipeResponse.Error(CliExit.Usage, string.IsNullOrEmpty(name)
                ? "An event needs a name, for example \"deskpal.focus.on\"."
                : $"\"{Short(name)}\" isn't a valid event name. Use <app>.<event> with lower-case letters, digits, '.', '_' and '-' (up to {MaxNameLength} characters).");
            return false;
        }

        string? payload = null;
        if (!string.IsNullOrWhiteSpace(request.Input))
        {
            if (request.Input.Length > MaxPayloadChars)
            {
                problem = PipeResponse.Error(CliExit.Problem, $"That event's details are too large. Automation Portal accepts up to {MaxPayloadChars / 1024} KB per event.");
                return false;
            }
            try
            {
                using var doc = JsonDocument.Parse(request.Input, new JsonDocumentOptions { MaxDepth = MaxDepth });
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    problem = PipeResponse.Error(CliExit.Usage, "An event's input must be a JSON object.");
                    return false;
                }
            }
            catch (JsonException)
            {
                problem = PipeResponse.Error(CliExit.Usage, "An event's input must be a JSON object.");
                return false;
            }
            payload = request.Input;
        }

        appEvent = new AppEvent { Name = name!, Payload = payload, Source = request.Source, ReceivedAt = now };
        return true;
    }

    private static string Short(string? s) => s is null ? "" : s.Length <= 60 ? s : s[..60] + "…";
}
