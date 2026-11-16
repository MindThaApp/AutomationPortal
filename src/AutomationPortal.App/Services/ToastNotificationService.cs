using System.Collections.Concurrent;
using AutomationPortal.Core;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace AutomationPortal.App.Services;

/// <summary>
/// Windows toasts for an unpackaged app (Microsoft.Toolkit.Uwp.Notifications). Buttons and reply boxes are
/// routed back to the awaiting caller while the app runs. If toasts are blocked (Focus Assist is fine, but
/// policies or a missing shell can break them) it falls back to a tray balloon or an in-app dialog.
/// </summary>
public sealed class ToastNotificationService : INotificationService, IDisposable
{
    private sealed class Pending
    {
        public TaskCompletionSource<NotificationResponse>? Tcs;
        public Action? OpenOnClick;
        public ToastNotification? Toast;
        public Dictionary<string, Action>? Actions;
    }

    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly IUiThread _ui;
    private readonly Func<IUserInteraction> _interaction;
    private readonly Action<string, string>? _balloonFallback;

    public ToastNotificationService(IUiThread ui, Func<IUserInteraction> interaction, Action<string, string>? balloonFallback = null)
    {
        _ui = ui;
        _interaction = interaction;
        _balloonFallback = balloonFallback;
        try { ToastNotificationManagerCompat.OnActivated += OnActivated; }
        catch (Exception) { /* toasts unavailable: handled when showing */ }
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        ToastArguments args;
        try { args = ToastArguments.Parse(e.Argument); }
        catch { return; }
        if (!args.TryGetValue("id", out var id) || !_pending.TryRemove(id, out var p)) return;

        args.TryGetValue("action", out var action);
        if (action == "button")
        {
            args.TryGetValue("label", out var label);
            string? reply = null;
            if (e.UserInput is { } input && input.TryGetValue("reply", out var r)) reply = r?.ToString();
            if (label is not null && p.Actions?.TryGetValue(label, out var act) == true) _ = _ui.InvokeAsync(act);
            p.Tcs?.TrySetResult(new NotificationResponse(label, string.IsNullOrEmpty(reply) ? null : reply, false));
        }
        else
        {
            if (p.OpenOnClick is { } open) _ = _ui.InvokeAsync(open);
            p.Tcs?.TrySetResult(new NotificationResponse(null, null, false));
        }
    }

    public void Show(string title, string body, string? imagePath = null, Action? openOnClick = null, bool silent = false)
    {
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var builder = Base(title, body, imagePath, id, "open");
            if (silent) builder.AddAudio(new ToastAudio { Silent = true });
            var toast = Build(builder, id);
            _pending[id] = new Pending { OpenOnClick = openOnClick, Toast = toast };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            // Unclicked plain toasts shouldn't pile up in memory.
            _ = Task.Delay(TimeSpan.FromMinutes(30)).ContinueWith(_ => _pending.TryRemove(id, out Pending? _));
        }
        catch (Exception)
        {
            _pending.TryRemove(id, out _);
            _balloonFallback?.Invoke(title, body);
        }
    }

    /// <summary>
    /// A toast with buttons that call back on the UI thread while the app runs, such as Undo and See details after a run. Each
    /// button works once. When toasts are blocked it falls back to a balloon without buttons.
    /// </summary>
    public void ShowWithActions(string title, string body, IReadOnlyList<(string Label, Action OnClick)> actions, Action? openOnClick = null)
    {
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var builder = Base(title, body, null, id, "open");
            foreach (var (label, _) in actions.Take(5))
                builder.AddButton(new ToastButton().SetContent(label).AddArgument("id", id).AddArgument("action", "button").AddArgument("label", label).SetBackgroundActivation());
            var toast = Build(builder, id);
            _pending[id] = new Pending { OpenOnClick = openOnClick, Toast = toast, Actions = actions.Take(5).ToDictionary(a => a.Label, a => a.OnClick) };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            _ = Task.Delay(TimeSpan.FromMinutes(30)).ContinueWith(_ => _pending.TryRemove(id, out Pending? _));
        }
        catch (Exception)
        {
            _pending.TryRemove(id, out _);
            _balloonFallback?.Invoke(title, body);
        }
    }

    public async Task<NotificationResponse> ShowInteractiveAsync(string title, string body, IReadOnlyList<string> buttons, bool allowReply,
        string? imagePath = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<NotificationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ToastNotification? toast = null;
        try
        {
            var builder = Base(title, body, imagePath, id, "open");
            if (allowReply)
            {
                builder.AddInputTextBox("reply", Strings.Get("ToastNotificationService_TypeAReply"));
                builder.AddButton(new ToastButton().SetContent("Send").AddArgument("id", id).AddArgument("action", "button").AddArgument("label", "Send").SetTextBoxId("reply").SetBackgroundActivation());
            }
            foreach (var label in buttons.Take(5))
                builder.AddButton(new ToastButton().SetContent(label).AddArgument("id", id).AddArgument("action", "button").AddArgument("label", label).SetBackgroundActivation());
            if (buttons.Count > 0 || allowReply) builder.SetToastScenario(ToastScenario.Reminder);

            toast = Build(builder, id);
            toast.Dismissed += (_, e) =>
            {
                if (e.Reason == ToastDismissalReason.UserCanceled || e.Reason == ToastDismissalReason.TimedOut)
                {
                    if (_pending.TryRemove(id, out var p)) p.Tcs?.TrySetResult(new NotificationResponse(null, null, true));
                }
            };
            _pending[id] = new Pending { Tcs = tcs, Toast = toast };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
        }
        catch (Exception)
        {
            _pending.TryRemove(id, out _);
            return await FallbackAsync(title, body, buttons, ct).ConfigureAwait(false);
        }

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        var wait = timeout ?? TimeSpan.FromMinutes(5);
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(wait, ct)).ConfigureAwait(false);
        if (finished != tcs.Task)
        {
            ct.ThrowIfCancellationRequested();
            _pending.TryRemove(id, out _);
            try { ToastNotificationManagerCompat.CreateToastNotifier().Hide(toast); } catch { }
            return new NotificationResponse(null, null, true);
        }
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<bool> AskToRunAsync(string shortcutName, string reason, CancellationToken ct = default)
    {
        var r = await ShowInteractiveAsync($"Run \"{shortcutName}\"?", reason, new[] { "Run", "Skip" }, false, null, TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
        return r.ButtonLabel == "Run";
    }

    private async Task<NotificationResponse> FallbackAsync(string title, string body, IReadOnlyList<string> buttons, CancellationToken ct)
    {
        if (buttons.Count == 0) { _balloonFallback?.Invoke(title, body); return new NotificationResponse(null, null, true); }
        var ok = await _interaction().ShowAlertAsync(title, body, true, ct).ConfigureAwait(false);
        return ok ? new NotificationResponse(buttons[0], null, false) : new NotificationResponse(buttons.Count > 1 ? buttons[1] : null, null, buttons.Count <= 1);
    }

    private static ToastContentBuilder Base(string title, string body, string? imagePath, string id, string action)
    {
        var b = new ToastContentBuilder()
            .AddArgument("id", id)
            .AddArgument("action", action)
            .AddText(title)
            .AddText(body);
        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            b.AddInlineImage(new Uri(imagePath));
        return b;
    }

    private static ToastNotification Build(ToastContentBuilder builder, string id)
    {
        var toast = new ToastNotification(builder.GetXml()) { Tag = id.Length > 16 ? id[..16] : id };
        return toast;
    }

    public void Dispose()
    {
        try { ToastNotificationManagerCompat.OnActivated -= OnActivated; } catch { }
        foreach (var p in _pending.Values) p.Tcs?.TrySetResult(new NotificationResponse(null, null, true));
    }
}
