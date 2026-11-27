using System.Text.RegularExpressions;
using AutomationPortal.Actions.Windows.Input;
using AutomationPortal.Actions.Windows.Native;
using AutomationPortal.Interop;

namespace AutomationPortal.Actions.Windows.Apps;

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
            var focused = Uia.Create().GetFocusedElement();
            if (focused is null) return null;
            if (Uia.GetBool(focused, Uia.IsPasswordProperty)) throw new ActionException(Strings.Get("UiaReader_Error_The_Selected_Box_Is_A"));
            var text = Uia.SelectedText(focused);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException or TimeoutException)
        {
            return null;
        }
    }
}

public sealed class GetSelectedTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.getSelected", Title = Strings.Get("GetSelectedTextAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "SelectAllOn24",
        Keywords = new[] { "highlighted", "selection", "copy text", "selected", "grab text" },
        Summary = Strings.Get("GetSelectedTextAction_Summary"),
        Description = Strings.Get("GetSelectedTextAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("GetSelectedTextAction_OutputName"),
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
            throw new ActionException(Strings.Get("GetSelectedTextAction_Error_Couldn_T_Find_Any_Selected_Text"));
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
        Id = "web.currentPage", Title = Strings.Get("GetCurrentWebPageAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "GlobeSearch24",
        Keywords = new[] { "browser", "url", "address", "tab", "link", "edge", "chrome", "firefox", "current tab" },
        Summary = Strings.Get("GetCurrentWebPageAction_Summary"),
        Description = Strings.Get("GetCurrentWebPageAction_Description"),
        OutputKind = ContentKind.Url, OutputName = Strings.Get("GetCurrentWebPageAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetCurrentWebPageAction_Param_Info_Label"), "url", new Choice("url", Strings.Get("GetCurrentWebPageAction_Param_Info_Choice_Url")), new Choice("title", Strings.Get("GetCurrentWebPageAction_Param_Info_Choice_Title")), new Choice("both", Strings.Get("GetCurrentWebPageAction_Param_Info_Choice_Both"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "url");
        var front = await Task.Run(() => WindowService.GetFront(), ctx.CancellationToken).ConfigureAwait(false);
        if (front is null || !BrowserUrl.IsBrowser(front.ProcessName))
            throw new ActionException(Strings.Get("GetCurrentWebPageAction_Error_No_Web_Browser_Is_In"));

        var handle = front.Handle;
        var firefox = front.ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase) || front.ProcessName.Contains("wolf", StringComparison.OrdinalIgnoreCase);
        var raw = await UiaReader.WithTimeoutAsync(() => ReadAddress(handle, firefox), TimeSpan.FromSeconds(6), ctx.CancellationToken).ConfigureAwait(false);
        var url = BrowserUrl.Normalize(raw);
        if (url is null)
            throw new ActionException(Strings.Get("GetCurrentWebPageAction_Error_Couldn_T_Read_The_Address_From"));
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
            var uia = Uia.Create();
            var root = uia.ElementFromHandle(hwnd);
            var edit = uia.CreatePropertyCondition(Uia.ControlTypeProperty, Uia.EditControlType);
            var condition = firefox
                ? uia.CreateAndCondition(edit, uia.CreatePropertyCondition(Uia.AutomationIdProperty, "urlbar-input"))
                : uia.CreateAndCondition(edit, uia.CreatePropertyCondition(Uia.ClassNameProperty, "OmniboxViewViews"));
            var bar = root.FindFirst(Uia.TreeScopeDescendants, condition);
            if (bar is null && !firefox && root.FindAll(Uia.TreeScopeDescendants, edit) is { } edits)
            {
                // Browsers built on Chromium that rename the address box: take the first text box that looks like an address bar.
                for (var i = 0; i < edits.Length; i++)
                {
                    var e = edits.GetElement(i);
                    var name = Uia.GetString(e, Uia.NameProperty) ?? "";
                    if (name.Contains("address", StringComparison.OrdinalIgnoreCase) || name.Contains("search", StringComparison.OrdinalIgnoreCase)) { bar = e; break; }
                }
            }
            if (bar is not null && Uia.GetBool(bar, Uia.IsValuePatternAvailableProperty)) return Uia.GetString(bar, Uia.ValueValueProperty);
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException) { }
        return null;
    }
}
