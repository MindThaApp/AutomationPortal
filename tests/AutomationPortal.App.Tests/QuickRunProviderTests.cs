using System.Threading;
using System.Threading.Tasks;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Services;
using Xunit;

namespace AutomationPortal.App.Tests;

public class QuickRunProviderTests
{
    private static readonly QuickRunContext Ctx = new();

    [Fact]
    public async Task Calculator_answers_and_offers_to_copy()
    {
        var items = await new CalculatorQuickRunProvider(null!).QueryAsync("12*7.5", Ctx, CancellationToken.None);
        var item = Assert.Single(items);
        Assert.Equal("= 90", item.Title);
        Assert.Equal("90", item.CopyText);
        Assert.Equal("Copy", item.ActionLabel);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("42")]
    public async Task Calculator_ignores_plain_words_and_numbers(string q) =>
        Assert.Empty(await new CalculatorQuickRunProvider(null!).QueryAsync(q, Ctx, CancellationToken.None));

    [Fact]
    public async Task Units_convert()
    {
        var items = await new UnitQuickRunProvider(null!).QueryAsync("5 km in miles", Ctx, CancellationToken.None);
        var item = Assert.Single(items);
        Assert.Contains("3.1", item.CopyText);
    }

    [Fact]
    public async Task Dates_understand_relative_phrases()
    {
        var items = await new DateQuickRunProvider(null!).QueryAsync("in 3 weeks", Ctx, CancellationToken.None);
        Assert.Single(items);
        Assert.Empty(await new DateQuickRunProvider(null!).QueryAsync("invoice", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Web_search_is_always_offered_for_text()
    {
        var items = await new WebSearchQuickRunProvider().QueryAsync("best pizza", Ctx, CancellationToken.None);
        Assert.Contains("best pizza", Assert.Single(items).Title);
    }
}
