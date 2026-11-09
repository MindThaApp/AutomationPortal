using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

public sealed class GalleryItem
{
    public required string FilePath { get; init; }
    public required Shortcut Shortcut { get; init; }
}

public sealed class GallerySection
{
    public required string Title { get; init; }
    public required IReadOnlyList<GalleryItem> Items { get; init; }
}

/// <summary>Reads the starter shortcuts that ship next to the app: <c>gallery\*.autoportal</c> with an optional <c>gallery\index.json</c>.</summary>
public sealed class GalleryService
{
    public GalleryService(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AppContext.BaseDirectory, "gallery");
    }

    public string Folder { get; }
    public bool FolderExists => Directory.Exists(Folder);
    public List<string> Problems { get; } = new();

    public IReadOnlyList<GallerySection> Load()
    {
        Problems.Clear();
        if (!FolderExists) return Array.Empty<GallerySection>();

        var files = Directory.EnumerateFiles(Folder, "*.autoportal", SearchOption.TopDirectoryOnly).ToList();
        string? indexJson = null;
        var indexPath = Path.Combine(Folder, "index.json");
        try { if (File.Exists(indexPath)) indexJson = File.ReadAllText(indexPath); }
        catch (IOException ex) { Problems.Add("index.json: " + ex.Message); }

        var plan = GalleryIndex.Arrange(files, GalleryIndex.Parse(indexJson));
        var sections = new List<GallerySection>();
        foreach (var section in plan)
        {
            var items = new List<GalleryItem>();
            foreach (var file in section.Files)
            {
                try { items.Add(new GalleryItem { FilePath = file, Shortcut = ShortcutPackage.Parse(File.ReadAllText(file)) }); }
                catch (Exception ex) when (ex is LibraryException or IOException) { Problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
            if (items.Count > 0) sections.Add(new GallerySection { Title = section.Title, Items = items });
        }
        return sections;
    }
}
