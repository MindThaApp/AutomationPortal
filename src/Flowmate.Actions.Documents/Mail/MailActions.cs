using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Flowmate.Actions.Documents;

public static class MailTo
{
    /// <summary>Splits "a@x.com; b@y.com, c@z.org" into addresses. Throws a friendly error for anything that isn't an address.</summary>
    public static IReadOnlyList<string> ParseRecipients(string text, string what = "recipient")
    {
        var list = new List<string>();
        foreach (var part in Regex.Split(text ?? "", @"[;,\s]+"))
        {
            var a = part.Trim().Trim('<', '>');
            if (a.Length == 0) continue;
            if (!Regex.IsMatch(a, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                throw new ActionException($"'{a}' doesn't look like an email address. Check the {what}.");
            list.Add(a);
        }
        return list;
    }

    /// <summary>Builds a mailto: link; the body is shortened if the link would get too long for Windows to open.</summary>
    public static string Build(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string body, int maxLength = 1900)
    {
        string Make(string b)
        {
            var sb = new StringBuilder("mailto:");
            sb.Append(string.Join(",", to.Select(Uri.EscapeDataString).Select(s => s.Replace("%40", "@"))));
            var query = new List<string>();
            if (cc.Count > 0) query.Add("cc=" + string.Join(",", cc.Select(Uri.EscapeDataString).Select(s => s.Replace("%40", "@"))));
            if (subject.Length > 0) query.Add("subject=" + Uri.EscapeDataString(subject));
            if (b.Length > 0) query.Add("body=" + Uri.EscapeDataString(b.Replace("\r\n", "\n").Replace("\n", "\r\n")));
            if (query.Count > 0) sb.Append('?').Append(string.Join("&", query));
            return sb.ToString();
        }
        var link = Make(body);
        if (link.Length <= maxLength) return link;
        var cut = body.Length;
        while (cut > 0 && link.Length > maxLength)
        {
            cut = Math.Max(0, cut - Math.Max(20, (link.Length - maxLength) / 3));
            link = Make(body[..cut].TrimEnd() + "…");
        }
        return link;
    }
}

internal static class MailParams
{
    public static ParameterDescriptor[] Common() => new[]
    {
        ParameterDescriptor.Text("to", "To", null, "name@example.com; another@example.com") with { Required = true },
        ParameterDescriptor.Text("subject", "Subject"),
        ParameterDescriptor.MultilineText("body", "Message"),
        ParameterDescriptor.Of("attachments", "Attachments", ParamType.File) with { Hint = "Attachments need classic Outlook. Without it, only a plain message is opened." },
        ParameterDescriptor.Text("cc", "Copy to (Cc)"),
    };

    /// <summary>Text from the previous action becomes the message; files from the previous action become attachments.</summary>
    public static async Task<MailDraft> ReadAsync(ActionContext ctx, bool requireTo)
    {
        var to = MailTo.ParseRecipients(await ctx.GetTextAsync("to"), "To address");
        if (requireTo && to.Count == 0) throw new ActionException("Type who the email is for in the To box.");
        var cc = MailTo.ParseRecipients(await ctx.GetTextAsync("cc"), "Cc address");
        var subject = await ctx.GetTextAsync("subject");
        var body = await ctx.GetTextAsync("body");

        var attachments = new List<string>();
        if (ctx.GetParameter("attachments") is { } && !(await ctx.GetContentAsync("attachments")).IsEmpty)
            attachments.AddRange((await ctx.GetFilesAsync("attachments")).Select(f => f.Path));
        else if (!ctx.Input.IsEmpty && body.Length == 0 && ctx.Input.All(i => i is PathItem and not FolderItem))
            attachments.AddRange(ctx.Input.OfType<PathItem>().Select(p => p.Path));
        else if (!ctx.Input.IsEmpty && body.Length == 0)
            body = ctx.Input.AsText();

        foreach (var a in attachments)
            if (!File.Exists(a)) throw new ActionException($"Couldn't find the attachment '{Path.GetFileName(a)}'. Check it still exists.");
        return new MailDraft(string.Join("; ", to), string.Join("; ", cc), subject, body, attachments);
    }
}

public sealed class ComposeEmailAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "mail.compose", Title = "Compose Email", Category = ActionCategories.EmailAndSharing, Icon = "MailEdit24",
        Keywords = new[] { "email", "outlook", "draft", "mailto", "attach" },
        Summary = "Write an email to {to} about {subject}",
        Description = "Opens a new email, ready for you to check and send. With classic Outlook it can include attachments; otherwise your default email app opens with the message (attachments can't be added that way).",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, HasOutput = false, OutputName = "Email",
        Parameters = MailParams.Common(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var draft = await MailParams.ReadAsync(ctx, requireTo: false);
        if (await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
        {
            try
            {
                await StaWorker.Com.RunAsync(() => OutlookBridge.ComposeMail(draft, send: false), ctx.CancellationToken);
                return ContentValue.Empty;
            }
            catch (ActionException ex) { ctx.Log($"Outlook couldn't open the draft ({ex.Message}); using the default email app."); }
        }
        if (draft.Attachments.Count > 0)
            ctx.ReportProgress("Attachments were left out: adding them needs classic Outlook.");
        var link = MailTo.Build(MailTo.ParseRecipients(draft.To), MailTo.ParseRecipients(draft.Cc), draft.Subject, draft.Body);
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException("Couldn't open an email app. Choose a default email app in Windows Settings › Default apps.", ex);
        }
        return ContentValue.Empty;
    }
}

public sealed class SendEmailAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "mail.send", Title = "Send Email via Outlook", Category = ActionCategories.EmailAndSharing, Icon = "MailArrowUp24",
        Keywords = new[] { "email", "outlook", "send", "attach" },
        Summary = "Send an email to {to} about {subject}",
        Description = "Sends an email through classic Outlook. By default the message is shown to you first so you can check it before it goes. Requires classic Outlook.",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, HasOutput = false, OutputName = "Email",
        IsDestructive = true,
        Parameters = MailParams.Common().Concat(new[]
        {
            ParameterDescriptor.Bool("showFirst", "Show the email before sending", true) with
            {
                Hint = "Turn this off only if you're sure — the email is sent immediately and can't be taken back.",
            },
        }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var draft = await MailParams.ReadAsync(ctx, requireTo: true);
        var showFirst = await ctx.GetBoolAsync("showFirst", true);
        if (!await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
            throw new ActionException("Sending needs classic Outlook, which isn't installed on this PC. Use Compose Email instead to open the message in your email app.");
        await StaWorker.Com.RunAsync(() => OutlookBridge.ComposeMail(draft, send: !showFirst), ctx.CancellationToken);
        ctx.ReportProgress(showFirst ? "Opened the email in Outlook" : "Email sent");
        return ContentValue.Empty;
    }
}
