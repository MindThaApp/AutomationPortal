using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>Lists a shortcut's earlier versions, with "Open as copy" and "Restore this version" for each.</summary>
internal sealed class VersionHistoryWindow : FluentWindow
{
    private readonly JsonShortcutLibrary _library;
    private readonly Guid _shortcutId;
    private readonly StackPanel _list = new();
    private readonly System.Windows.Controls.CheckBox _keepSettings;
    private readonly System.Windows.Controls.TextBlock _error;

    /// <summary>The id of the shortcut added by "Open as copy", when that was chosen.</summary>
    public Guid? OpenedCopy { get; private set; }
    /// <summary>True when a version was restored.</summary>
    public bool Restored { get; private set; }

    public VersionHistoryWindow(JsonShortcutLibrary library, Guid shortcutId, string name)
    {
        EditorResources.Ensure();
        _library = library;
        _shortcutId = shortcutId;
        Title = "Version history";
        Width = 620;
        Height = 600;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        FontFamily = Ui.Font;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = "Version history", ShowMaximize = false, ShowMinimize = false });

        var intro = Ui.Text($"Earlier versions of \"{name}\". A version is kept when you change it (changes made within "
            + $"{ShortcutVersions.MergeWithin.TotalMinutes:0} minutes count as one), on this PC only, for {ShortcutVersions.KeepFor.Days} days "
            + $"(the newest {ShortcutVersions.MaxPerShortcut}).", Ui.Secondary, 13, wrap: true);
        intro.Margin = new Thickness(24, 8, 24, 12);
        Grid.SetRow(intro, 1);
        root.Children.Add(intro);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(24, 0, 24, 0) };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var bottom = new StackPanel { Margin = new Thickness(24, 12, 24, 20) };
        _keepSettings = new System.Windows.Controls.CheckBox
        {
            IsChecked = true,
            Content = "When restoring, keep the current hotkey, Quick Run name and Send To setting",
        };
        bottom.Children.Add(_keepSettings);
        _error = Ui.Text("", Ui.Critical, 13, wrap: true);
        _error.Margin = new Thickness(0, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        bottom.Children.Add(_error);
        var close = Ui.TextButton("Close", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 12, 0, 0);
        close.IsCancel = true;
        bottom.Children.Add(close);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        Content = root;
        Fill();
    }

    private void Fill()
    {
        _list.Children.Clear();
        IReadOnlyList<ShortcutVersion> versions;
        try { versions = _library.Versions.List(_shortcutId); }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(Message(ex)); versions = Array.Empty<ShortcutVersion>(); }
        if (versions.Count == 0)
        {
            _list.Children.Add(Ui.Text("No earlier versions yet. One is kept each time you change this shortcut.", Ui.Tertiary, 13, wrap: true));
            return;
        }
        foreach (var v in versions) _list.Children.Add(Card(v));
    }

    private static string When(DateTimeOffset t) => t.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    private FrameworkElement Card(ShortcutVersion v)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(When(v.EditedAt), Ui.Primary, 14, FontWeights.SemiBold));
        var detail = v.CanOpen ? $"{v.StepCount} step{(v.StepCount == 1 ? "" : "s")} · {v.Changes}" : v.Changes;
        text.Children.Add(Ui.Text(detail, Ui.Secondary, 12.5));
        if (v.CanOpen) text.Children.Add(Ui.Text($"Named \"{v.Name}\"", Ui.Tertiary, 12));
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var copy = Ui.TextButton("Open as copy", () => OpenAsCopy(v), SymbolRegular.Copy24);
        copy.IsEnabled = v.CanOpen;
        copy.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(copy);
        var restore = Ui.TextButton("Restore this version…", () => Restore(v), SymbolRegular.ArrowCounterclockwise24);
        restore.IsEnabled = v.CanOpen;
        buttons.Children.Add(restore);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            Child = grid, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
        }.Dyn(Border.BackgroundProperty, Ui.CardFill).Dyn(Border.BorderBrushProperty, Ui.CardStroke);
    }

    private void OpenAsCopy(ShortcutVersion v)
    {
        try
        {
            OpenedCopy = _library.OpenVersionAsCopy(v).Shortcut.Id;
            Close();
        }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(Message(ex)); }
    }

    private void Restore(ShortcutVersion v)
    {
        var answer = System.Windows.MessageBox.Show(this,
            $"Restore the version from {When(v.EditedAt)}?\n\nThe shortcut as it is now is kept in Version history first, so you can go back to it.",
            "Restore this version?", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            var result = _library.RestoreVersion(v, _keepSettings.IsChecked == true);
            Restored = true;
            var notes = new List<string>();
            if (result.RenamedFrom is { } old) notes.Add($"Another shortcut is called \"{old}\", so it kept its current name.");
            if (result.ClearedHotkey is { } hk) notes.Add($"Its old hotkey {hk} is used by another shortcut, so it was left off.");
            if (result.ClearedAlias is { } alias) notes.Add($"Its old Quick Run name \"{alias}\" is used by another shortcut, so it was left off.");
            if (notes.Count > 0)
                System.Windows.MessageBox.Show(this, string.Join("\n\n", notes), "Version restored", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(Message(ex)); }
    }

    private static bool IsExpected(Exception ex) => ex is LibraryException or System.IO.IOException or UnauthorizedAccessException
        or System.Security.SecurityException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException;

    private static string Message(Exception ex) => ex is LibraryException ? ex.Message : $"That didn't work. {ex.Message}";

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
