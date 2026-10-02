using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Utilities;

public sealed record CurrencyResult(double Amount, string From, string To, double Rate, double Result, string? Date)
{
    /// <summary>"20 USD = 17.2 EUR"</summary>
    public string Display => $"{UnitConverter.FormatNumber(Amount)} {From} = {UnitConverter.FormatNumber(Math.Round(Result, 2))} {To}";
}

/// <summary>Currency conversion with the free frankfurter.dev / frankfurter.app rates (European Central Bank), no key needed.</summary>
public static class CurrencyConverter
{
    private static readonly string[] Endpoints = { "https://api.frankfurter.dev/v1/latest", "https://api.frankfurter.app/latest" };
    private static readonly ConcurrentDictionary<string, (DateTime At, double Rate, string? Date)> Cache = new();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    internal static void ClearCache() => Cache.Clear();

    public static IReadOnlyList<(string Code, string Name)> Currencies { get; } = new (string, string)[]
    {
        ("USD", "US dollar"), ("EUR", "Euro"), ("GBP", "British pound"), ("JPY", "Japanese yen"), ("CAD", "Canadian dollar"),
        ("AUD", "Australian dollar"), ("CHF", "Swiss franc"), ("CNY", "Chinese yuan"), ("INR", "Indian rupee"), ("MXN", "Mexican peso"),
        ("BRL", "Brazilian real"), ("KRW", "South Korean won"), ("SEK", "Swedish krona"), ("NOK", "Norwegian krone"), ("DKK", "Danish krone"),
        ("PLN", "Polish zloty"), ("CZK", "Czech koruna"), ("HUF", "Hungarian forint"), ("RON", "Romanian leu"), ("BGN", "Bulgarian lev"),
        ("TRY", "Turkish lira"), ("ILS", "Israeli shekel"), ("ZAR", "South African rand"), ("NZD", "New Zealand dollar"),
        ("SGD", "Singapore dollar"), ("HKD", "Hong Kong dollar"), ("THB", "Thai baht"), ("MYR", "Malaysian ringgit"),
        ("IDR", "Indonesian rupiah"), ("PHP", "Philippine peso"), ("ISK", "Icelandic krona"),
    };

    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in Currencies)
        {
            d[code] = code;
            d[name] = code;
            d[name + "s"] = code;
        }
        void A(string code, string list) { foreach (var a in list.Split('|')) d[a] = code; }
        A("USD", "$|dollar|dollars|us dollar|us dollars|usd|bucks|american dollar|american dollars");
        A("EUR", "€|euro|euros|eur");
        A("GBP", "£|pound|pounds|sterling|pound sterling|quid|gbp|british pounds");
        A("JPY", "¥|yen|jpy|japanese yen");
        A("CNY", "yuan|rmb|renminbi|cny|chinese yuan");
        A("INR", "₹|rupee|rupees|inr");
        A("CHF", "franc|francs|swiss francs|chf");
        A("CAD", "canadian dollars|cad|c$");
        A("AUD", "australian dollars|aud|a$");
        A("NZD", "new zealand dollars|nzd|kiwi dollars");
        A("MXN", "peso|pesos|mexican pesos|mxn");
        A("KRW", "won|krw|korean won");
        A("SEK", "swedish kronor|kronor|sek");
        A("NOK", "norwegian kroner|nok");
        A("DKK", "danish kroner|dkk");
        A("PLN", "zloty|zlotys|pln");
        A("TRY", "lira|turkish liras|try");
        A("ILS", "shekel|shekels|ils|nis");
        A("ZAR", "rand|zar");
        A("BRL", "real|reais|brl|brazilian reais");
        A("THB", "baht|thb");
        return d;
    }

    /// <summary>"usd", "US Dollars", "$", "euros" → "USD". Null when not recognised.</summary>
    public static string? NormalizeCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimEnd('.');
        if (Aliases.TryGetValue(t, out var code)) return code;
        if (t.Length == 3 && t.All(char.IsLetter)) return t.ToUpperInvariant();
        return null;
    }

    private static readonly Regex QueryRx = new(
        @"^\s*(?:(?<sym>[$€£¥₹])\s*)?(?<n>\d[\d,]*(?:\.\d+)?|\.\d+)\s*(?<from>[^\d\s][^\d]*?)?\s+(?:to|in|into|as|=>|->)\s+(?<to>[^\d]+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Understands "20 usd to eur", "20 dollars in euros", "$20 to eur", "5 gbp into jpy". For Quick Run.</summary>
    public static bool TryParseQuery(string text, out double amount, out string from, out string to)
    {
        amount = 0; from = to = "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80) return false;
        var m = QueryRx.Match(text);
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups["n"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return false;
        var fromText = m.Groups["sym"].Success ? m.Groups["sym"].Value : m.Groups["from"].Value;
        var f = NormalizeCode(fromText);
        var t = NormalizeCode(m.Groups["to"].Value);
        if (f is null || t is null) return false;
        // Avoid grabbing unit queries like "5 km in mi": both sides must look like currencies.
        if (!IsKnown(f) || !IsKnown(t)) return false;
        from = f; to = t;
        return true;
    }

    private static bool IsKnown(string code) => Currencies.Any(c => c.Code == code);

    public static async Task<CurrencyResult> ConvertAsync(HttpClient http, double amount, string from, string to, CancellationToken ct = default)
    {
        var f = NormalizeCode(from) ?? throw new ActionException($"Don't know the currency '{from}'. Use a code like USD, EUR or GBP.");
        var t = NormalizeCode(to) ?? throw new ActionException($"Don't know the currency '{to}'. Use a code like USD, EUR or GBP.");
        if (f == t) return new CurrencyResult(amount, f, t, 1, amount, null);

        var key = $"{f}>{t}";
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheFor)
            return new CurrencyResult(amount, f, t, hit.Rate, amount * hit.Rate, hit.Date);

        string? lastProblem = null;
        foreach (var endpoint in Endpoints)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = endpoint.Contains("frankfurter.dev") ? $"{endpoint}?base={f}&symbols={t}" : $"{endpoint}?from={f}&to={t}";
                using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                    throw new ActionException($"Exchange rates aren't available for {f} to {t}. Try a more common currency such as USD, EUR or GBP.");
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                var rate = doc.RootElement.GetProperty("rates").GetProperty(t).GetDouble();
                var date = doc.RootElement.TryGetProperty("date", out var d) ? d.GetString() : null;
                Cache[key] = (DateTime.UtcNow, rate, date);
                return new CurrencyResult(amount, f, t, rate, amount * rate, date);
            }
            catch (ActionException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
            {
                lastProblem = ex.Message;
            }
        }
        throw new ActionException($"Couldn't get today's exchange rates. Check your internet connection and try again. ({lastProblem})");
    }
}
