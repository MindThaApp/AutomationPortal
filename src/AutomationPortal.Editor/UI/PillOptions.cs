using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.UI;

/// <summary>What you can do with a variable pill: pick a property, convert it, use first / last / random, or remove it.</summary>
internal static class PillOptions
{
    private static readonly (ContentKind Kind, string Label)[] ConvertKinds =
    {
        (ContentKind.Text, "Text"), (ContentKind.Number, "Number"), (ContentKind.Boolean, "Yes / No"), (ContentKind.Date, "Date"),
        (ContentKind.Url, "Link"), (ContentKind.File, "File"), (ContentKind.Folder, "Folder"), (ContentKind.Image, "Image"), (ContentKind.Dictionary, "Dictionary"),
    };

    public static void Show(FrameworkElement anchor, EditorEnv env, VarRef current, Func<IReadOnlyList<VariableOption>> variables,
        Action<VarRef> onChange, Action onRemove)
    {
        var host = FloatingPanel.Containing(anchor);
        var root = new StackPanel { Width = 330 };
        var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, Ui.Primary);
        root.Children.Add(title);

        var baseKind = env.Describe(current with { Property = null, CoerceTo = null }).Kind;
        var props = PropertiesFor(baseKind);
        var propsBox = new WrapPanel();
        var convBox = new WrapPanel();
        var pickBox = new WrapPanel();
        var ref_ = current;
        FloatingPanel? own = null;

        void Apply(VarRef next)
        {
            ref_ = next;
            onChange(next);
            Render();
        }

        void Render()
        {
            title.Text = env.Describe(ref_).Label;
            propsBox.Children.Clear();
            propsBox.Children.Add(Chip("Whole value", ref_.Property is null, () => Apply(ref_ with { Property = null })));
            foreach (var p in props) propsBox.Children.Add(Chip(p, string.Equals(ref_.Property, p, StringComparison.OrdinalIgnoreCase), () => Apply(ref_ with { Property = p })));
            if (ref_.Property is { } cur && !props.Contains(cur, StringComparer.OrdinalIgnoreCase))
                propsBox.Children.Add(Chip(cur.StartsWith("key:", StringComparison.OrdinalIgnoreCase) ? cur[4..] : cur, true, () => { }));
            convBox.Children.Clear();
            convBox.Children.Add(Chip("As is", ref_.CoerceTo is null, () => Apply(ref_ with { CoerceTo = null })));
            foreach (var (k, label) in ConvertKinds) convBox.Children.Add(Chip(label, ref_.CoerceTo == k, () => Apply(ref_ with { CoerceTo = k })));
            pickBox.Children.Clear();
            foreach (var (p, label) in new[] { (VarPick.All, "All items"), (VarPick.First, "First item"), (VarPick.Last, "Last item"), (VarPick.Random, "Random item") })
                pickBox.Children.Add(Chip(label, ref_.Pick == p, () => Apply(ref_ with { Pick = p })));
        }

        root.Children.Add(Header("Get"));
        root.Children.Add(propsBox);
        if (baseKind is ContentKind.Dictionary or ContentKind.Any)
        {
            var key = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Or type a dictionary key (for example city)", Margin = new Thickness(0, 0, 0, 4) };
            key.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || string.IsNullOrWhiteSpace(key.Text)) return;
                e.Handled = true;
                Apply(ref_ with { Property = "key:" + key.Text.Trim() });
                key.Text = "";
            };
            root.Children.Add(key);
        }
        root.Children.Add(Header("Convert to"));
        root.Children.Add(convBox);
        root.Children.Add(Header("Use"));
        root.Children.Add(pickBox);
        root.Children.Add(Ui.Divider());
        root.Children.Add(Ui.TextButton("Remove variable", () =>
        {
            onRemove();
            if (host is not null) host.Back(); else own?.Close();
        }, Wpf.Ui.Controls.SymbolRegular.Delete24, Wpf.Ui.Controls.ControlAppearance.Danger));
        Render();

        if (host is not null) host.Navigate(root);
        else own = FloatingPanel.Open(anchor, root, 330);
    }

    private static UIElement Header(string text)
    {
        var t = Ui.Text(text, Ui.Secondary, 12, FontWeights.SemiBold);
        t.Margin = new Thickness(0, 8, 0, 4);
        return t;
    }

    private static ToggleButton Chip(string text, bool selected, Action click)
    {
        var b = new ToggleButton { Content = text, IsChecked = selected, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 13 };
        b.Click += (_, _) => click();
        return b;
    }

    private static IReadOnlyList<string> PropertiesFor(ContentKind kind)
    {
        if (kind != ContentKind.Any) return VariableCatalog.PropertiesOf(kind);
        return VariableCatalog.PropertiesOf(ContentKind.Text).Concat(VariableCatalog.PropertiesOf(ContentKind.File)).Distinct().ToList();
    }
}
