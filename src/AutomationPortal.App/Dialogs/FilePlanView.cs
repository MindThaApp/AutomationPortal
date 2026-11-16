using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>The list a preview shows: one line per planned change ("Move report.pdf → …"), in a scrolling box.</summary>
public static class FilePlanView
{
    public const int MaxLines = 300;

    public static FrameworkElement Build(FileChangePlan plan, double maxHeight = 240)
    {
        var lines = new StackPanel();
        var changes = plan.Changes;
        foreach (var c in changes.Take(MaxLines))
        {
            var line = Ui.Text(c.Describe(), secondary: !c.ChangesFiles, size: 13);
            line.Margin = new Thickness(0, 0, 0, 4);
            lines.Children.Add(line);
        }
        if (changes.Count > MaxLines) lines.Children.Add(Ui.Text($"… and {changes.Count - MaxLines} more", secondary: true, size: 13));
        if (changes.Count == 0) lines.Children.Add(Ui.Text("Nothing would change right now.", secondary: true, size: 13));
        var scroll = new ScrollViewer { Content = lines, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
        AutomationProperties.SetName(scroll, "Planned file changes");
        return new Border
        {
            Child = scroll, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0),
        }.WithResource(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush").WithResource(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
    }

    private static Border WithResource(this Border b, DependencyProperty p, string key)
    {
        b.SetResourceReference(p, key);
        return b;
    }

    /// <summary>
    /// Fills <paramref name="host"/> with the preview of <paramref name="shortcut"/>: a summary line and the list. Runs nothing for
    /// real; a shortcut that asks for files shows the usual file picker first.
    /// </summary>
    /// <returns>The plan, or null when the preview couldn't be made.</returns>
    public static async Task<FileChangePlan?> ShowPreviewAsync(Panel host, IShortcutRunner runner, Shortcut shortcut, ContentValue input)
    {
        host.Children.Clear();
        host.Children.Add(Ui.Text("Working out what it would do…", secondary: true, size: 13));
        FileChangePlan plan;
        try { plan = await FilePreview.RunAsync(runner, shortcut, input); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            host.Children.Clear();
            host.Children.Add(Ui.Text("Couldn't make the preview: " + ex.Message, size: 13));
            return null;
        }
        host.Children.Clear();
        var summary = Ui.Text(plan.Summary() + " Nothing has been changed.", size: 13, weight: FontWeights.SemiBold);
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        host.Children.Add(summary);
        host.Children.Add(Build(plan));
        return plan;
    }
}
