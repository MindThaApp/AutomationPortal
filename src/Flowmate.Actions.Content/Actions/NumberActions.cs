using System.Globalization;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Actions;

public sealed class CalculateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.calculate", Title = "Calculate", Category = ActionCategories.Numbers, Icon = "Calculator24",
        Keywords = new[] { "add", "subtract", "multiply", "divide", "plus", "minus", "times", "math", "power", "remainder" },
        Summary = "Calculate {number} {operation} {operand}",
        Description = "Does one calculation on a number: add, subtract, multiply, divide, remainder or power. If you give it a list of numbers, every number gets the same calculation.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = "Calculation Result",
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", "Number", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("operation", "Operation", "add",
                new Choice("add", "+"), new Choice("subtract", "−"), new Choice("multiply", "×"), new Choice("divide", "÷"),
                new Choice("remainder", "remainder after ÷"), new Choice("power", "to the power of")),
            ParameterDescriptor.Number("operand", "Number to use", 1),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException("There is no number to calculate with. Type a number or use the result of an earlier action.");
        var op = await ctx.GetChoiceAsync("operation", "add");
        var operand = await ctx.GetNumberAsync("operand", 1);
        return ContentValue.Numbers(numbers.Select(n => Apply(n, op, operand)));
    }

    internal static double Apply(double n, string op, double operand)
    {
        double r;
        switch (op)
        {
            case "add": r = n + operand; break;
            case "subtract": r = n - operand; break;
            case "multiply": r = n * operand; break;
            case "divide":
                if (operand == 0) throw new ActionException("Can't divide by zero. Change the number you're dividing by.");
                r = n / operand; break;
            case "remainder":
                if (operand == 0) throw new ActionException("Can't divide by zero. Change the number you're dividing by.");
                r = n % operand; break;
            case "power": r = Math.Pow(n, operand); break;
            default: throw new ActionException("Choose an operation.");
        }
        if (double.IsNaN(r) || double.IsInfinity(r)) throw new ActionException("That calculation doesn't have an answer that can be shown.");
        return Math.Round(r, 12) == r ? r : double.Parse(r.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}

public sealed class CalculateExpressionAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.expression", Title = "Calculate Expression", Category = ActionCategories.Numbers, Icon = "MathFormula24",
        Keywords = new[] { "formula", "math", "equation", "calculator", "sqrt", "sin", "percent" },
        Summary = "Calculate {expression}",
        Description = "Works out a calculation you type, like 2*(3+4) or sqrt(16)+10%. Understands + − × ÷ ^ %, brackets, pi, e and functions such as sqrt, abs, round, min, max, sin, cos, tan, log and ln.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Number, OutputName = "Result",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "expression", Label = "Expression", Type = ParamType.Text, IsInput = true, Required = true, Placeholder = "2*(3+4)" },
            ParameterDescriptor.ChoiceOf("angles", "Angles (sin, cos, tan)", "degrees", new Choice("degrees", "Degrees"), new Choice("radians", "Radians")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("expression");
        var degrees = await ctx.GetChoiceAsync("angles", "degrees") != "radians";
        return ContentValue.Number(ExpressionEvaluator.Evaluate(text, degrees));
    }
}

public sealed class RoundNumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.round", Title = "Round Number", Category = ActionCategories.Numbers, Icon = "NumberRow24",
        Keywords = new[] { "decimal", "ceiling", "floor", "up", "down", "nearest", "places" },
        Summary = "Round {number} {mode} to {places} decimal places",
        Description = "Rounds a number to the nearest whole number, or up or down, with as many decimal places as you like. Use a negative number of places to round to tens, hundreds and so on.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = "Rounded Number",
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", "Number", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("mode", "Round", "nearest", new Choice("nearest", "to the nearest"), new Choice("up", "up"), new Choice("down", "down")),
            ParameterDescriptor.Number("places", "Decimal places", 0, -9, 12),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException("There is no number to round. Type a number or use the result of an earlier action.");
        var mode = await ctx.GetChoiceAsync("mode", "nearest");
        var places = (int)Math.Clamp(await ctx.GetNumberAsync("places", 0), -9, 12);
        return ContentValue.Numbers(numbers.Select(n => Round(n, mode, places)));
    }

    internal static double Round(double n, string mode, int places)
    {
        var factor = Math.Pow(10, places);
        var scaled = n * factor;
        // guard against 1.005 * 100 = 100.49999999999999
        var tidy = Math.Round(scaled, 9);
        var r = mode switch
        {
            "up" => Math.Ceiling(tidy),
            "down" => Math.Floor(tidy),
            _ => Math.Round(tidy, MidpointRounding.AwayFromZero),
        };
        return r / factor;
    }
}

public sealed class RandomNumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.random", Title = "Random Number", Category = ActionCategories.Numbers, Icon = "Dice24",
        Keywords = new[] { "dice", "lottery", "pick", "chance", "roll" },
        Summary = "Pick a random number from {min} to {max}",
        Description = "Picks a random number between a smallest and a biggest value (both included). Ask for several to get a list.",
        OutputKind = ContentKind.Number, OutputName = "Random Number",
        Parameters = new[]
        {
            ParameterDescriptor.Number("min", "Smallest", 1),
            ParameterDescriptor.Number("max", "Biggest", 100),
            ParameterDescriptor.Bool("whole", "Whole numbers only", true),
            ParameterDescriptor.Number("count", "How many", 1, 1, 1000),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var min = await ctx.GetNumberAsync("min", 1);
        var max = await ctx.GetNumberAsync("max", 100);
        var whole = await ctx.GetBoolAsync("whole", true);
        var count = (int)Math.Clamp(await ctx.GetNumberAsync("count", 1), 1, 1000);
        if (min > max) (min, max) = (max, min);
        var rnd = Random.Shared;
        var values = new List<double>();
        for (var i = 0; i < count; i++)
        {
            if (whole)
            {
                var lo = (long)Math.Ceiling(min); var hi = (long)Math.Floor(max);
                if (lo > hi) throw new ActionException("There is no whole number between those two values. Turn off \"Whole numbers only\" or widen the range.");
                values.Add(rnd.NextInt64(lo, hi + 1));
            }
            else values.Add(Math.Round(min + rnd.NextDouble() * (max - min), 6));
        }
        return ContentValue.Numbers(values);
    }
}

public sealed class FormatNumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.format", Title = "Format Number", Category = ActionCategories.Numbers, Icon = "TextNumberFormat24",
        Keywords = new[] { "currency", "percent", "thousands", "decimal", "ordinal", "compact", "file size", "money" },
        Summary = "Format {number} as {style}",
        Description = "Turns a number into nicely written text: 1,234.50, a price, a percentage (0.25 becomes 25%), 1.2K, 3rd or a file size.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Text, OutputName = "Formatted Number",
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", "Number", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("style", "Style", "decimal",
                new Choice("decimal", "number"), new Choice("currency", "price"), new Choice("percent", "percentage"),
                new Choice("compact", "short (1.2K, 3.4M)"), new Choice("ordinal", "position (1st, 2nd, 3rd)"), new Choice("fileSize", "file size")),
            ParameterDescriptor.Number("decimals", "Decimal places", 2, 0, 10) with { VisibleWhen = new VisibleWhen("style", "decimal", "currency", "percent", "compact") },
            ParameterDescriptor.Bool("thousands", "Use thousands separators", true) with { VisibleWhen = new VisibleWhen("style", "decimal", "currency") },
            ParameterDescriptor.Text("currency", "Currency", null, "e.g. USD (blank = your region's)") with { VisibleWhen = new VisibleWhen("style", "currency") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException("There is no number to format. Type a number or use the result of an earlier action.");
        var style = await ctx.GetChoiceAsync("style", "decimal");
        var decimals = (int)Math.Clamp(await ctx.GetNumberAsync("decimals", 2), 0, 10);
        var thousands = await ctx.GetBoolAsync("thousands", true);
        var currency = (await ctx.GetTextAsync("currency")).Trim();
        return ContentValue.Texts(numbers.Select(n => Format(n, style, decimals, thousands, currency, CultureInfo.CurrentCulture)));
    }

    internal static string Format(double n, string style, int decimals, bool thousands, string currency, CultureInfo culture)
    {
        switch (style)
        {
            case "decimal": return n.ToString((thousands ? "N" : "F") + decimals, culture);
            case "percent": return n.ToString("P" + decimals, culture);
            case "currency":
                {
                    var nf = (NumberFormatInfo)culture.NumberFormat.Clone();
                    if (currency.Length > 0) nf.CurrencySymbol = SymbolFor(currency, culture);
                    if (!thousands) nf.CurrencyGroupSeparator = "";
                    return n.ToString("C" + decimals, nf);
                }
            case "compact": return Compact(n, decimals, culture);
            case "ordinal": return Ordinal(n);
            case "fileSize": return ContentFormat.Size((long)Math.Max(0, Math.Round(n)));
            default: return ContentFormat.Number(n);
        }
    }

    private static string SymbolFor(string code, CultureInfo current)
    {
        code = code.ToUpperInvariant();
        foreach (var c in new[] { current }.Concat(CultureInfo.GetCultures(CultureTypes.SpecificCultures)))
        {
            try
            {
                var r = new RegionInfo(c.Name);
                if (r.ISOCurrencySymbol == code) return c.NumberFormat.CurrencySymbol;
            }
            catch (ArgumentException) { }
        }
        return code + " ";
    }

    private static string Compact(double n, int decimals, CultureInfo culture)
    {
        var abs = Math.Abs(n);
        (double Div, string Suffix) = abs >= 1e12 ? (1e12, "T") : abs >= 1e9 ? (1e9, "B") : abs >= 1e6 ? (1e6, "M") : abs >= 1e3 ? (1e3, "K") : (1, "");
        var v = n / Div;
        return v.ToString("0." + new string('#', decimals), culture) + Suffix;
    }

    internal static string Ordinal(double n)
    {
        var whole = (long)Math.Round(n);
        var abs = Math.Abs(whole);
        var suffix = (abs % 100) is 11 or 12 or 13 ? "th" : (abs % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return whole.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}

public sealed class ConvertMeasurementAction : ShortcutAction
{
    private static readonly Choice[] UnitChoices = UnitConverter.AllUnits.Select(u => new Choice(u.Id, $"{u.Category}: {u.Label}")).ToArray();

    private static readonly ActionDescriptor D = new()
    {
        Id = "num.convertUnits", Title = "Convert Measurement", Category = ActionCategories.Numbers, Icon = "ArrowsBidirectional24",
        Keywords = new[] { "unit", "convert", "km", "miles", "kg", "pounds", "celsius", "fahrenheit", "litres", "gallons", "mph", "length", "weight", "temperature", "volume", "speed", "area", "data", "time" },
        Summary = "Convert {value} from {from} to {to}",
        Description = "Converts between units of length, weight, temperature, volume, speed, area, data size and time, for example kilometres to miles.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = "Converted Value",
        Parameters = new[]
        {
            ParameterDescriptor.Content("value", "Value", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("from", "From", "km", UnitChoices),
            ParameterDescriptor.ChoiceOf("to", "To", "mi", UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("value")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException("There is no value to convert. Type a number or use the result of an earlier action.");
        var from = await ctx.GetChoiceAsync("from", "km");
        var to = await ctx.GetChoiceAsync("to", "mi");
        return ContentValue.Numbers(numbers.Select(n => UnitConverter.Convert(n, from, to)));
    }
}

public sealed class CalculateStatisticsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.stats", Title = "Calculate Statistics", Category = ActionCategories.Numbers, Icon = "DataHistogram24",
        Keywords = new[] { "sum", "total", "average", "mean", "min", "max", "median", "range", "count" },
        Summary = "Get the {statistic} of {numbers}",
        Description = "Works out the sum, average, smallest, biggest or middle value (median) of a list of numbers.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = "Statistic",
        Parameters = new[]
        {
            ParameterDescriptor.Content("numbers", "Numbers", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("statistic", "Statistic", "sum",
                new Choice("sum", "sum"), new Choice("average", "average"), new Choice("min", "smallest"), new Choice("max", "biggest"),
                new Choice("median", "median"), new Choice("count", "count"), new Choice("range", "range (biggest minus smallest)")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("numbers")).AsNumbers();
        var stat = await ctx.GetChoiceAsync("statistic", "sum");
        if (numbers.Count == 0 && stat != "count" && stat != "sum")
            throw new ActionException("There are no numbers to work with. Give it a list of numbers.");
        return ContentValue.Number(Compute(numbers, stat));
    }

    internal static double Compute(IReadOnlyList<double> n, string stat)
    {
        switch (stat)
        {
            case "sum": return n.Sum();
            case "average": return n.Average();
            case "min": return n.Min();
            case "max": return n.Max();
            case "count": return n.Count;
            case "range": return n.Max() - n.Min();
            case "median":
                {
                    var s = n.OrderBy(x => x).ToList();
                    var mid = s.Count / 2;
                    return s.Count % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
                }
            default: throw new ActionException("Choose a statistic.");
        }
    }
}

public sealed class ConvertCurrencyAction : ShortcutAction
{
    private static readonly Choice[] CurrencyChoices = CurrencyConverter.Currencies.Select(c => new Choice(c.Code, $"{c.Code} – {c.Name}")).ToArray();

    private static readonly ActionDescriptor D = new()
    {
        Id = "num.currency", Title = "Convert Currency", Category = ActionCategories.Numbers, Icon = "Money24",
        Keywords = new[] { "money", "exchange", "dollars", "euros", "pounds", "rate", "usd", "eur", "gbp" },
        Summary = "Convert {amount} from {from} to {to}",
        Description = "Converts money from one currency to another using the latest European Central Bank rates (updated on working days). Needs an internet connection.",
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = "Converted Amount", NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("amount", "Amount", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("from", "From", "USD", CurrencyChoices),
            ParameterDescriptor.ChoiceOf("to", "To", "EUR", CurrencyChoices),
            ParameterDescriptor.Number("decimals", "Round to", 2, 0, 8, "decimal places"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var amounts = (await ctx.GetContentAsync("amount")).AsNumbers();
        if (amounts.Count == 0) throw new ActionException("There is no amount to convert. Type a number or use the result of an earlier action.");
        var from = await ctx.GetChoiceAsync("from", "USD");
        var to = await ctx.GetChoiceAsync("to", "EUR");
        var decimals = (int)Math.Clamp(await ctx.GetNumberAsync("decimals", 2), 0, 8);
        var results = new List<double>();
        foreach (var a in amounts)
        {
            var r = await CurrencyConverter.ConvertAsync(ctx.Services.Http, a, from, to, ctx.CancellationToken);
            var rounded = Math.Round(r.Result, decimals);
            if (rounded == 0 && r.Result != 0) rounded = double.Parse(r.Result.ToString("G4", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            results.Add(rounded);
            ctx.Log($"{r.Display} (rate {r.Rate}, {r.Date})");
        }
        return ContentValue.Numbers(results);
    }
}
