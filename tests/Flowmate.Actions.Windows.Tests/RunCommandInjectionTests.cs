using Flowmate.Actions.Windows.Advanced;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Windows.Tests;

public class RunCommandInjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public RunCommandInjectionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private Task<string> RunWith(string shell, string value, params object[] commandParts) => RunWith(shell, value, false, commandParts);

    private async Task<string> RunWith(string shell, string value, bool allowEmpty, params object[] commandParts)
    {
        var r = await Run(shell, value, allowEmpty, commandParts);
        Assert.True(r.Status == RunStatus.Succeeded, r.ErrorMessage);
        return r.Output.AsText();
    }

    private async Task<RunResult> Run(string shell, string value, bool allowEmpty, params object[] commandParts)
    {
        using var host = new TestHost().With(typeof(RunCommandAction).Assembly);
        var b = new ShortcutBuilder("T");
        var t = b.Add("content.text", ("text", value));
        var cmdParts = commandParts.Select(p => p is string s && s == "<VAR>" ? (object)VarRef.Output(t) : p).ToArray();
        b.Add("adv.runCommand", ("shell", ParamValue.Choice(shell)), ("command", ParamValue.Text(cmdParts)), ("folder", ParamValue.File(_dir)),
            ("allowEmpty", ParamValue.Bool(allowEmpty)));
        return await host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
    }

    [Theory]
    [InlineData("a$(New-Item -ItemType File pwned1)b")]
    [InlineData("x\"; New-Item -ItemType File pwned1; \"")]
    [InlineData("it's $(New-Item -ItemType File pwned1) \u2019 `")]
    public async Task PowerShell_variables_are_data_in_every_quote_context(string value)
    {
        Assert.Equal(value, await RunWith("powershell", value, "Write-Output \"", "<VAR>", "\""));
        Assert.Equal(value, await RunWith("powershell", value, "Write-Output '", "<VAR>", "'"));
        Assert.Equal(value, await RunWith("powershell", value, "Write-Output ", "<VAR>"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    // A file name that tries to break out of a single-quoted string ('; calc; ' with a harmless payload instead of calc).
    private const string BreakOut = "x'; New-Item -ItemType File pwned1; 'y";
    private const string Semicolon = "a; New-Item -ItemType File pwned1; #.txt";

    [Theory]
    [InlineData(BreakOut)]
    [InlineData(Semicolon)]
    [InlineData("a`$(New-Item -ItemType File pwned1)`\"b")]
    public async Task PowerShell_comments_never_change_how_a_variable_is_read(string value)
    {
        // An apostrophe in a comment used to leave the quote tracker "inside" a single-quoted string.
        Assert.Equal(value, await RunWith("powershell", value, "# back up the user's file\nWrite-Output ", "<VAR>"));
        // An unbalanced double quote in a comment.
        Assert.Equal(value, await RunWith("powershell", value, "# 5\" screen\nWrite-Output ", "<VAR>"));
        // Block comments with quotes in them.
        Assert.Equal(value, await RunWith("powershell", value, "<# it's a \"test\" #>Write-Output ", "<VAR>"));
        Assert.Equal(value, await RunWith("powershell", value, "<# it's #>\nWrite-Output '", "<VAR>", "'"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Theory]
    [InlineData(BreakOut)]
    [InlineData(Semicolon)]
    [InlineData("'@ \"@ $(New-Item -ItemType File pwned1)")]
    public async Task PowerShell_here_strings_hold_variables_as_data(string value)
    {
        Assert.Equal("hi " + value, await RunWith("powershell", value, "$x = @'\nhi ", "<VAR>", "\n'@\nWrite-Output $x"));
        Assert.Equal("hi " + value, await RunWith("powershell", value, "$x = @\"\nhi ", "<VAR>", "\n\"@\nWrite-Output $x"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Theory]
    [InlineData(BreakOut)]
    [InlineData(Semicolon)]
    [InlineData("a`b$(New-Item -ItemType File pwned1)")]
    public async Task PowerShell_subexpressions_and_backticks_hold_variables_as_data(string value)
    {
        Assert.Equal(value, await RunWith("powershell", value, "Write-Output \"$(", "<VAR>", ")\""));
        Assert.Equal("[" + value + "]", await RunWith("powershell", value, "Write-Output \"$('[' + ", "<VAR>", " + ']')\""));
        Assert.Equal("x\"y " + value, await RunWith("powershell", value, "Write-Output \"x`\"y ", "<VAR>", "\""));
        Assert.Equal("it's " + value + "!", await RunWith("powershell", value, "Write-Output 'it''s ", "<VAR>", "!'"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Fact]
    public void PowerShell_values_never_appear_in_the_script_text()
    {
        var (command, env) = RunCommandAction.Compose(new (string?, string?)[]
        {
            ("# the user's file\nCopy-Item ", null), (null, BreakOut), (" '", null), (null, Semicolon), ("' \"$(", null), (null, "z"), (")\"", null),
        }, cmd: false);
        Assert.DoesNotContain("pwned1", command);
        Assert.Null(env);
        Assert.Equal("# the user's file\nCopy-Item " + RunCommandAction.PowerShellLiteral(BreakOut) + " \"" + RunCommandAction.PowerShellLiteral(Semicolon)
            + "\" \"$(" + RunCommandAction.PowerShellLiteral("z") + ")\"", command);
        // The expression for a value holds nothing that could end a string, a comment or a here-string.
        foreach (var value in new[] { BreakOut, Semicolon, "'@\n\"@ #`$’”", "" })
        {
            var literal = RunCommandAction.PowerShellLiteral(value);
            Assert.DoesNotContain(literal, c => c is '\'' or '"' or '`' or '#' or '\n' or '\r' or ';' or '@' || c > 127);
        }
    }

    [Theory]
    [InlineData(BreakOut)]
    [InlineData("a$(New-Item -ItemType File pwned1)\"b")]
    public async Task PowerShell_variables_keep_their_value_in_script_blocks_that_run_elsewhere(string value)
    {
        // Start-Job runs the block in another process from its text; a remote or elevated session would do the same. The value must
        // travel with the text, not depend on this process's environment.
        Assert.Equal("x" + value, await RunWith("powershell", value,
            "Start-Job { [Environment]::GetEnvironmentVariables().Keys | Where-Object { $_ -like 'FLOWMATE_VAR_*' } | ForEach-Object { Remove-Item \"Env:$_\" }; Write-Output 'x",
            "<VAR>", "' } | Receive-Job -Wait -AutoRemoveJob"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("cmd")]
    public async Task An_empty_variable_stops_the_command_unless_allowed(string shell)
    {
        var r = await Run(shell, "", false, "echo x", "<VAR>", "y");
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("is empty", r.ErrorMessage);
        Assert.Equal("xy", (await RunWith(shell, "", true, "echo x", "<VAR>", "y")).Trim());
    }

    [Theory]
    [InlineData("x & echo.>pwned1 & y")]
    [InlineData("x'; echo.>pwned1; 'y")]
    [InlineData("100% | echo.>pwned1")]
    [InlineData("x ^& echo.>pwned1 !PATH!")]
    public async Task Cmd_variables_are_data(string value)
    {
        // A value is put in exactly where the variable is, with no quotes added.
        Assert.Equal(value, await RunWith("cmd", value, "echo ", "<VAR>"));
        Assert.Equal("[\"" + value + "\"]", await RunWith("cmd", value, "echo [\"", "<VAR>", "\"]"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Fact]
    public async Task Cmd_values_in_the_users_quotes_stay_one_argument()
    {
        // "del "{File}"" with a file named "a b.txt" must not delete "a" and "b.txt".
        File.WriteAllText(Path.Combine(_dir, "a"), "keep");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "keep");
        File.WriteAllText(Path.Combine(_dir, "a b.txt"), "target");
        await RunWith("cmd", "a b.txt", "del \"", "<VAR>", "\"");
        Assert.True(File.Exists(Path.Combine(_dir, "a")));
        Assert.True(File.Exists(Path.Combine(_dir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "a b.txt")));
    }

    [Fact]
    public async Task Cmd_values_reach_external_programs_as_part_of_the_users_word()
    {
        // findstr.exe splits its arguments the C runtime way, where \" is an escaped quote: a folder\{Name} path must arrive whole.
        File.WriteAllText(Path.Combine(_dir, "song.wav"), "x marks\r\n");
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x " + _dir + "\\", "<VAR>"));
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x \"" + _dir + "\\", "<VAR>", "\""));
    }

    [Fact]
    public async Task Cmd_keeps_the_commands_own_carets_and_exclamation_marks()
    {
        Assert.Equal("hi! \"wow!\" v", await RunWith("cmd", "v", "echo hi! \"wow!\" ", "<VAR>"));
        Assert.Equal("\"^a\" a^b c! a&b v", await RunWith("cmd", "v", "echo \"^a\" a^^b c^! a^&b ", "<VAR>"));
        // A regular expression anchor in quotes: only the line that starts with ERROR matches.
        File.WriteAllText(Path.Combine(_dir, "log.txt"), "ERROR one\r\nan ERROR two\r\n");
        Assert.Equal("ERROR one", await RunWith("cmd", "log.txt", "findstr /r \"^ERROR\" ", "<VAR>"));
    }

    [Theory]
    [InlineData("a\" & echo.>pwned1 & \"b", "double quote")]
    [InlineData("a\r\necho.>pwned1", "more than one line")]
    [InlineData("a\necho.>pwned1", "more than one line")]
    public async Task Cmd_refuses_values_with_a_double_quote_or_a_line_break(string value, string message)
    {
        var r = await Run("cmd", value, false, "echo ", "<VAR>");
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains(message, r.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Fact]
    public async Task Cmd_refuses_values_that_a_pipe_would_run()
    {
        // Each side of a pipe is read again by a new copy of cmd, so there & would start a new command.
        var r = await Run("cmd", "a & echo.>pwned1", false, "echo ", "<VAR>", " | sort");
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("pipe", r.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
        Assert.Equal("abc", (await RunWith("cmd", "abc", "echo ", "<VAR>", "| sort")).Trim());
        // || is not a pipe.
        Assert.Equal("a & b", (await RunWith("cmd", "a & b", "echo ", "<VAR>", " || echo no")).Trim());
    }

    [Theory]
    [InlineData("cmd", ".")]
    [InlineData("cmd", "..")]
    [InlineData("cmd", "\\")]
    [InlineData("cmd", "*")]
    [InlineData("cmd", "*.*")]
    [InlineData("cmd", "..\\Other")]
    [InlineData("cmd", "C:\\Shares\\*")]
    [InlineData("powershell", "..")]
    [InlineData("powershell", "?")]
    public async Task Values_that_widen_a_path_stop_the_command_unless_allowed(string shell, string value)
    {
        var r = await Run(shell, value, false, "echo \"Q:\\Shares\\", "<VAR>", "\"");
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("Allow wildcards and relative paths", r.ErrorMessage);
        Assert.Equal(value, await RunAllowingPatterns(shell, value, "echo ", "<VAR>"));
    }

    [Theory]
    [InlineData("Loading...")]
    [InlineData("Why? Because.")]
    [InlineData("5 * 3")]
    [InlineData("https://example.com/a?b=1")]
    public async Task Ordinary_text_with_dots_or_wildcard_characters_is_not_refused(string value) =>
        Assert.Equal(value, await RunWith("cmd", value, "echo ", "<VAR>"));

    private async Task<string> RunAllowingPatterns(string shell, string value, params object[] commandParts)
    {
        using var host = new TestHost().With(typeof(RunCommandAction).Assembly);
        var b = new ShortcutBuilder("T");
        var t = b.Add("content.text", ("text", value));
        var cmdParts = commandParts.Select(p => p is string s && s == "<VAR>" ? (object)VarRef.Output(t) : p).ToArray();
        b.Add("adv.runCommand", ("shell", ParamValue.Choice(shell)), ("command", ParamValue.Text(cmdParts)), ("folder", ParamValue.File(_dir)),
            ("allowPatterns", ParamValue.Bool(true)));
        var r = await host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.True(r.Status == RunStatus.Succeeded, r.ErrorMessage);
        return r.Output.AsText().Trim();
    }

    [Fact]
    public async Task PowerShell_values_too_long_for_the_command_line_travel_as_environment_variables()
    {
        var value = new string('a', 9000) + "'; New-Item -ItemType File pwned1; '" + new string('\u3042', 3000);
        Assert.Equal(value.Length.ToString(), (await RunWith("powershell", value, "$x = '", "<VAR>", "'; Write-Output $x.Length")).Trim());
        Assert.Equal(value, await RunWith("powershell", value, "Write-Output ", "<VAR>"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));

        var r = await Run("powershell", new string('a', 31000), false, "Write-Output ", "<VAR>");
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("too long", r.ErrorMessage);
        Assert.Contains("output of an earlier step", r.ErrorMessage);
    }

    private sealed class SecretText : ShortcutAction
    {
        public override ActionDescriptor Descriptor { get; } = new()
        {
            Id = "test.secret", Title = "Secret", Category = ActionCategories.Text, SensitiveOutput = true, OutputKind = ContentKind.Text,
        };
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Text("hunter2'; New-Item -ItemType File pwned1; '"));
    }

    [Fact]
    public async Task PowerShell_private_values_never_appear_in_the_command_line()
    {
        using var host = new TestHost().With(typeof(RunCommandAction).Assembly);
        host.Registry.Register(new SecretText());
        var b = new ShortcutBuilder("T");
        var secret = b.Add("test.secret");
        b.Add("adv.runCommand", ("shell", ParamValue.Choice("powershell")), ("folder", ParamValue.File(_dir)),
            ("command", ParamValue.Text("Write-Output '", VarRef.Output(secret), "'; (Get-CimInstance Win32_Process -Filter \"ProcessId=$PID\").CommandLine")));
        var r = await host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.True(r.Status == RunStatus.Succeeded, r.ErrorMessage);
        var lines = r.Output.AsText().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("hunter2'; New-Item -ItemType File pwned1; '", lines[0]);
        var commandLine = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(lines[1][(lines[1].LastIndexOf(' ') + 1)..]));
        Assert.DoesNotContain(string.Join(",", "hunter2".Select(c => (int)c)), commandLine);
        Assert.Contains("${env:" + RunCommandAction.VarPrefix, commandLine);
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Fact]
    public void Cmd_compose_puts_values_in_place_without_quotes()
    {
        var (command, env) = RunCommandAction.Compose(new (string?, string?)[] { ("copy C:\\In\\", null), (null, "a b"), (" \"E:\\", null), (null, "x"), ("\"", null) }, cmd: true);
        Assert.Equal("copy C:\\In\\!FLOWMATE_VAR_1! \"E:\\!FLOWMATE_VAR_2!\"", command);
        Assert.Equal("a b", env!["FLOWMATE_VAR_1"]);
    }
}
