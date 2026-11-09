using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Content.Tests;

public class DescriptorTests
{
    private static readonly string[] SpecIds =
    {
        "text.combine", "text.split", "text.replace", "text.case", "text.cleanup", "text.getPart", "text.find", "content.count",
        "text.fromInput", "text.encode", "text.hash", "text.password", "text.qrMake", "text.qrRead",
        "num.calculate", "num.expression", "num.round", "num.random", "num.format", "num.convertUnits", "num.stats", "num.currency",
        "date.date", "date.current", "date.format", "date.adjust", "date.between", "date.parse",
        "list.getItem", "list.sort", "list.unique", "list.filter", "dict.getValue", "dict.setValue", "dict.fromInput", "dict.toJson",
        "web.fetch", "web.download", "web.pageText", "web.pageLinks", "web.pageImages", "web.open", "web.search", "web.urlPart",
        "web.expand", "web.cleanUrl", "web.rss", "web.publicIp", "web.isUp",
        "weather.current", "weather.forecast", "loc.find",
        "sheet.read", "sheet.addRow", "sheet.makeCsv",
    };

    private static readonly List<ShortcutAction> Actions = typeof(CombineTextAction).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(ShortcutAction).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
        .Select(t => (ShortcutAction)Activator.CreateInstance(t)!).ToList();

    public static IEnumerable<object[]> AllActions() => Actions.Select(a => new object[] { a.Descriptor.Id });

    [Fact]
    public void Every_action_in_the_spec_exists_and_nothing_else_is_registered()
    {
        var ids = Actions.Select(a => a.Descriptor.Id).ToList();
        Assert.Equal(SpecIds.OrderBy(x => x), ids.OrderBy(x => x));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Actions_register_without_diagnostics()
    {
        using var host = new TestHost().With(typeof(CombineTextAction).Assembly);
        Assert.Empty(host.Registry.Diagnostics);
        foreach (var id in SpecIds) Assert.NotNull(host.Registry.Get(id));
    }

    [Theory, MemberData(nameof(AllActions))]
    public void Descriptor_is_complete_and_consistent(string id)
    {
        var d = Actions.Single(a => a.Descriptor.Id == id).Descriptor;
        Assert.False(string.IsNullOrWhiteSpace(d.Title));
        Assert.False(string.IsNullOrWhiteSpace(d.Description));
        Assert.False(string.IsNullOrWhiteSpace(d.Summary));
        Assert.False(string.IsNullOrWhiteSpace(d.OutputName));
        Assert.Contains(d.Category, ActionCategories.All);
        Assert.EndsWith("24", d.Icon);
        Assert.NotEmpty(d.Keywords);

        var keys = d.Parameters.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var token in d.SummarySegments.Where(s => s.IsToken))
            Assert.Contains(token.ParamKey!, keys);
        foreach (var p in d.Parameters)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Label));
            if (p.VisibleWhen is { } vw) Assert.Contains(vw.Key, keys);
            if (p.Type == ParamType.Choice && p.Choices is { } choices)
            {
                Assert.NotEmpty(choices);
                Assert.Equal(choices.Count, choices.Select(c => c.Id).Distinct().Count());
                if (p.Default is { } def) Assert.Contains(def.ToLiteralText(), choices.Select(c => c.Id));
            }
        }
    }

    [Fact]
    public void Plain_english_no_jargon_in_labels()
    {
        foreach (var d in Actions.Select(a => a.Descriptor))
        {
            var text = d.Title + " " + string.Join(" ", d.Parameters.Select(p => p.Label));
            Assert.DoesNotContain("regex", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hotkey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("directory", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
