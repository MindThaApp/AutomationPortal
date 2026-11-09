using System.Security.Cryptography;
using System.Text;

namespace Flowmate.Actions.Documents;

public static class FileCompare
{
    public const long MaxDiffBytes = 1024 * 1024;

    public static async Task<bool> AreSameAsync(string a, string b, CancellationToken ct = default)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
        await using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var ba = new byte[81920];
        var bb = new byte[81920];
        while (true)
        {
            var n = await ReadFullAsync(fa, ba, ct).ConfigureAwait(false);
            var m = await ReadFullAsync(fb, bb, ct).ConfigureAwait(false);
            if (n != m || !ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, m))) return false;
            if (n == 0) return true;
        }
    }

    private static async Task<int> ReadFullAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    /// <summary>A simple line diff: removed lines as "- 3: text", added lines as "+ 3: text" (numbers are line numbers in each file).</summary>
    public static string DiffText(string a, string b)
    {
        foreach (var p in new[] { a, b })
        {
            var len = new FileInfo(p).Length;
            if (len > MaxDiffBytes)
                throw new ActionException($"'{Path.GetFileName(p)}' is too big to show differences ({len / 1024 / 1024.0:0.#} MB; the limit is 1 MB). Use \"Are they the same?\" instead.");
            if (LooksBinary(p))
                throw new ActionException($"'{Path.GetFileName(p)}' isn't a text file, so differences can't be shown. Use \"Are they the same?\" instead.");
        }
        var la = SplitLines(File.ReadAllText(a));
        var lb = SplitLines(File.ReadAllText(b));

        var start = 0;
        while (start < la.Length && start < lb.Length && la[start] == lb[start]) start++;
        var endA = la.Length;
        var endB = lb.Length;
        while (endA > start && endB > start && la[endA - 1] == lb[endB - 1]) { endA--; endB--; }

        var sb = new StringBuilder();
        var n = endA - start;
        var m = endB - start;
        if (n == 0 && m == 0) return "The files have the same text.";

        if ((long)n * m > 25_000_000)
        {
            for (var i = start; i < endA; i++) sb.Append("- ").Append(i + 1).Append(": ").AppendLine(la[i]);
            for (var j = start; j < endB; j++) sb.Append("+ ").Append(j + 1).Append(": ").AppendLine(lb[j]);
            return sb.ToString().TrimEnd();
        }

        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = la[start + i] == lb[start + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && la[start + x] == lb[start + y]) { x++; y++; }
            else if (y >= m || (x < n && lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                sb.Append("- ").Append(start + x + 1).Append(": ").AppendLine(la[start + x]);
                x++;
            }
            else
            {
                sb.Append("+ ").Append(start + y + 1).Append(": ").AppendLine(lb[start + y]);
                y++;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string[] SplitLines(string text) =>
        text.Length == 0 ? Array.Empty<string>() : text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private static bool LooksBinary(string path)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[8000];
        var n = fs.Read(buf, 0, buf.Length);
        return buf.AsSpan(0, n).Contains((byte)0);
    }
}

public static class FileHasher
{
    public static async Task<string> HashAsync(string path, string algorithm, CancellationToken ct = default)
    {
        using HashAlgorithm h = algorithm switch
        {
            "md5" => MD5.Create(),
            "sha1" => SHA1.Create(),
            "sha512" => SHA512.Create(),
            _ => SHA256.Create(),
        };
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan | FileOptions.Asynchronous);
        return Convert.ToHexStringLower(await h.ComputeHashAsync(fs, ct).ConfigureAwait(false));
    }
}

public sealed class CompareFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.compare", Title = "Compare Files", Category = ActionCategories.FilesAndFolders, Icon = "DocumentSplitHint24",
        Keywords = new[] { "diff", "same", "equal", "identical", "difference" },
        Summary = "Compare {first} with {second}",
        Description = "Checks whether two files are the same, or shows which lines of text differ. For the second one you can choose a folder: the file with the same name in that folder is used.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Any, OutputName = "Comparison",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("first", "File", isInput: true) with { Required = true },
            ParameterDescriptor.FileParam("second", "Compare with") with { Required = true, Hint = "A file, or a folder holding a file with the same name." },
            ParameterDescriptor.ChoiceOf("mode", "Result", "same",
                new Choice("same", "Are they the same?"), new Choice("diff", "Show differences (text)")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var first = (await FileParams.RequireFilesAsync(ctx, "first", false))[0];
        var secondItems = await ctx.GetFilesAsync("second", true);
        if (secondItems.Count == 0) throw new ActionException("Choose the file to compare with.");
        var second = secondItems[0].Path;
        if (Directory.Exists(second))
        {
            var match = Path.Combine(second, first.FullName);
            if (!File.Exists(match)) throw new ActionException($"The folder '{second}' has no file called '{first.FullName}' to compare with.");
            second = match;
        }
        else if (!File.Exists(second))
            throw new ActionException($"Couldn't find '{second}'. Check it still exists.");

        try
        {
            if (await ctx.GetChoiceAsync("mode", "same") == "diff")
                return ContentValue.Text(await Task.Run(() => FileCompare.DiffText(first.Path, second), ctx.CancellationToken));
            return ContentValue.Bool(await FileCompare.AreSameAsync(first.Path, second, ctx.CancellationToken));
        }
        catch (IOException ex) { throw new ActionException($"Couldn't read the files to compare them. ({ex.Message})", ex); }
        catch (UnauthorizedAccessException ex) { throw new ActionException("Flow Mate isn't allowed to read one of these files.", ex); }
    }
}

public sealed class GetFileHashAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.hash", Title = "Get File Hash", Category = ActionCategories.FilesAndFolders, Icon = "Fingerprint24",
        Keywords = new[] { "checksum", "md5", "sha", "sha256", "verify", "integrity" },
        Summary = "Get the {algorithm} hash of {files}",
        Description = "Works out a checksum for each file, as lowercase hexadecimal text. Useful to check a download or to see whether two files are the same.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "File Hash",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("algorithm", "Algorithm", "sha256",
                new Choice("md5", "MD5"), new Choice("sha1", "SHA-1"), new Choice("sha256", "SHA-256"), new Choice("sha512", "SHA-512")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", false);
        var algorithm = await ctx.GetChoiceAsync("algorithm", "sha256");
        var hashes = new List<string>();
        try
        {
            foreach (var f in files)
                hashes.Add(await FileHasher.HashAsync(f.Path, algorithm, ctx.CancellationToken).ConfigureAwait(false));
        }
        catch (IOException ex) { throw new ActionException($"Couldn't read the file to hash it. ({ex.Message})", ex); }
        catch (UnauthorizedAccessException ex) { throw new ActionException("Flow Mate isn't allowed to read one of these files.", ex); }
        return ContentValue.Texts(hashes);
    }
}
