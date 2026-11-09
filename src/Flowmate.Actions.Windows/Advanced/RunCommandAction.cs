using System.Text;

namespace Flowmate.Actions.Windows.Advanced;

public sealed class RunCommandAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "adv.runCommand", Title = "Run Command", Category = ActionCategories.Advanced, Icon = "WindowConsole20",
        Keywords = new[] { "powershell", "cmd", "command prompt", "script", "terminal", "shell", "batch" },
        Summary = "Run {command} in {shell}",
        Description = "Runs a PowerShell or Command Prompt command and gives back what it printed. For advanced users: a command can change or delete anything your account can, so only run commands you trust. "
            + "In Command Prompt a variable is put in exactly as it is, so put quotes around one that may hold spaces, as you would when typing the command: del \"{File}\". To open a file with start, give it an empty title first: start \"\" \"{File}\".",
        IsAdvanced = true, ImportRisk = ImportRisk.RunsPrograms, OutputKind = ContentKind.Text, OutputName = "Command Output",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("shell", "Run in", "powershell", new Choice("powershell", "PowerShell"), new Choice("cmd", "Command Prompt")),
            ParameterDescriptor.MultilineText("command", "Command") with { Required = true },
            ParameterDescriptor.FolderParam("folder", "Start in this folder"),
            ParameterDescriptor.Duration("timeout", "Stop it after", 60),
            ParameterDescriptor.Bool("failOnError", "Stop the shortcut if the command fails", true),
            ParameterDescriptor.Bool("allowEmpty", "Allow empty variables in the command", false),
            ParameterDescriptor.Bool("allowPatterns", "Allow wildcards and relative paths in variables", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    /// <summary>The arguments that run a PowerShell script without quoting problems (the script travels Base64-encoded).</summary>
    public static string PowerShellArguments(string command)
    {
        var script = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + command;
        return "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    // Variables are data, never code.
    // - PowerShell: each value is written into the script as a self-contained expression that rebuilds the text from its character
    //   codes: $(-join [char[]](72,105)). That expression holds only letters, digits, $ ( ) [ ] , - : and spaces, so it can't close a
    //   quote, start a comment or end a here-string, whatever the scanner decides: in code it is one argument, in a double-quoted string
    //   or here-string it expands, in a comment or an unconverted single-quoted string it stays inert text. Because it carries the value
    //   itself (not a reference to the caller's environment), it also works in script blocks that run elsewhere: Invoke-Command
    //   -ComputerName, Start-Job, $using:. The context scanner below only decides whether a single-quoted string around a variable
    //   has to become a double-quoted one so the expression expands. Text that the script itself runs again as code (iex,
    //   [scriptblock]::Create, powershell -Command '...', cmd /c "...") reads the value as code there: don't build commands that way.
    // - cmd: see the cmd branch of Compose.
    // A variable that resolves to empty text stops the step unless "Allow empty variables" is on: an empty value would silently widen
    // a path such as "D:\Shares\{Folder}" to the whole share. For the same reason a value such as "..", "\", "*" or "..\Other", or a
    // path with a wildcard, stops the step unless "Allow wildcards and relative paths" is on.
    private static async Task<(string Command, Dictionary<string, string>? Env)> ComposeAsync(ActionContext ctx, string shell)
    {
        if (ctx.GetParameter("command") is not TextValue tv || !tv.Parts.Any(p => p.Var is not null))
            return ((await ctx.GetTextAsync("command")).Trim(), null);
        var allowEmpty = await ctx.GetBoolAsync("allowEmpty", false);
        var allowPatterns = await ctx.GetBoolAsync("allowPatterns", false);
        var parts = new List<(string? Literal, string? Value)>();
        var vars = new Dictionary<int, VarRef>();
        var sensitive = new HashSet<int>();
        foreach (var p in tv.Parts)
        {
            if (p.Var is not { } v) { parts.Add((p.Literal, null)); continue; }
            var content = await ctx.ResolveAsync(v).ConfigureAwait(false);
            var value = content.AsText();
            var name = ActionContext.DescribeVariable(v);
            if (string.IsNullOrWhiteSpace(value) && !allowEmpty)
                throw new ActionException($"{name} is empty, so the command wasn't run. Turn on \"Allow empty variables in the command\" if that is expected.");
            if (!allowPatterns && WidensPath(value, content.Items.Any(i => i is PathItem)))
                throw new ActionException($"{name} is '{Shorten(value)}', which could point to a different folder or to many files, so the command wasn't run. Turn on \"Allow wildcards and relative paths in variables\" if that is expected.");
            if (value.Length > MaxValueLength)
                throw new ActionException($"{name} is too long ({value.Length:N0} characters) to hand to a command. Save it to a file first and pass the file's path.");
            if (ctx.IsSensitive(v)) sensitive.Add(parts.Count);
            vars[parts.Count] = v;
            parts.Add((null, value));
        }
        string Describe(int i) => vars.TryGetValue(i, out var v) ? ActionContext.DescribeVariable(v) : "A value";
        if (shell == "cmd")
        {
            var composed = Compose(parts, cmd: true, describe: Describe);
            return (composed.Command.Trim(), composed.Env);
        }
        // Values travel inside the script, so they also work in script blocks that run elsewhere. Values from steps whose output is
        // private (a generated password, the clipboard, what was typed into Ask for Input) travel as environment variables instead,
        // so they never show up in the process's command line or in PowerShell's script log. When the script would be too long for
        // Windows, every value travels that way.
        var inline = Compose(parts, cmd: false, viaEnvironment: sensitive.Contains);
        if (PowerShellArguments(inline.Command.Trim()).Length <= MaxArgumentsLength) return (inline.Command.Trim(), inline.Env);
        var viaEnv = Compose(parts, cmd: false, viaEnvironment: _ => true);
        return (viaEnv.Command.Trim(), viaEnv.Env);
    }

    private static string Shorten(string value) => value.Length > 80 ? value[..80] + "…" : value;

    /// <summary>
    /// True for a value that would widen or move a path it is put into: only dots, slashes, spaces and wildcards ("..", "\", "*"),
    /// a ".." segment ("..\Other"), or a path (a file or folder, or text with a backslash or a drive) holding * or ?.
    /// </summary>
    internal static bool WidensPath(string value, bool isPath)
    {
        var t = value.Trim();
        if (t.Length == 0) return false;
        if (t.All(c => c is '.' or '*' or '?' or '\\' or '/' || char.IsWhiteSpace(c))) return true;
        if (t.Split('\\', '/').Any(s => s.Trim() == "..")) return true;
        var pathLike = isPath || t.Contains('\\') || (t.Length >= 2 && t[1] == ':' && char.IsAsciiLetter(t[0]));
        return pathLike && t.IndexOfAny(new[] { '*', '?' }) >= 0;
    }

    /// <summary>A PowerShell expression that evaluates to <paramref name="value"/> and contains no quote, newline, # or backtick.</summary>
    internal static string PowerShellLiteral(string value) =>
        value.Length == 0 ? "$([string]::Empty)" : "$(-join [char[]](" + string.Join(",", value.Select(c => (int)c)) + "))";

    // Windows limits a whole command line to 32,767 characters, including the program's path, and one environment variable to 32,767.
    private const int MaxArgumentsLength = 32000;
    private const int MaxValueLength = 30000;

    internal const string VarPrefix = "FLOWMATE_VAR_";

    private static readonly char[] PipeUnsafe = { '&', '|', '<', '>', '^', '%' };

    private static string AddEnv(ref Dictionary<string, string>? env, string value)
    {
        if (value.Length > MaxValueLength) throw new ActionException("A value is too long to hand to a command. Shorten it or save it to a file first.");
        env ??= new();
        var name = VarPrefix + (env.Count + 1);
        env[name] = value.Replace("\0", "");
        return name;
    }

    internal static (string Command, Dictionary<string, string>? Env) Compose(IReadOnlyList<(string? Literal, string? Value)> parts, bool cmd,
        Func<int, bool>? viaEnvironment = null, Func<int, string>? describe = null)
    {
        Dictionary<string, string>? env = null;
        var sb = new StringBuilder();
        if (cmd)
        {
            // Each value reaches cmd as an environment variable read with delayed expansion (!FLOWMATE_VAR_n!), placed exactly where
            // the variable is, with no quotes added: put quotes around a variable that may hold spaces, as you would in Command Prompt.
            // Delayed expansion happens after cmd has parsed operators and redirections, so & | < > ^ % in a value stay plain text for
            // cmd itself. A value with a double quote or a line break is refused: those could end the user's quotes or the command.
            // Delayed expansion also removes every ^ and ! it sees, inside quotes too, so a ^ or ! of the command's own that cmd would
            // otherwise keep ("^ERROR" in quotes, ^^ or ^! outside them, any !) is passed the same way as a value.
            // The two sides of a pipe are run by new copies of cmd that read the expanded text again, so there a value may not hold
            // a character those copies would act on.
            string? caret = null, bang = null;
            string Ref(ref string? name, string ch) => "!" + (name ??= AddEnv(ref env, ch)) + "!";
            var inQuotes = false;
            var hasPipe = false;
            for (var index = 0; index < parts.Count; index++)
            {
                var (literal, value) = parts[index];
                if (value is null)
                {
                    var text = literal ?? "";
                    for (var i = 0; i < text.Length; i++)
                    {
                        var c = text[i];
                        if (c == '"') { inQuotes = !inQuotes; sb.Append(c); }
                        else if (c == '!') sb.Append(Ref(ref bang, "!"));
                        else if (c == '^' && inQuotes) sb.Append(Ref(ref caret, "^"));
                        else if (c == '^' && i + 1 < text.Length)
                        {
                            // Outside quotes cmd reads ^x as a plain x before delayed expansion; ^^ is a plain ^ and ^! a plain !.
                            var next = text[++i];
                            if (next == '^') sb.Append(Ref(ref caret, "^"));
                            else if (next == '!') sb.Append(Ref(ref bang, "!"));
                            else sb.Append(c).Append(next);
                        }
                        else
                        {
                            if (c == '|' && !inQuotes && !(i + 1 < text.Length && text[i + 1] == '|') && !(i > 0 && text[i - 1] == '|')) hasPipe = true;
                            sb.Append(c);
                        }
                    }
                    continue;
                }
                // An empty variable would be undefined, and cmd would print !NAME! as it is: an empty value adds nothing.
                if (value.Length == 0) continue;
                var who = describe?.Invoke(index) ?? "A value";
                if (value.Contains('"'))
                    throw new ActionException($"{who} contains a double quote (\"), which Command Prompt can't pass on safely. Use PowerShell for this command instead.");
                if (value.Contains('\n') || value.Contains('\r'))
                    throw new ActionException($"{who} has more than one line, which Command Prompt can't pass on safely. Use PowerShell for this command instead.");
                sb.Append('!').Append(AddEnv(ref env, value)).Append('!');
            }
            if (hasPipe)
                for (var index = 0; index < parts.Count; index++)
                    if (parts[index].Value is { } v && v.IndexOfAny(PipeUnsafe) >= 0)
                        throw new ActionException($"{describe?.Invoke(index) ?? "A value"} contains one of & | < > ^ %, which Command Prompt would act on after the pipe (|). Use PowerShell for this command instead.");
            return (sb.ToString(), env);
        }

        // One flat stream, so the scanner can look ahead across part boundaries. Var holds the expression for a variable item.
        var items = new List<(char C, string? Var)>();
        for (var index = 0; index < parts.Count; index++)
        {
            var (literal, value) = parts[index];
            if (value is null) foreach (var c in literal ?? "") items.Add((c, null));
            else if (viaEnvironment?.Invoke(index) == true) items.Add(('\0', "${env:" + AddEnv(ref env, value) + "}"));
            else items.Add(('\0', PowerShellLiteral(value)));
        }
        var roles = ScanPowerShell(items);
        for (var i = 0; i < items.Count; i++)
        {
            var (c, name) = items[i];
            if (name is not null) { sb.Append(name); continue; }
            switch (roles[i])
            {
                case PsRole.ConvertedQuote: sb.Append('"'); break;
                case PsRole.DroppedQuote: break;
                case PsRole.InConvertedSingle:
                    if (c is '`' or '$' || IsDoubleQuote(c)) sb.Append('`');
                    sb.Append(c);
                    break;
                case PsRole.InConvertedHere:
                    if (c is '`' or '$') sb.Append('`');
                    sb.Append(c);
                    break;
                default: sb.Append(c); break;
            }
        }
        return (sb.ToString(), env);
    }

    private enum PsRole : byte { Keep, ConvertedQuote, DroppedQuote, InConvertedSingle, InConvertedHere }

    private enum PsCtx { Code, Single, Double, HereSingle, HereDouble, LineComment, BlockComment }

    private sealed class QuotedRegion
    {
        public int Open;
        public int Close = -1;
        public bool Here;
        public bool HasVar;
        public readonly List<int> DoubledSecond = new();
    }

    private static bool IsSingleQuote(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';
    private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”' or '„';

    /// <summary>
    /// Works out how each character must be written so that single-quoted strings and @' '@ here-strings holding a variable become
    /// double-quoted ones (and the variable reference expands). Tracks code, both quote kinds, here-strings, # and &lt;# #&gt; comments,
    /// and $( ) inside double-quoted strings.
    /// </summary>
    private static PsRole[] ScanPowerShell(List<(char C, string? Var)> items)
    {
        var roles = new PsRole[items.Count];
        var regions = new List<QuotedRegion>();
        var stack = new Stack<(PsCtx Ctx, int Depth, QuotedRegion? Region)>();
        stack.Push((PsCtx.Code, 0, null));
        bool IsVar(int k) => k >= 0 && k < items.Count && items[k].Var is not null;
        char At(int k) => k >= 0 && k < items.Count && items[k].Var is null ? items[k].C : '\0';
        bool HereOpens(int quote)
        {
            // @' or @" opens a here-string only when nothing but spaces or tabs follows it on the line.
            for (var k = quote + 1; k < items.Count; k++)
            {
                if (IsVar(k)) return false;
                var ch = items[k].C;
                if (ch is '\n' or '\r') return true;
                if (ch is not (' ' or '\t')) return false;
            }
            return false;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var top = stack.Peek();
            if (IsVar(i))
            {
                if (top.Region is { } r) r.HasVar = true;
                continue;
            }
            var c = items[i].C;
            switch (top.Ctx)
            {
                case PsCtx.Code:
                {
                    var prev = At(i - 1);
                    if (c == '`') i++;
                    else if (c == '#' && !IsVar(i - 1) && (i == 0 || char.IsWhiteSpace(prev) || prev is ';' or '|' or '&' or '(' or ')' or '{' or '}' or ','))
                        stack.Push((PsCtx.LineComment, 0, null));
                    else if (c == '<' && At(i + 1) == '#') { stack.Push((PsCtx.BlockComment, 0, null)); i++; }
                    else if (c == '@' && IsSingleQuote(At(i + 1)) && HereOpens(i + 1))
                    {
                        var region = new QuotedRegion { Open = i + 1, Here = true };
                        regions.Add(region);
                        stack.Push((PsCtx.HereSingle, 0, region));
                        i++;
                    }
                    else if (c == '@' && IsDoubleQuote(At(i + 1)) && HereOpens(i + 1)) { stack.Push((PsCtx.HereDouble, 0, null)); i++; }
                    else if (IsSingleQuote(c))
                    {
                        var region = new QuotedRegion { Open = i };
                        regions.Add(region);
                        stack.Push((PsCtx.Single, 0, region));
                    }
                    else if (IsDoubleQuote(c)) stack.Push((PsCtx.Double, 0, null));
                    else if (c == '(') { stack.Pop(); stack.Push((PsCtx.Code, top.Depth + 1, null)); }
                    else if (c == ')')
                    {
                        stack.Pop();
                        if (top.Depth > 0) stack.Push((PsCtx.Code, top.Depth - 1, null));
                        else if (stack.Count == 0) stack.Push((PsCtx.Code, 0, null)); // a stray ')' at the top level
                        // Otherwise this closed a $( ) subexpression and we're back in the enclosing string.
                    }
                    break;
                }
                case PsCtx.Single:
                    if (IsSingleQuote(c))
                    {
                        if (IsSingleQuote(At(i + 1))) { top.Region!.DoubledSecond.Add(i + 1); i++; }
                        else { top.Region!.Close = i; stack.Pop(); }
                    }
                    break;
                case PsCtx.Double:
                case PsCtx.HereDouble:
                    if (c == '`') i++;
                    else if (c == '$' && At(i + 1) == '(') { stack.Push((PsCtx.Code, 0, null)); i++; }
                    else if (top.Ctx == PsCtx.Double && IsDoubleQuote(c))
                    {
                        if (IsDoubleQuote(At(i + 1))) i++;
                        else stack.Pop();
                    }
                    else if (top.Ctx == PsCtx.HereDouble && c == '\n' && IsDoubleQuote(At(i + 1)) && At(i + 2) == '@') { stack.Pop(); i += 2; }
                    break;
                case PsCtx.HereSingle:
                    if (c == '\n' && IsSingleQuote(At(i + 1)) && At(i + 2) == '@')
                    {
                        top.Region!.Close = i + 1;
                        stack.Pop();
                        i += 2;
                    }
                    break;
                case PsCtx.LineComment:
                    if (c is '\n' or '\r') stack.Pop();
                    break;
                case PsCtx.BlockComment:
                    if (c == '#' && At(i + 1) == '>') { stack.Pop(); i++; }
                    break;
            }
        }

        foreach (var r in regions)
        {
            if (!r.HasVar) continue;
            var end = r.Close >= 0 ? r.Close : items.Count;
            roles[r.Open] = PsRole.ConvertedQuote;
            if (r.Close >= 0) roles[r.Close] = PsRole.ConvertedQuote;
            for (var k = r.Open + 1; k < end; k++)
                if (items[k].Var is null) roles[k] = r.Here ? PsRole.InConvertedHere : PsRole.InConvertedSingle;
            foreach (var d in r.DoubledSecond) roles[d] = PsRole.DroppedQuote;
        }
        return roles;
    }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var shell = await ctx.GetChoiceAsync("shell", "powershell");
        var (command, env) = await ComposeAsync(ctx, shell);
        if (command.Length == 0) throw new ActionException("Type the command to run first.");
        if (shell != "cmd" && PowerShellArguments(command).Length > MaxArgumentsLength)
            throw new ActionException("The command and its values are too long to hand to PowerShell. Save long text to a file first and pass the file's path.");
        var folder = await ctx.GetPathAsync("folder");
        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            throw new ActionException($"Couldn't find the folder '{folder}'. Check it exists, or leave it empty.");
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromSeconds(60));
        var failOnError = await ctx.GetBoolAsync("failOnError", true);

        ProcessResult result;
        try
        {
            result = shell == "cmd"
                ? await ProcessRunner.RunAsync("cmd.exe", $"/d {(env is null ? "" : "/v:on ")}/s /c \"{command}\"", timeout, ctx.CancellationToken, null, folder, env).ConfigureAwait(false)
                : await ProcessRunner.RunAsync("powershell.exe", PowerShellArguments(command), timeout, ctx.CancellationToken, new UTF8Encoding(false), folder, env).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ActionException($"Windows couldn't start {(shell == "cmd" ? "Command Prompt" : "PowerShell")}.", ex);
        }

        if (result.TimedOut)
            throw new ActionException($"The command was still running after {(int)timeout.TotalSeconds} seconds, so it was stopped. Raise the time limit if it needs longer.");
        if (result.ExitCode != 0 && failOnError)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (detail.Length > 600) detail = detail[..600] + "…";
            throw new ActionException($"The command failed (exit code {result.ExitCode}).{(detail.Length > 0 ? " " + detail : "")}");
        }
        return ContentValue.Text(result.Output);
    }
}
