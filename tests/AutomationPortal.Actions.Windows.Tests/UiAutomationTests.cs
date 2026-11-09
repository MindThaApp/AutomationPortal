using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AutomationPortal.Interop;

namespace AutomationPortal.Actions.Windows.Tests;

/// <summary>Checks the hand-declared UIAutomationCore COM interfaces against a window the test owns (nothing is typed or clicked).</summary>
[Trait("Category", "Live")]
public class UiAutomationTests
{
    [Fact]
    public async Task Reads_a_text_box_value_and_selection_through_the_COM_client()
    {
        Dispatcher? dispatcher = null;
        IntPtr hwnd = IntPtr.Zero;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var box = new TextBox { Text = "hello world" };
            var window = new Window { Width = 300, Height = 120, ShowActivated = false, ShowInTaskbar = false, Content = box, Left = -2000, Top = -2000 };
            window.Show();
            box.Select(0, 5);
            hwnd = new WindowInteropHelper(window).Handle;
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        try
        {
            var (value, selected, password) = await Task.Run(() =>
            {
                var uia = Uia.Create();
                if (uia.GetFocusedElement() is { } focused) _ = Uia.GetString(focused, Uia.NameProperty);
                _ = Uia.FocusedIsPassword();
                var root = uia.ElementFromHandle(hwnd);
                var edit = root.FindFirst(Uia.TreeScopeDescendants, uia.CreatePropertyCondition(Uia.ControlTypeProperty, Uia.EditControlType));
                Assert.NotNull(edit);
                var all = root.FindAll(Uia.TreeScopeDescendants, uia.CreateAndCondition(uia.CreateTrueCondition(), uia.CreatePropertyCondition(Uia.ControlTypeProperty, Uia.EditControlType)));
                Assert.Equal(1, all!.Length);
                Assert.Equal(Uia.GetString(edit!, Uia.AutomationIdProperty), Uia.GetString(all.GetElement(0), Uia.AutomationIdProperty));
                Assert.True(Uia.GetBool(edit!, Uia.IsValuePatternAvailableProperty));
                return (Uia.GetString(edit!, Uia.ValueValueProperty), Uia.SelectedText(edit!), Uia.GetBool(edit!, Uia.IsPasswordProperty));
            });
            Assert.Equal("hello world", value);
            Assert.Equal("hello", selected);
            Assert.False(password);
        }
        finally
        {
            dispatcher!.InvokeShutdown();
        }
    }
}
