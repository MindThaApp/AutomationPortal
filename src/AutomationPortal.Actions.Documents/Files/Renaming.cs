using System.Globalization;
using System.Text.RegularExpressions;

namespace AutomationPortal.Actions.Documents;

public sealed record RenameContext(string Path, int Number, DateTime Now, Func<string, DateTime?> DateTaken);

/// <summary>Expands name tokens: {name} {ext} {n} {n:000} {date:yyyy-MM-dd} {created} {modified} {taken} {kind} {folder}.</summary>
public static class NameTokens
{
    private static readonly Regex TokenRx = new(@"\{(?<name>[A-Za-z]+)(?::(?<fmt>[^}]*))?\}", RegexOptions.Compiled);
    private const string DefaultDate = "yyyy-MM-dd";

    public static bool Uses(string pattern, string token) =>
        TokenRx.Matches(pattern).Any(m => m.Groups["name"].Value.Equals(token, StringComparison.OrdinalIgnoreCase));

    public static string Expand(string pattern, RenameContext c)
    {
        return TokenRx.Replace(pattern, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            switch (name)
            {
                case "name": return Path.GetFileNameWithoutExtension(c.Path);
                case "ext": return Path.GetExtension(c.Path).TrimStart('.');
                case "n":
                    try { return c.Number.ToString(string.IsNullOrEmpty(fmt) ? "0" : fmt, CultureInfo.InvariantCulture); }
                    catch (FormatException) { return m.Value; }
                case "kind": return FileKinds.Of(c.Path);
                case "folder": return Path.GetFileName(Path.GetDirectoryName(c.Path)) ?? "";
                case "date": return FormatDate(c.Now, fmt, m.Value);
                case "created": return FormatDate(Safe(() => File.GetCreationTime(c.Path), c.Now), fmt, m.Value);
                case "modified": return FormatDate(Safe(() => File.GetLastWriteTime(c.Path), c.Now), fmt, m.Value);
                case "taken":
                    return FormatDate(c.DateTaken(c.Path) ?? Safe(() => File.GetLastWriteTime(c.Path), c.Now), fmt, m.Value);
                default: return m.Value;
            }
        });
    }

    private static DateTime Safe(Func<DateTime> f, DateTime fallback) { try { return f(); } catch { return fallback; } }

    private static string FormatDate(DateTime d, string? fmt, string original)
    {
        try { return d.ToString(string.IsNullOrEmpty(fmt) ? DefaultDate : fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { return original; }
    }

    /// <summary>Makes text safe for a file name (slashes in date formats etc. become dashes).</summary>
    public static string SafeName(string name)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => bad.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }
}

public sealed record RenameStep(string Source, string Target);

/// <summary>Turns wanted names into a collision-free plan and carries it out without clobbering anything.</summary>
public static class RenamePlanner
{
    /// <summary>
    /// Wanted: (full source path, wanted file name). Two files wanting the same name get " 2", " 3" …; a name that already
    /// belongs to a file outside the batch is never overwritten. Names may be swapped between files of the batch.
    /// </summary>
    public static IReadOnlyList<RenameStep> Plan(IReadOnlyList<(string Source, string WantedName)> wanted)
    {
        var sources = new HashSet<string>(wanted.Select(w => w.Source), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new RenameStep?[wanted.Count];

        // Files that keep their name keep it, no matter what others want.
        for (var i = 0; i < wanted.Count; i++)
        {
            var (src, name) = wanted[i];
            var dir = System.IO.Path.GetDirectoryName(src.TrimEnd('\\', '/'))!;
            if (string.Equals(System.IO.Path.GetFileName(src.TrimEnd('\\', '/')), name, StringComparison.Ordinal))
            {
                result[i] = new RenameStep(src, src);
                taken.Add(src);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (result[i] is not null) continue;
            var (src, name) = wanted[i];
            var dir = System.IO.Path.GetDirectoryName(src.TrimEnd('\\', '/'))!;
            var stem = System.IO.Path.GetFileNameWithoutExtension(name);
            var ext = System.IO.Path.GetExtension(name);
            var candidate = System.IO.Path.Combine(dir, name);
            for (var n = 2; IsBlocked(candidate); n++)
                candidate = System.IO.Path.Combine(dir, $"{stem} {n}{ext}");
            taken.Add(candidate);
            result[i] = new RenameStep(src, candidate);
        }
        return result!;

        bool IsBlocked(string candidate) =>
            taken.Contains(candidate) ||
            ((File.Exists(candidate) || Directory.Exists(candidate)) && !sources.Contains(candidate));
    }

    public static void Execute(IReadOnlyList<RenameStep> plan, CancellationToken ct = default)
    {
        var changes = plan.Where(s => !string.Equals(s.Source, s.Target, StringComparison.Ordinal)).ToList();
        if (changes.Count == 0) return;
        foreach (var c in changes) ProtectedPaths.Ensure(c.Source, "rename");

        var sourceSet = new HashSet<string>(changes.Select(c => c.Source), StringComparer.OrdinalIgnoreCase);
        var needsTemp = changes.Any(c => sourceSet.Contains(c.Target));
        if (!needsTemp)
        {
            foreach (var c in changes) { ct.ThrowIfCancellationRequested(); MovePath(c.Source, c.Target); }
            return;
        }

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var parked = new List<(string Temp, RenameStep Step)>();
        try
        {
            for (var i = 0; i < changes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = changes[i];
                var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(c.Source.TrimEnd('\\', '/'))!, $"~automationportal-{stamp}-{i}.tmp");
                MovePath(c.Source, temp);
                parked.Add((temp, c));
            }
            foreach (var (temp, step) in parked.ToList())
            {
                MovePath(temp, step.Target);
                parked.Remove((temp, step));
            }
        }
        catch
        {
            foreach (var (temp, step) in parked)
            {
                try { MovePath(temp, step.Source); } catch { /* best effort rollback */ }
            }
            throw;
        }
    }

    public static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }
}
