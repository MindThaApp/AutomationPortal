using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flowmate.Core;
using Flowmate.Editor;
using Flowmate.Editor.UI;
using Flowmate.Editor.ViewModels;

namespace Flowmate.EditorHarness;

/// <summary>Drives the editor like a user would (insert, edit, popups, run, undo, dialogs) and logs what breaks.</summary>
internal static class SelfTest
{
    private static void Check(bool ok, string what)
    {
        App.Log((ok ? "ok   " : "FAIL ") + what);
        if (!ok) App.Failed = true;
    }

    private static async Task Settle(int ms = 250) => await Task.Delay(ms);

    public static void Shot(FrameworkElement el, string? dir, string name)
    {
        if (dir is null || el.ActualWidth < 1 || el.ActualHeight < 1) return;
        Directory.CreateDirectory(dir);
        var dpi = VisualTreeHelper.GetDpi(el);
        var w = (int)Math.Ceiling(el.ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(el.ActualHeight * dpi.DpiScaleY);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
            dc.DrawRectangle(Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? Brushes.White, null, rect);
            dc.DrawRectangle(new VisualBrush(el), null, rect);
        }
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Visuals<T>(c)) yield return d;
        }
    }

    public static async Task RunAsync(Window window, EditorContext ctx, IShortcutLibrary library, Shortcut showcase, Shortcut stress, string? shots)
    {
        var w = (ShortcutEditorWindow)window;
        var vm = (EditorViewModel)w.DataContext;
        await Settle(600);
        Check(vm.Rows.Count == showcase.Actions.Count, $"rows match actions ({vm.Rows.Count})");
        Check(Visuals<ActionRowView>(w).Any(), "action cards are on screen");
        Shot(w, shots, "01-showcase");

        // open a token popup for each kind of token that is on screen
        var tokens = Visuals<TokenView>(w).Take(6).ToList();
        Check(tokens.Count > 0, $"tokens rendered ({tokens.Count})");
        var n = 0;
        foreach (var t in tokens)
        {
            t.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await Settle(350);
            if (FloatingPanel.OpenPanels.LastOrDefault() is { } p)
            {
                Check(true, $"popup opened for '{t.Param.Label}'");
                Shot(p.Frame, shots, $"02-popup-{n++}-{t.Param.Key}");
                p.Close();
            }
            else Check(false, $"popup opened for '{t.Param.Label}'");
            await Settle(100);
        }

        // structure operations
        var before = vm.Doc.Actions.Count;
        vm.InsertAt(ctx.Actions.Describe("flow.if")!, vm.Doc.Actions.Count);
        vm.InsertAt(ctx.Actions.Describe("flow.menu")!, vm.Doc.Actions.Count);
        Check(vm.Doc.Actions.Count == before + 3 + 4, "inserted If and Menu groups");
        vm.Undo(); vm.Undo();
        Check(vm.Doc.Actions.Count == before, "undo twice");
        vm.Redo();
        Check(vm.Doc.Actions.Count == before + 3, "redo");
        vm.Undo();

        // the sample action with every option, expanded
        var every = vm.Rows.First(r => r.Uid == "every");
        every.ShowMore = true;
        vm.SelectActions(new[] { every.Action });
        w.ActionList.ScrollIntoView(every);
        await Settle(700);
        Shot(w, shots, "03-every-option");

        // variable picker from the card of the last action
        var ask = vm.Rows.First(r => r.Uid == "ask");
        w.ActionList.ScrollIntoView(ask);
        await Settle(400);
        var anyToken = Visuals<TokenView>(w).LastOrDefault();
        if (anyToken is not null)
        {
            VariablePicker.Show(anyToken, Flowmate.Editor.Logic.VariableCatalog.Available(vm.Shortcut, ctx.Actions, vm.Doc.Actions.Count), true, _ => { });
            await Settle(350);
            if (FloatingPanel.OpenPanels.LastOrDefault() is { } p) { Shot(p.Frame, shots, "04-variable-picker"); p.Close(); }
        }

        // quick insert
        QuickInsert.Show(w.AddActionButton, vm, vm.Doc.Actions.Count);
        await Settle(350);
        if (FloatingPanel.OpenPanels.LastOrDefault() is { } qi) { Shot(qi.Frame, shots, "05-quick-insert"); qi.Close(); }

        // run, then run from a step
        var result = await RunWithTimeout(vm.RunAsync());
        Check(vm.LastResult is { } r && r.Status == RunStatus.Succeeded, "run succeeded: " + vm.StatusText);
        Check(vm.Rows.First(r => r.Uid == "t1").State == Flowmate.Editor.Logic.StepState.Succeeded, "first card shows finished");
        Check(vm.Inspector.Count > 0, $"variables inspector has {vm.Inspector.Count} entries");
        w.ActionList.ScrollIntoView(vm.Rows[0]);
        await Settle(600);
        Shot(w, shots, "06-after-run");
        var showIndex = vm.Doc.Actions.FindIndex(a => a.Uid == "show");
        await RunWithTimeout(vm.RunAsync(showIndex));
        Check(vm.LastResult?.Status == RunStatus.Succeeded, "run from here succeeded: " + vm.StatusText);

        // dialogs
        var sheet = new DetailsSheet(vm) { Owner = w };
        sheet.Show();
        await Settle(600);
        Shot((FrameworkElement)sheet.Content, shots, "07-details");
        sheet.Close();
        var picker = new IconPickerWindow(vm.Icon, vm.Color) { Owner = w };
        picker.Show();
        await Settle(600);
        Shot((FrameworkElement)picker.Content, shots, "08-icon-picker");
        picker.Close();

        // autosave
        vm.TryRename("Editor Showcase (edited)");
        await Settle(900);
        Check(library.GetById(showcase.Id)?.Name == "Editor Showcase (edited)", "autosave wrote the new name");
        Check(library.GetById(showcase.Id)?.Actions.Count == vm.Doc.Actions.Count, "autosave wrote the actions");

        // many actions
        var big = ShortcutEditor.Open(stress.Id, ctx)!;
        w.Close();
        await Settle(800);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var list = ((ShortcutEditorWindow)big).ActionList;
        var scroller = Visuals<ScrollViewer>(list).First();
        for (var i = 0; i < 20; i++) { scroller.ScrollToVerticalOffset(i * 400); big.UpdateLayout(); }
        Check(sw.ElapsedMilliseconds < 4000, $"scrolling 300 actions took {sw.ElapsedMilliseconds} ms");
        Shot(big, shots, "09-stress");

        await AiAndRecordingAsync(ctx, library, shots);
        big.Close();
    }

    private static async Task AiAndRecordingAsync(EditorContext ctx, IShortcutLibrary library, string? shots)
    {
        var host = (HarnessHost)ctx.Host;
        var fresh = new Shortcut { Name = "AI test", Icon = "Flash24", Color = "teal" };
        var win = (ShortcutEditorWindow)ShortcutEditor.Open(fresh, ctx);
        var vm = (EditorViewModel)win.DataContext;
        await Settle(700);
        Check(vm.IsEmpty && win.EmptyState.Visibility == Visibility.Visible, "new shortcut shows the start choices");
        Check(Visuals<Wpf.Ui.Controls.Button>(win.EmptyState).Any(b => b.Content as string == "Describe it"), "Describe it is offered when AI is set up");
        Shot(win, shots, "10-new-describe");

        var bad = await host.DescribeWithAiAsync("garbage", null);
        Check(!bad.Success && bad.Error!.Contains("wasn't usable"), "a failed draft carries a friendly error");

        var draft = await host.DescribeWithAiAsync("every morning tell me the weather", null);
        vm.ApplyAiResult(draft, isChange: false);
        await Settle(900);
        Check(vm.Doc.Actions.Count == 3 && vm.Name == "Morning briefing", "draft applied (steps, name)");
        Check(vm.AiNote is { IsDraft: true } n && n.DestructiveTitles.Count == 1 && n.Automations.Count == 1, "draft note lists the danger step and the automation");
        Check(vm.DraftPending && library.GetById(fresh.Id) is null, "an AI draft is not saved until it is kept");
        Check(win.NoteHost.Content is not null, "the banner is on screen");
        var banner = Visuals<TextBlock>(win.NoteHost).Select(t => t.Text).ToList();
        Check(banner.Any(t => t.StartsWith("Drafted by AI")), "banner says Drafted by AI");
        Check(Visuals<Wpf.Ui.Controls.Button>(win.NoteHost).Any(b => b.Content as string == "Every day at 8:00"), "banner offers the suggested automation");
        Check(Visuals<TextBlock>(win).Any(t => t.Text == "Check this step"), "the danger step is marked");
        Shot(win, shots, "11-ai-draft");

        var undone = vm.Doc.Actions.Count;
        vm.UndoAiResult();
        await Settle(300);
        Check(vm.IsEmpty && vm.AiNote is null && vm.Name == "AI test", "Undo takes the draft back (steps and name)");
        Check(library.GetById(fresh.Id) is null, "still nothing saved after Undo");

        vm.ApplyAiResult(draft, isChange: false);
        await Settle(200);
        win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        var autoButton = Visuals<Wpf.Ui.Controls.Button>(win.NoteHost).First(b => b.Content as string == "Every day at 8:00");
        autoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Settle(300);
        Check(host.LastSuggestion?.TriggerId == "time.daily", "clicking the suggestion opens the automation flow");
        Check(library.GetById(fresh.Id) is not null, "the shortcut is saved before the automation is added");

        // describe a change on a saved shortcut
        var change = await host.DescribeWithAiAsync("also show the result", vm.Shortcut.Clone());
        var stepsBefore = vm.Doc.Actions.Count;
        vm.ApplyAiResult(change, isChange: true);
        await Settle(300);
        Check(vm.Doc.Actions.Count == stepsBefore + 1 && vm.AiNote is { IsDraft: false } cn && cn.Detail.Contains("1 step added"), "a change is applied with a summary: " + vm.AiNote?.Detail);
        Shot(win, shots, "12-ai-change");
        vm.Undo();
        Check(vm.Doc.Actions.Count == stepsBefore, "a change is one undo step");

        // the Describe a change dialog, driven end to end
        var changeBefore = vm.Doc.Actions.Count;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dlg = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.Title == "Describe a change");
            Check(dlg is not null, "the Describe a change dialog opened");
            if (dlg is null) return;
            var box = Visuals<Wpf.Ui.Controls.TextBox>(dlg).First();
            box.Text = "also show the result";
            dlg.UpdateLayout();
            Shot((FrameworkElement)dlg.Content, shots, "14-describe-change-dialog");
            Visuals<Wpf.Ui.Controls.Button>(dlg).First(b => b.Content as string == "Make the change").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        timer.Start();
        typeof(ShortcutEditorWindow).GetMethod("DescribeChange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(win, null);
        await Settle(600);
        Check(vm.Doc.Actions.Count == changeBefore + 1 && vm.AiNote is { IsDraft: false }, "Describe a change applied through the dialog: " + vm.AiNote?.Detail);
        vm.Undo();

        // recording
        var before = vm.Doc.Actions.Count;
        await win.RecordAsync();
        Check(vm.Doc.Actions.Count == before + 2, "recorded steps were inserted");
        Check(Visuals<TextBlock>(win.NoteHost).Any(t => t.Text.Contains("relative to the window")), "banner explains window-relative clicks");
        Shot(win, shots, "13-recorded");
        vm.Undo();
        Check(vm.Doc.Actions.Count == before, "recording is one undo step");

        // screen reader names
        win.ActionList.UpdateLayout();
        var names = Enumerable.Range(0, win.ActionList.Items.Count)
            .Select(i => win.ActionList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem).Where(i => i is not null)
            .Select(i => System.Windows.Automation.AutomationProperties.GetName(i!)).ToList();
        Check(names.Count > 0 && names.All(nm => !string.IsNullOrWhiteSpace(nm) && !nm.Contains("ViewModel")), "every action card has a readable name: " + string.Join(" | ", names.Take(3)));
        win.Close();
    }

    private static async Task<bool> RunWithTimeout(Task t)
    {
        var done = await Task.WhenAny(t, Task.Delay(20000));
        Check(done == t, "run finished within 20 s");
        return done == t;
    }
}
