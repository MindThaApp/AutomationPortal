using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The first-run welcome, in three short pages: pick a few starter shortcuts, try one now (a harmless sample), keep it handy.
/// Back and Next move between pages; Skip or Esc closes it without adding anything.
/// </summary>
public static class WelcomeDialog
{
    public static async Task ShowAsync(AppHost host, Window? owner)
    {
        var s = host.Settings.Current;
        var pages = new List<FrameworkElement>();
        var checks = new List<(System.Windows.Controls.CheckBox Box, GalleryItem Item)>();

        // Page 1: pick a few. Only ones that need no questions, so they import exactly like the Gallery's "Add shortcut".
        var one = new StackPanel();
        one.Children.Add(PageTitle(Strings.Get("WelcomeDialog_PickAFewTo")));
        one.Children.Add(Ui.Text("Automation Portal does small jobs for you with one click or one hotkey. These are ready to use; you can change or remove any of them later.", secondary: true, size: 13));
        var offered = new List<(string FileName, GalleryItem Item)>();
        foreach (var section in host.GallerySections())
            foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0))
                offered.Add((item.FilePath, item));
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var item in FirstRun.PickStarters(offered))
        {
            var text = new StackPanel();
            text.Children.Add(Ui.Text(item.Shortcut.Name, size: 14));
            if (!string.IsNullOrWhiteSpace(item.Shortcut.Description)) text.Children.Add(Ui.Text(item.Shortcut.Description!, secondary: true, size: 12));
            var box = new System.Windows.Controls.CheckBox { Content = text, Margin = new Thickness(0, 2, 0, 2), IsChecked = FirstRun.IsTickedByDefault(item.FilePath) };
            AutomationProperties.SetName(box, item.Shortcut.Name);
            list.Children.Add(box);
            checks.Add((box, item));
        }
        if (checks.Count == 0) list.Children.Add(Ui.Text("Ready-made shortcuts are in the Gallery whenever you want them.", secondary: true, size: 13));
        one.Children.Add(list);
        pages.Add(one);

        // Page 2: try one now. A sample that shows a notification and changes nothing.
        var two = new StackPanel();
        two.Children.Add(PageTitle(Strings.Get("WelcomeDialog_TryOneNow")));
        two.Children.Add(Ui.Text("This is what a shortcut is: a small job that runs when you ask. This one only shows a notification. It changes nothing on your PC.", secondary: true, size: 13));
        var card = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16), CornerRadius = AutomationPortal.Editor.Radius.Card, BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        var cardRow = new Grid();
        cardRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cardRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = Ui.Chip("Sparkle24", "blue", 44, 24);
        chip.Margin = new Thickness(0, 0, 12, 0);
        cardRow.Children.Add(chip);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(Strings.Get("WelcomeDialog_SayHello"), size: 15, weight: FontWeights.SemiBold));
        names.Children.Add(Ui.Text(Strings.Get("WelcomeDialog_ShowsANotification"), secondary: true, size: 12));
        Grid.SetColumn(names, 1);
        cardRow.Children.Add(names);
        var runButton = new Wpf.Ui.Controls.Button { Content = Strings.Get("WelcomeDialog_RunIt"), Appearance = ControlAppearance.Primary, Icon = new SymbolIcon(SymbolRegular.Play24), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(runButton, "Run the sample shortcut");
        Grid.SetColumn(runButton, 2);
        cardRow.Children.Add(runButton);
        card.Child = cardRow;
        two.Children.Add(card);
        var resultText = Ui.Text("", size: 13);
        resultText.Margin = new Thickness(2, 14, 0, 0);
        AutomationProperties.SetLiveSetting(resultText, AutomationLiveSetting.Polite);
        two.Children.Add(resultText);
        runButton.Click += async (_, _) =>
        {
            runButton.IsEnabled = false;
            resultText.Text = "Running...";
            string message;
            try
            {
                var result = await host.Runs.RunAsync(SampleShortcut.Create(), null, RunSource.Library);
                message = result is { Status: RunStatus.Succeeded }
                    ? "Done. You should see a notification near the clock. That is all a shortcut is."
                    : Strings.Get("WelcomeDialog_ItDidntFinish") + (result?.ErrorMessage is { Length: > 0 } m ? ": " + m : ". You can carry on; shortcuts you add run the same way.");
            }
            catch (Exception ex) { message = "It didn't finish: " + ex.Message; }
            resultText.Text = message;
            runButton.IsEnabled = true;
            runButton.Content = Strings.Get("WelcomeDialog_RunItAgain");
        };
        pages.Add(two);

        // Page 3: keep it handy
        var three = new StackPanel();
        three.Children.Add(PageTitle(Strings.Get("WelcomeDialog_KeepItHandy")));
        three.Children.Add(Ui.Text(Strings.Get("WelcomeDialog_RunAnythingByTyping"), secondary: true, size: 13));
        var key = s.QuickRunHotkey;
        if (string.IsNullOrWhiteSpace(key))
            three.Children.Add(Ui.Text("Quick Run is a small bar to find and run any shortcut. It has no hotkey yet; you can choose one in Settings.", size: 13));
        else
        {
            var caps = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
            foreach (var part in key.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cap = new Border { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), CornerRadius = AutomationPortal.Editor.Radius.Control, BorderThickness = new Thickness(1, 1, 1, 3) };
                cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
                cap.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
                cap.Child = Ui.Text(part, size: 13, weight: FontWeights.SemiBold, wrap: false);
                caps.Children.Add(cap);
            }
            three.Children.Add(caps);
            three.Children.Add(Ui.Text(Strings.Get("WelcomeDialog_PressTheseTogetherA"), size: 13));
        }
        var start = new ToggleSwitch { Content = Strings.Get("WelcomeDialog_StartWithWindows"), IsChecked = s.StartWithWindows, Margin = new Thickness(0, 18, 0, 0) };
        AutomationProperties.SetName(start, "Start with Windows");
        start.Click += (_, _) =>
        {
            var on = start.IsChecked == true;
            try { host.Shell.SetStartWithWindows(on); }
            catch (Exception ex) { _ = Prompts.MessageAsync(owner, "Couldn't change that", ex.Message); }
            host.Settings.Update(x => x.StartWithWindows = on);
        };
        three.Children.Add(start);
        var tray = Ui.Text("Automation Portal lives in the icon near the clock. Closing the window doesn't quit it: it keeps running there so your hotkeys and automations keep working. Right-click the icon and choose Exit to quit.", secondary: true, size: 12);
        tray.Margin = new Thickness(0, 8, 0, 0);
        three.Children.Add(tray);
        System.Windows.Controls.CheckBox? crash = null;
        if (Startup.CrashReporting.IsAvailable)
        {
            crash = new System.Windows.Controls.CheckBox { Content = Strings.Get("WelcomeDialog_CrashReports"), IsChecked = true, Margin = new Thickness(0, 18, 0, 0) };
            AutomationProperties.SetName(crash, Strings.Get("WelcomeDialog_CrashReports"));
            three.Children.Add(crash);
            var whatsSent = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("WelcomeDialog_WhatsSent"), NavigateUri = "https://mindthaapp.github.io/automation-portal/privacy.html#crash-reports", Padding = new Thickness(0), Margin = new Thickness(24, 4, 0, 0) };
            three.Children.Add(whatsSent);
        }
        pages.Add(three);

        // Frame: the current page and three step dots
        var pageHost = new ContentControl { MinHeight = 300 };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var dotShapes = pages.Select(_ => new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0) }).ToList();
        foreach (var d in dotShapes) dots.Children.Add(d);
        var frame = new StackPanel();
        frame.Children.Add(pageHost);
        frame.Children.Add(dots);

        var dlg = new FlowDialog(Strings.Get("WelcomeDialog_WelcomeToAutomationPortal"), frame, "Next", "Skip", width: 540);
        var back = dlg.AddLeftButton("Back", () => { });
        var page = 0;
        void Show(bool? forward = null)
        {
            pageHost.Content = pages[page];
            if (forward is bool dir && pages[page] is FrameworkElement shown) PageSlide.Play(shown, dir);
            back.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
            dlg.SetPrimaryText(page == pages.Count - 1 ? "Continue" : "Next");
            for (var i = 0; i < dotShapes.Count; i++)
                dotShapes[i].SetResourceReference(System.Windows.Shapes.Shape.FillProperty, i == page ? "DsAccentBrush" : "ControlStrongStrokeColorDefaultBrush");
        }
        back.Click += (_, _) => { if (page > 0) { page--; Show(false); } };
        dlg.Validate = () =>
        {
            if (page < pages.Count - 1) { page++; Show(true); return false; }
            return true;
        };
        dlg.Loaded += (_, _) => Show();

        // Remembered as soon as it opens, so a crash or a forced close never brings it back.
        if (!s.WelcomeShown) host.Settings.Update(x => x.WelcomeShown = true);

        bool accepted;
        try { accepted = await dlg.ShowTopmostAsync(default, owner); }
        finally
        {
            // Saved however the welcome closes (Continue, Skip or Esc).
            if (crash is not null)
            {
                var on = crash.IsChecked == true;
                host.Settings.Update(x => x.SendCrashReports = on);
                if (Startup.CrashReporting.Current is { } reporter) _ = Task.Run(() => reporter.Apply(on));
            }
        }
        if (!accepted) return;
        // Starters are new shortcuts too: as many as the plan has room for are added, then the upgrade window explains the rest.
        var room = host.Plan.ShortcutRoom(host.Library.All.Count);
        var skipped = 0;
        foreach (var (box, item) in checks)
        {
            if (box.IsChecked != true) continue;
            if (room <= 0) { skipped++; continue; }
            room--;
            try
            {
                var result = host.Library.Import(item.Shortcut.Clone());   // a name that's taken gets a new one; nothing is replaced
                await host.FinishImportAsync(result, owner, warningsShown: false);
            }
            catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't add it", ex.Message); }
        }
        if (skipped > 0) host.AllowNewShortcut(owner, skipped);
    }

    private static System.Windows.Controls.TextBlock PageTitle(string text)
    {
        var t = Ui.Text(text, size: 20, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 6);
        return t;
    }
}

