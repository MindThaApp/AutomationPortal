using AutomationPortal.Core;
using AutomationPortal.Core.AppControl;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>A pretend UI Automation tree: no real windows are opened or touched.</summary>
internal sealed class FakeEl : IUiElement
{
    private readonly List<FakeEl> _children = new();
    public FakeEl(string type, string name = "", string id = "", string cls = "", int pid = 10, params UiPattern[] patterns)
    {
        ControlType = type; Name = name; AutomationId = id; ClassName = cls; ProcessId = pid;
        Patterns = new HashSet<UiPattern>(patterns);
    }
    public string AutomationId { get; set; }
    public string Name { get; set; }
    public string ControlType { get; }
    public string ClassName { get; }
    public int ProcessId { get; }
    public bool IsPassword { get; set; }
    public bool IsEnabled { get; set; } = true;
    public HashSet<UiPattern> Patterns { get; }
    public FakeEl? ParentEl { get; private set; }
    public IUiElement? Parent => ParentEl;
    public IReadOnlyList<IUiElement> Children => _children;
    public List<string> Done { get; } = new();

    public FakeEl Add(FakeEl child) { child.ParentEl = this; _children.Add(child); return this; }
    public FakeEl With(out FakeEl child, FakeEl c) { child = c; return Add(c); }
    public void Remove(FakeEl child) { _children.Remove(child); child.ParentEl = null; }
    public bool SameAs(IUiElement other) => ReferenceEquals(this, other);
    public bool Supports(UiPattern pattern) => Patterns.Contains(pattern);
    public void Invoke() => Done.Add("invoke");
    public void SetValue(string text) => Done.Add("value:" + text);
    public void Toggle() => Done.Add("toggle");
    public void Select() => Done.Add("select");
    public void ExpandOrCollapse() => Done.Add("expand");
    public void Focus() => Done.Add("focus");
}

internal sealed class FakeDesktop : IUiDesktop
{
    public List<FakeEl> Windows { get; } = new();
    public Dictionary<int, string> Processes { get; } = new() { [10] = "notepad", [20] = "calc" };
    public FakeEl? FocusedEl { get; set; }
    public int Reads { get; private set; }
    public IReadOnlyList<IUiElement> TopWindows() { Reads++; return Windows; }
    public IUiElement? ElementAt(int x, int y) => null;
    public IUiElement? Focused() => FocusedEl;
    public string ProcessName(int processId) => Processes.TryGetValue(processId, out var n) ? n : "";
}

public class AppControlSelectorTests
{
    // Untitled - Notepad
    //   Pane (class=Toolbar)
    //     Button "Save" (id=SaveBtn)
    //     Button "Open"
    //     Button "" (class=IconBtn)  x2
    //   Edit "Search" (id=SearchBox)
    //   Edit "PIN" (password)
    internal static (FakeDesktop Desktop, FakeEl Window, FakeEl Save, FakeEl Open, FakeEl Icon2, FakeEl Search, FakeEl Pin) Tree()
    {
        var d = new FakeDesktop();
        var w = new FakeEl("Window", "Untitled - Notepad", cls: "Notepad");
        var bar = new FakeEl("Pane", cls: "Toolbar");
        bar.With(out var save, new FakeEl("Button", "Save", "SaveBtn", "Button", 10, UiPattern.Invoke))
           .With(out var open, new FakeEl("Button", "Open", "", "Button", 10, UiPattern.Invoke))
           .With(out _, new FakeEl("Button", "", "", "IconBtn", 10, UiPattern.Invoke))
           .With(out var icon2, new FakeEl("Button", "", "", "IconBtn", 10, UiPattern.Invoke));
        w.Add(bar)
         .With(out var search, new FakeEl("Edit", "Search", "SearchBox", "Edit", 10, UiPattern.Value))
         .With(out var pin, new FakeEl("Edit", "PIN", "", "Edit", 10, UiPattern.Value) { IsPassword = true });
        d.Windows.Add(w);
        return (d, w, save, open, icon2, search, pin);
    }

    [Fact]
    public void Builds_a_selector_from_process_title_pattern_and_element_path()
    {
        var t = Tree();
        var s = UiSelectorBuilder.Build(t.Save, t.Desktop);
        Assert.Equal("notepad", s.Process);
        Assert.Equal("* - Notepad", s.WindowTitle);
        Assert.Equal("Pane(class=Toolbar) / Button(id=SaveBtn; name=Save; class=Button)", s.PathText);
        Assert.False(s.IsWeak);

        var weak = UiSelectorBuilder.Build(t.Icon2, t.Desktop);
        Assert.Equal(1, weak.Target!.Index);
        Assert.True(weak.IsWeak);
    }

    [Fact]
    public void Path_text_round_trips_with_escaped_characters()
    {
        var path = new[] { new UiNode("Pane", "a;b", "x/y (z)", "C=1", 2), new UiNode("Button", Name: "Save\\As") };
        var text = UiPathText.Format(path);
        var back = UiPathText.Parse(text)!;
        Assert.Equal(path, back);
        Assert.Empty(UiPathText.Parse("")!);
        Assert.Null(UiPathText.Parse("Button(name=Save"));
        Assert.Null(UiPathText.Parse("Button(colour=red)"));
    }

    [Theory]
    [InlineData("Untitled - Notepad", "* - Notepad")]
    [InlineData("Calculator", "Calculator")]
    [InlineData("Report.docx — Word", "* — Word")]
    [InlineData("", "")]
    public void Title_patterns_keep_the_app_part(string title, string pattern) => Assert.Equal(pattern, UiTitlePattern.From(title));

    [Fact]
    public void Title_pattern_matching_uses_star_and_ignores_case()
    {
        Assert.True(UiTitlePattern.Matches("* - Notepad", "notes.txt - notepad"));
        Assert.False(UiTitlePattern.Matches("* - Notepad", "Notepad"));
        Assert.True(UiTitlePattern.Matches("", "anything"));
        Assert.True(UiTitlePattern.Matches("a.b (1)", "A.B (1)"));
    }

    [Fact]
    public void Matcher_finds_the_recorded_control_after_the_window_title_changes()
    {
        var t = Tree();
        var s = UiSelectorBuilder.Build(t.Save, t.Desktop);
        t.Window.Name = "notes.txt - Notepad";
        var r = UiSelectorMatcher.Find(t.Desktop, s, "Notepad");
        Assert.Same(t.Save, r.Element);
        Assert.Same(t.Icon2, UiSelectorMatcher.Find(t.Desktop, UiSelectorBuilder.Build(t.Icon2, t.Desktop), "Notepad").Element);
    }

    [Fact]
    public void Matcher_falls_back_to_the_name_when_the_id_changed_and_to_a_search_when_the_layout_changed()
    {
        var t = Tree();
        var s = UiSelectorBuilder.Build(t.Save, t.Desktop);
        t.Save.AutomationId = "Btn_42";
        Assert.Same(t.Save, UiSelectorMatcher.Find(t.Desktop, s, "Notepad").Element);

        // The button moved into a new group: the path no longer fits, but its id and name are unique in the window.
        var t2 = Tree();
        var s2 = UiSelectorBuilder.Build(t2.Search, t2.Desktop);
        t2.Window.Remove(t2.Search);
        var group = new FakeEl("Group", "Find");
        group.Add(t2.Search);
        t2.Window.Add(group);
        Assert.Same(t2.Search, UiSelectorMatcher.Find(t2.Desktop, s2, "Notepad").Element);
    }

    [Fact]
    public void Matcher_does_not_guess_between_look_alikes_or_find_missing_controls()
    {
        var t = Tree();
        var s = UiSelectorBuilder.Build(t.Open, t.Desktop);
        var bar = (FakeEl)t.Open.Parent!;
        bar.Remove(t.Open);
        var g1 = new FakeEl("Group"); g1.Add(new FakeEl("Button", "Open", "", "Button", 10, UiPattern.Invoke));
        var g2 = new FakeEl("Group"); g2.Add(new FakeEl("Button", "Open", "", "Button", 10, UiPattern.Invoke));
        t.Window.Add(g1).Add(g2);
        var r = UiSelectorMatcher.Find(t.Desktop, s, "Notepad");
        Assert.Null(r.Element);
        Assert.Contains("2 controls look like it", r.Problem);

        var gone = UiSelectorMatcher.Find(t.Desktop, new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Button", Name: "Print") }), "Notepad");
        Assert.False(gone.Found);
        Assert.Contains("Notepad isn't open", UiSelectorMatcher.Find(t.Desktop, s with { Process = "winword" }, "Notepad").Problem.Replace("Notepad", "Notepad"));
    }

    [Fact]
    public void Matcher_checks_the_process_and_window_title()
    {
        var t = Tree();
        var calc = new FakeEl("Window", "Untitled - Notepad", pid: 20);
        t.Desktop.Windows.Insert(0, calc);
        var s = UiSelectorBuilder.Build(t.Save, t.Desktop);
        Assert.Same(t.Save, UiSelectorMatcher.Find(t.Desktop, s, "Notepad").Element);
        var window = UiSelectorMatcher.Find(t.Desktop, new UiSelector("notepad", "* - Word", Array.Empty<UiNode>()), "Notepad");
        Assert.False(window.Found);
        Assert.Equal("notepad", UiSelector.ProcessFromAppId(@"C:\Windows\System32\notepad.exe"));
    }
}

public class AppControlRecordingTests
{
    [Fact]
    public void Enter_in_a_text_box_presses_the_default_button_when_the_window_has_one()
    {
        var t = AppControlSelectorTests.Tree();
        var ok = new FakeEl("Button", "OK", "1", "", 10, UiPattern.Invoke);
        t.Window.Add(ok);
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        b.Key(t.Search, "a", "A");
        b.Key(t.Search, "", "Enter");
        var steps = b.Finish();
        Assert.Equal(new[] { AppControlIds.WaitWindow, AppControlIds.Type, AppControlIds.Click }, steps.Select(s => s.ActionId));
        Assert.Equal("Click 'OK' in Notepad", UiSteps.Describe(steps[^1]));
        Assert.Empty(b.LeftOut);
    }

    [Fact]
    public void Enter_outside_a_one_line_text_box_is_still_left_out()
    {
        var t = AppControlSelectorTests.Tree();
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        b.Key(t.Save, "", "Enter");
        Assert.Equal(new[] { "Pressing Enter" }, b.LeftOut);
    }

    [Fact]
    public void Clicks_and_typing_become_plain_word_steps_with_a_window_wait_first()
    {
        var t = AppControlSelectorTests.Tree();
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        b.Click(t.Search); // only moves focus: left out
        foreach (var c in "helo") b.Key(t.Search, c.ToString(), c.ToString());
        b.Key(t.Search, "", "Backspace", isBackspace: true);
        b.Key(t.Search, "o", "O");
        b.Click(t.Save);
        b.Key(t.Search, "", "Enter");
        var steps = b.Finish();
        Assert.Equal(new[] { AppControlIds.WaitWindow, AppControlIds.Type, AppControlIds.Click, AppControlIds.PressEnter }, steps.Select(s => s.ActionId));
        Assert.Equal(new[] { "Wait for the Notepad window '* - Notepad'", "Type 'helo' into 'Search'", "Click 'Save' in Notepad",
                "Press Enter in 'Search' (can't replay yet)" },
            steps.Select(UiSteps.Describe));
        Assert.Empty(b.LeftOut);
        Assert.DoesNotContain(steps.SelectMany(s => s.Parameters.Keys), k => k is "x" or "y");
    }

    [Fact]
    public void A_click_on_the_text_inside_a_button_records_the_button()
    {
        var t = AppControlSelectorTests.Tree();
        var label = new FakeEl("Text", "Save");
        t.Save.Add(label);
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        b.Click(label);
        Assert.Equal("Click 'Save' in Notepad", UiSteps.Describe(b.Finish()[^1]));
    }

    [Fact]
    public void Password_boxes_are_never_recorded_and_ask_each_time_instead()
    {
        var t = AppControlSelectorTests.Tree();
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        foreach (var c in "hunter2") b.Key(t.Pin, c.ToString(), c.ToString());
        Assert.DoesNotContain(b.Steps.SelectMany(s => s.Parameters.Values).OfType<TextValue>(), v => v.ToLiteralText().Contains("hunter", StringComparison.Ordinal));
        b.Key(t.Pin, "", "Backspace", isBackspace: true);
        var step = b.Finish()[^1];
        Assert.Equal(AppControlIds.Type, step.ActionId);
        Assert.IsType<AskValue>(step.Parameters["text"]);
        Assert.True(UiSteps.IsSecret(step));
        Assert.Equal("Type a password into 'PIN' (asks each time, never recorded)", UiSteps.Describe(step));
        var all = string.Join("|", step.Parameters.Values.Select(v => v.ToLiteralText()));
        Assert.DoesNotContain("hunter", all);
        Assert.DoesNotContain("2", UiSteps.TextParam(step, "text"));
    }

    [Fact]
    public void A_new_window_gets_its_own_wait_step()
    {
        var t = AppControlSelectorTests.Tree();
        var dialog = new FakeEl("Window", "Save As", cls: "#32770");
        dialog.With(out var ok, new FakeEl("Button", "OK", "1", "Button", 10, UiPattern.Invoke));
        t.Desktop.Windows.Add(dialog);
        var b = new UiRecordingBuilder(t.Desktop, "Notepad");
        b.Click(t.Save);
        b.Click(ok);
        b.Click(t.Open);
        Assert.Equal(new[] { "Wait for the Notepad window '* - Notepad'", "Click 'Save' in Notepad", "Wait for the Notepad window 'Save As'",
            "Click 'OK' in Notepad", "Wait for the Notepad window '* - Notepad'", "Click 'Open' in Notepad" }, b.Finish().Select(UiSteps.Describe));
    }

    [Fact]
    public void Plain_words_for_each_kind_of_step()
    {
        var s = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Edit", Name: "Search") });
        Assert.Equal("Type 'hello' into 'Search'", UiSteps.Describe(UiSteps.Type(s, "Notepad", "Search", "hello")));
        var ask = UiSteps.Type(s, "Notepad", "Search", "x").WithParam("text", ParamValue.Ask());
        Assert.Equal("Type what you enter into 'Search' (asks each time)", UiSteps.Describe(ask));
        var unnamed = new UiSelector("calc", "", new[] { new UiNode("Button") });
        Assert.Equal("Click the button in Calculator", UiSteps.Describe(UiSteps.Click(unnamed, "Calculator", UiSteps.LabelOf(unnamed.Target))));
        Assert.Equal("Wait for a Calculator window", UiSteps.Describe(UiSteps.WaitWindow("calc", "", "Calculator")));
        Assert.Equal("'" + new string('a', 39) + "…'", UiSteps.Quote(new string('a', 60)));
    }
}

public class AppControlReviewTests
{
    private static RecordingReview Review(out ActionInstance[] steps)
    {
        var s = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Edit", Name: "Search") });
        steps = new[]
        {
            UiSteps.WaitWindow("notepad", "* - Notepad", "Notepad"),
            UiSteps.Type(s, "Notepad", "Search", "hello"),
            UiSteps.Click(s with { Path = new[] { new UiNode("Button", Name: "Go") } }, "Notepad", "Go"),
            UiSteps.TypeSecret(s with { Path = new[] { new UiNode("Edit", Name: "PIN") } }, "Notepad", "PIN"),
        };
        return new RecordingReview(steps, new[] { "Pressing Enter" });
    }

    [Fact]
    public void Edits_text_and_ask_each_time_and_keeps_the_original_steps_untouched()
    {
        var r = Review(out var original);
        r.SetText(1, "bye");
        Assert.Equal("Type 'bye' into 'Search'", r.Describe(1));
        r.SetAskEachTime(1, true);
        Assert.True(r.AsksEachTime(1));
        Assert.Equal("bye", r.TextOf(1));
        r.SetAskEachTime(1, false);
        Assert.Equal("Type 'bye' into 'Search'", r.Describe(1));
        Assert.Equal("hello", UiSteps.TextParam(original[1], "text"));
        Assert.False(r.CanEditText(2));
        Assert.Throws<InvalidOperationException>(() => r.SetText(3, "1234"));
        Assert.Throws<InvalidOperationException>(() => r.SetAskEachTime(3, false));
        Assert.Equal(new[] { "Pressing Enter" }, r.LeftOut);
    }

    [Fact]
    public void Deletes_reorders_and_sets_waits()
    {
        var r = Review(out _);
        r.Move(2, 1);
        Assert.Equal("Click 'Go' in Notepad", r.Describe(1));
        r.SetTimeout(1, 0.2);
        Assert.Equal(1, r.TimeoutOf(1));
        r.SetTimeout(1, 999);
        Assert.Equal(300, r.TimeoutOf(1));
        r.SetTimeout(1, 12.34);
        Assert.Equal(12.3, r.TimeoutOf(1));
        r.Delete(3);
        Assert.Equal(3, r.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Delete(5));
        var result = r.Result();
        Assert.Equal(3, result.Count);
        Assert.NotSame(r.Steps[0], result[0]);
    }

    [Fact]
    public void A_window_wait_with_nothing_after_it_is_dropped_on_save()
    {
        var r = Review(out _);
        r.Delete(3); r.Delete(2); r.Delete(1);
        Assert.Empty(r.Result());
    }
}

public class AppControlReplayTests
{
    private static Task NoDelay(TimeSpan t, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task Refuses_with_the_element_named_when_it_is_missing()
    {
        var t = AppControlSelectorTests.Tree();
        var s = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Button", Name: "Print") });
        var clock = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            UiReplay.WaitForAsync(t.Desktop, s, "Notepad", "Print", TimeSpan.FromSeconds(3), CancellationToken.None,
                (d, _) => { clock += d; return Task.CompletedTask; }, () => clock));
        Assert.Contains("Couldn't find 'Print' in Notepad within 3 seconds", ex.Message);
        Assert.Contains("stopped instead of guessing", ex.Message);
        Assert.True(t.Desktop.Reads > 1);
        Assert.All(new[] { t.Save, t.Open, t.Icon2 }, e => Assert.Empty(e.Done));
    }

    [Fact]
    public async Task Finds_a_control_that_appears_while_waiting()
    {
        var t = AppControlSelectorTests.Tree();
        var print = new FakeEl("Button", "Print", "", "Button", 10, UiPattern.Invoke);
        var s = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Button", Name: "Print", ClassName: "Button") });
        var clock = DateTime.UtcNow;
        var found = await UiReplay.WaitForAsync(t.Desktop, s, "Notepad", "Print", TimeSpan.FromSeconds(5), CancellationToken.None,
            (d, _) => { clock += d; t.Window.Add(print); return Task.CompletedTask; }, () => clock);
        Assert.Same(print, found);
    }

    [Fact]
    public void Clicks_and_types_through_patterns_only_and_refuses_otherwise()
    {
        var t = AppControlSelectorTests.Tree();
        Assert.Equal("pressed", UiReplay.Click(t.Save, "Save"));
        Assert.Equal(new[] { "invoke" }, t.Save.Done);
        var check = new FakeEl("CheckBox", "Bold", patterns: UiPattern.Toggle);
        UiReplay.Click(check, "Bold");
        Assert.Equal(new[] { "toggle" }, check.Done);
        var label = new FakeEl("Text", "Hint");
        Assert.Throws<ActionException>(() => UiReplay.Click(label, "Hint"));
        t.Save.IsEnabled = false;
        Assert.Throws<ActionException>(() => UiReplay.Click(t.Save, "Save"));

        UiReplay.Type(t.Search, "hi", "Search");
        Assert.Equal(new[] { "value:hi" }, t.Search.Done);
        Assert.Throws<ActionException>(() => UiReplay.Type(t.Open, "hi", "Open"));
    }
}

public class AppControlSafetyTests
{
    [Fact]
    public void Links_always_ask_for_shortcuts_that_control_apps()
    {
        var settings = new AppSettings { LinksMode = LinkMode.PerShortcut };
        var id = Guid.NewGuid();
        settings.LinkTrustedShortcutIds = new() { id };
        Assert.Equal(LinkDecision.Run, LinkPolicy.Decide(settings, id, false));
        Assert.Equal(LinkDecision.Ask, LinkPolicy.Decide(settings, id, false, controlsApps: true));
        Assert.False(LinkPolicy.CanRemember(settings, false, controlsApps: true));
    }

    [Fact]
    public void Import_warns_about_steps_that_control_apps()
    {
        var w = new ImportWarning("u", AppControlIds.Click, "Click a Button in an App", ImportWarningKind.ControlsApps);
        Assert.Equal("Click a Button in an App (clicks and types in other apps)", w.Describe());
    }
}
