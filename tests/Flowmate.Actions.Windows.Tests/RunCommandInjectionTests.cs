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

    [Theory]
    [InlineData("x & echo.>pwned1 & y")]
    [InlineData("a\" & echo.>pwned1 & \"b")]
    [InlineData("100% | echo.>pwned1")]
    public async Task Cmd_variables_are_data(string value)
    {
        Assert.Equal(value, await RunWith("cmd", value, "echo ", "<VAR>"));
        Assert.Empty(Directory.GetFiles(_dir, "pwned*"));
    }
}
