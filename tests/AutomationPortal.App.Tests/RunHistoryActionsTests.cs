using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class RunHistoryActionsTests
{
    private static RunRecord Failed() => new()
    {
        RunId = "r1", ShortcutName = "Tidy downloads", Source = RunSource.Automation, Started = DateTimeOffset.Now, DurationMs = 2500,
        Status = RunStatus.Failed, ErrorMessage = "The file was in use.", FailedStepUid = "b",
        OutputPreview = "SECRET FILE CONTENTS",
        Steps = { new StepLog { Uid = "a", Title = "Find files" }, new StepLog { Uid = "b", Title = "Move files", Error = "The file was in use." } },
    };

    [Fact]
    public void Summary_has_the_basics_and_no_output()
    {
        var text = RunHistoryActions.Summary(Failed(), "Run by an automation", "Didn't finish");
        Assert.Contains("Tidy downloads", text);
        Assert.Contains("2.5 s", text);
        Assert.Contains("Run by an automation", text);
        Assert.Contains("2. Move files", text);
        Assert.Contains("The file was in use.", text);
        Assert.DoesNotContain("SECRET FILE CONTENTS", text);
    }

    [Theory]
    [InlineData("api_key=abc123def456ghi789", "abc123def456ghi789")]
    [InlineData("Authorization: Bearer abcdefghijklmnop12345", "abcdefghijklmnop12345")]
    [InlineData("failed with sk-proj1234567890abcdefgh", "sk-proj1234567890abcdefgh")]
    [InlineData("GET https://x.test/a?token=zzz999yyy888", "zzz999yyy888")]
    [InlineData("jwt eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.SflKxwRJSMeKKF2Q", "eyJhbGciOiJIUzI1")]
    public void Redact_hides_secrets(string input, string secret)
    {
        var o = RunHistoryActions.Redact(input);
        Assert.DoesNotContain(secret, o);
        Assert.Contains("[hidden]", o);
    }

    [Fact]
    public void Redact_leaves_plain_messages_alone()
    {
        const string msg = @"Couldn't find C:\Users\Sam\Documents\report.docx";
        Assert.Equal(msg, RunHistoryActions.Redact(msg));
    }

    [Fact]
    public void Summary_redacts_the_error()
    {
        var r = Failed();
        r.ErrorMessage = "Rejected: token=abcd1234efgh5678";
        Assert.DoesNotContain("abcd1234efgh5678", RunHistoryActions.Summary(r, "x", "y"));
    }

    [Fact]
    public void RunAgain_rules()
    {
        Assert.False(RunHistoryActions.RunAgain(null, true).Enabled);
        Assert.Contains("Recently deleted", RunHistoryActions.RunAgain(null, true).Tooltip);
        Assert.False(RunHistoryActions.RunAgain(null, false).Enabled);
        Assert.False(RunHistoryActions.RunAgain(new Shortcut { AcceptsInput = { ContentKind.File } }, false).Enabled);
        Assert.True(RunHistoryActions.RunAgain(new Shortcut(), false).Enabled);
    }

    [Fact]
    public void Source_groups()
    {
        Assert.True(RunHistoryActions.InGroup(RunSource.Hotkey, SourceGroup.Manual));
        Assert.False(RunHistoryActions.InGroup(RunSource.Automation, SourceGroup.Manual));
        Assert.True(RunHistoryActions.InGroup(RunSource.Automation, SourceGroup.Automations));
        Assert.True(RunHistoryActions.InGroup(RunSource.Link, SourceGroup.LinksAndApps));
        Assert.True(RunHistoryActions.InGroup(RunSource.Cli, SourceGroup.All));
    }
}
