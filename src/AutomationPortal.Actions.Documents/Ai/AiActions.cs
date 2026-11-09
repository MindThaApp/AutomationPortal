namespace AutomationPortal.Actions.Documents;

/// <summary>Turns action input into the pieces sent to the AI: pictures and PDFs as attachments, everything else as text.</summary>
public static class AiInput
{
    private const long MaxPdfBytes = 25_000_000;
    private const int MaxTextChars = 600_000;

    public static async Task<IReadOnlyList<AiPart>> ReadAsync(ContentValue content, ActionContext ctx, bool allowAttachments = true)
    {
        var parts = new List<AiPart>();
        var text = new List<string>();
        foreach (var item in content)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (item)
            {
                case FolderItem f:
                    throw new ActionException($"'{f.FullName}' is a folder. Choose a file, or use Get Contents of Folder first.");
                case PathItem p:
                    {
                        if (!p.Exists) throw new ActionException($"Couldn't find '{p.FullName}'. Check it still exists.");
                        var path = p.Path;
                        if (allowAttachments && FileKinds.IsImage(path))
                        {
                            var (data, media) = await StaWorker.Imaging.RunAsync(() => ImageEngine.PrepareForAi(path), ctx.CancellationToken);
                            parts.Add(new AiPart.Image(data, media));
                        }
                        else if (allowAttachments && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && new FileInfo(path).Length <= MaxPdfBytes)
                            parts.Add(new AiPart.Pdf(await File.ReadAllBytesAsync(path, ctx.CancellationToken)));
                        else
                        {
                            var body = await Task.Run(() => DocumentText.Read(path), ctx.CancellationToken);
                            text.Add(content.Count > 1 ? $"File: {p.FullName}\n{body}" : body);
                        }
                        break;
                    }
                default:
                    text.Add(item.DisplayText);
                    break;
            }
        }
        var joined = string.Join("\n\n", text);
        if (joined.Length > MaxTextChars) joined = joined[..MaxTextChars] + "\n[The rest was left out because it is very long.]";
        if (joined.Length > 0) parts.Add(new AiPart.Text(joined));
        return parts;
    }

    public static async Task<IReadOnlyList<AiPart>> RequireAsync(ActionContext ctx, string key, string what, bool allowAttachments = true)
    {
        var content = await ctx.GetContentAsync(key);
        if (content.IsEmpty) throw new ActionException($"There is nothing to work with. Add {what} first.");
        var parts = await ReadAsync(content, ctx, allowAttachments);
        if (parts.Count == 0) throw new ActionException($"There is nothing to work with. Add {what} first.");
        return parts;
    }
}

public sealed class AskAiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.ask", Title = "Ask AI", Category = ActionCategories.Ai, Icon = "Sparkle24",
        Keywords = new[] { "chatgpt", "claude", "question", "assistant", "gpt", "ask", "answer" },
        Summary = "Ask AI {prompt} and answer as {answerAs}",
        Description = "Asks the AI a question or gives it a task, optionally about text, a file, a picture or a PDF. Choose the kind of answer: text, Yes/No (handy for If), a number, a list, or named values. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = "AI Answer", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("prompt", "Question", null, "e.g. Is this email asking me for money?") with { Required = true },
            ParameterDescriptor.Content("input", "Look at", isInput: true),
            ParameterDescriptor.ChoiceOf("answerAs", "Answer as", "text",
                new Choice("text", "text"), new Choice("yesno", "Yes / No"), new Choice("number", "a number"),
                new Choice("list", "a list"), new Choice("dictionary", "named values")),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var prompt = (await ctx.GetTextAsync("prompt")).Trim();
        if (prompt.Length == 0) throw new ActionException("Type what you want to ask the AI.");
        var inputContent = await ctx.GetContentAsync("input");
        var input = inputContent.IsEmpty ? Array.Empty<AiPart>() : await AiInput.ReadAsync(inputContent, ctx);
        return await AiService.RunAsync(ctx, AiRequestBuilder.Ask(prompt, input, await ctx.GetChoiceAsync("answerAs", "text")));
    }
}

public sealed class SummarizeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.summarize", Title = "Summarize", Category = ActionCategories.Ai, Icon = "TextBulletList24",
        Keywords = new[] { "tldr", "shorten", "digest", "key points", "ai" },
        Summary = "Summarize {text} as {length}",
        Description = "Gets the AI to summarize text, a document, a PDF or a picture of text. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "Summary", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", "What to summarize", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("length", "Summary", "medium",
                AiRequestBuilder.SummaryLengths.Select(l => new Choice(l.Id, l.Label)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, "text", "something to summarize");
        return await AiService.RunAsync(ctx, AiRequestBuilder.Summarize(parts, await ctx.GetChoiceAsync("length", "medium")));
    }
}

public sealed class RewriteTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.rewrite", Title = "Rewrite Text", Category = ActionCategories.Ai, Icon = "TextEditStyle24",
        Keywords = new[] { "proofread", "grammar", "professional", "friendly", "shorter", "simpler", "table", "list", "tone", "ai" },
        Summary = "Rewrite {text}: {style}",
        Description = "Gets the AI to proofread text, change its tone, make it shorter, longer or simpler, or turn it into a table or a list. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Rewritten Text", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", "Text", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("style", "What to do", "proofread",
                AiRequestBuilder.RewriteStyles.Select(s => new Choice(s.Id, s.Label)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, "text", "some text", allowAttachments: false);
        var text = string.Join("\n\n", parts.OfType<AiPart.Text>().Select(t => t.Value));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Rewrite(text, await ctx.GetChoiceAsync("style", "proofread")));
    }
}

public sealed class TranslateTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.translate", Title = "Translate Text", Category = ActionCategories.Ai, Icon = "Translate24",
        Keywords = new[] { "language", "english", "spanish", "french", "german", "ai" },
        Summary = "Translate {text} to {language}",
        Description = "Gets the AI to translate text into another language. The original language is detected for you. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Translation", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", "Text", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("language", "Language", "English", AiRequestBuilder.Languages.Select(l => new Choice(l, l)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, "text", "some text to translate", allowAttachments: false);
        var text = string.Join("\n\n", parts.OfType<AiPart.Text>().Select(t => t.Value));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Translate(text, await ctx.GetChoiceAsync("language", "English")));
    }
}

public sealed class ExtractInformationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.extract", Title = "Extract Information", Category = ActionCategories.Ai, Icon = "DocumentSearch24",
        Keywords = new[] { "find", "fields", "invoice", "receipt", "parse", "details", "ai" },
        Summary = "Find {fields} in {text}",
        Description = "Gets the AI to pick out pieces of information — like a name, date or total — from text, a document, a PDF or a picture, and gives them back as named values. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Dictionary, OutputName = "Extracted Information", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.ListParam("fields", "Information to find") with { Required = true, Hint = "One item per piece of information, e.g. Name, Date, Total." },
            ParameterDescriptor.Content("text", "Where to look", isInput: true, required: true),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var fields = (await ctx.GetListAsync("fields")).SelectMany(i => i.DisplayText.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
        var parts = await AiInput.RequireAsync(ctx, "text", "something to look through");
        return await AiService.RunAsync(ctx, AiRequestBuilder.Extract(parts, fields));
    }
}

public sealed class DescribeImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.describeImage", Title = "Describe Image", Category = ActionCategories.Ai, Icon = "ImageSparkle24",
        Keywords = new[] { "alt text", "caption", "what is this", "read text", "vision", "ai" },
        Summary = "Describe {images} as {detail}",
        Description = "Gets the AI to look at a picture and describe it, write alt text, read the words in it, or answer your question about it. Needs your AI key in Settings › AI.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Text, OutputName = "Image Description", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("images", "Images", isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", "Describe", "short", AiRequestBuilder.DescribeDetails.Select(d => new Choice(d.Id, d.Label)).ToArray()),
            ParameterDescriptor.Text("question", "Or ask about the picture", null, "e.g. What color is the car?"),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException("There are no pictures to describe. Choose some pictures first.");
        var detail = await ctx.GetChoiceAsync("detail", "short");
        var question = await ctx.GetTextAsync("question");
        var texts = new List<string>();
        foreach (var img in images)
        {
            var path = img.Path;
            var (data, media) = await StaWorker.Imaging.RunAsync(() => ImageEngine.PrepareForAi(path), ctx.CancellationToken);
            var reply = await AiService.RunAsync(ctx, AiRequestBuilder.DescribeImage(new AiPart.Image(data, media), detail, question));
            texts.Add(reply.AsText());
        }
        return ContentValue.Texts(texts);
    }
}
