using Flowmate.Actions.Windows.Advanced;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Windows.Tests;

public class RunCommandInjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public RunCommandInjectionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private async Task<string> RunWith(string shell, string value, params object[] commandParts)
    {
        using var host = new TestHost().With(typeof(RunCommandAction).Assembly);
        var b = new ShortcutBuilder("T");
        var t = b.Add("content.text", ("text", value));
        var cmdParts = commandParts.Select(p => p is string s && s == "<VAR>" ? (object)VarRef.Output(t) : p).ToArray();
        b.Add("adv.runCommand", ("shell", ParamValue.Choice(shell)), ("command", ParamValue.Text(cmdParts)), ("folder", ParamValue.File(_dir)));
        var r = await host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.True(r.Status == RunStatus.Succeeded, r.ErrorMessage);
        return r.Output.AsText();
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
        Assert.Equal("# the user's file\nCopy-Item ${env:FLOWMATE_VAR_1} \"${env:FLOWMATE_VAR_2}\" \"$(${env:FLOWMATE_VAR_3})\"", command);
        Assert.Equal(BreakOut, env!["FLOWMATE_VAR_1"]);
        Assert.Equal(Semicolon, env["FLOWMATE_VAR_2"]);
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
