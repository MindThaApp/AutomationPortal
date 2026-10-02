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
        big.Close();
    }

    private static async Task<bool> RunWithTimeout(Task t)
    {
        var done = await Task.WhenAny(t, Task.Delay(20000));
        Check(done == t, "run finished within 20 s");
        return done == t;
    }
}
