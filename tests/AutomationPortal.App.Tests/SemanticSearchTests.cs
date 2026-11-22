using System.Threading;
using System.Threading.Tasks;
using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.App.Tests;

public class SemanticSearchTests
{
    /// <summary>Gives each text a fixed direction so the cosine between any two texts is known.</summary>
    private sealed class FakeEmbedder : ITextEmbedder
    {
        private readonly Dictionary<string, float[]> _vectors;
        public FakeEmbedder(Dictionary<string, float[]> vectors) { _vectors = vectors; }
        public bool Available { get; set; } = true;
        public string Model { get; set; } = "m1";
        public List<string> Embedded { get; } = new();
        public int Calls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(Available);

        public Task<EmbeddingBatch?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            if (!Available) return Task.FromResult<EmbeddingBatch?>(null);
            Embedded.AddRange(texts);
            var list = texts.Select(t => _vectors.FirstOrDefault(kv => t.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? new float[] { 0, 0, 1 }).ToList();
            return Task.FromResult<EmbeddingBatch?>(new EmbeddingBatch(Model, list));
        }
    }

    private static float[] V(double angleDegrees)
    {
        var r = angleDegrees * Math.PI / 180;
        return new[] { (float)Math.Cos(r), (float)Math.Sin(r), 0f };
    }

    [Fact]
    public void Cosine_of_unit_vectors()
    {
        Assert.Equal(1, SemanticIndex.Cosine(V(0), V(0)), 5);
        Assert.Equal(0, SemanticIndex.Cosine(V(0), V(90)), 5);
        Assert.Equal(Math.Cos(Math.PI / 3), SemanticIndex.Cosine(V(0), V(60)), 5);
        Assert.Equal(0, SemanticIndex.Cosine(new float[] { 1, 0 }, new float[] { 1, 0, 0 }));
    }

    [Fact]
    public async Task Only_documents_above_the_threshold_come_back_best_first()
    {
        var fake = new FakeEmbedder(new()
        {
            ["Tidy downloads"] = V(10),   // close to the query
            ["Backup photos"] = V(50),    // cos 50 deg = 0.64: related
            ["Daily weather"] = V(80),    // cos 80 deg = 0.17: below the threshold
            ["clean up my files"] = V(0),
        });
        var index = new SemanticIndex(fake);
        Assert.True(await index.SyncAsync(new[] { new SemanticDoc("a", "Tidy downloads"), new SemanticDoc("b", "Backup photos"), new SemanticDoc("c", "Daily weather") }, default));

        var hits = await index.QueryAsync("clean up my files", default);

        Assert.Equal(new[] { "a", "b" }, hits.Select(h => h.Key));
        Assert.All(hits, h => Assert.True(h.Similarity >= SemanticIndex.RelatedThreshold));
    }

    [Fact]
    public async Task Short_queries_never_reach_the_host()
    {
        var fake = new FakeEmbedder(new());
        var index = new SemanticIndex(fake);
        await index.SyncAsync(new[] { new SemanticDoc("a", "Tidy downloads") }, default);
        var before = fake.Calls;
        Assert.Empty(await index.QueryAsync("ab", default));
        Assert.Equal(before, fake.Calls);
    }

    [Fact]
    public async Task Sync_embeds_only_what_changed()
    {
        var fake = new FakeEmbedder(new());
        var index = new SemanticIndex(fake);
        await index.SyncAsync(new[] { new SemanticDoc("a", "One"), new SemanticDoc("b", "Two") }, default);
        fake.Embedded.Clear();
        await index.SyncAsync(new[] { new SemanticDoc("a", "One"), new SemanticDoc("b", "Two changed"), new SemanticDoc("c", "Three") }, default);
        Assert.Equal(new[] { "Two changed", "Three" }, fake.Embedded);
        await index.SyncAsync(new[] { new SemanticDoc("c", "Three") }, default);
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task A_different_model_replaces_every_vector()
    {
        var fake = new FakeEmbedder(new());
        var index = new SemanticIndex(fake);
        await index.SyncAsync(new[] { new SemanticDoc("a", "One") }, default);
        fake.Model = "m2";
        fake.Embedded.Clear();
        Assert.True(await index.SyncAsync(new[] { new SemanticDoc("a", "One"), new SemanticDoc("b", "Two") }, default));
        Assert.Contains("One", fake.Embedded);
    }

    [Fact]
    public async Task Host_unavailable_leaves_search_unchanged()
    {
        var fake = new FakeEmbedder(new()) { Available = false };
        var library = new InMemoryShortcutLibrary();
        library.Save(new Shortcut { Name = "Tidy downloads" });
        var service = new SemanticSearchService(library, ActionRegistry.CreateWithBuiltIns(), () => Array.Empty<GalleryItem>(), fake);

        Assert.Empty(await service.RelatedShortcutsAsync("clean up my files", default));
        Assert.Empty(await service.RelatedGalleryAsync("clean up my files", default));
        Assert.Equal(0, service.ShortcutIndex.Count);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void Blend_puts_text_matches_first_then_related_without_repeats()
    {
        var text = new[] { "Tidy downloads", "Backup photos" };
        var related = new[] { ("Backup photos", 0.9), ("Daily weather", 0.5), ("Send report", 0.7) };

        var merged = SemanticBlend.Merge(text, related, t => t);

        Assert.Equal(new[] { "Tidy downloads", "Backup photos", "Send report", "Daily weather" }, merged.Select(m => m.Item));
        Assert.Equal(new[] { false, false, true, true }, merged.Select(m => m.Related));
    }

    [Fact]
    public void Blend_without_related_is_the_text_list()
    {
        var merged = SemanticBlend.Merge(new[] { "a", "b" }, Array.Empty<(string, double)>(), t => t);
        Assert.Equal(new[] { "a", "b" }, merged.Select(m => m.Item));
        Assert.All(merged, m => Assert.False(m.Related));
    }

    [Fact]
    public async Task The_index_holds_name_description_and_step_titles_but_never_parameter_text()
    {
        var registry = ActionRegistry.CreateWithBuiltIns();
        var action = registry.All.First().Descriptor;
        var s = new Shortcut { Name = "Weekly report", Description = "Collects the numbers" };
        var step = ActionInstance.Create(action.Id);
        foreach (var p in action.Parameters) step.Parameters[p.Key] = ParamValue.Text("SECRET-PARAMETER-TEXT");
        step.Parameters["extra"] = ParamValue.Text("SECRET-PARAMETER-TEXT");
        s.Actions.Add(step);
        var library = new InMemoryShortcutLibrary();
        library.Save(s);
        var fake = new FakeEmbedder(new());
        var service = new SemanticSearchService(library, registry, () => Array.Empty<GalleryItem>(), fake);

        await service.RelatedShortcutsAsync("weekly numbers", default);

        var text = Assert.Single(service.ShortcutIndex.IndexedTexts);
        Assert.Contains("Weekly report", text);
        Assert.Contains("Collects the numbers", text);
        Assert.Contains(action.Title, text);
        Assert.DoesNotContain("SECRET-PARAMETER-TEXT", text);
        Assert.All(fake.Embedded, e => Assert.DoesNotContain("SECRET-PARAMETER-TEXT", e));
    }
}
