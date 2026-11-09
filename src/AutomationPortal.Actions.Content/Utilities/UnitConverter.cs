using System.Globalization;
using System.Text.RegularExpressions;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Utilities;

/// <summary>A measurement unit. <c>base = value * Factor + Offset</c> within its <see cref="Category"/>.</summary>
public sealed record UnitInfo(string Id, string Name, string Category, double Factor, double Offset = 0)
{
    /// <summary>"Kilometres (km)"</summary>
    public string Label => Name.Equals(Id, StringComparison.OrdinalIgnoreCase) ? Name : $"{Name} ({Id})";
}

public sealed record QuickConversion(double Value, UnitInfo From, UnitInfo To, double Result)
{
    /// <summary>"5 km = 3.10686 mi"</summary>
    public string Display => $"{UnitConverter.FormatNumber(Value)} {From.Id} = {UnitConverter.FormatNumber(Result)} {To.Id}";
}

/// <summary>Converts length, weight, temperature, volume, speed, area, data size and time. Understands names and abbreviations.</summary>
public static class UnitConverter
{
    public const string Length = "Length", Weight = "Weight", Temperature = "Temperature", Volume = "Volume",
        Speed = "Speed", Area = "Area", DataSize = "Data size", Time = "Time";

    private static readonly List<UnitInfo> Units = new();
    private static readonly Dictionary<string, UnitInfo> Exact = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, UnitInfo> Loose = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<UnitInfo> AllUnits => Units;
    public static IReadOnlyList<string> Categories { get; } = new[] { Length, Weight, Temperature, Volume, Speed, Area, DataSize, Time };

    private static void Add(string category, string id, string name, double factor, string aliases, double offset = 0, string? exactAliases = null)
    {
        var u = new UnitInfo(id, name, category, factor, offset);
        Units.Add(u);
        foreach (var a in aliases.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Loose.TryAdd(Normalize(a), u);
        Loose.TryAdd(Normalize(id), u);
        Exact[Normalize(id)] = u;
        if (exactAliases is not null)
            foreach (var a in exactAliases.Split('|', StringSplitOptions.RemoveEmptyEntries)) Exact[a] = u;
    }

    static UnitConverter()
    {
        // Length (base: metre)
        Add(Length, "m", "Metres", 1, "metre|metres|meter|meters");
        Add(Length, "km", "Kilometres", 1000, "kilometre|kilometres|kilometer|kilometers|kms|klick|klicks");
        Add(Length, "cm", "Centimetres", 0.01, "centimetre|centimetres|centimeter|centimeters|cms");
        Add(Length, "mm", "Millimetres", 0.001, "millimetre|millimetres|millimeter|millimeters|mms");
        Add(Length, "µm", "Micrometres", 1e-6, "um|micron|microns|micrometre|micrometres|micrometer|micrometers");
        Add(Length, "nm", "Nanometres", 1e-9, "nanometre|nanometres|nanometer|nanometers");
        Add(Length, "mi", "Miles", 1609.344, "mile|miles");
        Add(Length, "yd", "Yards", 0.9144, "yard|yards|yds");
        Add(Length, "ft", "Feet", 0.3048, "foot|feet|'|′");
        Add(Length, "in", "Inches", 0.0254, "inch|inches|\"|″");
        Add(Length, "nmi", "Nautical miles", 1852, "nautical mile|nautical miles|nm.|nautical");

        // Weight (base: kilogram)
        Add(Weight, "kg", "Kilograms", 1, "kilogram|kilograms|kilo|kilos|kgs|kilogramme|kilogrammes");
        Add(Weight, "g", "Grams", 0.001, "gram|grams|gramme|grammes|gr");
        Add(Weight, "mg", "Milligrams", 1e-6, "milligram|milligrams|milligramme|milligrammes");
        Add(Weight, "µg", "Micrograms", 1e-9, "ug|mcg|microgram|micrograms");
        Add(Weight, "t", "Tonnes", 1000, "tonne|tonnes|metric ton|metric tons|metric tonne");
        Add(Weight, "lb", "Pounds", 0.45359237, "lbs|pound|pounds|lbm");
        Add(Weight, "oz", "Ounces", 0.028349523125, "ounce|ounces");
        Add(Weight, "st", "Stone", 6.35029318, "stone|stones");
        Add(Weight, "ton", "US tons", 907.18474, "tons|us ton|us tons|short ton|short tons");

        // Temperature (base: kelvin)
        Add(Temperature, "°C", "Celsius", 1, "c|celsius|centigrade|degrees celsius|degree celsius|deg c|degc|°", 273.15);
        Add(Temperature, "°F", "Fahrenheit", 5.0 / 9.0, "f|fahrenheit|degrees fahrenheit|degree fahrenheit|deg f|degf", 459.67 * 5.0 / 9.0);
        Add(Temperature, "K", "Kelvin", 1, "kelvin|kelvins|degrees kelvin");

        // Volume (base: litre)
        Add(Volume, "L", "Litres", 1, "l|litre|litres|liter|liters|ltr");
        Add(Volume, "mL", "Millilitres", 0.001, "ml|millilitre|millilitres|milliliter|milliliters|cc");
        Add(Volume, "cL", "Centilitres", 0.01, "cl|centilitre|centilitres|centiliter|centiliters");
        Add(Volume, "m³", "Cubic metres", 1000, "m3|cubic metre|cubic metres|cubic meter|cubic meters|cu m");
        Add(Volume, "gal", "US gallons", 3.785411784, "gallon|gallons|gals|us gallon|us gallons");
        Add(Volume, "qt", "US quarts", 0.946352946, "quart|quarts|us quart|us quarts");
        Add(Volume, "pt", "US pints", 0.473176473, "pint|pints|us pint|us pints");
        Add(Volume, "cup", "US cups", 0.2365882365, "cups|us cup|us cups");
        Add(Volume, "fl oz", "US fluid ounces", 0.0295735295625, "floz|fluid ounce|fluid ounces|us fluid ounce|us fluid ounces|fl. oz");
        Add(Volume, "tbsp", "Tablespoons", 0.01478676478125, "tablespoon|tablespoons|tbs|tbl");
        Add(Volume, "tsp", "Teaspoons", 0.00492892159375, "teaspoon|teaspoons");
        Add(Volume, "imp gal", "UK gallons", 4.54609, "uk gallon|uk gallons|imperial gallon|imperial gallons|impgal");
        Add(Volume, "imp pt", "UK pints", 0.56826125, "uk pint|uk pints|imperial pint|imperial pints|imppt");

        // Speed (base: metre per second)
        Add(Speed, "m/s", "Metres per second", 1, "mps|metre per second|metres per second|meter per second|meters per second");
        Add(Speed, "km/h", "Kilometres per hour", 1000.0 / 3600, "kph|kmh|kmph|kilometre per hour|kilometres per hour|kilometer per hour|kilometers per hour");
        Add(Speed, "mph", "Miles per hour", 1609.344 / 3600, "mi/h|mile per hour|miles per hour");
        Add(Speed, "kn", "Knots", 1852.0 / 3600, "knot|knots|kt|kts");
        Add(Speed, "ft/s", "Feet per second", 0.3048, "fps|foot per second|feet per second");

        // Area (base: square metre)
        Add(Area, "m²", "Square metres", 1, "m2|sq m|sqm|square metre|square metres|square meter|square meters");
        Add(Area, "km²", "Square kilometres", 1e6, "km2|sq km|sqkm|square kilometre|square kilometres|square kilometer|square kilometers");
        Add(Area, "cm²", "Square centimetres", 1e-4, "cm2|sq cm|square centimetre|square centimetres|square centimeter|square centimeters");
        Add(Area, "ha", "Hectares", 10000, "hectare|hectares");
        Add(Area, "acre", "Acres", 4046.8564224, "acres|ac");
        Add(Area, "ft²", "Square feet", 0.09290304, "ft2|sq ft|sqft|square foot|square feet");
        Add(Area, "in²", "Square inches", 0.00064516, "in2|sq in|sqin|square inch|square inches");
        Add(Area, "yd²", "Square yards", 0.83612736, "yd2|sq yd|sqyd|square yard|square yards");
        Add(Area, "mi²", "Square miles", 2589988.110336, "mi2|sq mi|sqmi|square mile|square miles");

        // Data size (base: byte; storage uses 1024 like Windows does)
        Add(DataSize, "bit", "Bits", 0.125, "bits");
        Add(DataSize, "B", "Bytes", 1, "byte|bytes");
        Add(DataSize, "KB", "Kilobytes", 1024, "kilobyte|kilobytes|kib|kbyte|kbytes");
        Add(DataSize, "MB", "Megabytes", 1024.0 * 1024, "megabyte|megabytes|mib|mbyte|mbytes");
        Add(DataSize, "GB", "Gigabytes", Math.Pow(1024, 3), "gigabyte|gigabytes|gib|gbyte|gbytes|gig|gigs");
        Add(DataSize, "TB", "Terabytes", Math.Pow(1024, 4), "terabyte|terabytes|tib|tbyte|tbytes");
        Add(DataSize, "PB", "Petabytes", Math.Pow(1024, 5), "petabyte|petabytes|pib");
        Add(DataSize, "Kbit", "Kilobits", 125, "kilobit|kilobits|kbit", exactAliases: "Kb");
        Add(DataSize, "Mbit", "Megabits", 125_000, "megabit|megabits|mbit", exactAliases: "Mb");
        Add(DataSize, "Gbit", "Gigabits", 125_000_000, "gigabit|gigabits|gbit", exactAliases: "Gb");

        // Time (base: second)
        Add(Time, "ns", "Nanoseconds", 1e-9, "nanosecond|nanoseconds");
        Add(Time, "µs", "Microseconds", 1e-6, "us|microsecond|microseconds");
        Add(Time, "ms", "Milliseconds", 0.001, "millisecond|milliseconds|msec");
        Add(Time, "s", "Seconds", 1, "sec|secs|second|seconds");
        Add(Time, "min", "Minutes", 60, "mins|minute|minutes");
        Add(Time, "h", "Hours", 3600, "hr|hrs|hour|hours");
        Add(Time, "d", "Days", 86400, "day|days");
        Add(Time, "wk", "Weeks", 604800, "w|week|weeks|wks");
        Add(Time, "mo", "Months", 2629746, "month|months|mos");
        Add(Time, "yr", "Years", 31556952, "y|year|years|yrs");
    }

    private static string Normalize(string s)
    {
        var t = s.Trim().Replace('²', '2').Replace('³', '3').Replace("^2", "2").Replace("^3", "3").Replace('µ', 'µ');
        t = Regex.Replace(t, @"\s+", " ");
        t = t.Replace("square ", "square ").Replace("sq. ", "sq ");
        t = t.TrimEnd('.');
        return t;
    }

    /// <summary>Finds a unit by name or abbreviation ("km", "kilometres", "lbs", "°F"). Null when unknown.</summary>
    public static UnitInfo? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var n = Normalize(text);
        if (Exact.TryGetValue(n, out var u)) return u;
        if (Loose.TryGetValue(n, out u)) return u;
        var stripped = Regex.Replace(n, @"^(degrees?|deg)\s+(?=\w)", "", RegexOptions.IgnoreCase);
        if (stripped != n && Loose.TryGetValue(stripped, out u)) return u;
        if (n.StartsWith('°') && Loose.TryGetValue(n[1..], out u)) return u;
        return null;
    }

    public static double Convert(double value, UnitInfo from, UnitInfo to)
    {
        if (from.Category != to.Category)
            throw new ActionException($"Can't convert {from.Name.ToLowerInvariant()} to {to.Name.ToLowerInvariant()} because one measures {from.Category.ToLowerInvariant()} and the other {to.Category.ToLowerInvariant()}. Pick two units of the same kind.");
        var baseValue = value * from.Factor + from.Offset;
        var result = (baseValue - to.Offset) / to.Factor;
        return Tidy(result);
    }

    public static double Convert(double value, string from, string to)
    {
        var f = Find(from) ?? throw new ActionException($"Don't know the unit '{from}'. Try something like km, miles, kg, °C, litres or MB.");
        var t = Find(to) ?? throw new ActionException($"Don't know the unit '{to}'. Try something like km, miles, kg, °C, litres or MB.");
        return Convert(value, f, t);
    }

    private static double Tidy(double d)
    {
        if (d == 0 || !double.IsFinite(d)) return d;
        return double.Parse(d.ToString("G12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static string FormatNumber(double d)
    {
        if (Math.Abs(d) >= 1e15 || (d != 0 && Math.Abs(d) < 1e-6)) return d.ToString("G6", CultureInfo.InvariantCulture);
        return Math.Round(d, 6).ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static readonly Regex QueryRx = new(
        @"^\s*(?<n>[-+]?(?:\d[\d,]*\.?\d*|\.\d+)(?:e[-+]?\d+)?)\s*(?<rest>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SplitRx = new(@"\s+(?:to|in|into|as|=>|->|=)\s+|\s*(?:=>|->)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Understands "5 km in miles", "72 f to c", "10 lbs as kg", "3.5GB to MB". For Quick Run.</summary>
    public static bool TryConvertQuery(string text, out QuickConversion? conversion)
    {
        conversion = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100) return false;
        var m = QueryRx.Match(text);
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups["n"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        var rest = m.Groups["rest"].Value;
        foreach (Match sep in SplitRx.Matches(rest))
        {
            var left = rest[..sep.Index];
            var right = rest[(sep.Index + sep.Length)..];
            if (Find(left) is { } f && Find(right) is { } t && f.Category == t.Category)
            {
                conversion = new QuickConversion(value, f, t, Convert(value, f, t));
                return true;
            }
        }
        return false;
    }
}
