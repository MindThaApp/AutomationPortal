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

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var command = (await ctx.GetTextAsync("command")).Trim();
        if (command.Length == 0) throw new ActionException("Type the command to run first.");
        var shell = await ctx.GetChoiceAsync("shell", "powershell");
        var folder = await ctx.GetPathAsync("folder");
        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            throw new ActionException($"Couldn't find the folder '{folder}'. Check it exists, or leave it empty.");
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromSeconds(60));
        var failOnError = await ctx.GetBoolAsync("failOnError", true);

        ProcessResult result;
        try
        {
            result = shell == "cmd"
                ? await ProcessRunner.RunAsync("cmd.exe", $"/d /s /c \"{command}\"", timeout, ctx.CancellationToken, null, folder).ConfigureAwait(false)
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
