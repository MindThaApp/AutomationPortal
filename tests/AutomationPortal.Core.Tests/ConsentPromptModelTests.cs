using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>The caller prompt's wording, "Don't ask again" for one shortcut, and what links may do.</summary>
public class ConsentPromptModelTests
{
    [Fact]
    public void Prompt_reads_as_the_question_with_a_line_when_files_change()
    {
        var id = Guid.NewGuid();
        var p = new CallerPrompt("filesportal", "Resize Images", new[] { "a", "b", "c" }, false) { ShortcutId = id, ChangesFiles = true };
        Assert.Equal("Files Portal wants to run \"Resize Images\" on 3 files", p.Question);
        Assert.Equal("This shortcut changes files.", p.ChangesFilesLine);
        Assert.Equal("Don't ask again when Files Portal runs this shortcut", p.DontAskAgainText);
        Assert.Equal("Don't run (25)", CallerPrompt.DontRunLabel(25));
        Assert.Equal("Don't run (0)", CallerPrompt.DontRunLabel(-3));

        var q = new CallerPrompt("deskpal", "Tidy", new[] { "a" }, false);
        Assert.Equal("Desk Pal wants to run \"Tidy\" on 1 file", q.Question);
        Assert.Null(q.ChangesFilesLine);
        Assert.Equal("Desk Pal wants to run \"Tidy\" with some text", new CallerPrompt("deskpal", "Tidy", Array.Empty<string>(), true).Question);
    }

    [Fact]
    public async Task Dont_ask_again_covers_that_app_with_that_shortcut_only()
    {
        var settings = new InMemorySettingsStore();
        var asked = 0;
        var answer = CallerDecision.AlwaysAllow;
        var consent = new CallerConsent(settings, (_, _) => { asked++; return Task.FromResult(answer); });
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        CallerPrompt For(string caller, Guid id) => new(caller, "X", Array.Empty<string>(), false) { ShortcutId = id };

        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(For("filesportal", a), default));
        Assert.Equal(1, asked);
        Assert.Contains(CallerConsent.PairKey("filesportal", a), settings.Current.AllowedCallerShortcuts);
        Assert.Empty(settings.Current.AllowedCallers);

        // Same app, same shortcut: no question. Another shortcut, or another app: asked again.
        Assert.Equal(CallerVerdict.Allowed, await consent.RequestAsync(For("filesportal", a), default));
        Assert.Equal(1, asked);
        answer = CallerDecision.DontRun;
        Assert.Equal(CallerVerdict.Declined, await consent.RequestAsync(For("filesportal", b), default));
        Assert.Equal(CallerVerdict.Declined, await consent.RequestAsync(For("deskpal", a), default));
        Assert.Equal(3, asked);

        // Removing it in Settings asks again; a damaged entry is ignored.
        CallerConsent.ForgetPair(settings, CallerConsent.PairKey("filesportal", a));
        Assert.Equal(CallerVerdict.Declined, await consent.RequestAsync(For("filesportal", a), default));
        Assert.False(CallerConsent.TryParsePair("not a pair", out _, out _));
        Assert.True(CallerConsent.TryParsePair(CallerConsent.PairKey("filesportal", a), out var caller, out var sid));
        Assert.Equal(("filesportal", a), (caller, sid));
    }

    [Fact]
    public async Task Allow_once_and_dont_run_keep_their_meaning_and_time_out_still_means_no()
    {
        var settings = new InMemorySettingsStore();
        var once = new CallerConsent(settings, (_, _) => Task.FromResult(CallerDecision.AllowOnce));
        var p = new CallerPrompt("filesportal", "X", Array.Empty<string>(), false) { ShortcutId = Guid.NewGuid() };
        Assert.Equal(CallerVerdict.Allowed, await once.RequestAsync(p, default));
        Assert.Empty(settings.Current.AllowedCallerShortcuts);

        var slow = new CallerConsent(settings, async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return CallerDecision.AllowOnce; }, TimeSpan.FromMilliseconds(50));
        var verdict = await slow.RequestAsync(p, default);
        Assert.Equal(CallerVerdict.TimedOut, verdict);
        Assert.Equal(CliExit.Declined, slow.NotRun(verdict).ExitCode);
        Assert.Equal(6, CliExit.Declined);
    }

    [Fact]
    public void Links_follow_the_setting_and_files_always_ask()
    {
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        Assert.Equal(LinkMode.PerShortcut, settings.Current.LinksMode);
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, id, false));
        Assert.True(LinkPolicy.CanRemember(settings.Current, false));

        LinkPolicy.SetTrusted(settings, id, true);
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings.Current, id, false));
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, id, true));
        Assert.False(LinkPolicy.CanRemember(settings.Current, true));
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, Guid.NewGuid(), false));

        settings.Update(s => s.LinksMode = LinkMode.Ask);
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, id, false));
        Assert.False(LinkPolicy.CanRemember(settings.Current, false));
        settings.Update(s => s.LinksMode = LinkMode.Never);
        Assert.Equal(LinkDecision.Block, LinkPolicy.Decide(settings.Current, id, false));

        LinkPolicy.SetTrusted(settings, id, false);
        Assert.DoesNotContain(id, settings.Current.LinkTrustedShortcutIds);
    }

    [Fact]
    public void New_shortcut_gets_the_global_default_as_its_own()
    {
        var settings = new InMemorySettingsStore();
        settings.Update(s => s.LinksMode = LinkMode.Ask);
        var id = Guid.NewGuid();
        LinkPolicy.ApplyDefaultToNew(settings, id);
        Assert.Equal(LinkMode.Ask, settings.Current.ShortcutLinkModes[id.ToString("D")]);

        settings.Update(s => s.LinksMode = LinkMode.Never);
        Assert.Equal(LinkMode.Ask, LinkPolicy.ModeFor(settings.Current, id));
        LinkPolicy.ApplyDefaultToNew(settings, id);
        Assert.Equal(LinkMode.Ask, LinkPolicy.ModeFor(settings.Current, id));
    }

    [Fact]
    public void Old_shortcut_without_a_stored_value_inherits_the_global_setting()
    {
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        Assert.Empty(settings.Current.ShortcutLinkModes);
        settings.Update(s => s.LinksMode = LinkMode.Never);
        Assert.Equal(LinkDecision.Block, LinkPolicy.Decide(settings.Current, id, false));
        settings.Update(s => s.LinksMode = LinkMode.Ask);
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings.Current, id, false));
    }

    [Fact]
    public void A_shortcuts_own_setting_wins_over_the_global_one()
    {
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        settings.Update(s => s.LinksMode = LinkMode.Never);
        LinkPolicy.SetMode(settings, id, LinkMode.PerShortcut);
        LinkPolicy.SetTrusted(settings, id, true);
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings.Current, id, false));
        Assert.True(LinkPolicy.CanRemember(settings.Current, false, shortcutId: id));
        Assert.Equal(LinkDecision.Block, LinkPolicy.Decide(settings.Current, Guid.NewGuid(), false));

        settings.Update(s => s.LinksMode = LinkMode.PerShortcut);
        LinkPolicy.SetMode(settings, id, LinkMode.Never);
        Assert.Equal(LinkDecision.Block, LinkPolicy.Decide(settings.Current, id, false));
    }
}
