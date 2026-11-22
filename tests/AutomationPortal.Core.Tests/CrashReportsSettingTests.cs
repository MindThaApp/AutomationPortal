using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class CrashReportsSettingTests
{
    [Fact]
    public void New_install_sends_crash_reports()
    {
        Assert.True(new AppSettings().SendCrashReports);
        Assert.True(AutomationPortalJson.Deserialize<AppSettings>("{}")!.SendCrashReports);
    }

    [Fact]
    public void Old_file_that_only_has_the_old_default_counts_as_never_chosen()
    {
        var s = AutomationPortalJson.Deserialize<AppSettings>("{\"sendCrashReports\":false,\"welcomeShown\":true}")!;
        Assert.True(s.SendCrashReports);
    }

    [Fact]
    public void Legacy_key_with_false_reads_as_on()
    {
        var s = AutomationPortalJson.Deserialize<AppSettings>("{\"SendCrashReports\":false}")!;
        Assert.True(s.SendCrashReports);
        Assert.Null(s.CrashReportsChoice);
    }

    [Fact]
    public void An_explicit_off_stays_off_after_saving_and_loading()
    {
        var s = new AppSettings { SendCrashReports = false };
        var back = AutomationPortalJson.Deserialize<AppSettings>(AutomationPortalJson.Serialize(s, indented: false))!;
        Assert.False(back.SendCrashReports);
        Assert.False(back.CrashReportsChoice);
    }
}
