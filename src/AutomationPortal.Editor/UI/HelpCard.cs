using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Core;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor.UI;

/// <summary>Plain-English help for an action: what it does, what it takes, what it makes.</summary>
internal static class HelpCard
{
    public static FrameworkElement Build(ActionDescriptor d)
    {
        var root = new StackPanel { Width = 300 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Ui.IconChip(d.Icon, d.Color, 30, 17, 8));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(d.Title, Ui.Primary, 15, FontWeights.SemiBold));
        titles.Children.Add(Ui.Text(d.Category, Ui.Secondary, 12));
        head.Children.Add(titles);
        root.Children.Add(head);
        if (!string.IsNullOrWhiteSpace(d.Description)) root.Children.Add(Ui.Text(d.Description, Ui.Primary, 13, wrap: true));

        void Line(string label, string value)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Ui.Text(label, Ui.Secondary, 12, FontWeights.SemiBold));
            row.Children.Add(Ui.Text(value, Ui.Primary, 13, wrap: true));
            root.Children.Add(row);
        }

        if (d.InputKinds.Count > 0) Line("Works with", string.Join(", ", d.InputKinds.Select(Kind)));
        if (d.HasOutput && d.FlowKind != FlowKind.Menu) Line("Gives you", $"{d.EffectiveOutputName} ({Kind(d.OutputKind)})");
        var notes = new List<string>();
        if (d.NeedsInternet) notes.Add("Needs an internet connection");
        if (d.IsDestructive) notes.Add("Can change or delete things. Double-check before running");
        if (d.IsAdvanced) notes.Add("Advanced action");
        if (notes.Count > 0) Line("Good to know", string.Join(". ", notes));
        return root;
    }

    private static string Kind(ContentKind k) => EditorViewModel.KindName(k);
}
