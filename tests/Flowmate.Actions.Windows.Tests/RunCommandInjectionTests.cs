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

    [Fact]
    public async Task PowerShell_a_converted_here_string_keeps_lines_that_start_with_a_double_quote()
    {
        var output = await RunWith("powershell", "v", "$x = @'\nhi ", "<VAR>", "\n\"@ not the end\n'@\nWrite-Output $x");
        Assert.StartsWith("hi v", output);
        Assert.EndsWith("\"@ not the end", output.TrimEnd());
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
        // In Command Prompt the word around a variable outside quotes is quoted.
        Assert.Equal(shell == "cmd" ? "\"xy\"" : "xy", (await RunWith(shell, "", true, "echo x", "<VAR>", "y")).Trim());
    }

    [Theory]
    [InlineData("a&calc&.txt")]
    [InlineData("x & echo.>pwned1 & y")]
    [InlineData("100% | echo.>pwned1")]
    [InlineData("x ^& echo.>pwned1 !PATH!")]
    [InlineData("a < b > pwned1")]
    [InlineData("Report (1).pdf")]
    [InlineData("a\" & echo.>pwned1 & \"b")]
    [InlineData("a\r\necho.>pwned1")]
    [InlineData("a\necho.>pwned1")]
    public async Task Cmd_refuses_values_with_characters_it_would_act_on(string value)
    {
        foreach (var command in new[]
        {
            new object[] { "echo ", "<VAR>" },
            new object[] { "echo \"", "<VAR>", "\"" },
            new object[] { "for /f \"delims=\" %a in ('certutil -hashfile ", "<VAR>", " SHA256') do @echo %a" },
            new object[] { "cmd /c type \"", "<VAR>", "\"" },
            new object[] { "echo ", "<VAR>", " | sort" },
        })
        {
            var r = await Run("cmd", value, false, command);
            Assert.Equal(RunStatus.Failed, r.Status);
            Assert.Contains("Run in", r.ErrorMessage);
            Assert.Contains("PowerShell", r.ErrorMessage);
        }
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Fact]
    public async Task Cmd_values_stay_one_argument_with_or_without_the_users_quotes()
    {
        // "del {File}" with a file named "a b.txt" must not delete "a" and "b.txt".
        File.WriteAllText(Path.Combine(_dir, "a"), "keep");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "keep");
        File.WriteAllText(Path.Combine(_dir, "a b.txt"), "target");
        File.WriteAllText(Path.Combine(_dir, "c d.txt"), "target");
        await RunWith("cmd", "a b.txt", "del \"", "<VAR>", "\"");
        await RunWith("cmd", "c d.txt", "del ", "<VAR>");
        Assert.True(File.Exists(Path.Combine(_dir, "a")));
        Assert.True(File.Exists(Path.Combine(_dir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "a b.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "c d.txt")));
    }

    [Fact]
    public async Task Cmd_rd_on_a_folder_with_a_space_removes_only_that_folder()
    {
        foreach (var name in new[] { "Old", "Projects", "Old Projects" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, name));
            File.WriteAllText(Path.Combine(_dir, name, "f.txt"), "x");
        }
        await RunWith("cmd", Path.Combine(_dir, "Old Projects"), "rd /s /q ", "<VAR>");
        Assert.False(Directory.Exists(Path.Combine(_dir, "Old Projects")));
        Assert.True(File.Exists(Path.Combine(_dir, "Old", "f.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Projects", "f.txt")));
        // The folder as part of a longer path, with the user's own quotes elsewhere in the word.
        Directory.CreateDirectory(Path.Combine(_dir, "Old Projects", "sub"));
        await RunWith("cmd", "Old Projects", "rd /s /q \"" + _dir + "\"\\", "<VAR>", "\\sub");
        Assert.False(Directory.Exists(Path.Combine(_dir, "Old Projects", "sub")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "Old Projects")));
        Assert.True(File.Exists(Path.Combine(_dir, "Old", "f.txt")));
    }

    [Fact]
    public async Task Cmd_values_work_in_for_f_cmd_c_and_escaped_pipes()
    {
        File.WriteAllText(Path.Combine(_dir, "a b.txt"), "hello\r\nxyz\r\n");
        Assert.Equal("hello", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('findstr hello ", "<VAR>", "') do @echo %a")).Trim());
        Assert.Equal("hello", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('type ", "<VAR>", "') do @echo %a")).Split('\n')[0].Trim());
        Assert.Equal("xyz", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('type ", "<VAR>", " ^| findstr y') do @echo %a")).Trim());
        Assert.StartsWith("hello", (await RunWith("cmd", "a b.txt", "cmd /c type ", "<VAR>")).Trim());
        Assert.Equal("xyz", (await RunWith("cmd", "a b.txt", "type ", "<VAR>", " | findstr y")).Trim());
    }

    [Fact]
    public async Task Cmd_values_reach_external_programs_as_part_of_the_users_word()
    {
        // findstr.exe splits its arguments the C runtime way, where \" is an escaped quote: a folder\{Name} path must arrive whole.
        File.WriteAllText(Path.Combine(_dir, "song.wav"), "x marks\r\n");
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x " + _dir + "\\", "<VAR>"));
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x \"" + _dir + "\\", "<VAR>", "\""));
        // A folder ending in \ keeps its closing quote.
        Assert.EndsWith("song.wav", (await RunWith("cmd", _dir + "\\", "where /r ", "<VAR>", " song.wav")).Trim());
    }

    [Fact]
    public async Task Cmd_keeps_the_commands_own_carets_and_exclamation_marks()
    {
        Assert.Equal("hi! \"wow!\" \"v\"", await RunWith("cmd", "v", "echo hi! \"wow!\" ", "<VAR>"));
        Assert.Equal("\"^a\" a^b c! a&b \"v\"", await RunWith("cmd", "v", "echo \"^a\" a^^b c^! a^&b ", "<VAR>"));
        // A regular expression anchor in quotes: only the line that starts with ERROR matches.
        File.WriteAllText(Path.Combine(_dir, "log.txt"), "ERROR one\r\nan ERROR two\r\n");
        Assert.Equal("ERROR one", await RunWith("cmd", "log.txt", "findstr /r \"^ERROR\" ", "<VAR>"));
    }

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
        var expected = shell != "cmd" ? value : "\"" + value + (value.EndsWith('\\') ? "\\" : "") + "\"";
        Assert.Equal(expected, await RunAllowingPatterns(shell, value, "echo ", "<VAR>"));
    }

    [Theory]
    [InlineData("Loading...")]
    [InlineData("Why? Because.")]
    [InlineData("5 * 3")]
    [InlineData("https://example.com/a?b=1")]
    public async Task Ordinary_text_with_dots_or_wildcard_characters_is_not_refused(string value) =>
        Assert.Equal("\"" + value + "\"", await RunWith("cmd", value, "echo ", "<VAR>"));

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
    public void Cmd_compose_quotes_the_whole_word_around_a_variable_outside_quotes()
    {
        static string Compose(params (string?, string?)[] parts) => RunCommandAction.Compose(parts, cmd: true).Command;
        var (command, env) = RunCommandAction.Compose(new (string?, string?)[] { ("copy C:\\In\\", null), (null, "a b"), (" \"E:\\", null), (null, "x"), ("\"", null) }, cmd: true);
        Assert.Equal("copy \"C:\\In\\a b\" \"E:\\x\"", command);
        Assert.Null(env);
        Assert.Equal("rd /s /q \"E:\\\\\"", Compose(("rd /s /q ", null), (null, "E:\\")));
        Assert.Equal("rd /s /q \"C:\\Users\\me\\Old Projects\"", Compose(("rd /s /q ", null), (null, "C:\\Users\\me\\Old Projects")));
        Assert.Equal("dir \"C:\\a b\\c d\\x\"", Compose(("dir \"C:\\a b\"\\", null), (null, "c d"), ("\\x", null)));
        Assert.Equal("for /f %a in ('type \"a b\" ^| find \"x\"') do echo %a",
            Compose(("for /f %a in ('type ", null), (null, "a b"), (" ^| find \"x\"') do echo %a", null)));
        Assert.Equal("for /f %a in ('type \"a b\"') do echo %a", Compose(("for /f %a in ('type ", null), (null, "a b"), ("') do echo %a", null)));
        Assert.Equal("cmd /c type \"a b\"&echo \"x y\">\"out a\"", Compose(("cmd /c type ", null), (null, "a b"), ("&echo \"x y\">out", null), (null, " a")));
        Assert.Equal("set \"x=a b\"", Compose(("set x=", null), (null, "a b")));
        Assert.Equal("echo \"a&b c\"", Compose(("echo a^&b", null), (null, " c")));
        Assert.Equal("del \"C:\\In\\Bob's file.txt\"", Compose(("del C:\\In\\Bob's", null), (null, " file.txt")));
    }
}
