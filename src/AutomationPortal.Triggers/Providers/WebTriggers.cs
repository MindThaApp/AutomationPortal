using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public sealed class RssNewTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "web.rssNew", Title = "New item in an RSS feed", Category = TP.CatWeb, Icon = "Rss24",
        Summary = "When there's something new in {url}",
        Description = "Checks a news or blog feed every so often and runs when new items appear. The first check only remembers what is already there, so old posts don't trigger it.",
        OutputKind = ContentKind.Dictionary, OutputDescription = "The new items. Each one has Title, Link, Summary, Published and Feed",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "url", Label = "Feed address", Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = "https://example.com/feed.xml" },
            ParameterDescriptor.Number("minutes", "Check every (minutes)", 30, 5, 1440),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    private sealed class SeenState { public List<string> Ids { get; set; } = new(); }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var url = config.GetString("url").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            sink.ReportProblem("Paste the feed address (it starts with https://).");
            return new DisposableAction(() => { });
        }
        var minutes = Math.Clamp(config.GetNumber("minutes", 30), 5, 1440);
        var key = "rss-" + TP.Hash(url);
        return TP.Poll(async ct =>
        {
            string xml;
            try { xml = await env.Services.Http.GetStringAsync(uri, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                sink.ReportProblem("Couldn't download the feed. Automation Portal will keep trying.");
                return;
            }
            Feed feed;
            try { feed = FeedParser.Parse(xml); }
            catch (Exception)
            {
                sink.ReportProblem("That address doesn't look like an RSS or Atom feed. Check the link.");
                return;
            }
            sink.ReportProblem(null);
            var seen = env.StateStore.Load<SeenState>(key);
            var fresh = FeedParser.NewItems(feed.Items, seen?.Ids);
            var all = (seen?.Ids ?? new List<string>()).Concat(feed.Items.Select(i => i.Id)).Distinct().TakeLast(500).ToList();
            env.StateStore.Save(key, new SeenState { Ids = all });
            if (fresh.Count == 0) return;
            var output = ContentValue.Concat(fresh.Select(i => ContentValue.Dict(
                ("Title", ContentValue.Text(i.Title)), ("Link", ContentValue.Text(i.Link ?? "")), ("Summary", ContentValue.Text(i.Summary ?? "")),
                ("Published", i.Published is { } p ? ContentValue.Date(p) : ContentValue.Text("")), ("Feed", ContentValue.Text(feed.Title)))).ToArray());
            sink.Fire(output, fresh.Count == 1 ? $"New in {feed.Title}: {fresh[0].Title}" : $"{fresh.Count} new items in {feed.Title}");
        }, TimeSpan.FromSeconds(15), () => TimeSpan.FromMinutes(minutes));
    }
}

public sealed class PageChangedTrigger : TriggerProvider
{
    private const int MaxBytes = 3 * 1024 * 1024;
    private static readonly TriggerDescriptor D = new()
    {
        Id = "web.pageChanged", Title = "A web page changes", Category = TP.CatWeb, Icon = "Globe24",
        Summary = "When {url} changes",
        Description = "Checks a web page every so often and runs when its text changes, or when it starts to contain the words you type (for example \"In stock\"). Pages with a clock or ads may look changed every time.",
        OutputKind = ContentKind.Url, OutputDescription = "The web page address",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "url", Label = "Web page", Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = "https://example.com/product" },
            ParameterDescriptor.Text("contains", "Only when it contains", null, "Any change") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Number("minutes", "Check every (minutes)", 60, 5, 1440),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    private sealed class PageState { public string Hash { get; set; } = ""; public bool Present { get; set; } public bool Known { get; set; } }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var url = config.GetString("url").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            sink.ReportProblem("Paste the address of the web page (it starts with https://).");
            return new DisposableAction(() => { });
        }
        var contains = config.GetString("contains").Trim();
        var minutes = Math.Clamp(config.GetNumber("minutes", 60), 5, 1440);
        var key = "page-" + TP.Hash(url + "|" + contains);
        return TP.Poll(async ct =>
        {
            string html;
            try
            {
                using var response = await env.Services.Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var limited = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0 && limited.Length < MaxBytes) limited.Write(buffer, 0, n);
                html = System.Text.Encoding.UTF8.GetString(limited.ToArray());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                sink.ReportProblem("Couldn't open that web page. Automation Portal will keep trying.");
                return;
            }
            sink.ReportProblem(null);
            var text = FeedParser.PageText(html);
            var previous = env.StateStore.Load<PageState>(key);
            var state = new PageState { Known = true };
            bool fire;
            if (contains.Length > 0)
            {
                state.Present = text.Contains(contains, StringComparison.OrdinalIgnoreCase);
                fire = previous is { Known: true } && state.Present && !previous.Present;
            }
            else
            {
                state.Hash = TP.Hash(text);
                fire = previous is { Known: true } && previous.Hash != state.Hash;
            }
            env.StateStore.Save(key, state);
            if (fire) sink.Fire(ContentValue.Url(uri), contains.Length > 0 ? $"The page now mentions \"{contains}\"" : "The page changed");
        }, TimeSpan.FromSeconds(20), () => TimeSpan.FromMinutes(minutes));
    }
}

public sealed class WeatherAlertTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "weather.alert", Title = "Weather alert", Category = TP.CatWeb, Icon = "WeatherRainShowersDay24",
        Summary = "When {condition} {temperature} within {hours} hours",
        Description = "Checks the forecast for where you are (Open-Meteo, no account needed) every half hour and runs when rain or snow is expected, or the temperature will go above or below your number. It runs once per spell, not every check.",
        OutputKind = ContentKind.Text, OutputDescription = "A sentence describing the weather, like \"Rain is expected around 3:00 PM.\"",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("condition", "Alert me when", "rain", new Choice("rain", "rain is expected"), new Choice("snow", "snow is expected"),
                new Choice("above", "it gets warmer than"), new Choice("below", "it gets colder than")),
            ParameterDescriptor.Number("temperature", "Temperature", 25) with { VisibleWhen = new VisibleWhen("condition", "above", "below") },
            ParameterDescriptor.ChoiceOf("unit", "Degrees", "celsius", new Choice("celsius", "Celsius (°C)"), new Choice("fahrenheit", "Fahrenheit (°F)"))
                with { VisibleWhen = new VisibleWhen("condition", "above", "below") },
            ParameterDescriptor.Number("hours", "Look ahead (hours)", 6, 1, 48),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    private sealed class AlertState { public bool Active { get; set; } }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var condition = config.GetChoice("condition", "rain") switch
        {
            "snow" => WeatherCondition.Snow, "above" => WeatherCondition.Above, "below" => WeatherCondition.Below, _ => WeatherCondition.Rain,
        };
        var threshold = config.GetNumber("temperature", 25);
        var fahrenheit = config.GetChoice("unit", "celsius") == "fahrenheit";
        var hours = (int)Math.Clamp(config.GetNumber("hours", 6), 1, 48);
        var key = $"weather-{condition}-{threshold.ToString(CultureInfo.InvariantCulture)}-{fahrenheit}";
        return TP.Poll(async ct =>
        {
            var where = await env.Location.GetAsync(ct);
            if (where is null)
            {
                sink.ReportProblem("Automation Portal couldn't work out where you are, so it can't check the weather. Turn on Windows location or check your internet connection.");
                return;
            }
            string json;
            try
            {
                var url = string.Create(CultureInfo.InvariantCulture,
                    $"https://api.open-meteo.com/v1/forecast?latitude={where.Latitude:0.####}&longitude={where.Longitude:0.####}&hourly=temperature_2m,rain,snowfall&forecast_days=3&timezone=auto{(fahrenheit ? "&temperature_unit=fahrenheit" : "")}");
                json = await env.Services.Http.GetStringAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                sink.ReportProblem("Couldn't get the weather forecast. Automation Portal will keep trying.");
                return;
            }
            sink.ReportProblem(null);
            var alert = WeatherAlertEvaluator.Evaluate(WeatherAlertEvaluator.ParseHourly(json), condition, threshold, hours, DateTime.Now, fahrenheit ? "°F" : "°C");
            var state = env.StateStore.Load<AlertState>(key) ?? new AlertState();
            if (alert is not null && !state.Active)
            {
                env.StateStore.Save(key, new AlertState { Active = true });
                sink.Fire(ContentValue.Text(alert.Message), alert.Message);
            }
            else if (alert is null && state.Active) env.StateStore.Save(key, new AlertState { Active = false });
        }, TimeSpan.FromSeconds(25), () => TimeSpan.FromMinutes(30));
    }
}
