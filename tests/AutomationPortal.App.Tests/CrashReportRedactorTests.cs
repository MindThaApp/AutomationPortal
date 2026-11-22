using AutomationPortal.App.Startup;
using Sentry;
using Sentry.Protocol;
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
    [InlineData(@"Could not find file 'C:\Users\Bob\Documents\Report.docx'.", "Report")]
    [InlineData(@"Access to the path 'C:\Users\Bob\OneDrive - Contoso\Taxes 2026\Return.pdf' is denied.", "Contoso")]
    [InlineData(@"The network path was not found: \\fileserver\HR\Salaries\Q3 Review.xlsx", "Salaries")]
    [InlineData(@"The network path was not found: \\fileserver\HR\Salaries\Q3 Review.xlsx", "fileserver")]
    [InlineData("Shortcut 'Backup holiday photos' failed at step 3", "holiday")]
    [InlineData("Shortcut \"Pay Mum's rent\" failed", "rent")]
    [InlineData("Shortcut \u201CMorning routine\u201D failed", "Morning")]
    [InlineData("Could not copy Report.docx to the share", "Report")]
    [InlineData("Could not copy Holiday-2026(1).jpeg to the share", "Holiday")]
    [InlineData(@"Denied for CONTOSO\bob.smith", "bob.smith")]
    [InlineData(@"Denied for BOBS-PC\Bob", "BOBS-PC")]
    [InlineData("Owner S-1-5-21-1004336348-1177238915-682003330-1001 not allowed", "1004336348")]
    [InlineData("Clipboard held 'my bank PIN is 4821'", "4821")]
    public void Realistic_messages_do_not_leak(string input, string secret)
    {
        var text = Redactor().Redact(input);
        Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bob", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(text, Redactor().Redact(text));
    }

    [Fact]
    public void Plain_error_text_survives_apostrophes()
    {
        var r = Redactor();
        const string message = "It couldn't finish and doesn't know why: step timed out after 30 seconds.";
        Assert.Equal(message, r.Redact(message));
    }

    [Fact]
    public void Whole_event_has_no_personal_text()
    {
        var reporter = CrashReporting.Create(null, sandbox: true);
        var evt = new SentryEvent
        {
            ServerName = Environment.MachineName,
            Message = new SentryMessage { Message = @"Failed 'Rename invoices' on C:\Users\" + Environment.UserName + @"\Report.docx" },
            User = { Username = Environment.UserName, Email = "bob@example.com", IpAddress = "203.0.113.9", Id = "user-1" },
        };
        evt.SentryExceptions =
        [
            new SentryException
            {
                Type = "IOException",
                Value = @"Could not find file '\\nas\home\Bob\Quarterly Report.docx' (shortcut 'Archive Q3').",
                Stacktrace = new SentryStackTrace
                {
                    Frames = [new SentryStackFrame { FileName = @"C:\Users\" + Environment.UserName + @"\src\Step.cs", ContextLine = "var f = \"Report.docx\";" }],
                },
            },
        ];
        evt.SetExtra("clipboard", "password hunter2 for bob@example.com");
        evt.SetTag("shortcut", "Archive Q3");
        reporter.Redact(evt);

        var all = string.Join("\n",
            evt.ServerName, evt.Message!.Message, evt.User.Username, evt.User.Email, evt.User.IpAddress, evt.User.Id,
            evt.SentryExceptions!.Single().Value, evt.SentryExceptions!.Single().Stacktrace!.Frames[0].FileName,
            evt.SentryExceptions!.Single().Stacktrace!.Frames[0].ContextLine, evt.Extra["clipboard"], evt.Tags["shortcut"]);
        foreach (var leak in new[] { "Report", "Rename", "invoices", "Archive", "Quarterly", "nas", "hunter2", "example.com", "203.0", Environment.UserName, Environment.MachineName })
            Assert.DoesNotContain(leak, all, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true, "store")]
    [InlineData(true, false, "store")]
    [InlineData(false, true, "dev")]
    [InlineData(false, false, "beta")]
    public void Environment_names(bool packaged, bool debug, string expected) =>
        Assert.Equal(expected, CrashReporting.EnvironmentName(packaged, debug));
}
