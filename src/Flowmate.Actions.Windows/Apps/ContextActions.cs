using System.Text.RegularExpressions;
using System.Windows.Automation;
using Flowmate.Actions.Windows.Input;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Apps;

/// <summary>Reads text and the address from the app in front through UI Automation.</summary>
internal static class UiaReader
{
    /// <summary>Runs UI Automation work with a time limit; UIA calls into other apps can hang when those apps misbehave.</summary>
    public static async Task<T?> WithTimeoutAsync<T>(Func<T?> work, TimeSpan limit, CancellationToken ct)
    {
        var task = Task.Run(work, CancellationToken.None);
        var finished = await Task.WhenAny(task, Task.Delay(limit, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return finished == task ? await task.ConfigureAwait(false) : default;
    }

    /// <summary>Selected text of the focused control, or null when it doesn't expose a selection. Throws for password boxes.</summary>
    public static string? ReadSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return null;
            if (focused.Current.IsPassword) throw new ActionException("The selected box is a password field, so Flowmate won't read it.");
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
            var ranges = ((TextPattern)pattern).GetSelection();
            var text = string.Concat(ranges.Select(r => r.GetText(-1)));
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException or TimeoutException)
        {
            return null;
        }
    }
}

public sealed class GetSelectedTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.getSelected", Title = "Get Selected Text", Category = ActionCategories.AppsAndWindows, Icon = "SelectAllOn24",
        Keywords = new[] { "highlighted", "selection", "copy text", "selected", "grab text" },
        Summary = "Get the text selected in the front app",
        Description = "Gets the text you have highlighted in the app in front. It reads the selection directly when the app allows it; otherwise it presses Ctrl+C for a moment and puts your clipboard back exactly as it was afterwards.",
        OutputKind = ContentKind.Text, OutputName = "Selected Text",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var direct = await UiaReader.WithTimeoutAsync(UiaReader.ReadSelection, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(direct)) return ContentValue.Text(direct);

        var clipboard = ctx.Services.Clipboard;
        var before = await clipboard.SnapshotAsync().ConfigureAwait(false);
        var sequence = Win32.GetClipboardSequenceNumber();
        string? copied = null;
        try
        {
            await InputSender.ReleaseHeldModifiersAsync(400, ct).ConfigureAwait(false);
            await InputSender.PressAsync(KeyCombo.Parse("Ctrl+C"), 1, ct).ConfigureAwait(false);
            var until = Environment.TickCount64 + 500;
            while (Environment.TickCount64 < until && Win32.GetClipboardSequenceNumber() == sequence)
                await Task.Delay(15, ct).ConfigureAwait(false);
            if (Win32.GetClipboardSequenceNumber() != sequence)
            {
                await Task.Delay(30, ct).ConfigureAwait(false);
                var now = await clipboard.GetAsync().ConfigureAwait(false);
                copied = now.IsEmpty ? null : now.AsText();
            }
        }
        finally
        {
            try
            {
                if (Win32.GetClipboardSequenceNumber() != sequence)
                {
                    await clipboard.RestoreAsync(before).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { ctx.Log($"Couldn't put the clipboard back: {ex.Message}"); }
        }
        if (string.IsNullOrEmpty(copied))
            throw new ActionException("Couldn't find any selected text. Select some text in the app first, and keep that app in front when the shortcut runs.");
        return ContentValue.Text(copied);
    }
}

public static class BrowserUrl
{
    public static readonly IReadOnlyList<string> BrowserProcesses = new[] { "msedge", "chrome", "brave", "firefox", "opera", "vivaldi", "chromium", "arc", "librewolf", "waterfox" };

    public static bool IsBrowser(string processName) =>
        BrowserProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Browsers often hide "https://" in the address bar; puts it back. Returns null for empty text.</summary>
    public static string? Normalize(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (Regex.IsMatch(v, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://")) return v;
        if (Regex.IsMatch(v, @"^(about|chrome|edge|brave|file|view-source|data|mailto|vivaldi|opera):", RegexOptions.IgnoreCase)) return v;
        var host = Regex.Match(v, @"^[^/?#:]+").Value;
        var local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(host, @"^\d{1,3}(\.\d{1,3}){3}$") || !host.Contains('.');
        return (local ? "http://" : "https://") + v;
    }

    /// <summary>Removes " - Google Chrome" style endings from a browser window title.</summary>
    public static string CleanTitle(string title)
    {
        var t = Regex.Replace(title, @"\s+[-–—]\s+(Google Chrome|Microsoft\W*Edge|Mozilla Firefox|Brave|Opera|Vivaldi|Chromium|LibreWolf|Waterfox)\s*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+and \d+ more pages?(\s+[-–—]\s+[^-–—]+)?$", "", RegexOptions.IgnoreCase);
        return t.Trim();
    }
}

public sealed class GetCurrentWebPageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.currentPage", Title = "Get Current Web Page", Category = ActionCategories.AppsAndWindows, Icon = "GlobeSearch24",
        Keywords = new[] { "browser", "url", "address", "tab", "link", "edge", "chrome", "firefox", "current tab" },
        Summary = "Get the {info} of the web page in front",
        Description = "Gets the address and title of the page open in the browser that is in front (Edge, Chrome, Brave, Firefox and similar). The browser has to be the front app when the shortcut runs.",
        OutputKind = ContentKind.Url, OutputName = "Current Page",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "url", new Choice("url", "address (link)"), new Choice("title", "title"), new Choice("both", "address and title")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "url");
        var front = await Task.Run(() => WindowService.GetFront(), ctx.CancellationToken).ConfigureAwait(false);
        if (front is null || !BrowserUrl.IsBrowser(front.ProcessName))
            throw new ActionException("No web browser is in front. Click on your browser (Edge, Chrome, Brave or Firefox) so it is the front app, then run this again.");

        var handle = front.Handle;
        var firefox = front.ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase) || front.ProcessName.Contains("wolf", StringComparison.OrdinalIgnoreCase);
        var raw = await UiaReader.WithTimeoutAsync(() => ReadAddress(handle, firefox), TimeSpan.FromSeconds(6), ctx.CancellationToken).ConfigureAwait(false);
        var url = BrowserUrl.Normalize(raw);
        if (url is null)
            throw new ActionException("Couldn't read the address from the browser. Make sure a normal web page tab is open (not a full-screen video), and try again.");
        var title = BrowserUrl.CleanTitle(front.Title);

        if (info == "title") return ContentValue.Text(title);
        var link = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? ContentValue.Url(uri) : ContentValue.Text(url);
        if (info == "both") return ContentValue.Dict(("URL", link), ("Title", ContentValue.Text(title)));
        return link;
    }

    private static string? ReadAddress(IntPtr hwnd, bool firefox)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var edit = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            Condition condition = firefox
                ? new AndCondition(edit, new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"))
                : new AndCondition(edit, new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews"));
            var bar = root.FindFirst(TreeScope.Descendants, condition);
            if (bar is null && !firefox)
            {
                // Browsers built on Chromium that rename the address box: take the first text box that looks like an address bar.
                foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, edit))
                {
                    var name = e.Current.Name ?? "";
                    if (name.Contains("address", StringComparison.OrdinalIgnoreCase) || name.Contains("search", StringComparison.OrdinalIgnoreCase)) { bar = e; break; }
                }
            }
            if (bar is not null && bar.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)) return ((ValuePattern)vp).Current.Value;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException) { }
        return null;
    }
}
