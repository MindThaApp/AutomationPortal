using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>The short welcome: pick starter shortcuts, try Quick Run, choose whether to start with Windows. Skip or Esc closes it.</summary>
public static class WelcomeDialog
{
    public static async Task ShowAsync(AppHost host, Window? owner)
    {
        var body = new StackPanel();
        var s = host.Settings.Current;

        void Heading(string text, double top)
        {
            var h = Ui.Text(text, size: 15, weight: FontWeights.SemiBold);
            h.Margin = new Thickness(0, top, 0, 6);
            body.Children.Add(h);
        }

        body.Children.Add(Ui.Text("Automation Portal does small jobs for you with one click or one hotkey. Three quick things to get going.", secondary: true, size: 13));

        // 1. Starter shortcuts. Only ones that need no questions, so they import exactly like the Gallery's "Add shortcut".
        Heading("1. Pick some starter shortcuts", 18);
        var offered = new List<(string FileName, GalleryItem Item)>();
        foreach (var section in host.GallerySections())
            foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0))
                offered.Add((item.FilePath, item));
        var checks = new List<(CheckBox Box, GalleryItem Item)>();
        foreach (var item in FirstRun.PickStarters(offered))
        {
            var text = new StackPanel();
            text.Children.Add(Ui.Text(item.Shortcut.Name, size: 14));
            if (!string.IsNullOrWhiteSpace(item.Shortcut.Description)) text.Children.Add(Ui.Text(item.Shortcut.Description!, secondary: true, size: 12));
            var box = new CheckBox
            {
                Content = text, Margin = new Thickness(0, 2, 0, 2),
                IsChecked = string.Equals(Path.GetFileNameWithoutExtension(item.FilePath), FirstRun.TickedByDefault, StringComparison.OrdinalIgnoreCase),
            };
            AutomationProperties.SetName(box, item.Shortcut.Name);
            body.Children.Add(box);
            checks.Add((box, item));
        }
        if (checks.Count == 0) body.Children.Add(Ui.Text("Ready-made shortcuts are in the Gallery whenever you want them.", secondary: true, size: 13));

        // 2. Quick Run
        Heading("2. Try Quick Run", 18);
        var key = s.QuickRunHotkey;
        body.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(key)
            ? "Quick Run is a small bar to find and run anything by typing. It has no hotkey yet; you can choose one in Settings."
            : $"Press {key} now. A small bar opens where you type to find and run any shortcut.", size: 13));

        // 3. Start with Windows
        Heading("3. Keep it handy", 18);
        var start = new ToggleSwitch { Content = "Start with Windows", IsChecked = s.StartWithWindows };
        AutomationProperties.SetName(start, "Start with Windows");
        start.Click += (_, _) =>
        {
            var on = start.IsChecked == true;
            try { host.Shell.SetStartWithWindows(on); }
            catch (Exception ex) { _ = Prompts.MessageAsync(owner, "Couldn't change that", ex.Message); }
            host.Settings.Update(x => x.StartWithWindows = on);
        };
        body.Children.Add(start);
        var tray = Ui.Text("Automation Portal lives in the icon near the clock. Closing the window doesn't quit it: it keeps running there so your hotkeys and automations keep working. Right-click the icon and choose Exit to quit.", secondary: true, size: 12);
        tray.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(tray);

        var dlg = new FlowDialog("Welcome to Automation Portal", body, "Add selected", "Skip", width: 540);
        void Refresh() => dlg.SetPrimaryEnabled(checks.Any(c => c.Box.IsChecked == true));
        foreach (var (box, _) in checks) { box.Checked += (_, _) => Refresh(); box.Unchecked += (_, _) => Refresh(); }
        dlg.Loaded += (_, _) => Refresh();

        // Remembered as soon as it opens, so a crash or a forced close never brings it back.
        if (!s.WelcomeShown) host.Settings.Update(x => x.WelcomeShown = true);

        if (!await dlg.ShowTopmostAsync(default, owner)) return;
        foreach (var (box, item) in checks)
        {
            if (box.IsChecked != true) continue;
            try
            {
                var result = host.Library.Import(item.Shortcut.Clone());   // a name that's taken gets a new one; nothing is replaced
                await host.FinishImportAsync(result, owner, warningsShown: false);
            }
            catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't add it", ex.Message); }
        }
    }
}
