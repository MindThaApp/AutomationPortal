using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;
using AutomationPortal.Core.AppControl;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Windows.Tests;

/// <summary>The steps that work other apps, run against a pretend control tree: no real window is opened or touched.</summary>
public sealed class AppControlActionTests : IDisposable
{
    private sealed class El : IUiElement
    {
        private readonly List<El> _kids = new();
        public El(string type, string name, int pid = 7, bool password = false, params UiPattern[] p) { ControlType = type; Name = name; ProcessId = pid; IsPassword = password; Patterns = p; }
        public UiPattern[] Patterns { get; }
        public List<string> Done { get; } = new();
        public string AutomationId => "";
        public string Name { get; }
        public string ControlType { get; }
        public string ClassName => "";
        public int ProcessId { get; }
        public bool IsPassword { get; }
        public bool IsEnabled => true;
        public IUiElement? Parent { get; private set; }
        public IReadOnlyList<IUiElement> Children => _kids;
        public El Add(El c) { c.Parent = this; _kids.Add(c); return this; }
        public bool SameAs(IUiElement other) => ReferenceEquals(this, other);
        public bool Supports(UiPattern pattern) => Patterns.Contains(pattern);
        public void Invoke() => Done.Add("invoke");
        public void SetValue(string text) => Done.Add("value:" + text);
        public void Toggle() => Done.Add("toggle");
        public void Select() => Done.Add("select");
        public void ExpandOrCollapse() => Done.Add("expand");
        public void Focus() => Done.Add("focus");
    }

    private sealed class Desk : IUiDesktop
    {
        public List<El> Windows { get; } = new();
        public IReadOnlyList<IUiElement> TopWindows() => Windows;
        public IUiElement? ElementAt(int x, int y) => null;
        public IUiElement? Focused() => null;
        public string ProcessName(int processId) => processId == 7 ? "notepad" : "";
    }

    private readonly Desk _desk = new();
    private readonly El _window = new("Window", "Untitled - Notepad");
    private readonly El _save = new("Button", "Save", 7, false, UiPattern.Invoke);
    private readonly El _pin = new("Edit", "PIN", 7, true, UiPattern.Value);

    public AppControlActionTests()
    {
        _window.Add(_save).Add(_pin);
        _desk.Windows.Add(_window);
        AppControlHost.Desktop = () => _desk;
        AppControlHost.Delay = (_, _) => Task.Delay(1);
    }

    public void Dispose()
    {
        AppControlHost.Desktop = () => new AutomationPortal.Interop.UiaDesktop();
        AppControlHost.Delay = (t, ct) => Task.Delay(t, ct);
    }

    private static (string, ParamValue)[] Params(string control, double timeout = 0.05, params (string, ParamValue)[] more) =>
        new (string, ParamValue)[]
        {
            ("app", ParamValue.App("notepad", "Notepad")), ("window", ParamValue.Text("* - Notepad")),
            ("control", ParamValue.Text(control)), ("timeout", ParamValue.Duration(timeout)),
        }.Concat(more).ToArray();

    [Fact]
    public async Task Click_presses_the_recorded_button_through_its_pattern()
    {
        using var host = new TestHost();
        await ActionTestHarness.RunAsync(host, new ClickInAppAction(), Params("Button(name=Save)"));
        Assert.Equal(new[] { "invoke" }, _save.Done);
        Assert.Equal(new[] { "focus" }, _window.Done);
    }

    [Fact]
    public async Task Click_refuses_and_names_the_control_when_it_is_missing()
    {
        using var host = new TestHost();
        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            ActionTestHarness.RunAsync(host, new ClickInAppAction(), Params("Button(name=Print)", 0.05, ("label", ParamValue.Text("Print")))));
        Assert.Contains("Couldn't find 'Print' in Notepad", ex.Message);
        Assert.Empty(_save.Done);
        Assert.Empty(_window.Done);
    }

    [Fact]
    public async Task Typing_into_a_password_box_asks_each_time_and_types_the_answer()
    {
        using var host = new TestHost();
        host.UI.Enqueue("s3cret");
        await ActionTestHarness.RunAsync(host, new TypeInAppAction(),
            Params("Edit(name=PIN)", 1, ("text", ParamValue.Ask()), ("secret", ParamValue.Bool(true)), ("label", ParamValue.Text("PIN"))));
        Assert.Equal(new[] { "value:s3cret" }, _pin.Done);
        Assert.Contains(host.UI.Prompts, p => p.Contains("password", StringComparison.OrdinalIgnoreCase) && p.Contains("PIN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Typed_text_can_hold_a_variable_such_as_the_clipboard()
    {
        using var host = new TestHost();
        host.Clipboard.Value = ContentValue.Text("pasted");
        var edit = new El("Edit", "Search", 7, false, UiPattern.Value);
        _window.Add(edit);
        await ActionTestHarness.RunAsync(host, new TypeInAppAction(),
            Params("Edit(name=Search)", 1, ("text", ParamValue.Text("Find ", VarRef.Clipboard)), ("secret", ParamValue.Bool(false))));
        Assert.Equal(new[] { "value:Find pasted" }, edit.Done);
    }

    [Fact]
    public async Task Press_enter_step_fails_the_run_with_a_plain_message_unless_continue_is_set()
    {
        using var host = new TestHost().With(typeof(PressEnterInAppAction).Assembly);
        var failing = new ShortcutBuilder("Enter");
        failing.Add(AppControlIds.PressEnter, ("control", "Edit(name=PIN)"));
        var r = await host.Runner.RunAsync(failing.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Equal("This step can't be replayed. Record it again.", r.ErrorMessage);

        var carrying = new ShortcutBuilder("Enter");
        carrying.Add(AppControlIds.PressEnter, ("control", "Edit(name=PIN)"));
        var shortcut = carrying.Build();
        shortcut.Actions[0].OnErrorContinue();
        r = await host.Runner.RunAsync(shortcut,ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(RunStatus.Succeeded, r.Status);
    }

    [Fact]
    public async Task Press_enter_steps_explain_they_cannot_replay_and_touch_nothing()
    {
        using var host = new TestHost();
        var ex = await Assert.ThrowsAsync<ActionException>(() =>
            ActionTestHarness.RunAsync(host, new PressEnterInAppAction(), Params("Edit(name=PIN)")));
        Assert.Equal("This step can't be replayed. Record it again.", ex.Message);
        Assert.Empty(_pin.Done);
        Assert.Empty(_window.Done);
    }

    [Fact]
    public void Steps_that_control_apps_are_flagged_for_imports_and_consent()
    {
        foreach (var a in new ShortcutAction[] { new ClickInAppAction(), new TypeInAppAction(), new WaitForWindowAction(), new PressEnterInAppAction() })
        {
            Assert.Equal(ImportRisk.ControlsApps, a.Descriptor.ImportRisk);
            Assert.True(AppControlIds.IsAppControl(a.Descriptor.Id));
        }
    }
}
