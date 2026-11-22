using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace AutomationPortal.Actions.Documents;

/// <summary>Builds the request sent to ntfy.sh.</summary>
public static class NtfyRequest
{
    private static readonly Regex TopicRx = new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    public static readonly Choice[] PriorityChoices =
    {
        new("default", "Normal"), new("min", "Lowest (silent)"), new("low", "Low"), new("high", "High"), new("max", "Urgent"),
    };

    public static string ValidateTopic(string? topic)
    {
        var t = (topic ?? "").Trim();
        if (t.Length == 0)
            throw new ActionException("Choose a topic name for Send to My Phone: type one in the action (a long, hard-to-guess name works best), then subscribe to the same topic in the free ntfy app on your phone.");
        if (!TopicRx.IsMatch(t))
            throw new ActionException("A topic name can only use letters, numbers, dashes and underscores (up to 64 characters).");
        return t;
    }

    public static HttpRequestMessage Build(string topic, string message, string? title, string priority, string? click, string? tags)
    {
        topic = ValidateTopic(topic);
        if (string.IsNullOrWhiteSpace(message)) throw new ActionException("There is nothing to send. Type a message first.");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://ntfy.sh/" + topic)
        {
            Content = new StringContent(Truncate(message, 4000), Encoding.UTF8, "text/plain"),
        };
        if (!string.IsNullOrWhiteSpace(title)) request.Headers.TryAddWithoutValidation("Title", HeaderValue(title.Trim()));
        var p = (priority ?? "default").ToLowerInvariant();
        if (p is "min" or "low" or "high" or "max") request.Headers.TryAddWithoutValidation("Priority", p);
        if (!string.IsNullOrWhiteSpace(click))
        {
            if (!Uri.TryCreate(click.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ActionException($"'{click}' isn't a web link. Use a link that starts with https://.");
            request.Headers.TryAddWithoutValidation("Click", uri.AbsoluteUri);
        }
        var cleanTags = string.Join(",", (tags ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0));
        if (cleanTags.Length > 0) request.Headers.TryAddWithoutValidation("Tags", HeaderValue(cleanTags));
        return request;
    }

    /// <summary>HTTP headers are ASCII; ntfy understands RFC 2047 encoded words for anything else.</summary>
    public static string HeaderValue(string value)
    {
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.All(c => c < 128) ? value : "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static string Truncate(string s, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes - 3) break;
            sb.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return sb.Append('…').ToString();
    }
}

public sealed class SendToPhoneAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "share.phone", Title = "Send to My Phone", Category = ActionCategories.EmailAndSharing, Icon = "PhoneArrowRight20",
        Keywords = new[] { "ntfy", "push", "notification", "phone", "android", "iphone" },
        Summary = "Send {message} to my phone",
        Description = "Sends a notification to your phone using the free ntfy service. Install the free ntfy app on your phone (Android or iPhone), subscribe to the same topic name, and messages appear instantly. Anyone who knows your topic name can read what you send, so choose a long, hard-to-guess name.",
        InputKinds = new[] { ContentKind.Text }, HasOutput = false, NeedsInternet = true, OutputName = "Sent Message",
        ImportRisk = ImportRisk.SendsData,
        Parameters = new[]
        {
            ParameterDescriptor.Content("message", "Message", isInput: true, required: true),
            ParameterDescriptor.Text("title", "Title"),
            ParameterDescriptor.Text("topic", "Topic name", null, "leave empty to use the one in Settings") with { Hint = "Must match the topic you subscribed to in the ntfy app." },
            ParameterDescriptor.ChoiceOf("priority", "Importance", "default", NtfyRequest.PriorityChoices),
            ParameterDescriptor.Of("click", "Open this link when tapped", ParamType.Url),
            ParameterDescriptor.Text("tags", "Tags", null, "e.g. warning, tada") with { Hint = "Comma-separated. Names of emoji (like tada) show as emoji." },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var message = await ctx.GetTextAsync("message");
        var topic = await ctx.GetTextAsync("topic");
        if (string.IsNullOrWhiteSpace(topic)) topic = ctx.Services.Settings.Current.NtfyTopic ?? "";
        using var request = NtfyRequest.Build(topic, message, await ctx.GetTextAsync("title"),
            await ctx.GetChoiceAsync("priority", "default"), await ctx.GetTextAsync("click"), await ctx.GetTextAsync("tags"));
        try
        {
            using var response = await ctx.Services.Http.SendAsync(request, ctx.CancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ActionException("ntfy says you're sending too quickly. Wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new ActionException($"ntfy couldn't deliver the message ({(int)response.StatusCode} {response.ReasonPhrase}). Check the topic name and try again.");
        }
        catch (HttpRequestException ex)
        {
            throw new ActionException("Couldn't reach ntfy. Check your internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!ctx.CancellationToken.IsCancellationRequested)
        {
            throw new ActionException("ntfy took too long to answer. Check your internet connection and try again.", ex);
        }
        return ContentValue.Empty;
    }
}

public sealed class ShareAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "share.share", Title = "Share…", Category = ActionCategories.EmailAndSharing, Icon = "Share24",
        Keywords = new[] { "send", "share sheet", "nearby", "mail", "teams" },
        Summary = "Share {items}",
        Description = "Opens the Windows Share window so you can send text, links or files to an app, a contact or a nearby device. This is a best-effort feature and may not work in every setup.",
        InputKinds = new[] { ContentKind.Any }, HasOutput = false, OutputName = "Shared Items",
        Parameters = new[]
        {
            ParameterDescriptor.Content("items", "What to share", isInput: true, required: true),
            ParameterDescriptor.Text("title", "Title", "Shared from Automation Portal"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("items");
        if (content.IsEmpty) throw new ActionException("There is nothing to share. Add some text, a link or files first.");
        var title = await ctx.GetTextAsync("title", "Shared from Automation Portal");

        var files = new List<string>();
        var text = new List<string>();
        Uri? link = null;
        foreach (var item in content)
        {
            if (item is PathItem p and not FolderItem && File.Exists(p.Path)) files.Add(p.Path);
            else if (item is UrlItem u) link ??= u.Value;
            else text.Add(item.DisplayText);
        }
        // The shortcut's temp folder disappears when the run ends, but the share target may read the files later.
        var staging = Path.Combine(UserFolders.TempPath(), "AutomationPortal", "share", Guid.NewGuid().ToString("N")[..8]);
        if (files.Count > 0)
        {
            Directory.CreateDirectory(staging);
            files = files.Select(f => { var t = OutputTarget.UniquePath(staging, Path.GetFileName(f)); File.Copy(f, t); return t; }).ToList();
        }
        CleanOldStaging();

        var storage = new List<Windows.Storage.IStorageItem>();
        foreach (var f in files)
            storage.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(f).AsTask(ctx.CancellationToken));

        try
        {
            await ctx.Services.UiThread.InvokeAsync(() => ShareSheet.Show(title, string.Join("\n", text), link, storage));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or TypeInitializationException)
        {
            throw new ActionException("Couldn't open the Windows Share window. Try again, or use Send to My Phone, Compose Email or Save File instead.", ex);
        }
        return ContentValue.Empty;
    }

    private static void CleanOldStaging()
    {
        try
        {
            var root = Path.Combine(UserFolders.TempPath(), "AutomationPortal", "share");
            if (!Directory.Exists(root)) return;
            foreach (var d in Directory.EnumerateDirectories(root))
                if (Directory.GetCreationTime(d) < DateTime.Now.AddDays(-1)) Directory.Delete(d, true);
        }
        catch { /* best effort */ }
    }
}

/// <summary>Shows the Windows share sheet for a desktop app through IDataTransferManagerInterop and a small helper window.</summary>
internal static class ShareSheet
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    // UnmanagedType.HString isn't supported on modern .NET, so the HSTRING is created by hand.
    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr hstring);
    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    private static readonly Guid DataTransferManagerIid = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");
    private static readonly Guid InteropIid = new("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");

    private static IDataTransferManagerInterop GetInterop()
    {
        WindowsCreateString("Windows.ApplicationModel.DataTransfer.DataTransferManager", "Windows.ApplicationModel.DataTransfer.DataTransferManager".Length, out var hs);
        try
        {
            var iid = InteropIid;
            RoGetActivationFactory(hs, ref iid, out var factory);
            try { return (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory); }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(hs); }
    }

    private static DataTransferManager GetManager(IDataTransferManagerInterop interop, IntPtr hwnd)
    {
        var dtmIid = DataTransferManagerIid;
        var ptr = interop.GetForWindow(hwnd, ref dtmIid);
        try { return WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }

    /// <summary>Connects to the share manager for a window without showing anything (used to check that sharing works).</summary>
    internal static bool CanConnect(IntPtr hwnd)
    {
        try { return GetManager(GetInterop(), hwnd) is not null; }
        catch { return false; }
    }

    /// <summary>Runs on the UI thread (any thread with a message loop: the helper window and its timer live there).</summary>
    public static void Show(string title, string text, Uri? link, IReadOnlyList<Windows.Storage.IStorageItem> files)
    {
        var hwnd = HelperWindow.Create();
        try
        {
            var interop = GetInterop();
            var manager = GetManager(interop, hwnd);

            manager.DataRequested += (_, args) =>
            {
                var data = args.Request.Data;
                data.Properties.Title = string.IsNullOrWhiteSpace(title) ? "Shared from Automation Portal" : title;
                if (!string.IsNullOrEmpty(text)) data.SetText(text);
                if (link is not null) data.SetWebLink(link);
                if (files.Count > 0) data.SetStorageItems(files);
            };
            interop.ShowShareUIForWindow(hwnd);
        }
        catch
        {
            HelperWindow.Destroy(hwnd);
            throw;
        }
        // Keep the helper window alive while the share window is open, then tidy up.
        HelperWindow.DestroyAfter(hwnd, TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// A nearly invisible 1×1 topmost tool window centred on the screen with the mouse, that the share sheet anchors to.
    /// Created on the calling thread; its timer message is delivered by that thread's message loop.
    /// </summary>
    internal static class HelperWindow
    {
        private const string ClassName = "AutomationPortalShareHelper";
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x2;
        private const int SW_SHOW = 5, WM_TIMER = 0x0113, ERROR_CLASS_ALREADY_EXISTS = 1410;

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Must stay referenced for the life of the process: the window class points at it.
        private static readonly WndProc Proc = WindowProc;
        private static readonly object Gate = new();
        private static bool _registered;

        public static IntPtr Create()
        {
            var instance = GetModuleHandle(null);
            lock (Gate)
            {
                if (!_registered)
                {
                    var wc = new WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                        hInstance = instance, lpszClassName = ClassName,
                    };
                    if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                        throw new InvalidOperationException("Couldn't create the share helper window.", new System.ComponentModel.Win32Exception());
                    _registered = true;
                }
            }
            GetCursorPos(out var pt);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            var (x, y) = GetMonitorInfo(MonitorFromPoint(pt, 2), ref info)
                ? ((info.rcWork.Left + info.rcWork.Right) / 2, (info.rcWork.Top + info.rcWork.Bottom) / 2)
                : (pt.X, pt.Y);
            var hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED, ClassName, "", WS_POPUP, x, y, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Couldn't create the share helper window.", new System.ComponentModel.Win32Exception());
            SetLayeredWindowAttributes(hwnd, 0, 3, LWA_ALPHA);
            ShowWindow(hwnd, SW_SHOW);
            return hwnd;
        }

        public static void Destroy(IntPtr hwnd) => DestroyWindow(hwnd);

        public static void DestroyAfter(IntPtr hwnd, TimeSpan delay)
        {
            if (SetTimer(hwnd, (IntPtr)1, (uint)delay.TotalMilliseconds, IntPtr.Zero) == IntPtr.Zero) DestroyWindow(hwnd);
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TIMER)
            {
                KillTimer(hwnd, wParam);
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint elapseMs, IntPtr timerProc);
        [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, IntPtr id);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }
}
