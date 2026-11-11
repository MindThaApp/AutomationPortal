using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Triggers;

namespace AutomationPortal.App.Dialogs;

/// <summary>Recent trigger events of one automation: fired, ran (with the result) or skipped (with the reason). Kept in memory only.</summary>
public static class AutomationActivityDialog
{
    public static Task ShowAsync(Window? owner, AutomationActivityLog log, Guid automationId, string title)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text("What this automation did since Automation Portal started, newest first. Nothing here is saved to disk.", secondary: true));
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var scroll = new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        AutomationProperties.SetName(scroll, "Activity list");
        body.Children.Add(scroll);

        var dlg = new FlowDialog("Activity: " + title, body, "Close", null, width: 600);

        void Refresh()
        {
            list.Children.Clear();
            var entries = log.Get(automationId);
            if (entries.Count == 0)
            {
                list.Children.Add(Ui.Text("Nothing has happened yet. When this automation's trigger fires, it shows up here.", secondary: true));
                return;
            }
            foreach (var e in entries)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(Ui.Text($"{Label(e.Kind)}  ·  {e.Time.LocalDateTime:G}", secondary: true, size: 12));
                row.Children.Add(Ui.Text(e.Text, wrap: true));
                list.Children.Add(row);
            }
        }

        Action<Guid> onAdded = id => { if (id == automationId) dlg.Dispatcher.BeginInvoke(Refresh); };
        log.Added += onAdded;
        Refresh();
        return dlg.ShowTopmostAsync(default, owner).ContinueWith(_ => log.Added -= onAdded, TaskScheduler.Default);
    }

    private static string Label(ActivityKind kind) => kind switch
    {
        ActivityKind.Fired => "Triggered",
        ActivityKind.Ran => "Ran",
        _ => "Skipped",
    };
}
