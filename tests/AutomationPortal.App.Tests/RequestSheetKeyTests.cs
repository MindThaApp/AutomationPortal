using AutomationPortal.App.Dialogs;
using Xunit;

namespace AutomationPortal.App.Tests;

public class RequestSheetKeyTests
{
    [Fact]
    public void Run_gets_no_access_key_on_request_sheets_and_starts_disabled()
    {
        SharedSta.Run(() =>
        {
            var run = new Wpf.Ui.Controls.Button { Content = "_Run" };
            var dontRun = new Wpf.Ui.Controls.Button { Content = "Don't run (60)" };
            FlowDialog.AssignAccessKeys(run, primaryKey: false, dontRun, Array.Empty<System.Windows.Controls.Primitives.ButtonBase>());
            Assert.Equal("Run", run.Content);
            Assert.Equal("_Don't run (60)", dontRun.Content);

            // Other dialogs keep their access keys.
            var ok = new Wpf.Ui.Controls.Button { Content = "Run" };
            FlowDialog.AssignAccessKeys(ok, primaryKey: true, new Wpf.Ui.Controls.Button { Content = "Cancel" }, Array.Empty<System.Windows.Controls.Primitives.ButtonBase>());
            Assert.Equal("_Run", ok.Content);

            var window = new System.Windows.Window();
            var held = new Wpf.Ui.Controls.Button { Content = "Run" };
            FlowDialog.HoldDisabled(held, Prompts.RunButtonDelay, window);
            Assert.False(held.IsEnabled);   // enabled only a second after the sheet has rendered
            Assert.Equal(TimeSpan.FromSeconds(1), Prompts.RunButtonDelay);
            window.Close();
        });
    }
}
