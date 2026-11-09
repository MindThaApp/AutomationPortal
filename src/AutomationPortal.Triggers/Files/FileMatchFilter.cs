using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>Decides whether a path passes a "kind" and "extensions" filter. Pure.</summary>
public static class FileMatchFilter
{
    public static readonly IReadOnlyList<Choice> KindChoices = new[]
    {
        new Choice("any", "any kind of file"), new Choice("Image", "pictures"), new Choice("PDF", "PDFs"), new Choice("Document", "documents"),
        new Choice("Spreadsheet", "spreadsheets"), new Choice("Presentation", "presentations"), new Choice("Audio", "music"),
        new Choice("Video", "videos"), new Choice("Archive", "zip files"), new Choice("Program", "programs"),
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
