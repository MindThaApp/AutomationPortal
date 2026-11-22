using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// The optional first step of "Describe it": asks the AI whether the request is missing anything essential (at most three
/// questions), and turns the answers into extra lines for the build request. Anything unusable in the reply means no questions.
/// </summary>
public static class ShortcutAiClarifier
{
    public const int MaxQuestions = 3;
    public const int MaxChoices = 5;
    /// <summary>One clarify call, one build and its repair rounds.</summary>
    public const int MaxTotalCalls = 1 + 1 + ShortcutAiBuilder.MaxRepairRounds;

    public const string SchemaJson = """
        {"type":"object","properties":{"questions":{"type":"array","items":{"type":"object","properties":{
        "text":{"type":"string"},"kind":{"type":"string","enum":["choice","folder","file","text","yesno"]},
        "options":{"type":"array","items":{"type":"string"}},"default":{"type":"string"}},
        "required":["text","kind","options","default"],"additionalProperties":false}}},"required":["questions"],"additionalProperties":false}
        """;

    public static string SystemPrompt { get; } =
        "You help someone describe an automation for their Windows PC. Decide whether their request is missing something essential " +
        "that you can't sensibly guess (which folder, which file, a name or text to use, a choice between clearly different options). " +
        $"Ask at most {MaxQuestions} short, friendly questions, and none at all if the request is clear enough. Never ask about technical details.\n" +
        "Reply with JSON only: {\"questions\":[{\"text\":\"...\",\"kind\":\"choice|folder|file|text|yesno\",\"options\":[...],\"default\":\"...\"}]}.\n" +
        $"- choice: give 2 to {MaxChoices} short options and make default one of them.\n- yesno: default is \"yes\" or \"no\", options is [].\n" +
        "- folder, file: options is [] and default is \"\" (the person picks it).\n- text: options is [] and default is a sensible example or \"\".\n" +
        "If nothing is needed reply {\"questions\":[]}.";

    public static AiRequest BuildRequest(string description, bool structured) =>
        new(SystemPrompt, new AiPart[] { new AiPart.Text($"Request:\n<request>\n{description.Trim()}\n</request>") }, AiEffort.Low, AiShape.Fields,
            structured ? SchemaJson : null);

    /// <summary>The questions in a reply, or an empty list when it isn't usable.</summary>
    public static IReadOnlyList<AiQuestion> Parse(string? reply)
    {
        var result = new List<AiQuestion>();
        if (string.IsNullOrWhiteSpace(reply)) return result;
        JsonNode? root;
        try
        {
            var start = reply.IndexOfAny(new[] { '{', '[' });
            var end = Math.Max(reply.LastIndexOf('}'), reply.LastIndexOf(']'));
            if (start < 0 || end <= start) return result;
            root = JsonNode.Parse(reply[start..(end + 1)]);
        }
        catch (JsonException) { return result; }

        var items = root switch { JsonArray a => a, JsonObject o when o["questions"] is JsonArray q => q, _ => null };
        if (items is null) return result;
        foreach (var node in items)
        {
            if (result.Count >= MaxQuestions) break;
            if (node is not JsonObject o) continue;
            var text = Str(o["text"]);
            if (text.Length == 0) continue;
            var options = (o["options"] as JsonArray)?.Select(Str).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxChoices).ToList() ?? new List<string>();
            var given = Str(o["default"]);
            switch (Str(o["kind"]).Replace("-", "").Replace("_", "").ToLowerInvariant())
            {
                case "choice":
                    if (options.Count < 2) continue;
                    result.Add(new AiQuestion(text, AiQuestionKind.Choice, options, options.FirstOrDefault(x => x.Equals(given, StringComparison.OrdinalIgnoreCase)) ?? options[0]));
                    break;
                case "yesno" or "boolean" or "bool":
                    result.Add(new AiQuestion(text, AiQuestionKind.YesNo, Array.Empty<string>(), given.StartsWith("y", StringComparison.OrdinalIgnoreCase) || given.Equals("true", StringComparison.OrdinalIgnoreCase) ? "yes" : "no"));
                    break;
                case "folder":
                    result.Add(new AiQuestion(text, AiQuestionKind.Folder, Array.Empty<string>(), ""));
                    break;
                case "file":
                    result.Add(new AiQuestion(text, AiQuestionKind.File, Array.Empty<string>(), ""));
                    break;
                case "text":
                    result.Add(new AiQuestion(text, AiQuestionKind.Text, Array.Empty<string>(), given.Length > 200 ? given[..200] : given));
                    break;
            }
        }
        return result;
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";
}
