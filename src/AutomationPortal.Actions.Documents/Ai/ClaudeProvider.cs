using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace AutomationPortal.Actions.Documents;

/// <summary>Claude through the official Anthropic SDK (non-streaming; adaptive thinking is left at its default).</summary>
public sealed class ClaudeProvider : IAiProvider
{
    private readonly AnthropicClient _client;
    private readonly AiProviderInfo _info;

    public ClaudeProvider(AiProviderContext context)
    {
        _info = context.Info;
        var key = context.Key ?? throw new ActionException(Strings.Get("AiProviderStore_Message_AddYourAIKey"));
        _client = context.Http is null ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = key, HttpClient = context.Http };
    }

    public string Id => AiProviderIds.Claude;
    public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(AiAvailability.Yes);

    /// <summary>Claude's fixed model ids (Best, Smart, Fast).</summary>
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { AiModels.Best, AiModels.Smart, AiModels.Fast });

    /// <summary>Builds the SDK request. Effort is skipped on Haiku (unsupported); no thinking setting is sent (it is on by default and can't be disabled on newer models).</summary>
    public static MessageCreateParams BuildParams(string model, AiRequest request)
    {
        var content = new List<ContentBlockParam>();
        foreach (var part in request.Parts)
        {
            switch (part)
            {
                case AiPart.Image img:
                    content.Add(new ImageBlockParam { Source = new Base64ImageSource { Data = Convert.ToBase64String(img.Data), MediaType = MediaTypeOf(img.MediaType) } });
                    break;
                case AiPart.Pdf pdf:
                    content.Add(new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf.Data) } });
                    break;
                case AiPart.Text text:
                    content.Add(new TextBlockParam { Text = text.Value });
                    break;
            }
        }

        Effort? effort = null;
        if (request.Effort != AiEffort.None && AiModels.SupportsEffort(model))
            effort = request.Effort == AiEffort.Low ? Effort.Low : Effort.Medium;
        JsonOutputFormat? format = null;
        if (request.SchemaJson is not null)
            format = new JsonOutputFormat { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.SchemaJson)! };
        OutputConfig? output = null;
        if (effort is not null || format is not null)
        {
            output = new OutputConfig { Format = format };
            if (effort is { } e) output = new OutputConfig { Effort = e, Format = format };
        }

        return new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = request.System,
            Messages = [new MessageParam { Role = Role.User, Content = content }],
            OutputConfig = output,
        };
    }

    private static MediaType MediaTypeOf(string mediaType) => mediaType switch
    {
        "image/png" => MediaType.ImagePng,
        "image/gif" => MediaType.ImageGif,
        "image/webp" => MediaType.ImageWebP,
        _ => MediaType.ImageJpeg,
    };

    public async Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct)
    {
        Message response;
        try { response = await _client.Messages.Create(BuildParams(model, request), ct); }
        catch (Exception ex) { throw AiErrors.Map(ex, _info, ct); }
        return ReadReply(response);
    }

    public static string ReadReply(Message response)
    {
        if (response.StopReason == "refusal")
            throw new ActionException(Strings.Get("ClaudeProvider_Error_TheAIDeclinedTo"));
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException(Strings.Get("ClaudeProvider_Error_TheAIDidnT"));
        return text;
    }
}

/// <summary>Plain-English versions of SDK and network failures, the same for every provider. Never includes an API key.</summary>
public static class AiErrors
{
    public static Exception Map(Exception ex, AiProviderInfo info, CancellationToken ct = default)
    {
        if (ex is ActionException || (ct.IsCancellationRequested && ex is OperationCanceledException)) return ex;
        if (ex is AggregateException agg && agg.InnerExceptions.Count > 0) return Map(agg.InnerExceptions[0], info, ct);
        switch (ex)
        {
            case AnthropicUnauthorizedException or AnthropicForbiddenException:
                return Key(info, ex);
            case AnthropicRateLimitException:
                return Busy(ex);
            case AnthropicNotFoundException:
                return NoModel(info, ex);
            case AnthropicBadRequestException bad:
                return Bad(bad.ResponseBody, ex);
            case AnthropicApiException api when (int)api.StatusCode == 529 || api is Anthropic5xxException:
                return Busy(ex);
            case AnthropicApiException:
                return new ActionException(Strings.Get("AiErrors_Error_TheAIServiceCouldn"), ex);
            case AnthropicIOException:
                return Network(info, ex);
            case System.ClientModel.ClientResultException cre:
                return cre.Status switch
                {
                    401 or 403 => Key(info, ex),
                    429 => Busy(ex),
                    404 => NoModel(info, ex),
                    400 or 422 => Bad(cre.Message, ex),
                    >= 500 => Busy(ex),
                    0 => Network(info, ex),
                    _ => new ActionException(Strings.Get("AiErrors_Error_TheAIServiceCouldn"), ex),
                };
            case HttpRequestException or IOException or System.Net.Sockets.SocketException:
                return Network(info, ex);
            case TaskCanceledException or TimeoutException:
                return new ActionException(Strings.Get("AiErrors_Error_TheAIServiceTook"), ex);
            default:
                if (ex.InnerException is HttpRequestException or System.Net.Sockets.SocketException) return Network(info, ex);
                return ex;
        }
    }

    private static ActionException Key(AiProviderInfo info, Exception ex) =>
        new(info.IsLocal ? Strings.Format("AiErrors_Message_RefusedTheRequestCheck", info.Name) : Strings.Get("AiErrors_Message_YourAIKeyIsn"), ex);
    private static ActionException Busy(Exception ex) => new(Strings.Get("AiErrors_Message_TheAIServiceIs"), ex);
    private static ActionException NoModel(AiProviderInfo info, Exception ex) =>
        new(info.IsLocal ? Strings.Format("AiErrors_Message_TheSelectedAIModel", info.Name) : Strings.Get("AiErrors_Message_TheSelectedAIModelKey"), ex);
    private static ActionException Bad(string? body, Exception ex) =>
        new(body?.Contains("credit balance", StringComparison.OrdinalIgnoreCase) == true || body?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true
            ? Strings.Get("AiErrors_Message_YourAIAccountIs")
            : Strings.Get("AiErrors_Message_TheAIServiceCouldn"), ex);
    /// <summary>The provider name without a trailing "(...)" note, e.g. "Ollama (on this PC)" becomes "Ollama".</summary>
    private static string ShortName(string name)
    {
        var i = name.IndexOfAny(new[] { '(', '\uFF08' });
        return i > 0 ? name[..i].TrimEnd() : name;
    }
    private static ActionException Network(AiProviderInfo info, Exception ex) =>
        new(info.IsLocal ? Strings.Format("AiErrors_Message_CouldnTReachStart", info.Name, ShortName(info.Name))
                         : Strings.Get("AiErrors_Message_CouldnTReachThe"), ex);
}
