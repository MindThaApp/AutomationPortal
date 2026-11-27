using System.Globalization;
using System.Text.RegularExpressions;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

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

    /// <summary>Display name of a measurement category id such as "Length".</summary>
    public static string CategoryName(string category) => Strings.Get("Unit_Category_" + category.Replace(" ", "", StringComparison.Ordinal));

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
        Add(Length, "m", Strings.Get("Unit_Name_Metres"), 1, "metre|metres|meter|meters");
        Add(Length, "km", Strings.Get("Unit_Name_Kilometres"), 1000, "kilometre|kilometres|kilometer|kilometers|kms|klick|klicks");
        Add(Length, "cm", Strings.Get("Unit_Name_Centimetres"), 0.01, "centimetre|centimetres|centimeter|centimeters|cms");
        Add(Length, "mm", Strings.Get("Unit_Name_Millimetres"), 0.001, "millimetre|millimetres|millimeter|millimeters|mms");
        Add(Length, "µm", Strings.Get("Unit_Name_Micrometres"), 1e-6, "um|micron|microns|micrometre|micrometres|micrometer|micrometers");
        Add(Length, "nm", Strings.Get("Unit_Name_Nanometres"), 1e-9, "nanometre|nanometres|nanometer|nanometers");
        Add(Length, "mi", Strings.Get("Unit_Name_Miles"), 1609.344, "mile|miles");
        Add(Length, "yd", Strings.Get("Unit_Name_Yards"), 0.9144, "yard|yards|yds");
        Add(Length, "ft", Strings.Get("Unit_Name_Feet"), 0.3048, "foot|feet|'|′");
        Add(Length, "in", Strings.Get("Unit_Name_Inches"), 0.0254, "inch|inches|\"|″");
        Add(Length, "nmi", Strings.Get("Unit_Name_NauticalMiles"), 1852, "nautical mile|nautical miles|nm.|nautical");

        // Weight (base: kilogram)
        Add(Weight, "kg", Strings.Get("Unit_Name_Kilograms"), 1, "kilogram|kilograms|kilo|kilos|kgs|kilogramme|kilogrammes");
        Add(Weight, "g", Strings.Get("Unit_Name_Grams"), 0.001, "gram|grams|gramme|grammes|gr");
        Add(Weight, "mg", Strings.Get("Unit_Name_Milligrams"), 1e-6, "milligram|milligrams|milligramme|milligrammes");
        Add(Weight, "µg", Strings.Get("Unit_Name_Micrograms"), 1e-9, "ug|mcg|microgram|micrograms");
        Add(Weight, "t", Strings.Get("Unit_Name_Tonnes"), 1000, "tonne|tonnes|metric ton|metric tons|metric tonne");
        Add(Weight, "lb", Strings.Get("Unit_Name_Pounds"), 0.45359237, "lbs|pound|pounds|lbm");
        Add(Weight, "oz", Strings.Get("Unit_Name_Ounces"), 0.028349523125, "ounce|ounces");
        Add(Weight, "st", Strings.Get("Unit_Name_Stone"), 6.35029318, "stone|stones");
        Add(Weight, "ton", Strings.Get("Unit_Name_USTons"), 907.18474, "tons|us ton|us tons|short ton|short tons");

        // Temperature (base: kelvin)
        Add(Temperature, "°C", Strings.Get("Unit_Name_Celsius"), 1, "c|celsius|centigrade|degrees celsius|degree celsius|deg c|degc|°", 273.15);
        Add(Temperature, "°F", Strings.Get("Unit_Name_Fahrenheit"), 5.0 / 9.0, "f|fahrenheit|degrees fahrenheit|degree fahrenheit|deg f|degf", 459.67 * 5.0 / 9.0);
        Add(Temperature, "K", Strings.Get("Unit_Name_Kelvin"), 1, "kelvin|kelvins|degrees kelvin");

        // Volume (base: litre)
        Add(Volume, "L", Strings.Get("Unit_Name_Litres"), 1, "l|litre|litres|liter|liters|ltr");
        Add(Volume, "mL", Strings.Get("Unit_Name_Millilitres"), 0.001, "ml|millilitre|millilitres|milliliter|milliliters|cc");
        Add(Volume, "cL", Strings.Get("Unit_Name_Centilitres"), 0.01, "cl|centilitre|centilitres|centiliter|centiliters");
        Add(Volume, "m³", Strings.Get("Unit_Name_CubicMetres"), 1000, "m3|cubic metre|cubic metres|cubic meter|cubic meters|cu m");
        Add(Volume, "gal", Strings.Get("Unit_Name_USGallons"), 3.785411784, "gallon|gallons|gals|us gallon|us gallons");
        Add(Volume, "qt", Strings.Get("Unit_Name_USQuarts"), 0.946352946, "quart|quarts|us quart|us quarts");
        Add(Volume, "pt", Strings.Get("Unit_Name_USPints"), 0.473176473, "pint|pints|us pint|us pints");
        Add(Volume, "cup", Strings.Get("Unit_Name_USCups"), 0.2365882365, "cups|us cup|us cups");
        Add(Volume, "fl oz", Strings.Get("Unit_Name_USFluidOunces"), 0.0295735295625, "floz|fluid ounce|fluid ounces|us fluid ounce|us fluid ounces|fl. oz");
        Add(Volume, "tbsp", Strings.Get("Unit_Name_Tablespoons"), 0.01478676478125, "tablespoon|tablespoons|tbs|tbl");
        Add(Volume, "tsp", Strings.Get("Unit_Name_Teaspoons"), 0.00492892159375, "teaspoon|teaspoons");
        Add(Volume, "imp gal", Strings.Get("Unit_Name_UKGallons"), 4.54609, "uk gallon|uk gallons|imperial gallon|imperial gallons|impgal");
        Add(Volume, "imp pt", Strings.Get("Unit_Name_UKPints"), 0.56826125, "uk pint|uk pints|imperial pint|imperial pints|imppt");

        // Speed (base: metre per second)
        Add(Speed, "m/s", Strings.Get("Unit_Name_MetresPerSecond"), 1, "mps|metre per second|metres per second|meter per second|meters per second");
        Add(Speed, "km/h", Strings.Get("Unit_Name_KilometresPerHour"), 1000.0 / 3600, "kph|kmh|kmph|kilometre per hour|kilometres per hour|kilometer per hour|kilometers per hour");
        Add(Speed, "mph", Strings.Get("Unit_Name_MilesPerHour"), 1609.344 / 3600, "mi/h|mile per hour|miles per hour");
        Add(Speed, "kn", Strings.Get("Unit_Name_Knots"), 1852.0 / 3600, "knot|knots|kt|kts");
        Add(Speed, "ft/s", Strings.Get("Unit_Name_FeetPerSecond"), 0.3048, "fps|foot per second|feet per second");

        // Area (base: square metre)
        Add(Area, "m²", Strings.Get("Unit_Name_SquareMetres"), 1, "m2|sq m|sqm|square metre|square metres|square meter|square meters");
        Add(Area, "km²", Strings.Get("Unit_Name_SquareKilometres"), 1e6, "km2|sq km|sqkm|square kilometre|square kilometres|square kilometer|square kilometers");
        Add(Area, "cm²", Strings.Get("Unit_Name_SquareCentimetres"), 1e-4, "cm2|sq cm|square centimetre|square centimetres|square centimeter|square centimeters");
        Add(Area, "ha", Strings.Get("Unit_Name_Hectares"), 10000, "hectare|hectares");
        Add(Area, "acre", Strings.Get("Unit_Name_Acres"), 4046.8564224, "acres|ac");
        Add(Area, "ft²", Strings.Get("Unit_Name_SquareFeet"), 0.09290304, "ft2|sq ft|sqft|square foot|square feet");
        Add(Area, "in²", Strings.Get("Unit_Name_SquareInches"), 0.00064516, "in2|sq in|sqin|square inch|square inches");
        Add(Area, "yd²", Strings.Get("Unit_Name_SquareYards"), 0.83612736, "yd2|sq yd|sqyd|square yard|square yards");
        Add(Area, "mi²", Strings.Get("Unit_Name_SquareMiles"), 2589988.110336, "mi2|sq mi|sqmi|square mile|square miles");

        // Data size (base: byte; storage uses 1024 like Windows does)
        Add(DataSize, "bit", Strings.Get("Unit_Name_Bits"), 0.125, "bits");
        Add(DataSize, "B", Strings.Get("Unit_Name_Bytes"), 1, "byte|bytes");
        Add(DataSize, "KB", Strings.Get("Unit_Name_Kilobytes"), 1024, "kilobyte|kilobytes|kib|kbyte|kbytes");
        Add(DataSize, "MB", Strings.Get("Unit_Name_Megabytes"), 1024.0 * 1024, "megabyte|megabytes|mib|mbyte|mbytes");
        Add(DataSize, "GB", Strings.Get("Unit_Name_Gigabytes"), Math.Pow(1024, 3), "gigabyte|gigabytes|gib|gbyte|gbytes|gig|gigs");
        Add(DataSize, "TB", Strings.Get("Unit_Name_Terabytes"), Math.Pow(1024, 4), "terabyte|terabytes|tib|tbyte|tbytes");
        Add(DataSize, "PB", Strings.Get("Unit_Name_Petabytes"), Math.Pow(1024, 5), "petabyte|petabytes|pib");
        Add(DataSize, "Kbit", Strings.Get("Unit_Name_Kilobits"), 125, "kilobit|kilobits|kbit", exactAliases: "Kb");
        Add(DataSize, "Mbit", Strings.Get("Unit_Name_Megabits"), 125_000, "megabit|megabits|mbit", exactAliases: "Mb");
        Add(DataSize, "Gbit", Strings.Get("Unit_Name_Gigabits"), 125_000_000, "gigabit|gigabits|gbit", exactAliases: "Gb");

        // Time (base: second)
        Add(Time, "ns", Strings.Get("Unit_Name_Nanoseconds"), 1e-9, "nanosecond|nanoseconds");
        Add(Time, "µs", Strings.Get("Unit_Name_Microseconds"), 1e-6, "us|microsecond|microseconds");
        Add(Time, "ms", Strings.Get("Unit_Name_Milliseconds"), 0.001, "millisecond|milliseconds|msec");
        Add(Time, "s", Strings.Get("Unit_Name_Seconds"), 1, "sec|secs|second|seconds");
        Add(Time, "min", Strings.Get("Unit_Name_Minutes"), 60, "mins|minute|minutes");
        Add(Time, "h", Strings.Get("Unit_Name_Hours"), 3600, "hr|hrs|hour|hours");
        Add(Time, "d", Strings.Get("Unit_Name_Days"), 86400, "day|days");
        Add(Time, "wk", Strings.Get("Unit_Name_Weeks"), 604800, "w|week|weeks|wks");
        Add(Time, "mo", Strings.Get("Unit_Name_Months"), 2629746, "month|months|mos");
        Add(Time, "yr", Strings.Get("Unit_Name_Years"), 31556952, "y|year|years|yrs");
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
            throw new ActionException(Strings.Format("UnitConverter_Error_CanTConvert0", from.Name.ToLowerInvariant(), to.Name.ToLowerInvariant(), CategoryName(from.Category).ToLowerInvariant(), CategoryName(to.Category).ToLowerInvariant()));
        var baseValue = value * from.Factor + from.Offset;
        var result = (baseValue - to.Offset) / to.Factor;
        return Tidy(result);
    }

    public static double Convert(double value, string from, string to)
    {
        var f = Find(from) ?? throw new ActionException(Strings.Format("UnitConverter_Error_DonTKnowThe", from));
        var t = Find(to) ?? throw new ActionException(Strings.Format("UnitConverter_Error_DonTKnowThe", to));
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
