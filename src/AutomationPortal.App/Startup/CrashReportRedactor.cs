using System.Text.RegularExpressions;

namespace AutomationPortal.App.Startup;

/// <summary>
/// Takes personal and automation-related text out of crash-report strings before they leave the PC: web addresses, e-mail addresses,
/// file system paths (names become <c>&lt;path&gt;</c>, only a short extension stays), keys and tokens, the Windows user name and the PC name.
/// The Windows folder, Program Files and the app's own folder keep their paths, since they say nothing about the user.
/// Running it again over its own output changes nothing.
/// </summary>
public sealed partial class CrashReportRedactor
{
    public const string UrlToken = "<url>";
    public const string EmailToken = "<email>";
    public const string PathToken = "<path>";
    public const string SecretToken = "<secret>";
    public const string UserToken = "<user>";
    public const string MachineToken = "<machine>";
    public const string AppToken = "<app>";

    private readonly string[] _keptRoots;
    private readonly string? _appDirectory;
    private readonly Regex? _user;
    private readonly Regex? _machine;

    public CrashReportRedactor(string? userName, string? machineName, string? appDirectory = null, IEnumerable<string>? keptRoots = null)
    {
        _appDirectory = Trim(appDirectory);
        _keptRoots = [.. (keptRoots ?? []).Select(Trim).Where(static r => r is { Length: > 3 })!];
        _user = Word(userName);
        _machine = Word(machineName);
    }

    public static CrashReportRedactor ForThisPc(string? appDirectory) =>
        new(Environment.UserName, Environment.MachineName, appDirectory,
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ]);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = UrlPattern().Replace(text, UrlToken);
        result = EmailPattern().Replace(result, EmailToken);
        result = DrivePath().Replace(result, m => RedactPath(m.Value));
        result = UncPath().Replace(result, m => RedactPath(m.Value));
        result = BearerSecret().Replace(result, "Bearer " + SecretToken);
        result = KeyedSecret().Replace(result, "${key}${sep}" + SecretToken);
        result = KnownKey().Replace(result, SecretToken);
        result = LongToken().Replace(result, SecretToken);
        if (_user is not null) result = _user.Replace(result, UserToken);
        if (_machine is not null) result = _machine.Replace(result, MachineToken);
        return result;
    }

    private string RedactPath(string path)
    {
        var flat = path.Replace('/', '\\');
        if (_appDirectory is not null && StartsWithFolder(flat, _appDirectory)) return AppToken + flat[_appDirectory.Length..];
        foreach (var root in _keptRoots)
            if (StartsWithFolder(flat, root)) return path;
        var ext = Extension().Match(path);
        return PathToken + (ext.Success ? ext.Value : "");
    }

    private static bool StartsWithFolder(string path, string folder) =>
        path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && (path.Length == folder.Length || path[folder.Length] == '\\');

    private static string? Trim(string? folder) => string.IsNullOrWhiteSpace(folder) ? null : folder.Trim().TrimEnd('\\', '/').Replace('/', '\\');

    private static Regex? Word(string? word)
    {
        var trimmed = word?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2) return null;
        // Whole words only, and never inside our own placeholders.
        return new Regex(@"(?<![\w<])" + Regex.Escape(trimmed) + @"(?![\w>])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Names may hold spaces, so folders run to the next separator; the file ends at its extension or the next space.
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:[^\\/:*?""<>|\r\n]+[\\/])*(?:[^\\/:*?""<>|\r\n]*?\.[A-Za-z0-9]{1,8}(?![A-Za-z0-9])|[^\\/:*?""<>|\s]*)", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"(?<![\\\w:])\\\\[A-Za-z0-9][^\\/:*?""<>|\r\n]*(?:[\\/](?:[^\\/:*?""<>|\r\n]*?\.[A-Za-z0-9]{1,8}(?![A-Za-z0-9])|[^\\/:*?""<>|\s]*|[^\\/:*?""<>|\r\n]+[\\/])?)*", RegexOptions.CultureInvariant)]
    private static partial Regex UncPath();

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex Extension();

    [GeneratedRegex(@"\b(?:https?|ftp|wss?|file|mailto)://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<key>\b(?:api[_\- ]?key|apikey|access[_\- ]?token|auth[_\- ]?token|token|secret|password|passwd|pwd|authorization)\b)(?<sep>\s*[:=]\s*|\s+)(?!<secret>|Bearer\b)[^\s,;""'&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyedSecret();

    [GeneratedRegex(@"\bBearer\s+(?!<secret>)[A-Za-z0-9._~+/\-]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerSecret();

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{12,}|\b(?:ghp|gho|ghu|ghs|github_pat)_[A-Za-z0-9_]{12,}|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\bAKIA[0-9A-Z]{12,}|\bAIza[0-9A-Za-z_\-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex KnownKey();

    // A long run of letters and digits mixed (a key, a hash); plain long words are left alone.
    [GeneratedRegex(@"\b(?=[A-Za-z0-9_\-]*\d)(?=[A-Za-z0-9_\-]*[A-Za-z])[A-Za-z0-9_\-]{32,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongToken();
}

