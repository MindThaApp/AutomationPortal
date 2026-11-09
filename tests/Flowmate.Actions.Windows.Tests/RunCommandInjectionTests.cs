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
        var command = RunCommandAction.Compose(new (string?, string?)[]
        {
            ("# the user's file\nCopy-Item ", null), (null, BreakOut), (" '", null), (null, Semicolon), ("' \"$(", null), (null, "z"), (")\"", null),
        }, cmd: false);
        Assert.DoesNotContain("pwned1", command);
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
        // Start-Job runs the block in another process from its text; a remote or elevated session would do the same. The value
        // travels with the text.
        Assert.Equal("x" + value, await RunWith("powershell", value, "Start-Job { Write-Output 'x", "<VAR>", "' } | Receive-Job -Wait -AutoRemoveJob"));
        Assert.Equal("x" + value, await RunWith("powershell", value, "sajb { Write-Output 'x", "<VAR>", "' } | Receive-Job -Wait -AutoRemoveJob"));
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
    [InlineData("a&calc&.txt")]
    [InlineData("x & echo.>pwned1 & y")]
    [InlineData("100% | echo.>pwned1")]
    [InlineData("x ^& echo.>pwned1 !PATH!")]
    [InlineData("a < b > pwned1")]
    [InlineData("a\" & echo.>pwned1 & \"b")]
    [InlineData("a\"b")]
    [InlineData("a\r\necho.>pwned1")]
    [InlineData("a\necho.>pwned1")]
    public async Task Cmd_refuses_values_with_characters_it_would_act_on(string value)
    {
        foreach (var command in new[]
        {
            new object[] { "echo ", "<VAR>" },
            new object[] { "echo \"", "<VAR>", "\"" },
            new object[] { "for /f \"delims=\" %a in ('certutil -hashfile \"", "<VAR>", "\" SHA256') do @echo %a" },
            new object[] { "cmd /c type \"", "<VAR>", "\"" },
            new object[] { "cmd /c \"type \"", "<VAR>", "\"\"" },
            new object[] { "echo \"", "<VAR>", "\" | sort" },
        })
        {
            var r = await Run("cmd", value, false, command);
            Assert.Equal(RunStatus.Failed, r.Status);
            Assert.Contains("Run in", r.ErrorMessage);
            Assert.Contains("PowerShell", r.ErrorMessage);
        }
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }

    [Theory]
    [InlineData("c d.txt")]
    [InlineData("c\td.txt")]
    [InlineData("Report (1).pdf")]
    [InlineData("a,b")]
    [InlineData("a;b")]
    [InlineData("a=b")]
    public async Task Cmd_refuses_spaces_and_separators_outside_quotes_right_around_the_variable(string value)
    {
        // "del {File}" with a file named "c d.txt" would delete "c" and "d.txt".
        File.WriteAllText(Path.Combine(_dir, "c"), "keep");
        File.WriteAllText(Path.Combine(_dir, "d.txt"), "keep");
        foreach (var command in new[]
        {
            new object[] { "del ", "<VAR>" },
            new object[] { "del \"", "<VAR>", " x\"" },
            new object[] { "del \"x ", "<VAR>", "\"" },
            new object[] { "del ", "<VAR>", "\"" },
        })
        {
            var r = await Run("cmd", value, false, command);
            Assert.Equal(RunStatus.Failed, r.Status);
            Assert.Contains("Put quotes right around the variable", r.ErrorMessage);
            Assert.Contains("\"{Name}\"", r.ErrorMessage);
        }
        Assert.True(File.Exists(Path.Combine(_dir, "c")));
        Assert.True(File.Exists(Path.Combine(_dir, "d.txt")));
    }

    [Fact]
    public async Task Cmd_a_quoted_variable_may_hold_spaces_and_brackets()
    {
        File.WriteAllText(Path.Combine(_dir, "Report"), "keep");
        File.WriteAllText(Path.Combine(_dir, "(1).pdf"), "keep");
        File.WriteAllText(Path.Combine(_dir, "Report (1).pdf"), "target");
        await RunWith("cmd", "Report (1).pdf", "del \"", "<VAR>", "\"");
        Assert.False(File.Exists(Path.Combine(_dir, "Report (1).pdf")));
        Assert.True(File.Exists(Path.Combine(_dir, "Report")));
        Assert.True(File.Exists(Path.Combine(_dir, "(1).pdf")));
        // A bracket in a quoted value can't close an if block around it.
        Assert.Equal("in a) b", (await RunWith("cmd", "a) b", "if 1==1 (for %v in (\"", "<VAR>", "\") do @echo in %~v)")).Trim());
    }

    [Fact]
    public async Task Cmd_rd_on_a_folder_with_a_space_removes_only_that_folder()
    {
        foreach (var name in new[] { "Old", "Projects", "Old Projects" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, name));
            File.WriteAllText(Path.Combine(_dir, name, "f.txt"), "x");
        }
        // Relative to the start folder, which is this test's own temp folder.
        await RunWith("cmd", "Old Projects", "cmd /c rd /s /q \"", "<VAR>", "\"");
        Assert.False(Directory.Exists(Path.Combine(_dir, "Old Projects")));
        Assert.True(File.Exists(Path.Combine(_dir, "Old", "f.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Projects", "f.txt")));

        Directory.CreateDirectory(Path.Combine(_dir, "Old Projects"));
        await RunWith("cmd", Path.Combine(_dir, "Old Projects"), "rd /s /q \"", "<VAR>", "\"");
        Assert.False(Directory.Exists(Path.Combine(_dir, "Old Projects")));
        Assert.True(File.Exists(Path.Combine(_dir, "Old", "f.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Projects", "f.txt")));
    }

    [Fact]
    public async Task Cmd_values_work_in_for_f_cmd_c_and_escaped_pipes()
    {
        File.WriteAllText(Path.Combine(_dir, "a b.txt"), "hello\r\nxyz\r\n");
        Assert.Equal("hello", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('findstr hello \"", "<VAR>", "\"') do @echo %a")).Trim());
        Assert.Equal("hello", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('type \"", "<VAR>", "\"') do @echo %a")).Split('\n')[0].Trim());
        Assert.Equal("xyz", (await RunWith("cmd", "a b.txt", "for /f \"delims=\" %a in ('type \"", "<VAR>", "\" ^| findstr y') do @echo %a")).Trim());
        Assert.StartsWith("hello", (await RunWith("cmd", "a b.txt", "cmd /c type \"", "<VAR>", "\"")).Trim());
        Assert.Equal("xyz", (await RunWith("cmd", "a b.txt", "type \"", "<VAR>", "\" | findstr y")).Trim());
    }

    [Fact]
    public async Task Cmd_values_reach_external_programs_as_they_are()
    {
        File.WriteAllText(Path.Combine(_dir, "song.wav"), "x marks\r\n");
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x " + _dir + "\\", "<VAR>"));
        Assert.Equal("x marks", await RunWith("cmd", "song.wav", "findstr x \"" + _dir + "\\", "<VAR>", "\""));
        // A folder ending in \ keeps its closing quote: where.exe reads "C:\dir\\" as C:\dir\.
        Assert.EndsWith("song.wav", (await RunWith("cmd", _dir + "\\", "where /r \"", "<VAR>", "\" song.wav")).Trim());
        Assert.EndsWith("song.wav", (await RunWith("cmd", _dir + "\\", "where /r ", "<VAR>", " song.wav")).Trim());
    }

    [Fact]
    public async Task Cmd_keeps_the_commands_own_text_as_it_is()
    {
        Assert.Equal("hi! \"wow!\" v", await RunWith("cmd", "v", "echo hi! \"wow!\" ", "<VAR>"));
        Assert.Equal("\"^a\" a^b c! a&b v", await RunWith("cmd", "v", "echo \"^a\" a^^b c^! a^&b ", "<VAR>"));
        // A regular expression anchor in quotes: only the line that starts with ERROR matches.
        File.WriteAllText(Path.Combine(_dir, "log.txt"), "ERROR one\r\nan ERROR two\r\n");
        Assert.Equal("ERROR one", await RunWith("cmd", "log.txt", "findstr /r \"^ERROR\" ", "<VAR>"));
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
        Assert.Equal("\"" + value + "\"", await RunWith("cmd", value, "echo \"", "<VAR>", "\""));

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
    public async Task PowerShell_values_too_long_for_the_command_line_are_refused()
    {
        // A value that fits travels inside the script, even into a job that runs in another process.
        var value = new string('a', 1000) + "'; New-Item -ItemType File pwned1; '" + new string('\u3042', 300);
        Assert.Equal(value.Length.ToString(), (await RunWith("powershell", value, "$x = '", "<VAR>", "'; Write-Output $x.Length")).Trim());
        Assert.Equal(value.Length.ToString(), (await RunWith("powershell", value, "Start-Job { $x = '", "<VAR>", "'; $x.Length } | Receive-Job -Wait -AutoRemoveJob")).Trim());
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));

        foreach (var length in new[] { 9000, 31000 })
            foreach (var command in new[]
            {
                new object[] { "New-Item -ItemType File ran1; Write-Output ", "<VAR>" },
                new object[] { "New-Item -ItemType File ran1; Start-Job { $x = '", "<VAR>", "'; $x.Length } | Receive-Job -Wait -AutoRemoveJob" },
            })
            {
                var r = await Run("powershell", new string('a', length), false, command);
                Assert.Equal(RunStatus.Failed, r.Status);
                Assert.Contains("The output of an earlier step is too long to put in a command", r.ErrorMessage);
                Assert.Contains("Save it to a file and pass the file path instead", r.ErrorMessage);
            }
        Assert.Empty(Directory.GetFiles(_dir, "ran*"));
    }

    private sealed class SecretText : ShortcutAction
    {
        public override ActionDescriptor Descriptor { get; } = new()
        {
            Id = "test.secret", Title = "Secret", Category = ActionCategories.Text, SensitiveOutput = true, OutputKind = ContentKind.Text,
        };
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Text("hunter2"));
    }

    [Theory]
    [InlineData("powershell", "New-Item -ItemType File ran1; Write-Output '", "'")]
    [InlineData("powershell", "New-Item -ItemType File ran1; Invoke-Command -ComputerName fs01 { Remove-Item \"Q:\\Shares\\", "\" -Recurse -WhatIf }")]
    [InlineData("powershell", "New-Item -ItemType File ran1; icm -cn fs01 { Remove-Item \"Q:\\Shares\\", "\" -Recurse -WhatIf }")]
    [InlineData("powershell", "New-Item -ItemType File ran1; Start-Job { Write-Output '", "' } | Receive-Job -Wait")]
    [InlineData("powershell", "New-Item -ItemType File ran1; sajb { Write-Output '", "' } | Receive-Job -Wait")]
    [InlineData("powershell", "New-Item -ItemType File ran1; & ([scriptblock]::Create('Write-Output ", "'))")]
    [InlineData("cmd", "echo.>ran1 & echo \"", "\"")]
    public async Task Private_values_are_never_put_into_a_command(string shell, string before, string after)
    {
        using var host = new TestHost().With(typeof(RunCommandAction).Assembly);
        host.Registry.Register(new SecretText());
        var b = new ShortcutBuilder("T");
        var secret = b.Add("test.secret");
        b.Add("adv.runCommand", ("shell", ParamValue.Choice(shell)), ("folder", ParamValue.File(_dir)),
            ("command", ParamValue.Text(before, VarRef.Output(secret), after)));
        var r = await host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("private", r.ErrorMessage);
        Assert.Contains("wasn't run", r.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_dir, "ran*"));
    }

    [Fact]
    public void Cmd_compose_puts_values_in_as_they_are()
    {
        static string Compose(params (string?, string?)[] parts) => RunCommandAction.Compose(parts, cmd: true);
        // The command's own text is never changed: no quotes added or removed, carets and quotes kept.
        Assert.Equal("copy C:\\In\\a.txt E:\\", Compose(("copy C:\\In\\", null), (null, "a.txt"), (" E:\\", null)));
        Assert.Equal("echo \"^a\" a^^b \"x\"y", Compose(("echo \"^a\" a^^b \"", null), (null, "x"), ("\"y", null)));
        Assert.Equal("del \"Report (1).pdf\"", Compose(("del \"", null), (null, "Report (1).pdf"), ("\"", null)));
        Assert.Equal("cmd /c rd /s /q \"Old Projects\"", Compose(("cmd /c rd /s /q \"", null), (null, "Old Projects"), ("\"", null)));
        Assert.Equal("if exist x (echo \"Report (1).pdf\")", Compose(("if exist x (echo \"", null), (null, "Report (1).pdf"), ("\")", null)));
        // Nested quotes: the quote before the value closes an open one, so spaces would be loose. Refused.
        Assert.Throws<ActionException>(() => Compose(("cmd /c \"rd /s /q \"", null), (null, "Old Projects"), ("\"\"", null)));
        Assert.Throws<ActionException>(() => Compose(("\"a\"", null), (null, "x)y"), ("\"b\"", null)));
        Assert.Throws<ActionException>(() => Compose(("\"a\"", null), (null, "x y"), ("\"b\"", null)));
        Assert.Equal("set \"x=a b; c=d, e\"", Compose(("set \"x=", null), (null, "a"), (" b; c=d, e\"", null)));
        // A trailing backslash inside quotes is doubled so it can't escape the closing quote; it is never run here.
        Assert.Equal("rd /s /q \"E:\\\\\"", Compose(("rd /s /q \"", null), (null, "E:\\"), ("\"", null)));
        Assert.Equal("dir \"C:\\a b\\\\\\\\\"", Compose(("dir \"", null), (null, "C:\\a b\\\\"), ("\"", null)));
        Assert.Equal("dir E:\\", Compose(("dir ", null), (null, "E:\\")));
        // A quote on only one side, or a second variable in between, is not "{v}".
        foreach (var parts in new[]
        {
            new (string?, string?)[] { ("del ", null), (null, "a b") },
            new (string?, string?)[] { ("del \"", null), (null, "a b"), (" x\"", null) },
            new (string?, string?)[] { ("del \"x", null), (null, "a b"), ("\"", null) },
            new (string?, string?)[] { ("del \"", null), (null, "a b"), (null, "c"), ("\"", null) },
            new (string?, string?)[] { ("del C:\\In\\", null), (null, "a(1)") },
        })
            Assert.Throws<ActionException>(() => Compose(parts));
    }
}
