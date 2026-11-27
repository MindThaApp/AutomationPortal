using Xunit;

namespace AutomationPortal.Core.Tests;

[Collection("WindowActivation")]
public class WindowActivationTests : IDisposable
{
    public WindowActivationTests() => WindowActivation.Set(false);
    public void Dispose() => WindowActivation.Set(false);

    [Theory]
    [InlineData("--no-activate", null, true)]
    [InlineData("--NO-ACTIVATE", null, true)]
    [InlineData("--no-global-input", null, false)]
    [InlineData("--no-global-input", "1", true)]
    [InlineData("--no-global-input", "0", false)]
    [InlineData("--no-global-input", "", false)]
    public void Parse_reads_flag_and_environment(string arg, string? env, bool expected) =>
        Assert.Equal(expected, WindowActivation.Parse(new[] { arg }, env));

    [Fact]
    public void Run_activates_when_switch_is_off()
    {
        var calls = 0;
        Assert.True(WindowActivation.Run(() => calls++));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Run_does_nothing_when_switch_is_on()
    {
        WindowActivation.Set(true);
        var calls = 0;
        Assert.False(WindowActivation.Run(() => calls++));
        Assert.Equal(0, calls);
        Assert.False(WindowActivation.Run(() => { calls++; return true; }, false));
        Assert.Equal(0, calls);
    }
}
