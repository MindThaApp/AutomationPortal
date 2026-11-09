using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// ChatGPT, Gemini, DeepSeek, Kimi, Azure OpenAI, Ollama, LM Studio and any custom OpenAI-compatible service,
/// all through the official OpenAI .NET SDK (Azure through Azure.AI.OpenAI) pointed at the right address.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    private readonly AiProviderContext _ctx;
    private readonly OpenAIClient _client;

    public OpenAiProvider(AiProviderContext context)
    {
        _ctx = context;
        var info = context.Info;
        var endpoint = context.Settings.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ActionException($"Enter the address for {info.Name} in Settings › AI.");
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ActionException($"'{endpoint}' isn't a valid address. It should look like https://example.com/v1.");
        // Local servers ignore the key, but the SDK insists on one.
        var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(context.Key) ? "not-needed" : context.Key!);
        if (info.UsesDeployment)
        {
            var options = new AzureOpenAIClientOptions();
            if (context.Http is not null) options.Transport = new HttpClientPipelineTransport(context.Http);
            _client = new AzureOpenAIClient(uri, credential, options);
        }
        else
        {
            var options = new OpenAIClientOptions { Endpoint = uri };
            if (context.Http is not null) options.Transport = new HttpClientPipelineTransport(context.Http);
            _client = new OpenAIClient(credential, options);
        }
    }

    public string Id => _ctx.Info.Id;
    public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(AiAvailability.Yes);

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        if (_ctx.Info.UsesDeployment)
            return string.IsNullOrWhiteSpace(_ctx.Settings.Deployment) ? Array.Empty<string>() : new[] { _ctx.Settings.Deployment! };
        try
        {
            var result = await _client.GetOpenAIModelClient().GetModelsAsync(ct);
            return result.Value.Select(m => m.Id.StartsWith("models/") ? m.Id[7..] : m.Id).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) { throw AiErrors.Map(ex, _ctx.Info, ct); }
    }

    /// <summary>The chat messages and options for a request (public so request building can be unit-tested).</summary>
    public static (List<ChatMessage> Messages, ChatCompletionOptions Options) Build(AiProviderInfo info, AiRequest request)
    {
        var parts = new List<ChatMessageContentPart>();
        foreach (var part in request.Parts)
        {
            switch (part)
            {
                case AiPart.Image img:
                    if (!info.SupportsImages)
                        throw new ActionException($"{info.Name} can't look at pictures. Choose another provider for this action.");
                    parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(img.Data), img.MediaType));
                    break;
                case AiPart.Pdf pdf:
                    parts.Add(ChatMessageContentPart.CreateTextPart("Text of the attached PDF:\n" + PdfToText(pdf.Data)));
                    break;
                case AiPart.Text t:
                    parts.Add(ChatMessageContentPart.CreateTextPart(t.Value));
                    break;
            }
        }
        var messages = new List<ChatMessage> { new SystemChatMessage(request.System), new UserChatMessage(parts) };
        var options = new ChatCompletionOptions();
        if (request.Shape != AiShape.Text)
        {
            if (info.JsonMode == AiJsonMode.JsonSchema && request.SchemaJson is not null)
                options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("answer", BinaryData.FromString(request.SchemaJson), jsonSchemaIsStrict: true);
            else if (info.JsonMode != AiJsonMode.None)
                options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }
        return (messages, options);
    }

    /// <summary>Providers other than Claude can't read PDFs directly, so the PDF's text is sent instead.</summary>
    public static string PdfToText(byte[] pdf)
    {
        try
        {
            using var doc = PigDocument.Open(pdf);
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page).TrimEnd());
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw new ActionException("Couldn't read the text of the PDF to send it to this provider.", ex);
        }
    }

    public async Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ActionException($"Choose a model for {_ctx.Info.Name} in Settings › AI.");
        var chat = _client.GetChatClient(model);
        var (messages, options) = Build(_ctx.Info, request);
        ChatCompletion completion;
        try { completion = (await chat.CompleteChatAsync(messages, options, ct)).Value; }
        catch (ClientResultException ex) when (ex.Status == 400 && options.ResponseFormat is not null)
        {
            // Some models or servers don't know structured answers: ask again with plain instructions (the prompt already describes the JSON).
            try { completion = (await chat.CompleteChatAsync(messages, new ChatCompletionOptions(), ct)).Value; }
            catch (Exception ex2) { throw AiErrors.Map(ex2, _ctx.Info, ct); }
        }
        catch (Exception ex) { throw AiErrors.Map(ex, _ctx.Info, ct); }
        return ReadReply(completion);
    }

    public static string ReadReply(ChatCompletion completion)
    {
        if (completion.FinishReason == ChatFinishReason.ContentFilter || !string.IsNullOrWhiteSpace(completion.Refusal))
            throw new ActionException("The AI declined to answer that. Try rewording your request.");
        var text = string.Concat(completion.Content.Where(c => c.Kind == ChatMessageContentPartKind.Text).Select(c => c.Text));
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException("The AI didn't send back an answer. Try again.");
        return text;
    }
}
