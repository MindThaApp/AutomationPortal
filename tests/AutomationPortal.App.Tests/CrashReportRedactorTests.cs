using AutomationPortal.App.Startup;
using Xunit;

namespace AutomationPortal.App.Tests;

public class CrashReportRedactorTests
{
    private static CrashReportRedactor Redactor() =>
        new("Bob", "BOBS-PC", @"C:\Program Files\WindowsApps\AutomationPortal", [@"C:\Windows", @"C:\Program Files"]);

    [Fact]
    public void User_file_path_loses_its_names()
    {
        var text = Redactor().Redact(@"Could not open C:\Users\Bob\Documents\secret.docx because it is in use");
        Assert.DoesNotContain("Bob", text);
        Assert.DoesNotContain("secret", text);
        Assert.Contains("<path>.docx", text);
        Assert.Contains("because it is in use", text);
    }

    [Fact]
    public void Path_with_spaces_and_other_drive_is_redacted()
    {
        var text = Redactor().Redact(@"Missing D:\Tax Returns 2025\Final Draft.xlsx.");
        Assert.DoesNotContain("Tax", text);
        Assert.DoesNotContain("Final", text);
    }

    [Fact]
    public void Network_path_is_redacted()
    {
        var text = Redactor().Redact(@"Access denied: \\nas01\private\plans.pdf");
        Assert.DoesNotContain("nas01", text);
        Assert.DoesNotContain("plans", text);
    }

    [Fact]
    public void Windows_and_app_folders_keep_their_paths()
    {
        var r = Redactor();
        Assert.Equal(@"at C:\Windows\System32\kernel32.dll", r.Redact(@"at C:\Windows\System32\kernel32.dll"));
        Assert.Equal(@"<app>\AutomationPortal.dll", r.Redact(@"C:\Program Files\WindowsApps\AutomationPortal\AutomationPortal.dll"));
    }

    [Fact]
    public void Url_with_query_is_removed()
    {
        var text = Redactor().Redact("Request to https://api.example.com/v1/orders?customer=jane&token=abc123 failed");
        Assert.DoesNotContain("example", text);
        Assert.DoesNotContain("jane", text);
        Assert.Equal("Request to <url> failed", text);
    }

    [Theory]
    [InlineData("Invalid key sk-proj-AbCdEf1234567890XyZ")]
    [InlineData("api_key=Zx81QwErTy99Uu")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig")]
    [InlineData("used 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Keys_and_tokens_are_removed(string input)
    {
        var text = Redactor().Redact(input);
        Assert.Contains("<secret>", text);
        Assert.DoesNotContain("AbCdEf", text);
        Assert.DoesNotContain("Zx81", text);
        Assert.DoesNotContain("eyJ", text);
        Assert.DoesNotContain("9f86d", text);
    }

    [Fact]
    public void User_machine_and_email_are_removed()
    {
        var text = Redactor().Redact("User Bob on BOBS-PC wrote to bob@example.com");
        Assert.Equal("User <user> on <machine> wrote to <email>", text);
    }

    [Fact]
    public void Redaction_is_stable_and_leaves_plain_text()
    {
        var r = Redactor();
        var once = r.Redact(@"Failed C:\Users\Bob\a.txt https://x.y/z sk-abcdefghijklmnop1");
        Assert.Equal(once, r.Redact(once));
        Assert.Equal("Object reference not set to an instance of an object.", r.Redact("Object reference not set to an instance of an object."));
        Assert.Null(r.Redact(null));
    }

    [Theory]
    [InlineData(true, true, "store")]
    [InlineData(true, false, "store")]
    [InlineData(false, true, "dev")]
    [InlineData(false, false, "beta")]
    public void Environment_names(bool packaged, bool debug, string expected) =>
        Assert.Equal(expected, CrashReporting.EnvironmentName(packaged, debug));
}
