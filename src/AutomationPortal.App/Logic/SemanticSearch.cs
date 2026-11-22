using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public sealed record SemanticDoc(string Key, string Text);

public sealed record SemanticHit(string Key, double Similarity);

/// <summary>What gets embedded for a shortcut or Gallery template: its name, description and the titles of its steps. Never parameter values or file contents.</summary>
public static class SemanticText
{
    public static string ForShortcut(Shortcut s, Func<string, string?> stepTitle)
    {
        var titles = s.Actions.Where(a => !a.Disabled).Select(a => stepTitle(a.ActionId)).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(12);
        return Compose(s.Name, s.Description, titles!);
    }

    public static string Compose(string name, string? description, IEnumerable<string> stepTitles)
    {
        var text = name.Trim();
        if (!string.IsNullOrWhiteSpace(description)) text += ". " + description.Trim();
        var steps = string.Join(", ", stepTitles);
        if (steps.Length > 0) text += ". Steps: " + steps;
        return text;
    }
}

/// <summary>
/// An in-memory index of one list of texts (nothing is saved). <see cref="SyncAsync"/> embeds only what is new or changed, in batches,
/// and forgets what is gone; <see cref="QueryAsync"/> returns what is close in meaning. Without embeddings both do nothing.
/// </summary>
public sealed class SemanticIndex
{
    /// <summary>Cosine similarity a document must reach to count as related. Unit-length vectors, so this is a plain dot product.</summary>
    public const double RelatedThreshold = 0.45;
    public const int MaxHits = 8;

    private readonly ITextEmbedder _embedder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private Dictionary<string, (string Text, float[] Vector)> _docs = new();
    private string? _model;

    public SemanticIndex(ITextEmbedder embedder) { _embedder = embedder; }

    public int Count { get { lock (_lock) return _docs.Count; } }

    public IReadOnlyCollection<string> IndexedTexts { get { lock (_lock) return _docs.Values.Select(d => d.Text).ToList(); } }

    /// <returns>False when embeddings weren't available (the index is then left as it was).</returns>
    public async Task<bool> SyncAsync(IReadOnlyList<SemanticDoc> docs, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Dictionary<string, (string Text, float[] Vector)> current;
            lock (_lock) current = _docs;
            var next = new Dictionary<string, (string Text, float[] Vector)>(StringComparer.Ordinal);
            var missing = new List<SemanticDoc>();
            foreach (var d in docs)
            {
                if (current.TryGetValue(d.Key, out var have) && have.Text == d.Text) next[d.Key] = have;
                else missing.Add(d);
            }
            if (missing.Count == 0)
            {
                lock (_lock) _docs = next;
                return true;
            }
            var batch = await _embedder.EmbedAsync(missing.Select(m => m.Text).ToList(), ct);
            if (batch is null || batch.Vectors.Count != missing.Count) return false;
            // A different model means every old vector is stale: start again with the new model's vectors only.
            if (_model is not null && batch.Model != _model)
            {
                var all = await _embedder.EmbedAsync(docs.Select(m => m.Text).ToList(), ct);
                if (all is null || all.Vectors.Count != docs.Count || all.Model != batch.Model) return false;
                next.Clear();
                for (var i = 0; i < docs.Count; i++) next[docs[i].Key] = (docs[i].Text, all.Vectors[i]);
            }
            else
                for (var i = 0; i < missing.Count; i++) next[missing[i].Key] = (missing[i].Text, batch.Vectors[i]);
            lock (_lock) { _docs = next; _model = batch.Model; }
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SemanticHit>> QueryAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length < 3) return Array.Empty<SemanticHit>();
        Dictionary<string, (string Text, float[] Vector)> snapshot;
        string? model;
        lock (_lock) { snapshot = _docs; model = _model; }
        if (snapshot.Count == 0) return Array.Empty<SemanticHit>();
        var q = await _embedder.EmbedAsync(new[] { query }, ct);
        if (q is null || q.Vectors.Count != 1 || q.Model != model) return Array.Empty<SemanticHit>();
        return Rank(q.Vectors[0], snapshot.Select(d => (d.Key, d.Value.Vector)), RelatedThreshold, MaxHits);
    }

    public static IReadOnlyList<SemanticHit> Rank(float[] query, IEnumerable<(string Key, float[] Vector)> docs, double threshold, int max) =>
        docs.Select(d => new SemanticHit(d.Key, Cosine(query, d.Vector)))
            .Where(h => h.Similarity >= threshold)
            .OrderByDescending(h => h.Similarity).ThenBy(h => h.Key, StringComparer.Ordinal).Take(max).ToList();

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; na += (double)a[i] * a[i]; nb += (double)b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}

/// <summary>Puts the text matches first, then the meaning matches that aren't already listed.</summary>
public static class SemanticBlend
{
    public static IReadOnlyList<(T Item, bool Related)> Merge<T>(IReadOnlyList<T> textMatches, IEnumerable<(T Item, double Similarity)> related, Func<T, string> key, int maxRelated = SemanticIndex.MaxHits)
    {
        var result = textMatches.Select(t => (t, false)).ToList();
        var seen = new HashSet<string>(textMatches.Select(key), StringComparer.Ordinal);
        var added = 0;
        foreach (var (item, _) in related.OrderByDescending(r => r.Similarity))
        {
            if (added >= maxRelated) break;
            if (seen.Add(key(item))) { result.Add((item, true)); added++; }
        }
        return result;
    }
}
