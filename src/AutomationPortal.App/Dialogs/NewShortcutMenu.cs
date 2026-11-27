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
    /// <summary>Adds a ready-made Gallery shortcut to the library, exactly like the Gallery page does. A name that is taken gets a new one; nothing is replaced.</summary>
    internal static async Task AddGalleryItemAsync(AppHost host, AutomationPortal.App.Services.GalleryItem item, Window? owner)
    {
        if (!host.AllowNewShortcut(owner)) return;
        try
        {
            var result = host.Library.Import(item.Shortcut.Clone());
            await host.FinishImportAsync(result, owner, warningsShown: false);
        }
        catch (AutomationPortal.Core.LibraryException ex) { await Prompts.MessageAsync(owner, Strings.Get("NewShortcutMenu_CouldnTAddIt"), ex.Message); }
    }

    /// <param name="describeFirst">Show only the description box (or its fallback), for the empty state's Describe it button.</param>
    public static void Show(AppHost host, FrameworkElement anchor, Guid? folderId, bool describeFirst = false)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None,
        };
        var card = new Border { Width = 380, CornerRadius = AutomationPortal.Editor.Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(10, 4, 10, 14) };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        card.Effect = AutomationPortal.Editor.HighContrast.Shadow(16, 3, 0.3);
        var stack = new StackPanel();
        card.Child = stack;
        popup.Child = card;
        AutomationPortal.Editor.MenuMotion.Attach(popup);
        AutomationProperties.SetName(card, Strings.Get("NewShortcutMenu_NewShortcut"));

        TextBlock Label(string text, double size, bool secondary, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "DsTextSecondaryBrush" : "DsTextPrimaryBrush");
            return t;
        }

        var ready = ShortcutAiService.IsReady(host.Services);
        if (describeFirst && !ready) { GetAiReadySheet.Show(host); return; }
        var blank = new Wpf.Ui.Controls.Button { Content = Strings.Get("NewShortcutMenu_StartBlank"), Icon = new SymbolIcon(SymbolRegular.Add24), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        blank.Click += (_, _) => { popup.IsOpen = false; host.EditorLauncher.OpenNew(folderId); };
        if (!describeFirst)
        {
            stack.Children.Add(blank);
            var divider = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
            divider.SetResourceReference(Border.BackgroundProperty, "DsDividerBrush");
            stack.Children.Add(divider);
        }

        stack.Children.Add(Label(ready ? Strings.Get("NewShortcutMenu_DescribeIt") : Strings.Get("NewShortcutMenu_StartFromAReady"), 14, false, FontWeights.SemiBold));
        if (ready)
        {
            var hint = Label(Strings.Get("NewShortcutMenu_SayWhatYouWant"), 12.5, true);
            hint.Margin = new Thickness(0, 2, 0, 8);
            stack.Children.Add(hint);
            var box = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = Strings.Get("NewShortcutMenu_EveryMorningTellMe"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74,
            };
            AutomationProperties.SetName(box, Strings.Get("NewShortcutMenu_DescribeTheShortcutYou"));
            stack.Children.Add(box);
            var create = new Wpf.Ui.Controls.Button
            {
                Content = Strings.Get("NewShortcutMenu_CreateDraft"), Icon = new SymbolIcon(SymbolRegular.Sparkle24), Appearance = ControlAppearance.Primary,
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
            // No model is set up: offer ready-made shortcuts instead of a dead end.
            var hint = Label(Strings.Get("NewShortcutMenu_PickOneToAdd"), 12.5, true);
            hint.Margin = new Thickness(0, 2, 0, 8);
            stack.Children.Add(hint);
            var offered = new List<(string FileName, AutomationPortal.App.Services.GalleryItem Item)>();
            foreach (var section in host.GallerySections())
                foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0)) offered.Add((item.FilePath, item));
            var chips = new WrapPanel();
            foreach (var item in AutomationPortal.App.Logic.FirstRun.PickStarters(offered, 3))
            {
                var chip = new Wpf.Ui.Controls.Button { Content = item.Shortcut.Name, Margin = new Thickness(0, 0, 6, 6) };
                chip.Click += (_, _) => { popup.IsOpen = false; _ = AddGalleryItemAsync(host, item, host.MainWindow); };
                chips.Children.Add(chip);
            }
            stack.Children.Add(chips);
            var aiReady = new Wpf.Ui.Controls.Button { Content = Strings.Get("GetAiReady_Title"), Icon = new SymbolIcon(SymbolRegular.Sparkle24), Appearance = ControlAppearance.Primary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetName(aiReady, Strings.Get("GetAiReady_Title"));
            aiReady.Click += (_, _) => { popup.IsOpen = false; GetAiReadySheet.Show(host); };
            stack.Children.Add(aiReady);
            var gallery = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("NewShortcutMenu_BrowseTheGallery"), Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
            gallery.Click += (_, _) => { popup.IsOpen = false; host.ShowMainWindow(new AutomationPortal.App.Pages.NavTarget(AutomationPortal.App.Pages.AppPage.Gallery)); };
            stack.Children.Add(gallery);
        }

        if (describeFirst) { popup.IsOpen = true; return; }

        var divider2 = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        divider2.SetResourceReference(Border.BackgroundProperty, "DsDividerBrush");
        stack.Children.Add(divider2);
        var record = new Wpf.Ui.Controls.Button
        {
            Content = Strings.Get("NewShortcutMenu_RecordANewShortcut"), Icon = new SymbolIcon(SymbolRegular.Record24), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = Strings.Get("NewShortcutMenu_DoItOnceAnd"),
        };
        record.Click += (_, _) => { popup.IsOpen = false; host.RecordNewShortcut(folderId); };
        stack.Children.Add(record);
        var import = new Wpf.Ui.Controls.Button
        {
            Content = Strings.Get("NewShortcutMenu_Import"), Icon = new SymbolIcon(SymbolRegular.ArrowImport24), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0), ToolTip = Strings.Get("NewShortcutMenu_AddAShortcutFrom"),
        };
        import.Click += (_, _) => { popup.IsOpen = false; _ = ImportRestore.ImportFileAsync(host, host.MainWindow); };
        stack.Children.Add(import);

        popup.IsOpen = true;
    }
}
