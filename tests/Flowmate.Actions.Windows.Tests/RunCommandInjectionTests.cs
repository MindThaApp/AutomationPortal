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
    [InlineData("a\" & echo.>pwned1 & \"b")]
    [InlineData("100% | echo.>pwned1")]
    public async Task Cmd_variables_are_data(string value)
    {
        Assert.Equal(value, await RunWith("cmd", value, "echo ", "<VAR>"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }
}
