using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

/// <summary>
/// "Related by meaning" matches for the library and the Gallery, from Portal AI Host embeddings. The indexes live in memory only:
/// they fill on the first search, and later searches embed just the shortcuts that were added or edited since. Without the host
/// every method returns an empty list at once and search behaves exactly as before.
/// </summary>
public sealed class SemanticSearchService
{
    /// <summary>Bigger libraries skip meaning search rather than embed hundreds of items on a keystroke.</summary>
    public const int MaxItems = 400;

    private readonly IShortcutLibrary _library;
    private readonly ActionRegistry _actions;
    private readonly Func<IReadOnlyList<GalleryItem>> _gallery;
    private readonly ITextEmbedder _embedder;
    private readonly SemanticIndex _shortcuts;
    private readonly SemanticIndex _templates;

    public SemanticSearchService(IShortcutLibrary library, ActionRegistry actions, Func<IReadOnlyList<GalleryItem>> gallery, ITextEmbedder embedder)
    {
        _library = library; _actions = actions; _gallery = gallery; _embedder = embedder;
        _shortcuts = new SemanticIndex(embedder);
        _templates = new SemanticIndex(embedder);
    }

    internal SemanticIndex ShortcutIndex => _shortcuts;
    internal SemanticIndex TemplateIndex => _templates;

    public Task<bool> IsAvailableAsync(CancellationToken ct) => Safe(() => _embedder.IsAvailableAsync(ct), false);

    public IReadOnlyList<SemanticDoc> ShortcutDocs() =>
        _library.All.Select(s => new SemanticDoc(s.Id.ToString("N"), SemanticText.ForShortcut(s, id => _actions.Describe(id)?.Title))).ToList();

    public IReadOnlyList<SemanticDoc> TemplateDocs(IReadOnlyList<GalleryItem> items) =>
        items.Select(g => new SemanticDoc(Path.GetFileName(g.FilePath), SemanticText.ForShortcut(g.Shortcut, id => _actions.Describe(id)?.Title))).ToList();

    public async Task<IReadOnlyList<(Shortcut Shortcut, double Similarity)>> RelatedShortcutsAsync(string query, CancellationToken ct)
    {
        var none = Array.Empty<(Shortcut, double)>();
        if (query.Trim().Length < 3 || _library.All.Count > MaxItems || !await IsAvailableAsync(ct)) return none;
        var hits = await Safe(async () =>
        {
            if (!await _shortcuts.SyncAsync(ShortcutDocs(), ct)) return Array.Empty<SemanticHit>();
            return await _shortcuts.QueryAsync(query, ct);
        }, Array.Empty<SemanticHit>());
        return hits.Select(h => (Shortcut: _library.All.FirstOrDefault(s => s.Id.ToString("N") == h.Key), h.Similarity))
            .Where(x => x.Shortcut is not null).Select(x => (x.Shortcut!, x.Similarity)).ToList();
    }

    public async Task<IReadOnlyList<(GalleryItem Item, double Similarity)>> RelatedGalleryAsync(string query, CancellationToken ct)
    {
        var none = Array.Empty<(GalleryItem, double)>();
        var items = _gallery();
        if (query.Trim().Length < 3 || items.Count == 0 || items.Count > MaxItems || !await IsAvailableAsync(ct)) return none;
        var hits = await Safe(async () =>
        {
            if (!await _templates.SyncAsync(TemplateDocs(items), ct)) return Array.Empty<SemanticHit>();
            return await _templates.QueryAsync(query, ct);
        }, Array.Empty<SemanticHit>());
        return hits.Select(h => (Item: items.FirstOrDefault(g => Path.GetFileName(g.FilePath) == h.Key), h.Similarity))
            .Where(x => x.Item is not null).Select(x => (x.Item!, x.Similarity)).ToList();
    }

    /// <summary>A host that is gone or answers badly must never break a search: the caller just gets nothing extra.</summary>
    private static async Task<T> Safe<T>(Func<Task<T>> run, T fallback)
    {
        try { return await run(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or ActionException or IOException or InvalidOperationException)
        {
            AppLog.Write("Search by meaning unavailable: " + ex.Message);
            return fallback;
        }
    }
}
