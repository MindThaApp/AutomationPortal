using System.Globalization;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class CalculateAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.calculate", Title = Strings.Get("NumCalculate_Title"), Category = ActionCategories.Numbers, Icon = "Calculator24",
        Keywords = new[] { "add", "subtract", "multiply", "divide", "plus", "minus", "times", "math", "power", "remainder" },
        Summary = Strings.Get("NumCalculate_Summary"),
        Description = Strings.Get("NumCalculate_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumCalculate_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", Strings.Get("NumCalculate_Param_Number_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("operation", Strings.Get("NumCalculate_Param_Operation_Label"), "add",
                new Choice("add", Strings.Get("NumCalculate_Choice_Add")), new Choice("subtract", Strings.Get("NumCalculate_Choice_Subtract")), new Choice("multiply", Strings.Get("NumCalculate_Choice_Multiply")), new Choice("divide", Strings.Get("NumCalculate_Choice_Divide")),
                new Choice("remainder", Strings.Get("NumCalculate_Choice_Remainder")), new Choice("power", Strings.Get("NumCalculate_Choice_Power"))),
            ParameterDescriptor.Number("operand", Strings.Get("NumCalculate_Param_Operand_Label"), 1),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException(Strings.Get("NumCalculate_Error_ThereIsNoNumber"));
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
                if (operand == 0) throw new ActionException(Strings.Get("NumCalculate_Error_CanTDivideBy"));
                r = n / operand; break;
            case "remainder":
                if (operand == 0) throw new ActionException(Strings.Get("NumCalculate_Error_CanTDivideBy"));
                r = n % operand; break;
            case "power": r = Math.Pow(n, operand); break;
            default: throw new ActionException(Strings.Get("NumCalculate_Error_ChooseAnOperation"));
        }
        if (double.IsNaN(r) || double.IsInfinity(r)) throw new ActionException(Strings.Get("NumCalculate_Error_ThatCalculationDoesnT"));
        return Math.Round(r, 12) == r ? r : double.Parse(r.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}

public sealed class CalculateExpressionAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.expression", Title = Strings.Get("NumExpression_Title"), Category = ActionCategories.Numbers, Icon = "MathFormula24",
        Keywords = new[] { "formula", "math", "equation", "calculator", "sqrt", "sin", "percent" },
        Summary = Strings.Get("NumExpression_Summary"),
        Description = Strings.Get("NumExpression_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumExpression_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "expression", Label = Strings.Get("NumExpression_Param_Expression_Label"), Type = ParamType.Text, IsInput = true, Required = true, Placeholder = Strings.Get("NumExpression_Param_Expression_Placeholder") },
            ParameterDescriptor.ChoiceOf("angles", Strings.Get("NumExpression_Param_Angles_Label"), "degrees", new Choice("degrees", Strings.Get("NumExpression_Choice_Degrees")), new Choice("radians", Strings.Get("NumExpression_Choice_Radians"))),
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
        Id = "num.round", Title = Strings.Get("NumRound_Title"), Category = ActionCategories.Numbers, Icon = "NumberRow24",
        Keywords = new[] { "decimal", "ceiling", "floor", "up", "down", "nearest", "places" },
        Summary = Strings.Get("NumRound_Summary"),
        Description = Strings.Get("NumRound_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumRound_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", Strings.Get("NumRound_Param_Number_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("NumRound_Param_Mode_Label"), "nearest", new Choice("nearest", Strings.Get("NumRound_Choice_Nearest")), new Choice("up", Strings.Get("NumRound_Choice_Up")), new Choice("down", Strings.Get("NumRound_Choice_Down"))),
            ParameterDescriptor.Number("places", Strings.Get("NumRound_Param_Places_Label"), 0, -9, 12),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException(Strings.Get("NumRound_Error_ThereIsNoNumber"));
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
        return places >= 0 ? r / factor : r * Math.Pow(10, -places);
    }
}

public sealed class RandomNumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.random", Title = Strings.Get("NumRandom_Title"), Category = ActionCategories.Numbers, Icon = "ArrowShuffle24",
        Keywords = new[] { "dice", "lottery", "pick", "chance", "roll" },
        Summary = Strings.Get("NumRandom_Summary"),
        Description = Strings.Get("NumRandom_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("NumRandom_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Number("min", Strings.Get("NumRandom_Param_Min_Label"), 1),
            ParameterDescriptor.Number("max", Strings.Get("NumRandom_Param_Max_Label"), 100),
            ParameterDescriptor.Bool("whole", Strings.Get("NumRandom_Param_Whole_Label"), true),
            ParameterDescriptor.Number("count", Strings.Get("NumRandom_Param_Count_Label"), 1, 1, 1000),
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
                if (lo > hi) throw new ActionException(Strings.Get("NumRandom_Error_ThereIsNoWhole"));
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
        Id = "num.format", Title = Strings.Get("NumFormat_Title"), Category = ActionCategories.Numbers, Icon = "TextNumberFormat24",
        Keywords = new[] { "currency", "percent", "thousands", "decimal", "ordinal", "compact", "file size", "money" },
        Summary = Strings.Get("NumFormat_Summary"),
        Description = Strings.Get("NumFormat_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Text, OutputName = Strings.Get("NumFormat_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("number", Strings.Get("NumFormat_Param_Number_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("style", Strings.Get("NumFormat_Param_Style_Label"), "decimal",
                new Choice("decimal", Strings.Get("NumFormat_Choice_Decimal")), new Choice("currency", Strings.Get("NumFormat_Choice_Currency")), new Choice("percent", Strings.Get("NumFormat_Choice_Percent")),
                new Choice("compact", Strings.Get("NumFormat_Choice_Compact")), new Choice("ordinal", Strings.Get("NumFormat_Choice_Ordinal")), new Choice("fileSize", Strings.Get("NumFormat_Choice_FileSize"))),
            ParameterDescriptor.Number("decimals", Strings.Get("NumFormat_Param_Decimals_Label"), 2, 0, 10) with { VisibleWhen = new VisibleWhen("style", "decimal", "currency", "percent", "compact") },
            ParameterDescriptor.Bool("thousands", Strings.Get("NumFormat_Param_Thousands_Label"), true) with { VisibleWhen = new VisibleWhen("style", "decimal", "currency") },
            ParameterDescriptor.Text("currency", Strings.Get("NumFormat_Param_Currency_Label"), null, Strings.Get("NumFormat_Param_Currency_Placeholder")) with { VisibleWhen = new VisibleWhen("style", "currency") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("number")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException(Strings.Get("NumFormat_Error_ThereIsNoNumber"));
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
    private static readonly Choice[] UnitChoices = UnitConverter.AllUnits.Select(u => new Choice(u.Id, Strings.Format("NumberActions_Choice_01", UnitConverter.CategoryName(u.Category), u.Label))).ToArray();

    private static readonly ActionDescriptor D = new()
    {
        Id = "num.convertUnits", Title = Strings.Get("NumConvertUnits_Title"), Category = ActionCategories.Numbers, Icon = "ArrowsBidirectional24",
        Keywords = new[] { "unit", "convert", "km", "miles", "kg", "pounds", "celsius", "fahrenheit", "litres", "gallons", "mph", "length", "weight", "temperature", "volume", "speed", "area", "data", "time" },
        Summary = Strings.Get("NumConvertUnits_Summary"),
        Description = Strings.Get("NumConvertUnits_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumConvertUnits_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("value", Strings.Get("NumConvertUnits_Param_Value_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("from", Strings.Get("NumConvertUnits_Param_From_Label"), "km", UnitChoices),
            ParameterDescriptor.ChoiceOf("to", Strings.Get("NumConvertUnits_Param_To_Label"), "mi", UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("value")).AsNumbers();
        if (numbers.Count == 0) throw new ActionException(Strings.Get("NumConvertUnits_Error_ThereIsNoValue"));
        var from = await ctx.GetChoiceAsync("from", "km");
        var to = await ctx.GetChoiceAsync("to", "mi");
        return ContentValue.Numbers(numbers.Select(n => UnitConverter.Convert(n, from, to)));
    }
}

public sealed class CalculateStatisticsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "num.stats", Title = Strings.Get("NumStats_Title"), Category = ActionCategories.Numbers, Icon = "DataHistogram24",
        Keywords = new[] { "sum", "total", "average", "mean", "min", "max", "median", "range", "count" },
        Summary = Strings.Get("NumStats_Summary"),
        Description = Strings.Get("NumStats_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumStats_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("numbers", Strings.Get("NumStats_Param_Numbers_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("statistic", Strings.Get("NumStats_Param_Statistic_Label"), "sum",
                new Choice("sum", Strings.Get("NumStats_Choice_Sum")), new Choice("average", Strings.Get("NumStats_Choice_Average")), new Choice("min", Strings.Get("NumStats_Choice_Min")), new Choice("max", Strings.Get("NumStats_Choice_Max")),
                new Choice("median", Strings.Get("NumStats_Choice_Median")), new Choice("count", Strings.Get("NumStats_Choice_Count")), new Choice("range", Strings.Get("NumStats_Choice_Range"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var numbers = (await ctx.GetContentAsync("numbers")).AsNumbers();
        var stat = await ctx.GetChoiceAsync("statistic", "sum");
        if (numbers.Count == 0 && stat != "count" && stat != "sum")
            throw new ActionException(Strings.Get("NumStats_Error_ThereAreNoNumbers"));
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
            default: throw new ActionException(Strings.Get("NumStats_Error_ChooseAStatistic"));
        }
    }
}

public sealed class ConvertCurrencyAction : ShortcutAction
{
    private static readonly Choice[] CurrencyChoices = CurrencyConverter.Currencies.Select(c => new Choice(c.Code, Strings.Format("NumberActions_Choice_012", c.Code, c.Name))).ToArray();

    private static readonly ActionDescriptor D = new()
    {
        Id = "num.currency", Title = Strings.Get("NumCurrency_Title"), Category = ActionCategories.Numbers, Icon = "Money24",
        Keywords = new[] { "money", "exchange", "dollars", "euros", "pounds", "rate", "usd", "eur", "gbp" },
        Summary = Strings.Get("NumCurrency_Summary"),
        Description = Strings.Get("NumCurrency_Description"),
        InputKinds = new[] { ContentKind.Number }, OutputKind = ContentKind.Number, OutputName = Strings.Get("NumCurrency_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("amount", Strings.Get("NumCurrency_Param_Amount_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("from", Strings.Get("NumCurrency_Param_From_Label"), "USD", CurrencyChoices),
            ParameterDescriptor.ChoiceOf("to", Strings.Get("NumCurrency_Param_To_Label"), "EUR", CurrencyChoices),
            ParameterDescriptor.Number("decimals", Strings.Get("NumCurrency_Param_Decimals_Label"), 2, 0, 8, Strings.Get("NumCurrency_Param_Decimals_Unit")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var amounts = (await ctx.GetContentAsync("amount")).AsNumbers();
        if (amounts.Count == 0) throw new ActionException(Strings.Get("NumCurrency_Error_ThereIsNoAmount"));
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
            ctx.Log(Strings.Format("NumCurrency_Log_0Rate12", r.Display, r.Rate, r.Date));
        }
        return ContentValue.Numbers(results);
    }
}
