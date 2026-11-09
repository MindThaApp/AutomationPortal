using System.Net;
using System.Text.RegularExpressions;
using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

/// <summary>Real internet calls. Run with: dotnet test --filter Category=Network</summary>
[Trait("Category", "Network")]
public class NetworkTests : IDisposable
{
    private readonly TestHost _host = new();

    public NetworkTests() => _host.Services.Http = AutomationPortalServices.CreateHttpClient();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Current_weather_for_london()
    {
        var r = await Run(_host, new GetCurrentWeatherAction(), null, ("location", "London"), ("units", C("metric")));
        var d = Assert.IsType<DictionaryItem>(r[0]);
        Assert.Matches(@"^-?\d+° and .+ in London$", d.Get("Summary").AsText());
        Assert.InRange(d.Get("Temperature").AsNumber()!.Value, -40, 50);
        Assert.InRange(d.Get("Humidity").AsNumber()!.Value, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(d.Get("Condition").AsText()));
        Assert.True(d.Get("High").AsNumber() >= d.Get("Low").AsNumber());
        Assert.True(d.Get("Sunset").AsDate() > d.Get("Sunrise").AsDate());
        var loc = (LocationItem)d.Get("Location")[0];
        Assert.InRange(loc.Latitude, 51, 52);
        Assert.Equal("United Kingdom", loc.Country);
    }

    [Fact]
    public async Task Imperial_units_give_fahrenheit_numbers()
    {
        var c = (DictionaryItem)(await Run(_host, new GetCurrentWeatherAction(), null, ("location", "Oslo"), ("units", C("metric"))))[0];
        var f = (DictionaryItem)(await Run(_host, new GetCurrentWeatherAction(), null, ("location", "Oslo"), ("units", C("imperial"))))[0];
        Assert.Equal("°F", f.Get("Temperature Unit").AsText());
        Assert.InRange(f.Get("Temperature").AsNumber()!.Value, c.Get("Temperature").AsNumber()!.Value * 1.8 + 32 - 3, c.Get("Temperature").AsNumber()!.Value * 1.8 + 32 + 3);
    }

    [Fact]
    public async Task Forecast_daily_and_hourly()
    {
        var daily = await Run(_host, new GetWeatherForecastAction(), null, ("location", "Berlin"), ("days", 5), ("units", C("metric")));
        Assert.Equal(5, daily.Count);
        var first = (DictionaryItem)daily[0];
        Assert.True(first.Get("High").AsNumber() >= first.Get("Low").AsNumber());
        Assert.IsType<DateItem>(first.Get("Date")[0]);
        Assert.False(string.IsNullOrEmpty(first.Get("Summary").AsText()));

        var hourly = await Run(_host, new GetWeatherForecastAction(), null, ("location", "Berlin"), ("period", C("hourly")), ("hours", 12), ("units", C("metric")));
        Assert.Equal(12, hourly.Count);
        var times = hourly.Select(i => ((DictionaryItem)i).Get("Time").AsDate()!.Value).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
        Assert.True(times[0] > DateTimeOffset.UtcNow.AddHours(-1.5));
    }

    [Fact]
    public async Task Finds_locations_by_name()
    {
        var tokyo = (LocationItem)(await Run(_host, new FindLocationByNameAction(), null, ("name", "Tokyo")))[0];
        Assert.InRange(tokyo.Latitude, 35, 36);
        Assert.InRange(tokyo.Longitude, 139, 140);
        Assert.Equal("Japan", tokyo.Country);

        var texas = (LocationItem)(await Run(_host, new FindLocationByNameAction(), null, ("name", "Paris, Texas")))[0];
        Assert.InRange(texas.Longitude, -96, -95);

        var several = await Run(_host, new FindLocationByNameAction(), null, ("name", "Springfield"), ("limit", 3));
        Assert.Equal(3, several.Count);

        var ex = await Fails(Run(_host, new FindLocationByNameAction(), null, ("name", "Qzxwvbnmlkjh")));
        Assert.Contains("Couldn't find a place", ex.Message);
    }

    [Fact]
    public async Task Currency_conversion_uses_live_rates()
    {
        CurrencyConverter.ClearCache();
        var r = await Run(_host, new ConvertCurrencyAction(), null, ("amount", 100), ("from", C("USD")), ("to", C("EUR")));
        Assert.InRange(r.AsNumber()!.Value, 50, 150);

        var detail = await CurrencyConverter.ConvertAsync(_host.Services.Http, 1, "GBP", "USD");
        Assert.InRange(detail.Rate, 0.5, 3);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", detail.Date);
        var back = await CurrencyConverter.ConvertAsync(_host.Services.Http, detail.Result, "USD", "GBP");
        Assert.InRange(back.Result, 0.97, 1.03);

        Assert.Equal(1, (await CurrencyConverter.ConvertAsync(_host.Services.Http, 1, "EUR", "EUR")).Result);
    }

    [Fact]
    public async Task Public_ip_address()
    {
        var ip = (await Run(_host, new GetPublicIpAction())).AsText();
        Assert.True(IPAddress.TryParse(ip, out _), ip);
    }

    [Fact]
    public async Task Website_up_and_down()
    {
        Assert.True((await Run(_host, new CheckWebsiteIsUpAction(), null, ("url", "https://example.com"))).AsBool());
        Assert.False((await Run(_host, new CheckWebsiteIsUpAction(), null, ("url", "https://automationportal-no-such-site.invalid"), ("timeout", 5))).AsBool());
    }

    [Fact]
    public async Task Fetch_html_json_and_errors()
    {
        var html = (await Run(_host, new GetContentsOfUrlAction(), null, ("url", "https://example.com"))).AsText();
        Assert.Contains("Example Domain", html);

        var json = await Run(_host, new GetContentsOfUrlAction(), null, ("url", "https://api.frankfurter.dev/v1/latest?base=USD&symbols=EUR"));
        var dict = Assert.IsType<DictionaryItem>(json[0]);
        Assert.Equal("USD", dict.Get("base").AsText());
        Assert.True(dict.GetPath("rates.EUR").AsNumber() > 0);

        var ex = await Fails(Run(_host, new GetContentsOfUrlAction(), null, ("url", "https://example.com/definitely-not-here-404")));
        Assert.Contains("404", ex.Message);

        var offline = await Fails(Run(_host, new GetContentsOfUrlAction(), null, ("url", "https://automationportal-no-such-site.invalid/")));
        Assert.Contains("automationportal-no-such-site.invalid", offline.Message);
    }

    [Fact]
    public async Task Post_json_echo()
    {
        var r = await Run(_host, new GetContentsOfUrlAction(), null, ("url", "https://httpbingo.org/post"), ("method", C("POST")), ("bodyType", C("json")),
            ("json", ParamValue.Dict(("hello", ParamValue.Text("automationportal")))), ("headers", ParamValue.Dict(("X-Test", ParamValue.Text("yes")))));
        var d = (DictionaryItem)r[0];
        Assert.Equal("automationportal", d.GetPath("json.hello").AsText());
    }

    [Fact]
    public async Task Page_text_links_and_images()
    {
        var text = (await Run(_host, new GetWebPageTextAction(), null, ("url", "https://example.com"))).AsText();
        Assert.Contains("documentation examples", text);
        Assert.Equal("Example Domain", (await Run(_host, new GetWebPageTextAction(), null, ("url", "https://example.com"), ("what", C("title")))).AsText());

        var links = await Run(_host, new GetLinksFromWebPageAction(), null, ("url", "https://en.wikipedia.org/wiki/Cat"));
        Assert.True(links.Count > 50);
        Assert.Contains(links, l => l.DisplayText.StartsWith("https://en.wikipedia.org/wiki/"));
        var external = await Run(_host, new GetLinksFromWebPageAction(), null, ("url", "https://en.wikipedia.org/wiki/Cat"), ("scope", C("otherSites")));
        Assert.All(external, l => Assert.DoesNotContain("://en.wikipedia.org/", l.DisplayText));

        var wiki = await Run(_host, new GetImagesFromWebPageAction(), null, ("url", "https://en.wikipedia.org/wiki/Cat"), ("limit", 2));
        Assert.Equal(2, wiki.Count);
        Assert.All(wiki, i => Assert.True(new FileInfo(((ImageItem)i).Path).Length > 100));
    }

    [Fact]
    public async Task Rss_feed()
    {
        var items = await Run(_host, new GetRssItemsAction(), null, ("url", "https://feeds.bbci.co.uk/news/rss.xml"), ("limit", 5));
        Assert.True(items.Count >= 3);
        foreach (var i in items.Cast<DictionaryItem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Get("Title").AsText()));
            Assert.StartsWith("http", i.Get("Link").AsText());
        }
        Assert.IsType<DateItem>(((DictionaryItem)items[0]).Get("Published")[0]);
    }

    [Fact]
    public async Task Expand_a_redirecting_link()
    {
        var r = await Run(_host, new ExpandShortLinkAction(), ContentValue.Text("http://github.com"));
        Assert.StartsWith("https://github.com", r.AsText());
        var plain = await Run(_host, new ExpandShortLinkAction(), ContentValue.Text("https://example.com/"));
        Assert.Equal("https://example.com/", plain.AsText());
    }

    [Fact]
    public async Task Download_a_file()
    {
        var r = await Run(_host, new DownloadFileAction(), null, ("url", "https://example.com/"));
        var file = (PathItem)r[0];
        Assert.True(File.Exists(file.Path));
        Assert.Contains("Example Domain", await File.ReadAllTextAsync(file.Path));
    }
}
