using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public sealed class RssNewTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "web.rssNew", Title = Strings.Get("Trigger_WebRssNew_Title"), Category = TP.CatWeb, Icon = "Rss24",
        Summary = Strings.Get("Trigger_WebRssNew_Summary"),
        Description = Strings.Get("Trigger_WebRssNew_Description"),
        OutputKind = ContentKind.Dictionary, OutputDescription = Strings.Get("Trigger_WebRssNew_OutputDescription"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "url", Label = Strings.Get("Trigger_WebRssNew_Param_Url_Label"), Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = Strings.Get("Trigger_WebRssNew_Param_Url_Placeholder") },
            ParameterDescriptor.Number("minutes", Strings.Get("Trigger_WebRssNew_Param_Minutes_Label"), 30, 5, 1440),
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
            sink.ReportProblem(Strings.Get("Trigger_WebRssNew_Problem_PasteTheFeedAddress"));
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
                sink.ReportProblem(Strings.Get("Trigger_WebRssNew_Problem_CouldnTDownloadThe"));
                return;
            }
            Feed feed;
            try { feed = FeedParser.Parse(xml); }
            catch (Exception)
            {
                sink.ReportProblem(Strings.Get("Trigger_WebRssNew_Problem_ThatAddressDoesnT"));
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
            sink.Fire(output, fresh.Count == 1 ? Strings.Format("Trigger_Feed_Fired_NewItem", feed.Title, fresh[0].Title) : Strings.Format("Trigger_Feed_Fired_NewItems", fresh.Count, feed.Title));
        }, TimeSpan.FromSeconds(15), () => TimeSpan.FromMinutes(minutes));
    }
}

public sealed class PageChangedTrigger : TriggerProvider
{
    private const int MaxBytes = 3 * 1024 * 1024;
    private static readonly TriggerDescriptor D = new()
    {
        Id = "web.pageChanged", Title = Strings.Get("Trigger_WebPageChanged_Title"), Category = TP.CatWeb, Icon = "Globe24",
        Summary = Strings.Get("Trigger_WebPageChanged_Summary"),
        Description = Strings.Get("Trigger_WebPageChanged_Description"),
        OutputKind = ContentKind.Url, OutputDescription = Strings.Get("Trigger_WebPageChanged_OutputDescription"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "url", Label = Strings.Get("Trigger_WebPageChanged_Param_Url_Label"), Type = ParamType.Url, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = Strings.Get("Trigger_WebPageChanged_Param_Url_Placeholder") },
            ParameterDescriptor.Text("contains", Strings.Get("Trigger_WebPageChanged_Param_Contains_Label"), null, Strings.Get("Trigger_WebPageChanged_Param_Contains_Placeholder")) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Number("minutes", Strings.Get("Trigger_WebRssNew_Param_Minutes_Label"), 60, 5, 1440),
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
            sink.ReportProblem(Strings.Get("Trigger_WebPageChanged_Problem_PasteTheAddressOf"));
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
                sink.ReportProblem(Strings.Get("Trigger_WebPageChanged_Problem_CouldnTOpenThat"));
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
            if (fire) sink.Fire(ContentValue.Url(uri), contains.Length > 0 ? Strings.Format("Trigger_WebPageChanged_Fired_Mentions", contains) : Strings.Get("Trigger_WebPageChanged_Fired_ThePageChanged"));
        }, TimeSpan.FromSeconds(20), () => TimeSpan.FromMinutes(minutes));
    }
}

public sealed class WeatherAlertTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "weather.alert", Title = Strings.Get("Trigger_WeatherAlert_Title"), Category = TP.CatWeb, Icon = "WeatherRainShowersDay24",
        Summary = Strings.Get("Trigger_WeatherAlert_Summary"),
        Description = Strings.Get("Trigger_WeatherAlert_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_WeatherAlert_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("condition", Strings.Get("Trigger_WeatherAlert_Param_Condition_Label"), "rain", new Choice("rain", Strings.Get("Trigger_WeatherAlert_Param_Condition_Choice_Rain")), new Choice("snow", Strings.Get("Trigger_WeatherAlert_Param_Condition_Choice_Snow")),
                new Choice("above", Strings.Get("Trigger_WeatherAlert_Param_Condition_Choice_Above")), new Choice("below", Strings.Get("Trigger_WeatherAlert_Param_Condition_Choice_Below"))),
            ParameterDescriptor.Number("temperature", Strings.Get("Trigger_WeatherAlert_Param_Temperature_Label"), 25) with { VisibleWhen = new VisibleWhen("condition", "above", "below") },
            ParameterDescriptor.ChoiceOf("unit", Strings.Get("Trigger_WeatherAlert_Param_Unit_Label"), "celsius", new Choice("celsius", Strings.Get("Trigger_WeatherAlert_Param_Unit_Choice_Celsius")), new Choice("fahrenheit", Strings.Get("Trigger_WeatherAlert_Param_Unit_Choice_Fahrenheit")))
                with { VisibleWhen = new VisibleWhen("condition", "above", "below") },
            ParameterDescriptor.Number("hours", Strings.Get("Trigger_WeatherAlert_Param_Hours_Label"), 6, 1, 48),
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
                sink.ReportProblem(Strings.Get("Trigger_WeatherAlert_Problem_AutomationPortalCouldnT"));
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
                sink.ReportProblem(Strings.Get("Trigger_WeatherAlert_Problem_CouldnTGetThe"));
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
