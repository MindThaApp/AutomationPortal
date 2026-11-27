using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>Decides whether a path passes a "kind" and "extensions" filter. Pure.</summary>
public static class FileMatchFilter
{
    public static readonly IReadOnlyList<Choice> KindChoices = new[]
    {
        new Choice("any", Strings.Get("Trigger_FileKind_Any")), new Choice("Image", Strings.Get("Trigger_FileKind_Image")), new Choice("PDF", Strings.Get("Trigger_FileKind_PDF")), new Choice("Document", Strings.Get("Trigger_FileKind_Document")),
        new Choice("Spreadsheet", Strings.Get("Trigger_FileKind_Spreadsheet")), new Choice("Presentation", Strings.Get("Trigger_FileKind_Presentation")), new Choice("Audio", Strings.Get("Trigger_FileKind_Audio")),
        new Choice("Video", Strings.Get("Trigger_FileKind_Video")), new Choice("Archive", Strings.Get("Trigger_FileKind_Archive")), new Choice("Program", Strings.Get("Trigger_FileKind_Program")),
    };

    /// <summary>Splits "pdf, .docx; txt" into ["pdf", "docx", "txt"].</summary>
    public static IReadOnlyList<string> ParseExtensions(string? text) =>
        (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('*', '.').ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();

    public static bool Accepts(string path, string kind, IReadOnlyCollection<string> extensions)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extensions.Count > 0 && !extensions.Contains(ext)) return false;
        return string.IsNullOrEmpty(kind) || kind == "any" || FileKinds.Of(path).Equals(kind, StringComparison.OrdinalIgnoreCase);
    }
}
