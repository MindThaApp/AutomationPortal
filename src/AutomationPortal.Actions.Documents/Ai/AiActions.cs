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
                    throw new ActionException(Strings.Format("AiInput_Error_IsAFolderChoose", f.FullName));
                case PathItem p:
                    {
                        if (!p.Exists) throw new ActionException(Strings.Format("AiInput_Error_CouldnTFindCheck", p.FullName));
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
        if (content.IsEmpty) throw new ActionException(Strings.Format("AiInput_Error_ThereIsNothingTo", what));
        var parts = await ReadAsync(content, ctx, allowAttachments);
        if (parts.Count == 0) throw new ActionException(Strings.Format("AiInput_Error_ThereIsNothingTo", what));
        return parts;
    }
}

public sealed class AskAiAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.ask", Title = Strings.Get("AskAiAction_Title"), Category = ActionCategories.Ai, Icon = "Sparkle24",
        Keywords = new[] { "chatgpt", "claude", "question", "assistant", "gpt", "ask", "answer" },
        Summary = Strings.Get("AskAiAction_Summary"),
        Description = Strings.Get("AskAiAction_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = Strings.Get("AskAiAction_OutputName"), NeedsInternet = true,
        OutputKindFor = a => AnswerKind(a.Parameters.TryGetValue("answerAs", out var v) && v is ChoiceValue c ? c.Value : "text"),
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("prompt", Strings.Get("AskAiAction_Param_prompt_Label"), null, Strings.Get("AskAiAction_Param_prompt_Placeholder")) with { Required = true },
            ParameterDescriptor.Content("input", Strings.Get("AskAiAction_Param_input_Label"), isInput: true),
            ParameterDescriptor.ChoiceOf("answerAs", Strings.Get("AskAiAction_Param_answerAs_Label"), "text",
                new Choice("text", Strings.Get("AskAiAction_Param_answerAs_Choice_text")), new Choice("yesno", Strings.Get("AskAiAction_Param_answerAs_Choice_yesno")), new Choice("number", Strings.Get("AskAiAction_Param_answerAs_Choice_number")),
                new Choice("list", Strings.Get("AskAiAction_Param_answerAs_Choice_list")), new Choice("choice", Strings.Get("AskAiAction_Param_answerAs_Choice_choice")), new Choice("dictionary", Strings.Get("AskAiAction_Param_answerAs_Choice_dictionary"))) with
            {
                Hint = Strings.Get("AskAiAction_Param_answerAs_Hint"),
            },
            ParameterDescriptor.ListParam("options", Strings.Get("AskAiAction_Param_options_Label")) with
            {
                VisibleWhen = new VisibleWhen("answerAs", "choice"), AllowAskEachTime = false,
                Hint = Strings.Get("AskAiAction_Param_options_Hint"),
            },
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    private static ContentKind AnswerKind(string answerAs) => answerAs.ToLowerInvariant() switch
    {
        "yesno" => ContentKind.Boolean, "number" => ContentKind.Number, "dictionary" => ContentKind.Dictionary,
        "list" => ContentKind.Any, _ => ContentKind.Text,
    };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var prompt = (await ctx.GetTextAsync("prompt")).Trim();
        if (prompt.Length == 0) throw new ActionException(Strings.Get("AskAiAction_Error_TypeWhatYouWant"));
        var inputContent = await ctx.GetContentAsync("input");
        var input = inputContent.IsEmpty ? Array.Empty<AiPart>() : await AiInput.ReadAsync(inputContent, ctx);
        var answerAs = await ctx.GetChoiceAsync("answerAs", "text");
        var options = answerAs.Equals("choice", StringComparison.OrdinalIgnoreCase)
            ? (await ctx.GetListAsync("options")).SelectMany(i => i.DisplayText.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList()
            : null;
        return await AiService.RunAsync(ctx, AiRequestBuilder.Ask(prompt, input, answerAs, options));
    }
}

public sealed class SummarizeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.summarize", Title = Strings.Get("SummarizeAction_Title"), Category = ActionCategories.Ai, Icon = "TextBulletList24",
        Keywords = new[] { "tldr", "shorten", "digest", "key points", "ai" },
        Summary = Strings.Get("SummarizeAction_Summary"),
        Description = Strings.Get("SummarizeAction_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = Strings.Get("SummarizeAction_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", Strings.Get("SummarizeAction_Param_text_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("length", Strings.Get("SummarizeAction_Param_length_Label"), "medium",
                AiRequestBuilder.SummaryLengths.Select(l => new Choice(l.Id, l.Label)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, Strings.Get("SummarizeAction_Noun_Text"), Strings.Get("SummarizeAction_Noun_SomethingToSummarize"));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Summarize(parts, await ctx.GetChoiceAsync("length", "medium")));
    }
}

public sealed class RewriteTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.rewrite", Title = Strings.Get("RewriteTextAction_Title"), Category = ActionCategories.Ai, Icon = "TextEditStyle24",
        Keywords = new[] { "proofread", "grammar", "professional", "friendly", "shorter", "simpler", "table", "list", "tone", "ai" },
        Summary = Strings.Get("RewriteTextAction_Summary"),
        Description = Strings.Get("RewriteTextAction_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("RewriteTextAction_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", Strings.Get("RewriteTextAction_Param_text_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("style", Strings.Get("RewriteTextAction_Param_style_Label"), "proofread",
                AiRequestBuilder.RewriteStyles.Select(s => new Choice(s.Id, s.Label)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, Strings.Get("RewriteTextAction_Noun_Text"), Strings.Get("RewriteTextAction_Noun_SomeText"), allowAttachments: false);
        var text = string.Join("\n\n", parts.OfType<AiPart.Text>().Select(t => t.Value));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Rewrite(text, await ctx.GetChoiceAsync("style", "proofread")));
    }
}

public sealed class TranslateTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.translate", Title = Strings.Get("TranslateTextAction_Title"), Category = ActionCategories.Ai, Icon = "Translate24",
        Keywords = new[] { "language", "english", "spanish", "french", "german", "ai" },
        Summary = Strings.Get("TranslateTextAction_Summary"),
        Description = Strings.Get("TranslateTextAction_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TranslateTextAction_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("text", Strings.Get("TranslateTextAction_Param_text_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("language", Strings.Get("TranslateTextAction_Param_language_Label"), "English", AiRequestBuilder.Languages.Select(l => new Choice(l, l)).ToArray()),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var parts = await AiInput.RequireAsync(ctx, Strings.Get("TranslateTextAction_Noun_Text"), Strings.Get("TranslateTextAction_Noun_SomeTextToTranslate"), allowAttachments: false);
        var text = string.Join("\n\n", parts.OfType<AiPart.Text>().Select(t => t.Value));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Translate(text, await ctx.GetChoiceAsync("language", "English")));
    }
}

public sealed class ExtractInformationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.extract", Title = Strings.Get("ExtractInformationAction_Title"), Category = ActionCategories.Ai, Icon = "DocumentSearch24",
        Keywords = new[] { "find", "fields", "invoice", "receipt", "parse", "details", "ai" },
        Summary = Strings.Get("ExtractInformationAction_Summary"),
        Description = Strings.Get("ExtractInformationAction_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("ExtractInformationAction_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.ListParam("fields", Strings.Get("ExtractInformationAction_Param_fields_Label")) with { Required = true, Hint = Strings.Get("ExtractInformationAction_Param_fields_Hint") },
            ParameterDescriptor.Content("text", Strings.Get("ExtractInformationAction_Param_text_Label"), isInput: true, required: true),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var fields = (await ctx.GetListAsync("fields")).SelectMany(i => i.DisplayText.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var parts = await AiInput.RequireAsync(ctx, Strings.Get("ExtractInformationAction_Noun_Text"), Strings.Get("ExtractInformationAction_Noun_SomethingToLookThrough"));
        return await AiService.RunAsync(ctx, AiRequestBuilder.Extract(parts, fields));
    }
}

public sealed class DescribeImageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ai.describeImage", Title = Strings.Get("DescribeImageAction_Title"), Category = ActionCategories.Ai, Icon = "ImageSparkle24",
        Keywords = new[] { "alt text", "caption", "what is this", "read text", "vision", "ai" },
        Summary = Strings.Get("DescribeImageAction_Summary"),
        Description = Strings.Get("DescribeImageAction_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Text, OutputName = Strings.Get("DescribeImageAction_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("images", Strings.Get("DescribeImageAction_Param_images_Label"), isInput: true) with { Required = true },
            ParameterDescriptor.ChoiceOf("detail", Strings.Get("DescribeImageAction_Param_detail_Label"), "short", AiRequestBuilder.DescribeDetails.Select(d => new Choice(d.Id, d.Label)).ToArray()),
            ParameterDescriptor.Text("question", Strings.Get("DescribeImageAction_Param_question_Label"), null, Strings.Get("DescribeImageAction_Param_question_Placeholder")),
            AiModels.ProviderParameter(),
            AiModels.ModelParameter(),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("images");
        if (images.Count == 0) throw new ActionException(Strings.Get("DescribeImageAction_Error_ThereAreNoPictures"));
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
