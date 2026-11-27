using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationPortal.Actions.Documents;

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
                throw new ActionException(Strings.Format("MailTo_Error_DoesnTLookLike", a, what));
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
        ParameterDescriptor.Text("to", Strings.Get("MailParams_Param_to_Label"), null, Strings.Get("MailParams_Param_to_Placeholder")) with { Required = true },
        ParameterDescriptor.Text("subject", Strings.Get("MailParams_Param_subject_Label")),
        ParameterDescriptor.MultilineText("body", Strings.Get("MailParams_Param_body_Label")),
        ParameterDescriptor.Of("attachments", Strings.Get("MailParams_Param_attachments_Label"), ParamType.File) with { Hint = Strings.Get("MailParams_Param_attachments_Hint") },
        ParameterDescriptor.Text("cc", Strings.Get("MailParams_Param_cc_Label")),
    };

    /// <summary>Text from the previous action becomes the message; files from the previous action become attachments.</summary>
    public static async Task<MailDraft> ReadAsync(ActionContext ctx, bool requireTo)
    {
        var to = MailTo.ParseRecipients(await ctx.GetTextAsync("to"), Strings.Get("MailParams_Noun_ToAddress"));
        if (requireTo && to.Count == 0) throw new ActionException(Strings.Get("MailParams_Error_TypeWhoTheEmail"));
        var cc = MailTo.ParseRecipients(await ctx.GetTextAsync("cc"), Strings.Get("MailParams_Noun_CcAddress"));
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
            if (!File.Exists(a)) throw new ActionException(Strings.Format("MailParams_Error_CouldnTFindThe", Path.GetFileName(a)));
        return new MailDraft(string.Join("; ", to), string.Join("; ", cc), subject, body, attachments);
    }
}

public sealed class ComposeEmailAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "mail.compose", Title = Strings.Get("ComposeEmailAction_Title"), Category = ActionCategories.EmailAndSharing, Icon = "MailEdit24",
        Keywords = new[] { "email", "outlook", "draft", "mailto", "attach" },
        Summary = Strings.Get("ComposeEmailAction_Summary"),
        Description = Strings.Get("ComposeEmailAction_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, HasOutput = false, OutputName = Strings.Get("ComposeEmailAction_OutputName"),
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
            ctx.ReportProgress(Strings.Get("ComposeEmailAction_Progress_AttachmentsWereLeftOut"));
        var link = MailTo.Build(MailTo.ParseRecipients(draft.To), MailTo.ParseRecipients(draft.Cc), draft.Subject, draft.Body);
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException(Strings.Get("ComposeEmailAction_Error_CouldnTOpenAn"), ex);
        }
        return ContentValue.Empty;
    }
}

public sealed class SendEmailAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "mail.send", Title = Strings.Get("SendEmailAction_Title"), Category = ActionCategories.EmailAndSharing, Icon = "MailArrowUp24",
        Keywords = new[] { "email", "outlook", "send", "attach" },
        Summary = Strings.Get("SendEmailAction_Summary"),
        Description = Strings.Get("SendEmailAction_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, HasOutput = false, OutputName = Strings.Get("SendEmailAction_OutputName"),
        IsDestructive = true, ImportRisk = ImportRisk.SendsData,
        Parameters = MailParams.Common().Concat(new[]
        {
            ParameterDescriptor.Bool("showFirst", Strings.Get("SendEmailAction_Param_showFirst_Label"), true) with
            {
                Hint = Strings.Get("SendEmailAction_Param_showFirst_Hint"),
            },
        }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var draft = await MailParams.ReadAsync(ctx, requireTo: true);
        var showFirst = await ctx.GetBoolAsync("showFirst", true);
        if (!await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable, ctx.CancellationToken))
            throw new ActionException(Strings.Get("SendEmailAction_Error_SendingNeedsClassicOutlook"));
        await StaWorker.Com.RunAsync(() => OutlookBridge.ComposeMail(draft, send: !showFirst), ctx.CancellationToken);
        ctx.ReportProgress(showFirst ? Strings.Get("SendEmailAction_Progress_OpenedTheEmailIn") : Strings.Get("SendEmailAction_Progress_EmailSent"));
        return ContentValue.Empty;
    }
}
