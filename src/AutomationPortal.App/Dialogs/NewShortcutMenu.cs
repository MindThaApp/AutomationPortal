using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.Actions.Documents;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The menu under "New shortcut": Start blank, Describe it (a sentence the AI turns into a draft), Record a new shortcut.
/// "Describe it" is replaced by a hint that links to Settings › AI when no AI is set up.
/// </summary>
public static class NewShortcutMenu
{
    public static void Show(AppHost host, FrameworkElement anchor, Guid? folderId)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
        };
        var card = new Border { Width = 380, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(10, 4, 10, 14) };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.3, Color = Colors.Black };
        var stack = new StackPanel();
        card.Child = stack;
        popup.Child = card;
        AutomationProperties.SetName(card, "New shortcut");

        TextBlock Label(string text, double size, bool secondary, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
            return t;
        }

        var blank = new Wpf.Ui.Controls.Button { Content = "Start blank", Icon = new SymbolIcon(SymbolRegular.Add24), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        blank.Click += (_, _) => { popup.IsOpen = false; host.EditorLauncher.OpenNew(folderId); };
        stack.Children.Add(blank);

        var divider = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        divider.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        stack.Children.Add(divider);

        stack.Children.Add(Label("Describe it", 14, false, FontWeights.SemiBold));
        if (ShortcutAiService.IsReady(host.Services))
        {
            var hint = Label("Say what you want in a sentence. You get a draft to check; nothing runs by itself.", 12.5, true);
            hint.Margin = new Thickness(0, 2, 0, 8);
            stack.Children.Add(hint);
            var box = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = "Every morning, tell me the weather and open Outlook", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74,
            };
            AutomationProperties.SetName(box, "Describe the shortcut you want");
            stack.Children.Add(box);
            var create = new Wpf.Ui.Controls.Button
            {
                Content = "Create draft", Icon = new SymbolIcon(SymbolRegular.Sparkle24), Appearance = ControlAppearance.Primary,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0),
            };
            void Go()
            {
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                var text = box.Text.Trim();
                popup.IsOpen = false;
                host.EditorLauncher.OpenNew(folderId, new EditorOpenOptions(Describe: text));
            }
            create.Click += (_, _) => Go();
            box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Go(); } };
            stack.Children.Add(create);
            popup.Opened += (_, _) => box.Dispatcher.BeginInvoke(new Action(() => box.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            var hint = Label("Describe a shortcut in words and get a draft to check and save. It needs an AI key or a local model first.", 12.5, true);
            hint.Margin = new Thickness(0, 2, 0, 6);
            stack.Children.Add(hint);
            var link = new Wpf.Ui.Controls.HyperlinkButton { Content = "Set up AI in Settings › AI", Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 0) };
            link.Click += (_, _) => { popup.IsOpen = false; host.OpenAiSettings(); };
            stack.Children.Add(link);
        }

        var divider2 = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        divider2.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        stack.Children.Add(divider2);
        var record = new Wpf.Ui.Controls.Button
        {
            Content = "Record a new shortcut", Icon = new SymbolIcon(SymbolRegular.Record24), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = "Do it once and Automation Portal writes down your clicks and typing",
        };
        record.Click += (_, _) => { popup.IsOpen = false; host.RecordNewShortcut(folderId); };
        stack.Children.Add(record);

        popup.IsOpen = true;
    }
}
