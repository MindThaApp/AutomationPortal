using System.Net;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Flowmate.Actions.Documents;

public interface IAiCompleter
{
    /// <summary>Sends the request and returns the AI's reply text. Throws <see cref="ActionException"/> with a plain-English message on failure.</summary>
    Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct);
}

/// <summary>Where the AI actions get their completer. Tests replace <see cref="Factory"/> so nothing touches the network.</summary>
public static class AiService
{
    public const string NoKeyMessage = "Add your AI key in Settings › AI to use AI actions.";

    public static Func<string, HttpClient?, IAiCompleter> Factory { get; set; } = (key, http) => new AnthropicCompleter(key, http);

    public static IAiCompleter Create(ActionContext ctx)
    {
        var key = ctx.Services.Secrets.Get("ai.apiKey");
        if (string.IsNullOrWhiteSpace(key)) throw new ActionException(NoKeyMessage);
        return Factory(key.Trim(), null);
    }

    public static async Task<ContentValue> RunAsync(ActionContext ctx, AiRequest request)
    {
        var completer = Create(ctx);
        var model = AiModels.Resolve(ctx.Services.Settings.Current.AiModel, await ctx.GetChoiceAsync("model", "default"));
        ctx.ReportProgress("Asking the AI…");
        var reply = await completer.CompleteAsync(model, request, ctx.CancellationToken);
        return AiAnswerParser.Parse(request.Shape, reply, request.Fields);
    }
}

/// <summary>Talks to the Claude API through the official Anthropic SDK (non-streaming, adaptive thinking left at its default).</summary>
public sealed class AnthropicCompleter : IAiCompleter
{
    private readonly AnthropicClient _client;

    public AnthropicCompleter(string apiKey, HttpClient? http = null)
    {
        _client = http is null ? new AnthropicClient { ApiKey = apiKey } : new AnthropicClient { ApiKey = apiKey, HttpClient = http };
    }

    /// <summary>Builds the SDK request. Effort is skipped on Haiku (unsupported); no thinking setting is sent (it is on by default and can't be disabled on newer models).</summary>
    public static MessageCreateParams BuildParams(string model, AiRequest request)
    {
        var content = new List<ContentBlockParam>();
        foreach (var part in request.Parts)
        {
            switch (part)
            {
                case AiPart.Image img:
                    content.Add(new ImageBlockParam
                    {
                        Source = new Base64ImageSource { Data = Convert.ToBase64String(img.Data), MediaType = MediaTypeOf(img.MediaType) },
                    });
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
        try
        {
            response = await _client.Messages.Create(BuildParams(model, request), ct);
        }
        catch (Exception ex)
        {
            throw Map(ex, ct);
        }
        return ReadReply(response);
    }

    public static string ReadReply(Message response)
    {
        if (response.StopReason == "refusal")
            throw new ActionException("The AI declined to answer that. Try rewording your request.");
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(text))
            throw new ActionException("The AI didn't send back an answer. Try again.");
        return text;
    }

    /// <summary>Plain-English version of an SDK / network failure. Never includes the API key.</summary>
    public static Exception Map(Exception ex, CancellationToken ct = default)
    {
        switch (ex)
        {
            case ActionException or OperationCanceledException when ct.IsCancellationRequested:
                return ex;
            case ActionException:
                return ex;
            case AnthropicUnauthorizedException or AnthropicForbiddenException:
                return new ActionException("Your AI key isn't valid — update it in Settings › AI.", ex);
            case AnthropicRateLimitException:
                return new ActionException("The AI service is busy — try again in a minute.", ex);
            case AnthropicNotFoundException:
                return new ActionException("The selected AI model isn't available for your key. Choose another model in Settings › AI.", ex);
            case AnthropicBadRequestException bad:
                return new ActionException(bad.ResponseBody?.Contains("credit balance", StringComparison.OrdinalIgnoreCase) == true
                    ? "Your AI account is out of credit. Add credit in your Anthropic account, then try again."
                    : "The AI service couldn't process that request. If you attached a very large file or picture, try a smaller one.", ex);
            case AnthropicApiException api when (int)api.StatusCode == 529 || api is Anthropic5xxException:
                return new ActionException("The AI service is busy — try again in a minute.", ex);
            case AnthropicApiException:
                return new ActionException("The AI service couldn't answer right now. Try again in a minute.", ex);
            case HttpRequestException or AnthropicIOException:
                return new ActionException("Couldn't reach the AI service — check your internet connection.", ex);
            case TaskCanceledException or TimeoutException:
                return new ActionException("The AI service took too long to answer. Try again.", ex);
            default:
                return ex;
        }
    }
}
