using System.Globalization;
using System.Net;
using Flowmate.Actions.Content.Actions;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;
using Flowmate.Core.Testing;
using static Flowmate.Actions.Content.Tests.Kit;

namespace Flowmate.Actions.Content.Tests;

public class ExpressionEvaluatorTests
{
    [Theory]
    [InlineData("2*(3+4)", 14)]
    [InlineData("12*7.5", 90)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("10 % 3", 1)]
    [InlineData("7 % 4 * 2", 6)]
    [InlineData("50%", 0.5)]
    [InlineData("200+10%", 220)]
    [InlineData("100 - 10%", 90)]
    [InlineData("sqrt(16)+abs(-3)", 7)]
    [InlineData("round(3.14159,2)", 3.14)]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(1234,-2)", 1200)]
    [InlineData("min(4,2,8)", 2)]
    [InlineData("max(4,2,8)", 8)]
    [InlineData("avg(1,2,3)", 2)]
    [InlineData("sin(30)", 0.5)]
    [InlineData("cos(60)", 0.5)]
    [InlineData("tan(45)", 1)]
    [InlineData("log(100)", 2)]
    [InlineData("log(8,2)", 3)]
    [InlineData("ln(e)", 1)]
    [InlineData("2(3+4)", 14)]
    [InlineData("3 x 4", 12)]
    [InlineData("1e3+1", 1001)]
    [InlineData("6 × 7", 42)]
    [InlineData("8 ÷ 2", 4)]
    [InlineData("5 − 3", 2)]
    [InlineData("2**3", 8)]
    [InlineData("5!", 120)]
    [InlineData("fact(5)", 120)]
    [InlineData("  ( 1 + 2 ) * 3 =", 9)]
    [InlineData("√9", 3)]
    [InlineData("+5", 5)]
    [InlineData("--5", 5)]
    [InlineData("1,000", double.NaN)]
    public void Evaluates(string expression, double expected)
    {
        if (double.IsNaN(expected))
        {
            Assert.False(ExpressionEvaluator.TryEvaluate(expression, out _));
            return;
        }
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(expression), 9);
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(Math.PI, ExpressionEvaluator.Evaluate("pi"), 12);
        Assert.Equal(2 * Math.PI, ExpressionEvaluator.Evaluate("2pi"), 12);
        Assert.Equal(Math.E, ExpressionEvaluator.Evaluate("e"), 12);
        Assert.Equal(1, ExpressionEvaluator.Evaluate("sin(pi/2)", degrees: false), 12);
    }

    [Fact]
    public void Trigonometry_is_clean_at_the_famous_angles()
    {
        Assert.Equal(0, ExpressionEvaluator.Evaluate("sin(180)"));
        Assert.Equal(1, ExpressionEvaluator.Evaluate("sin(90)"));
        Assert.Equal(0, ExpressionEvaluator.Evaluate("cos(90)"));
        Assert.Equal(90, ExpressionEvaluator.Evaluate("asin(1)"), 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2+")]
    [InlineData("*3")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("1/0")]
    [InlineData("5 % 0")]
    [InlineData("sqrt(-1)")]
    [InlineData("ln(0)")]
    [InlineData("tan(90)")]
    [InlineData("foo")]
    [InlineData("foo(1)")]
    [InlineData("sqrt()")]
    [InlineData("sqrt(1,2)")]
    [InlineData("min()")]
    [InlineData("2 3")]
    [InlineData("1.2.3")]
    [InlineData("10^1000")]
    [InlineData("(-8)^0.5")]
    [InlineData("fact(-1)")]
    [InlineData("fact(171)")]
    [InlineData("Process.Start(1)")]
    [InlineData("System.IO.File.Delete('x')")]
    public void Rejects_with_a_friendly_error(string expression)
    {
        var ex = Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(expression));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(ExpressionEvaluator.TryEvaluate(expression, out _));
    }

    [Fact]
    public void Deep_nesting_and_huge_input_are_refused_not_crashed()
    {
        Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(new string('(', 5000) + "1" + new string(')', 5000)));
        Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate("(" + string.Concat(Enumerable.Repeat("1+", 5000)) + "1)"));
        Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(new string('(', 200) + "1" + new string(')', 200)));
    }

    [Fact]
    public void Quick_run_detection()
    {
        Assert.True(ExpressionEvaluator.LooksLikeCalculation("12*7.5"));
        Assert.True(ExpressionEvaluator.LooksLikeCalculation("sqrt(9)"));
        Assert.False(ExpressionEvaluator.LooksLikeCalculation("hello"));
        Assert.False(ExpressionEvaluator.LooksLikeCalculation("42"));
        Assert.False(ExpressionEvaluator.LooksLikeCalculation("5 km in miles"));
    }

    [Fact]
    public async Task Action_calculates_with_variables_in_the_text()
    {
        using var host = new TestHost();
        var r = await Run(host, new CalculateExpressionAction(), null, ("expression", "2*(3+4)"));
        Assert.Equal(14, r.AsNumber());
        var fromInput = await Run(host, new CalculateExpressionAction(), ContentValue.Text("10/4"));
        Assert.Equal(2.5, fromInput.AsNumber());
        var rad = await Run(host, new CalculateExpressionAction(), null, ("expression", "cos(0)"), ("angles", C("radians")));
        Assert.Equal(1, rad.AsNumber());
        var ex = await Fails(Run(host, new CalculateExpressionAction(), null, ("expression", "2+")));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public async Task Engine_reports_expression_errors_as_a_failed_step()
    {
        using var host = new TestHost().With(typeof(CalculateExpressionAction).Assembly);
        var result = await ActionTestHarness.RunInShortcutAsync(host, "num.expression", null, ("expression", "1/0"));
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("divide by zero", result.ErrorMessage);
    }
}

public class UnitConverterTests
{
    [Theory]
    [InlineData(5, "km", "mi", 3.10685596118667)]
    [InlineData(1, "mi", "km", 1.609344)]
    [InlineData(100, "°C", "°F", 212)]
    [InlineData(32, "F", "C", 0)]
    [InlineData(0, "celsius", "kelvin", 273.15)]
    [InlineData(-40, "c", "f", -40)]
    [InlineData(1, "kg", "lb", 2.20462262185)]
    [InlineData(10, "lbs", "kg", 4.5359237)]
    [InlineData(1, "gal", "L", 3.785411784)]
    [InlineData(1, "GB", "MB", 1024)]
    [InlineData(1, "byte", "bit", 8)]
    [InlineData(1, "h", "min", 60)]
    [InlineData(60, "mph", "km/h", 96.56064)]
    [InlineData(1, "acre", "m²", 4046.8564224)]
    [InlineData(1, "kilometres", "metres", 1000)]
    [InlineData(1, "Kilometers", "Feet", 3280.8398950131)]
    [InlineData(12, "inches", "cm", 30.48)]
    [InlineData(1, "ft", "in", 12)]
    [InlineData(2, "tbsp", "tsp", 6)]
    [InlineData(1, "week", "days", 7)]
    [InlineData(8, "Mb", "MB", 0.9536743164)]
    public void Converts(double value, string from, string to, double expected) =>
        Assert.Equal(expected, UnitConverter.Convert(value, from, to), 6);

    [Fact]
    public void Every_unit_can_be_found_by_its_own_id_and_ids_are_unique()
    {
        var units = UnitConverter.AllUnits;
        Assert.Equal(units.Count, units.Select(u => u.Id).Distinct().Count());
        foreach (var u in units) Assert.Same(u, UnitConverter.Find(u.Id));
        Assert.All(UnitConverter.Categories, c => Assert.Contains(units, u => u.Category == c));
    }

    [Fact]
    public void Round_trips_within_every_category()
    {
        foreach (var g in UnitConverter.AllUnits.GroupBy(u => u.Category))
            foreach (var a in g)
                foreach (var b in g)
                {
                    var there = UnitConverter.Convert(123.456, a, b);
                    Assert.Equal(123.456, UnitConverter.Convert(there, b, a), 6);
                }
    }

    [Fact]
    public void Mixed_categories_and_unknown_units_explain_themselves()
    {
        var mixed = Assert.Throws<ActionException>(() => UnitConverter.Convert(1, "km", "kg"));
        Assert.Contains("same kind", mixed.Message);
        var unknown = Assert.Throws<ActionException>(() => UnitConverter.Convert(1, "furlongs-per-fortnight", "km"));
        Assert.Contains("furlongs", unknown.Message);
    }

    [Theory]
    [InlineData("5 km in miles", 5, "km", "mi", 3.10686)]
    [InlineData("72 f to c", 72, "°F", "°C", 22.2222)]
    [InlineData("10 lbs as kg", 10, "lb", "kg", 4.53592)]
    [InlineData("3.5GB to MB", 3.5, "GB", "MB", 3584)]
    [InlineData("1,000 m into km", 1000, "m", "km", 1)]
    [InlineData("10 in to cm", 10, "in", "cm", 25.4)]
    [InlineData("100 km/h in mph", 100, "km/h", "mph", 62.1371)]
    public void Quick_run_queries(string text, double value, string fromId, string toId, double result)
    {
        Assert.True(UnitConverter.TryConvertQuery(text, out var q));
        Assert.Equal(value, q!.Value);
        Assert.Equal(fromId, q.From.Id);
        Assert.Equal(toId, q.To.Id);
        Assert.Equal(result, q.Result, 3);
        Assert.Contains("=", q.Display);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("5 km")]
    [InlineData("5 km in kg")]
    [InlineData("20 usd to eur")]
    [InlineData("abc km to mi")]
    [InlineData("")]
    public void Quick_run_ignores_non_conversions(string text) => Assert.False(UnitConverter.TryConvertQuery(text, out _));

    [Fact]
    public async Task Action_converts_lists()
    {
        var r = await Run(new ConvertMeasurementAction(), ContentValue.Numbers(new double[] { 1, 10 }), ("from", C("km")), ("to", C("m")));
        Assert.Equal(new double[] { 1000, 10000 }, r.AsNumbers());
        var text = await Run(new ConvertMeasurementAction(), ContentValue.Text("12.5 kg"), ("from", C("kg")), ("to", C("g")));
        Assert.Equal(12500, text.AsNumber());
        await Fails(Run(new ConvertMeasurementAction(), null, ("value", 5), ("from", C("km")), ("to", C("kg"))));
        await Fails(Run(new ConvertMeasurementAction(), ContentValue.Empty));
    }
}

public class CurrencyTests
{
    [Theory]
    [InlineData("20 usd to eur", 20, "USD", "EUR")]
    [InlineData("20 dollars in euros", 20, "USD", "EUR")]
    [InlineData("$20 to eur", 20, "USD", "EUR")]
    [InlineData("€5 to usd", 5, "EUR", "USD")]
    [InlineData("5 gbp into jpy", 5, "GBP", "JPY")]
    [InlineData("1,250.50 US dollars to pounds", 1250.5, "USD", "GBP")]
    [InlineData("100 EUR as CHF", 100, "EUR", "CHF")]
    public void Parses_quick_run_queries(string text, double amount, string from, string to)
    {
        Assert.True(CurrencyConverter.TryParseQuery(text, out var a, out var f, out var t));
        Assert.Equal(amount, a);
        Assert.Equal(from, f);
        Assert.Equal(to, t);
    }

    [Theory]
    [InlineData("5 km in mi")]
    [InlineData("hello to world")]
    [InlineData("20 usd")]
    [InlineData("20 xyz to eur")]
    public void Ignores_things_that_are_not_currency(string text) => Assert.False(CurrencyConverter.TryParseQuery(text, out _, out _, out _));

    [Fact]
    public void Normalizes_codes_and_names()
    {
        Assert.Equal("USD", CurrencyConverter.NormalizeCode("usd"));
        Assert.Equal("EUR", CurrencyConverter.NormalizeCode("Euros"));
        Assert.Equal("GBP", CurrencyConverter.NormalizeCode("£"));
        Assert.Equal("JPY", CurrencyConverter.NormalizeCode("yen"));
        Assert.Null(CurrencyConverter.NormalizeCode("not a currency"));
    }

    [Fact]
    public async Task Converts_with_the_first_endpoint()
    {
        CurrencyConverter.ClearCache();
        var handler = new StubHandler(_ => Json("{\"amount\":1.0,\"base\":\"CAD\",\"date\":\"2026-10-02\",\"rates\":{\"NOK\":8}}"));
        var r = await CurrencyConverter.ConvertAsync(handler.Client(), 12.5, "cad", "nok");
        Assert.Equal(100, r.Result);
        Assert.Equal(8, r.Rate);
        Assert.Equal("2026-10-02", r.Date);
        Assert.Contains("frankfurter.dev", handler.Requests[0].RequestUri!.Host);
        Assert.Contains("base=CAD", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("12.5 CAD = 100 NOK", r.Display);

        // cached: no new request
        var again = await CurrencyConverter.ConvertAsync(handler.Client(), 2, "CAD", "NOK");
        Assert.Equal(16, again.Result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Falls_back_to_the_second_endpoint()
    {
        CurrencyConverter.ClearCache();
        var handler = new StubHandler(req => req.RequestUri!.Host.EndsWith(".dev")
            ? Resp(HttpStatusCode.ServiceUnavailable, "down")
            : Json("{\"amount\":1.0,\"base\":\"SEK\",\"date\":\"2026-10-02\",\"rates\":{\"PLN\":0.4}}"));
        var r = await CurrencyConverter.ConvertAsync(handler.Client(), 10, "SEK", "PLN");
        Assert.Equal(4, r.Result, 9);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("from=SEK", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Same_currency_needs_no_request_and_failures_are_friendly()
    {
        CurrencyConverter.ClearCache();
        var handler = new StubHandler(_ => Resp(HttpStatusCode.InternalServerError, "no"));
        Assert.Equal(5, (await CurrencyConverter.ConvertAsync(handler.Client(), 5, "usd", "USD")).Result);
        Assert.Empty(handler.Requests);
        var ex = await Assert.ThrowsAsync<ActionException>(() => CurrencyConverter.ConvertAsync(handler.Client(), 5, "HUF", "ISK"));
        Assert.Contains("exchange rates", ex.Message);
        await Assert.ThrowsAsync<ActionException>(() => CurrencyConverter.ConvertAsync(handler.Client(), 5, "nonsense!", "USD"));
    }

    [Fact]
    public async Task Action_converts_and_rounds()
    {
        CurrencyConverter.ClearCache();
        using var host = new TestHost();
        host.Services.Http = new StubHandler(_ => Json("{\"amount\":1.0,\"base\":\"BGN\",\"date\":\"2026-10-02\",\"rates\":{\"RON\":2.55555}}")).Client();
        var r = await Run(host, new ConvertCurrencyAction(), null, ("amount", 10), ("from", C("BGN")), ("to", C("RON")));
        Assert.Equal(25.56, r.AsNumber());
        var precise = await Run(host, new ConvertCurrencyAction(), null, ("amount", 10), ("from", C("BGN")), ("to", C("RON")), ("decimals", 4));
        Assert.Equal(25.5555, precise.AsNumber());
        await Fails(Run(host, new ConvertCurrencyAction(), ContentValue.Empty));
    }
}

public class NumberActionTests
{
    [Fact]
    public async Task Calculate_operations()
    {
        async Task<double> Calc(double n, string op, double operand) =>
            (await Run(new CalculateAction(), ContentValue.Number(n), ("operation", C(op)), ("operand", operand))).AsNumber()!.Value;

        Assert.Equal(15, await Calc(10, "add", 5));
        Assert.Equal(5, await Calc(10, "subtract", 5));
        Assert.Equal(50, await Calc(10, "multiply", 5));
        Assert.Equal(2, await Calc(10, "divide", 5));
        Assert.Equal(1, await Calc(10, "remainder", 3));
        Assert.Equal(1024, await Calc(2, "power", 10));
        Assert.Equal(0.3, await Calc(0.1, "add", 0.2));
    }

    [Fact]
    public async Task Calculate_works_on_lists_and_refuses_division_by_zero()
    {
        var r = await Run(new CalculateAction(), ContentValue.Numbers(new double[] { 1, 2, 3 }), ("operation", C("multiply")), ("operand", 2));
        Assert.Equal(new double[] { 2, 4, 6 }, r.AsNumbers());
        var ex = await Fails(Run(new CalculateAction(), ContentValue.Number(1), ("operation", C("divide")), ("operand", 0)));
        Assert.Contains("divide by zero", ex.Message);
        await Fails(Run(new CalculateAction(), ContentValue.Text("no numbers here")));
    }

    [Theory]
    [InlineData(2.5, "nearest", 0, 3)]
    [InlineData(-2.5, "nearest", 0, -3)]
    [InlineData(1.005, "nearest", 2, 1.01)]
    [InlineData(1.21, "up", 1, 1.3)]
    [InlineData(1.29, "down", 1, 1.2)]
    [InlineData(0.7, "down", 1, 0.7)]
    [InlineData(1234, "nearest", -1, 1230)]
    [InlineData(1201, "up", -2, 1300)]
    [InlineData(-1.5, "up", 0, -1)]
    [InlineData(-1.5, "down", 0, -2)]
    public void Rounds(double n, string mode, int places, double expected) =>
        Assert.Equal(expected, RoundNumberAction.Round(n, mode, places), 9);

    [Fact]
    public async Task Round_action()
    {
        var r = await Run(new RoundNumberAction(), ContentValue.Numbers(new[] { 1.234, 5.678 }), ("places", 1));
        Assert.Equal(new[] { 1.2, 5.7 }, r.AsNumbers());
        await Fails(Run(new RoundNumberAction(), ContentValue.Empty));
    }

    [Fact]
    public async Task Random_stays_in_range()
    {
        Assert.Equal(5, (await Run(new RandomNumberAction(), null, ("min", 5), ("max", 5))).AsNumber());
        var many = await Run(new RandomNumberAction(), null, ("min", 1), ("max", 6), ("count", 200));
        Assert.Equal(200, many.Count);
        Assert.All(many.AsNumbers(), n => { Assert.InRange(n, 1, 6); Assert.Equal(Math.Floor(n), n); });
        Assert.Equal(6, many.AsNumbers().Distinct().Count());

        var swapped = (await Run(new RandomNumberAction(), null, ("min", 10), ("max", 1), ("count", 50))).AsNumbers();
        Assert.All(swapped, n => Assert.InRange(n, 1, 10));
        var fractional = (await Run(new RandomNumberAction(), null, ("min", 0), ("max", 1), ("whole", false), ("count", 50))).AsNumbers();
        Assert.All(fractional, n => Assert.InRange(n, 0, 1));
        Assert.Contains(fractional, n => n != Math.Floor(n));
        await Fails(Run(new RandomNumberAction(), null, ("min", 1.2), ("max", 1.8)));
    }

    private static readonly CultureInfo US = new("en-US");

    [Fact]
    public void Formats_numbers()
    {
        string F(double n, string style, int decimals = 2, bool thousands = true, string currency = "") =>
            FormatNumberAction.Format(n, style, decimals, thousands, currency, US);

        Assert.Equal("1,234.50", F(1234.5, "decimal"));
        Assert.Equal("1234.5", F(1234.5, "decimal", 1, false));
        Assert.Equal("25.6%", F(0.256, "percent", 1));
        Assert.Equal("$1,234.50", F(1234.5, "currency"));
        Assert.Contains("€", F(1234.5, "currency", 2, true, "EUR"));
        Assert.Contains("1,234.50", F(1234.5, "currency", 2, true, "eur"));
        Assert.Equal("1.2K", F(1234, "compact", 1));
        Assert.Equal("1.5M", F(1_500_000, "compact", 1));
        Assert.Equal("999", F(999, "compact", 1));
        Assert.Equal("-2.5K", F(-2500, "compact", 1));
        Assert.Equal("1.5 KB", F(1536, "fileSize"));
    }

    [Theory]
    [InlineData(1, "1st")] [InlineData(2, "2nd")] [InlineData(3, "3rd")] [InlineData(4, "4th")] [InlineData(11, "11th")]
    [InlineData(12, "12th")] [InlineData(13, "13th")] [InlineData(21, "21st")] [InlineData(22, "22nd")] [InlineData(101, "101st")]
    [InlineData(111, "111th")] [InlineData(0, "0th")]
    public void Ordinals(double n, string expected) => Assert.Equal(expected, FormatNumberAction.Ordinal(n));

    [Fact]
    public async Task Format_action_returns_text_for_each_number()
    {
        var r = await Run(new FormatNumberAction(), ContentValue.Numbers(new double[] { 1, 2, 3 }), ("style", C("ordinal")));
        Assert.Equal(new[] { "1st", "2nd", "3rd" }, Lines(r));
    }

    [Fact]
    public async Task Statistics()
    {
        var nums = ContentValue.Numbers(new double[] { 4, 1, 3, 2 });
        async Task<double> S(string stat, ContentValue? v = null) => (await Run(new CalculateStatisticsAction(), v ?? nums, ("statistic", C(stat)))).AsNumber()!.Value;
        Assert.Equal(10, await S("sum"));
        Assert.Equal(2.5, await S("average"));
        Assert.Equal(1, await S("min"));
        Assert.Equal(4, await S("max"));
        Assert.Equal(2.5, await S("median"));
        Assert.Equal(4, await S("count"));
        Assert.Equal(3, await S("range"));
        Assert.Equal(3, await S("median", ContentValue.Numbers(new double[] { 9, 1, 3 })));
        Assert.Equal(0, await S("sum", ContentValue.Empty));
        await Fails(Run(new CalculateStatisticsAction(), ContentValue.Empty, ("statistic", C("max"))));
    }

    [Fact]
    public async Task Statistics_read_numbers_out_of_text_lists()
    {
        var r = await Run(new CalculateStatisticsAction(), Texts("$10", "20 kg", "x", "5"), ("statistic", C("sum")));
        Assert.Equal(35, r.AsNumber());
    }
}
