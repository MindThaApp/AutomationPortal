using System.Text;

namespace Flowmate.Actions.Windows.Advanced;

public sealed class RunCommandAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "adv.runCommand", Title = "Run Command", Category = ActionCategories.Advanced, Icon = "WindowConsole20",
        Keywords = new[] { "powershell", "cmd", "command prompt", "script", "terminal", "shell", "batch" },
        Summary = "Run {command} in {shell}",
        Description = "Runs a PowerShell or Command Prompt command and gives back what it printed. For advanced users: a command can change or delete anything your account can, so only run commands you trust.",
        IsAdvanced = true, OutputKind = ContentKind.Text, OutputName = "Command Output",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("shell", "Run in", "powershell", new Choice("powershell", "PowerShell"), new Choice("cmd", "Command Prompt")),
            ParameterDescriptor.MultilineText("command", "Command") with { Required = true },
            ParameterDescriptor.FolderParam("folder", "Start in this folder"),
            ParameterDescriptor.Duration("timeout", "Stop it after", 60),
            ParameterDescriptor.Bool("failOnError", "Stop the shortcut if the command fails", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    /// <summary>The arguments that run a PowerShell script without quoting problems (the script travels Base64-encoded).</summary>
    public static string PowerShellArguments(string command)
    {
        var script = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + command;
        return "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    // Variables are data, never code: PowerShell gets them escaped for the quote context they sit in; cmd gets them as environment
    // variables read with delayed expansion (!NAME!), which the parser never re-interprets as operators.
    private static async Task<(string Command, Dictionary<string, string>? Env)> ComposeAsync(ActionContext ctx, string shell)
    {
        if (ctx.GetParameter("command") is not TextValue tv || !tv.Parts.Any(p => p.Var is not null))
            return ((await ctx.GetTextAsync("command")).Trim(), null);
        var parts = new List<(string? Literal, string? Value)>();
        foreach (var p in tv.Parts)
            parts.Add(p.Var is { } v ? (null, (await ctx.ResolveAsync(v).ConfigureAwait(false)).AsText()) : (p.Literal, null));
        var composed = Compose(parts, shell == "cmd");
        return (composed.Command.Trim(), composed.Env);
    }

    internal static (string Command, Dictionary<string, string>? Env) Compose(IReadOnlyList<(string? Literal, string? Value)> parts, bool cmd)
    {
        var sb = new StringBuilder();
        Dictionary<string, string>? env = null;
        var quote = PsQuote.None;
        foreach (var (literal, value) in parts)
        {
            if (value is null)
            {
                var text = literal ?? "";
                if (cmd) text = text.Replace("!", "^^!");
                else quote = ScanPowerShellQuotes(text, quote);
                sb.Append(text);
                continue;
            }
            if (cmd)
            {
                if (value.Length > 30000) throw new ActionException("A value is too long to hand to a command. Shorten it or save it to a file first.");
                env ??= new();
                var name = "FLOWMATE_VAR_" + (env.Count + 1);
                env[name] = value.Replace("\0", "");
                sb.Append('!').Append(name).Append('!');
            }
            else sb.Append(EscapeForPowerShell(value.Replace("\0", ""), quote));
        }
        return (sb.ToString(), env);
    }

    internal enum PsQuote { None, Single, Double }

    private static bool IsSingleQuote(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';
    private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”' or '„';

    private static PsQuote ScanPowerShellQuotes(string text, PsQuote state)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (state)
            {
                case PsQuote.None:
                    if (c == '`') i++;
                    else if (IsSingleQuote(c)) state = PsQuote.Single;
                    else if (IsDoubleQuote(c)) state = PsQuote.Double;
                    break;
                case PsQuote.Single:
                    if (IsSingleQuote(c)) state = PsQuote.None;
                    break;
                case PsQuote.Double:
                    if (c == '`') i++;
                    else if (IsDoubleQuote(c)) state = PsQuote.None;
                    break;
            }
        }
        return state;
    }

    internal static string EscapeForPowerShell(string value, PsQuote context)
    {
        var sb = new StringBuilder();
        if (context == PsQuote.Double)
        {
            foreach (var c in value)
            {
                if (c is '`' or '$' || IsDoubleQuote(c)) sb.Append('`');
                sb.Append(c);
            }
            return sb.ToString();
        }
        // Single-quoted literal: only the quote characters need doubling. Outside quotes the value is wrapped in quotes itself.
        foreach (var c in value)
        {
            sb.Append(c);
            if (IsSingleQuote(c)) sb.Append(c);
        }
        return context == PsQuote.Single ? sb.ToString() : "'" + sb + "'";
    }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var shell = await ctx.GetChoiceAsync("shell", "powershell");
        var (command, env) = await ComposeAsync(ctx, shell);
        if (command.Length == 0) throw new ActionException("Type the command to run first.");
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
                : await ProcessRunner.RunAsync("powershell.exe", PowerShellArguments(command), timeout, ctx.CancellationToken, new UTF8Encoding(false), folder).ConfigureAwait(false);
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
