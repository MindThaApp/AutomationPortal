using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>Links and other apps: what counts as a path, trust that ends when the steps change, and how often links may run a shortcut.</summary>
public class LinkSafetyTests
{
    [Theory]
    [InlineData(@"%USERPROFILE%\Documents")]
    [InlineData("%APPDATA%")]
    [InlineData("~")]
    [InlineData("~/notes.txt")]
    [InlineData(@"Documents\report.docx")]
    [InlineData("photos/2024")]
    [InlineData("..")]
    [InlineData(@"..\secrets")]
    [InlineData(".ssh")]
    [InlineData("C:")]
    [InlineData(@"d:\x")]
    [InlineData("file:///C:/x")]
    [InlineData("hello\n\"%TEMP%\"")]
    public void Anything_that_could_name_a_file_counts_as_a_path(string input) =>
        Assert.True(LaunchArguments.InputLooksLikePath(input));

    [Theory]
    [InlineData("just some text")]
    [InlineData("Meeting at 10, bring coffee")]
    [InlineData("   ")]
    public void Plain_text_is_not_a_path(string input) =>
        Assert.False(LaunchArguments.InputLooksLikePath(input));

    private static Shortcut Sample()
    {
        var s = new Shortcut { Name = "Tidy" };
        s.Actions.Add(ActionInstance.Create("file.getFolder"));
        s.Actions.Add(ActionInstance.Create("file.move"));
        return s;
    }

    [Fact]
    public void Link_trust_is_dropped_when_the_steps_change()
    {
        var settings = new InMemorySettingsStore();
        var s = Sample();
        LinkPolicy.SetTrusted(settings, s.Id, true, LinkPolicy.StepsHash(s));
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(s)));

        // renaming doesn't matter
        s.Name = "Tidy up";
        s.Actions[0].UiCollapsed = true;
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(s)));

        // a step edit, an added step and a reorder each ask again
        var edited = s.Clone();
        edited.Actions[1].Parameters["folder"] = ParamValue.Text(@"C:\Elsewhere");
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(edited)));
        var added = s.Clone();
        added.Actions.Add(ActionInstance.Create("file.delete"));
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(added)));
        var reordered = s.Clone();
        reordered.Actions.Reverse();
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(reordered)));

        // trust saved before steps were tracked doesn't match a real shortcut's steps
        var legacy = new InMemorySettingsStore();
        LinkPolicy.SetTrusted(legacy, s.Id, true);
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(legacy.Current, s.Id, false, stepsHash: LinkPolicy.StepsHash(s)));
    }

    [Fact]
    public async Task Caller_dont_ask_again_is_dropped_when_the_steps_change()
    {
        var settings = new InMemorySettingsStore();
        var asked = 0;
        var consent = new CallerConsent(settings, (_, _) => { asked++; return Task.FromResult(CallerDecision.AlwaysAllow); });
        var s = Sample();
        CallerPrompt Prompt(Shortcut sc) => new("filesportal", sc.Name, Array.Empty<string>(), false) { ShortcutId = sc.Id, StepsHash = LinkPolicy.StepsHash(sc) };

        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt(s), CancellationToken.None));
        Assert.Equal(1, asked);
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(Prompt(s), CancellationToken.None));
        Assert.Equal(1, asked);   // remembered

        var edited = s.Clone();
        edited.Actions.Add(ActionInstance.Create("file.delete"));
        Assert.False(consent.IsAlwaysAllowed(Prompt(edited)));
        await consent.RequestAsync(Prompt(edited), CancellationToken.None);
        Assert.Equal(2, asked);   // asked again after the edit
    }

    [Fact]
    public void Links_run_a_shortcut_at_most_once_in_ten_seconds_and_twenty_times_an_hour()
    {
        var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        var limiter = new LinkRunLimiter(() => now);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.Null(limiter.Check(id));
        limiter.Ran(id);
        now = now.AddSeconds(5);
        Assert.Contains("10 seconds", limiter.Check(id));
        Assert.Null(limiter.Check(other));   // per shortcut
        now = now.AddSeconds(6);
        Assert.Null(limiter.Check(id));

        for (var i = 1; i < LinkRunLimiter.PerHour; i++) { limiter.Ran(id); now = now.AddSeconds(30); }
        Assert.Contains("20 times in the last hour", limiter.Check(id));
        now = now.AddHours(1);
        Assert.Null(limiter.Check(id));

        // a rate-limited link asks even for a trusted shortcut
        var settings = new InMemorySettingsStore();
        LinkPolicy.SetTrusted(settings, id, true, "h");
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings.Current, id, false, stepsHash: "h"));
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, id, false, stepsHash: "h", rateLimited: true));
    }
}
