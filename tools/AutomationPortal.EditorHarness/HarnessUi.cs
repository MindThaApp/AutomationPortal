using System.Windows;
using System.Windows.Controls;
using Flowmate.Core;

namespace Flowmate.EditorHarness;

/// <summary>Bare-bones dialogs so shortcuts can be run from the harness. The real app has proper ones.</summary>
public sealed class HarnessUi : IUserInteraction
{
    /// <summary>When true every dialog answers immediately (automated self test).</summary>
    public static bool Quiet { get; set; }

    private static Task<T> OnUi<T>(Func<T> f) => Application.Current.Dispatcher.InvokeAsync(f).Task;

    public Task<bool> ShowAlertAsync(string title, string message, bool showCancel, CancellationToken ct = default) =>
        Quiet ? Task.FromResult(true) : OnUi(() => MessageBox.Show(message, title, showCancel ? MessageBoxButton.OKCancel : MessageBoxButton.OK) == MessageBoxResult.OK);

    public Task<ContentValue> AskForFormAsync(string title, IReadOnlyList<FormField> fields, CancellationToken ct = default) =>
        Task.FromException<ContentValue>(new UserCancelledException());

    public Task<ContentValue> AskForInputAsync(string prompt, InputKind kind, string? defaultValue, CancellationToken ct = default) =>
        Quiet ? Task.FromResult(ContentValue.Text(defaultValue ?? "sample")) : OnUi(() =>
        {
            var box = new TextBox { Text = defaultValue ?? "", Margin = new Thickness(0, 8, 0, 12), MinWidth = 280 };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80 };
            var win = new Window { Title = prompt, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock { Text = prompt });
            stack.Children.Add(box);
            stack.Children.Add(ok);
            win.Content = stack;
            ok.Click += (_, _) => win.DialogResult = true;
            win.Loaded += (_, _) => box.Focus();
            if (win.ShowDialog() != true) throw new UserCancelledException();
            return ContentValue.Text(box.Text);
        });

    public Task<ContentValue> ChooseFromListAsync(string prompt, IReadOnlyList<ContentItem> items, bool multiSelect, CancellationToken ct = default) =>
        OnUi(() => items.Count > 0 ? ContentValue.From(items[0]) : ContentValue.Empty);

    public Task<int> ChooseFromMenuAsync(string prompt, IReadOnlyList<string> labels, CancellationToken ct = default) => Task.FromResult(0);

    public Task ShowResultAsync(string title, ContentValue value, CancellationToken ct = default) =>
        Quiet ? Task.CompletedTask : OnUi(() => { MessageBox.Show(value.AsText(), title); return 0; });

    public Task PreviewAsync(ContentValue value, CancellationToken ct = default) => ShowResultAsync("Preview", value, ct);

    public Task<ContentValue> PickFilesAsync(string prompt, bool multiple, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        Task.FromException<ContentValue>(new UserCancelledException());

    public Task<ContentValue> PickFolderAsync(string prompt, CancellationToken ct = default) =>
        Task.FromException<ContentValue>(new UserCancelledException());

    public Task<string> PickSaveFileAsync(string prompt, string? suggestedName = null, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        Task.FromException<string>(new UserCancelledException());

    public Task<ScreenRect?> SelectScreenRegionAsync(CancellationToken ct = default) => Task.FromResult<ScreenRect?>(null);
    public Task<ScreenPoint?> SelectScreenPointAsync(CancellationToken ct = default) => Task.FromResult<ScreenPoint?>(null);
}
