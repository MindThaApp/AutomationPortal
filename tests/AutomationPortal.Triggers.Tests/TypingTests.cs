using AutomationPortal.Core;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class PhraseBufferTests
{
    private static PhraseDef Def(string phrase, bool matchCase = false) => new(phrase, matchCase, new object());

    [Fact]
    public void Matches_when_the_buffer_ends_with_the_phrase()
    {
        var m = PhraseBuffer.Match("hello ;date", new[] { Def(";date") });
        Assert.Equal(";date", m!.Value.Typed);
    }

    [Fact]
    public void Is_not_case_sensitive_unless_asked_and_reports_what_was_typed()
    {
        Assert.Equal(";DATE", PhraseBuffer.Match("x;DATE", new[] { Def(";date") })!.Value.Typed);
        Assert.Null(PhraseBuffer.Match("x;DATE", new[] { Def(";date", matchCase: true) }));
        Assert.NotNull(PhraseBuffer.Match("x;date", new[] { Def(";date", matchCase: true) }));
    }

    [Fact]
    public void Longest_phrase_wins()
    {
        var m = PhraseBuffer.Match("see ;addr2", new[] { Def("addr2"), Def(";addr2"), Def("r2") });
        Assert.Equal(";addr2", m!.Value.Def.Phrase);
    }

    [Fact]
    public void Phrases_starting_with_a_letter_only_match_at_the_start_of_a_word()
    {
        Assert.Null(PhraseBuffer.Match("headdr", new[] { Def("addr") }));
        Assert.NotNull(PhraseBuffer.Match("my addr", new[] { Def("addr") }));
        Assert.NotNull(PhraseBuffer.Match("addr", new[] { Def("addr") }));
        Assert.NotNull(PhraseBuffer.Match("(addr", new[] { Def("addr") }));
    }

    [Fact]
    public void Phrases_starting_with_a_symbol_match_anywhere()
    {
        Assert.NotNull(PhraseBuffer.Match("abc;sig", new[] { Def(";sig") }));
    }

    [Fact]
    public void Buffer_keeps_only_the_most_recent_characters_and_supports_backspace()
    {
        var b = new PhraseBuffer();
        b.Append(new string('x', 500));
        Assert.Equal(PhraseBuffer.Capacity, b.Length);
        b.Clear();
        b.Append(";dat");
        b.Backspace();
        b.Append("a");
        Assert.Equal(";daa", b.Text);
        b.Backspace(); b.Backspace(); b.Backspace(); b.Backspace(); b.Backspace();
        Assert.Equal("", b.Text);
    }
}

public class TypingEngineTests
{
    private static readonly IntPtr WinA = new(100), WinB = new(200);
    private DateTime _now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private readonly TypingEngine _engine;

    public TypingEngineTests()
    {
        _engine = new TypingEngine(() => _now);
        _engine.SetPhrases(new[] { new PhraseDef(";sig", false, "sig"), new PhraseDef("brb", false, "brb") });
    }

    private TypingResult Type(string text, IntPtr? window = null)
    {
        TypingResult last = new();
        foreach (var c in text) last = _engine.Process(new KeyInput(KeyKind.Text, c.ToString()), window ?? WinA);
        return last;
    }

    private TypingResult Key(KeyKind kind, IntPtr? window = null) => _engine.Process(new KeyInput(kind), window ?? WinA);

    [Fact]
    public void Matches_as_the_last_character_is_typed()
    {
        Assert.Null(Type(";si").Match);
        var r = Type("g");
        Assert.Equal(";sig", r.Match!.Phrase);
        Assert.Equal(";sig", r.Match.Typed);
        Assert.Equal(WinA, r.Match.Window);
    }

    [Fact]
    public void The_typed_case_is_reported()
    {
        Assert.Equal(";SIG", Type(";SIG").Match!.Typed);
        Assert.Equal("Brb", Type("hello Brb").Match!.Typed);
    }

    [Fact]
    public void Backspace_edits_the_buffer()
    {
        Type(";sib");
        Key(KeyKind.Backspace);
        Assert.NotNull(Type("g").Match);
    }

    [Fact]
    public void Enter_arrows_and_shortcuts_reset_the_buffer()
    {
        Type(";si");
        Key(KeyKind.Reset);
        Assert.Null(Type("g").Match);
    }

    [Fact]
    public void Switching_windows_resets_the_buffer()
    {
        Type(";si");
        Assert.Null(Type("g", WinB).Match);
    }

    [Fact]
    public void A_click_or_focus_move_in_the_same_window_resets_the_buffer()
    {
        Type(";si");
        _engine.ResetContext();
        Assert.Null(Type("g").Match);
    }

    [Fact]
    public void A_click_cancels_the_pending_undo_so_Backspace_is_left_alone()
    {
        Type(";sig");
        _engine.NoteExpansion(";sig", 20, 0, WinA);
        _engine.ResetContext();
        Assert.Null(Key(KeyKind.Backspace).Undo);
    }

    [Fact]
    public void A_click_invalidates_a_match_waiting_to_be_applied_but_a_automationportal_dialog_focus_move_does_not()
    {
        var gen = Type(";sig").Match!.Generation;
        _engine.ResetContext(invalidatePending: false);
        Assert.Equal(gen, _engine.Generation);
        _engine.ResetContext();
        Assert.NotEqual(gen, _engine.Generation);
    }

    [Fact]
    public void Modifier_keys_alone_do_not_disturb_typing()
    {
        Type(";si");
        Key(KeyKind.Ignore);
        Assert.NotNull(Type("g").Match);
    }

    [Fact]
    public void Matching_clears_the_buffer_so_the_next_character_does_not_match_again()
    {
        Type(";sig");
        Assert.Null(Type("x").Match);
        Assert.Equal("x", _engine.BufferText);
    }

    [Fact]
    public void Backspace_right_after_an_expansion_asks_for_an_undo()
    {
        var match = Type(";sig").Match!;
        _engine.NoteExpansion(match.Typed, expandedLength: 22, cursorBack: 3, WinA);

        var undo = Key(KeyKind.Backspace).Undo;

        Assert.NotNull(undo);
        Assert.Equal(";sig", undo!.Typed);
        Assert.Equal(22, undo.ExpandedLength);
        Assert.Equal(3, undo.CursorBack);
    }

    [Fact]
    public void Undo_is_offered_only_once()
    {
        var match = Type(";sig").Match!;
        _engine.NoteExpansion(match.Typed, 10, 0, WinA);
        Assert.NotNull(Key(KeyKind.Backspace).Undo);
        Assert.Null(Key(KeyKind.Backspace).Undo);
    }

    [Fact]
    public void Typing_anything_else_cancels_the_undo()
    {
        var match = Type(";sig").Match!;
        _engine.NoteExpansion(match.Typed, 10, 0, WinA);
        Type("x");
        Assert.Null(Key(KeyKind.Backspace).Undo);
    }

    [Fact]
    public void Undo_expires_and_does_not_follow_you_to_another_window()
    {
        var match = Type(";sig").Match!;
        _engine.NoteExpansion(match.Typed, 10, 0, WinA);
        _now += TypingEngine.UndoWindow + TimeSpan.FromSeconds(1);
        Assert.Null(Key(KeyKind.Backspace).Undo);

        match = Type(";sig").Match!;
        _engine.NoteExpansion(match.Typed, 10, 0, WinA);
        Assert.Null(Key(KeyKind.Backspace, WinB).Undo);
    }

    [Fact]
    public void Generation_counts_keys_so_late_replacements_can_be_refused()
    {
        var match = Type(";sig").Match!;
        var generation = _engine.Generation;
        Assert.Equal(match.Generation, generation);
        Type("x");
        Assert.NotEqual(match.Generation, _engine.Generation);
    }

    [Fact]
    public void Generation_does_not_change_for_modifier_keys_or_window_switches()
    {
        var match = Type(";sig").Match!;
        Key(KeyKind.Ignore);
        Assert.Equal(match.Generation, _engine.Generation);
    }

    [Fact]
    public void No_phrases_means_no_matches()
    {
        _engine.SetPhrases(Array.Empty<PhraseDef>());
        Assert.Null(Type(";sig").Match);
    }
}

public class SnippetPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 15, 45, 0, TimeSpan.Zero);

    private static Snippet Make(string text, bool matchCase = false, string abbr = ";addr") =>
        new() { Abbreviation = abbr, Text = text, MatchCase = matchCase };

    [Fact]
    public void Plain_text_is_used_as_is()
    {
        var plan = SnippetExpansionService.Plan(Make("12 High Street"), ";addr", Now, null, null);
        Assert.Equal("12 High Street", plan.Text);
        Assert.Equal(0, plan.CursorBack);
    }

    [Fact]
    public void Case_follows_what_was_typed_unless_match_case_is_on()
    {
        Assert.Equal("Be right back", SnippetExpansionService.Plan(Make("be right back", abbr: "brb"), "Brb", Now, null, null).Text);
        Assert.Equal("BE RIGHT BACK", SnippetExpansionService.Plan(Make("be right back", abbr: "brb"), "BRB", Now, null, null).Text);
        Assert.Equal("be right back", SnippetExpansionService.Plan(Make("be right back", abbr: "brb"), "brb", Now, null, null).Text);
        Assert.Equal("be right back", SnippetExpansionService.Plan(Make("be right back", matchCase: true, abbr: "brb"), "brb", Now, null, null).Text);
    }

    [Fact]
    public void An_abbreviation_typed_exactly_as_defined_changes_nothing()
    {
        Assert.Equal("on my way", SnippetExpansionService.Plan(Make("on my way", abbr: "OMW"), "OMW", Now, null, null).Text);
    }

    [Fact]
    public void Cursor_placeholder_gives_the_number_of_characters_to_move_back()
    {
        var plan = SnippetExpansionService.Plan(Make("Hello {cursor},\nBest wishes"), ";addr", Now, null, null);
        Assert.Equal("Hello ,\nBest wishes", plan.Text);
        Assert.Equal(",\nBest wishes".Length, plan.CursorBack);
    }

    [Fact]
    public void Clipboard_date_and_fill_in_placeholders_are_expanded()
    {
        var plan = SnippetExpansionService.Plan(Make("Hi {input:Name}, {clipboard} on {date:yyyy-MM-dd} at {time:HH:mm}"), ";addr", Now, "COPIED",
            new Dictionary<string, string> { ["Name"] = "Sam" });
        Assert.Equal("Hi Sam, COPIED on 2026-10-02 at 15:45", plan.Text);
    }

    [Fact]
    public void Windows_line_breaks_become_single_characters_so_the_caret_maths_is_right()
    {
        var plan = SnippetExpansionService.Plan(Make("a\r\nb{cursor}\r\nc"), ";addr", Now, null, null);
        Assert.Equal("a\nb\nc", plan.Text);
        Assert.Equal(2, plan.CursorBack);
    }

    [Fact]
    public void Fill_in_labels_are_listed_in_order()
    {
        Assert.Equal(new[] { "Name", "Day" }, SnippetExpander.FillInLabels("Hi {input:Name}, {input:Day} {input:Name}"));
    }
}
