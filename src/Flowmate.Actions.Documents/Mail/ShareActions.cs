using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace Flowmate.Actions.Documents;

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
            ParameterDescriptor.Text("title", "Title", "Shared from Flow Mate"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("items");
        if (content.IsEmpty) throw new ActionException("There is nothing to share. Add some text, a link or files first.");
        var title = await ctx.GetTextAsync("title", "Shared from Flow Mate");

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
        var staging = Path.Combine(Path.GetTempPath(), "Flowmate", "share", Guid.NewGuid().ToString("N")[..8]);
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
            var root = Path.Combine(Path.GetTempPath(), "Flowmate", "share");
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

    /// <summary>Runs on the UI (STA) thread.</summary>
    public static void Show(string title, string text, Uri? link, IReadOnlyList<Windows.Storage.IStorageItem> files)
    {
        var window = new System.Windows.Window
        {
            Width = 1, Height = 1, WindowStyle = System.Windows.WindowStyle.None, ShowInTaskbar = false,
            ResizeMode = System.Windows.ResizeMode.NoResize, Opacity = 0.01, Topmost = true,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
        };
        window.Show();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        try
        {
            var interop = GetInterop();
            var manager = GetManager(interop, hwnd);

            manager.DataRequested += (_, args) =>
            {
                var data = args.Request.Data;
                data.Properties.Title = string.IsNullOrWhiteSpace(title) ? "Shared from Flow Mate" : title;
                if (!string.IsNullOrEmpty(text)) data.SetText(text);
                if (link is not null) data.SetWebLink(link);
                if (files.Count > 0) data.SetStorageItems(files);
            };
            interop.ShowShareUIForWindow(hwnd);
        }
        catch
        {
            window.Close();
            throw;
        }
        // Keep the helper window alive while the share window is open, then tidy up.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        timer.Start();
    }
}
