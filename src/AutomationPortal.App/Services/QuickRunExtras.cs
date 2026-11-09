using System.Diagnostics;
using System.Globalization;
using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Integration;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

/// <summary>Quick Run answers that don't need a shortcut: maths, unit and currency conversion, dates, then web search and Ask AI as fall-backs.</summary>
internal static class QuickRunText
{
    public static async Task CopyAsync(AppHost host, string text, string what)
    {
        await host.Services.Clipboard.SetAsync(ContentValue.Text(text));
        host.Notifications.Show("Copied", $"{what} is on your clipboard.", null, null, silent: true);
    }
}

public sealed class CalculatorQuickRunProvider : IQuickRunProvider
{
    private readonly AppHost _host;
    public CalculatorQuickRunProvider(AppHost host) { _host = host; }

    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        var q = query.Trim();
        if (!ExpressionEvaluator.LooksLikeCalculation(q) || !ExpressionEvaluator.TryEvaluate(q, out var value) || double.IsNaN(value) || double.IsInfinity(value))
            return Task.FromResult<IReadOnlyList<QuickRunItem>>(Array.Empty<QuickRunItem>());
        var text = UnitConverter.FormatNumber(value);
        IReadOnlyList<QuickRunItem> items = new[]
        {
            new QuickRunItem
            {
                Id = "calc", Title = "= " + text, Subtitle = q + "   (Enter copies the answer)", Icon = "Calculator24", Color = "orange", Score = 1500,
                ActionLabel = "Copy", CopyText = text,
                ExecuteAsync = _ => QuickRunText.CopyAsync(_host, text, "The answer"),
            },
        };
        return Task.FromResult(items);
    }
}

public sealed class UnitQuickRunProvider : IQuickRunProvider
{
    private readonly AppHost _host;
    public UnitQuickRunProvider(AppHost host) { _host = host; }

    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        if (!UnitConverter.TryConvertQuery(query.Trim(), out var c) || c is null)
            return Task.FromResult<IReadOnlyList<QuickRunItem>>(Array.Empty<QuickRunItem>());
        var result = UnitConverter.FormatNumber(c.Result);
        var title = $"{UnitConverter.FormatNumber(c.Value)} {c.From.Name} = {result} {c.To.Name}";
        IReadOnlyList<QuickRunItem> items = new[]
        {
            new QuickRunItem
            {
                Id = "units", Title = title, Subtitle = "Enter copies the answer", Icon = "ArrowSwap24", Color = "teal", Score = 1400,
                ActionLabel = "Copy", CopyText = result,
                ExecuteAsync = _ => QuickRunText.CopyAsync(_host, result, "The answer"),
            },
        };
        return Task.FromResult(items);
    }
}

public sealed class CurrencyQuickRunProvider : IQuickRunProvider
{
    private readonly AppHost _host;
    public CurrencyQuickRunProvider(AppHost host) { _host = host; }

    public async Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        if (!CurrencyConverter.TryParseQuery(query.Trim(), out var amount, out var from, out var to)) return Array.Empty<QuickRunItem>();
        // Typing fires a search per key press; wait for a pause before asking the internet.
        await Task.Delay(350, ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var r = await CurrencyConverter.ConvertAsync(_host.Services.Http, amount, from, to, timeout.Token);
            var result = UnitConverter.FormatNumber(Math.Round(r.Result, 2));
            return new[]
            {
                new QuickRunItem
                {
                    Id = "currency", Title = $"{UnitConverter.FormatNumber(amount)} {from} = {result} {to}",
                    Subtitle = r.Date is null ? "Enter copies the answer" : $"Rates from {r.Date}. Enter copies the answer", Icon = "Money24", Color = "green", Score = 1400,
                    ActionLabel = "Copy", CopyText = result,
                    ExecuteAsync = _ => QuickRunText.CopyAsync(_host, result, "The answer"),
                },
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Array.Empty<QuickRunItem>(); }
    }
}

public sealed class DateQuickRunProvider : IQuickRunProvider
{
    private static readonly string[] Words =
    {
        "today", "tomorrow", "yesterday", "next ", "last ", "this ", "in ", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "tonight", "noon", "midnight",
    };

    private readonly AppHost _host;
    public DateQuickRunProvider(AppHost host) { _host = host; }

    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        var q = query.Trim();
        var plausible = q.Length >= 4 && (q.Any(char.IsLetter) && Words.Any(w => q.StartsWith(w, StringComparison.OrdinalIgnoreCase)));
        if (!plausible || !DateParser.TryParse(q, DateTimeOffset.Now, out var date))
            return Task.FromResult<IReadOnlyList<QuickRunItem>>(Array.Empty<QuickRunItem>());
        var local = date.ToLocalTime();
        var hasTime = local.TimeOfDay != TimeSpan.Zero;
        var text = local.ToString(hasTime ? "dddd, d MMMM yyyy, h:mm tt" : "dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
        IReadOnlyList<QuickRunItem> items = new[]
        {
            new QuickRunItem
            {
                Id = "date", Title = text, Subtitle = q + "   (Enter copies the date)", Icon = "CalendarLtr24", Color = "red", Score = 1300,
                ActionLabel = "Copy", CopyText = text,
                ExecuteAsync = _ => QuickRunText.CopyAsync(_host, text, "The date"),
            },
        };
        return Task.FromResult(items);
    }
}

public sealed class WebSearchQuickRunProvider : IQuickRunProvider
{
    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 2) return Task.FromResult<IReadOnlyList<QuickRunItem>>(Array.Empty<QuickRunItem>());
        IReadOnlyList<QuickRunItem> items = new[]
        {
            new QuickRunItem
            {
                Id = "web-search", Title = $"Search the web for \"{q}\"", Icon = "Search24", Color = "blue", Score = 10, ActionLabel = "Search",
                ExecuteAsync = _ =>
                {
                    Process.Start(new ProcessStartInfo(SearchTheWebAction.BuildUrl("google", q).AbsoluteUri) { UseShellExecute = true })?.Dispose();
                    return Task.CompletedTask;
                },
            },
        };
        return Task.FromResult(items);
    }
}

/// <summary>"Ask AI …" with the default AI provider. Hidden until an AI provider is set up.</summary>
public sealed class AskAiQuickRunProvider : IQuickRunProvider
{
    private readonly AppHost _host;
    public AskAiQuickRunProvider(AppHost host) { _host = host; }

    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 3 || !IsAiReady()) return Task.FromResult<IReadOnlyList<QuickRunItem>>(Array.Empty<QuickRunItem>());
        var hasSelection = !string.IsNullOrEmpty(context.PresetInput);
        IReadOnlyList<QuickRunItem> items = new[]
        {
            new QuickRunItem
            {
                Id = "ask-ai", Title = $"Ask AI: \"{q}\"", Subtitle = hasSelection ? "About the selected text" : null, Icon = "Sparkle24", Color = "indigo", Score = 5,
                ActionLabel = "Ask", ExecuteAsync = inv => _host.AskAiAsync(q, inv.Input),
            },
        };
        return Task.FromResult(items);
    }

    private bool IsAiReady()
    {
        try { var store = AiProviderStore.For(_host.Services); return store.IsConfigured(store.DefaultProviderId); }
        catch (Exception) { return false; }
    }
}
