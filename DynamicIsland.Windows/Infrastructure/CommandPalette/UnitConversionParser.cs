using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Local, deterministic conversions. Volume aliases use US customary measures;
/// decimal data units and binary IEC units are distinct.</summary>
public static partial class UnitConversionParser
{
    private const string Number = @"[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?";

    [GeneratedRegex(@"^(?:convert\s+)?(?<value>" + Number + @")\s*(?<from>[a-z°'""′″].*?)\s+(?:to|in|into)\s+(?<to>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConversionRegex();

    [GeneratedRegex(@"^" + Number + @"\s*[a-zA-Z°µμ'""′″]", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    private sealed record Unit(string Symbol, string Dimension, decimal Scale, decimal Offset = 0);

    private static readonly Dictionary<string, Unit> Units = CreateUnits();
    public static IReadOnlyList<string> Categories { get; } = Units.Values.Select(unit => unit.Dimension).Distinct()
        .Select(name => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name)).ToArray();
    public static IReadOnlyList<string> SymbolsFor(string category) => Units.Values
        .Where(unit => unit.Dimension.Equals(category, StringComparison.OrdinalIgnoreCase)).Select(unit => unit.Symbol).Distinct().ToArray();

    public static bool IsCandidate(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var text = query.Trim();
        return text.Equals("convert", StringComparison.OrdinalIgnoreCase) ||
            (text.StartsWith("convert", StringComparison.OrdinalIgnoreCase) && text.Length > 7 && char.IsWhiteSpace(text[7])) ||
            (text.Length <= 256 && CandidateRegex().IsMatch(text));
    }

    public static bool TryConvert(string query, out string result, out string? error)
    {
        result = "";
        error = null;
        if (query.Length > 256)
        {
            error = "Conversion is too long.";
            return false;
        }
        var match = ConversionRegex().Match(query.Trim());
        if (!match.Success)
        {
            error = "Use a conversion like 12 in to ft, 5 kg to lb, or 72 F to C.";
            return false;
        }
        if (!decimal.TryParse(match.Groups["value"].Value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var value))
        {
            error = "That number is too large or too small.";
            return false;
        }
        var fromText = Normalize(match.Groups["from"].Value);
        var toText = Normalize(match.Groups["to"].Value);
        if (!Units.TryGetValue(fromText, out var from) || !Units.TryGetValue(toText, out var to))
        {
            error = "Unknown unit. Try length, weight, temperature, volume, time, area, speed, or data size.";
            return false;
        }
        if (from.Dimension != to.Dimension)
        {
            error = $"Cannot convert {from.Symbol} to {to.Symbol}: choose units of the same kind.";
            return false;
        }
        try
        {
            // Offsets are expressed in each unit before scaling (Fahrenheit uses -32).
            var converted = from == to ? value : (value + from.Offset) * from.Scale / to.Scale - to.Offset;
            result = $"{converted.ToString("0.############", CultureInfo.InvariantCulture)} {to.Symbol}";
            // Preserve very small nonzero quantities instead of rounding them to zero.
            if (converted != 0 && decimal.Round(converted, 12) == 0)
                result = $"{CalculatorParser.Format(converted)} {to.Symbol}";
            return true;
        }
        catch (OverflowException)
        {
            error = "That conversion result is too large.";
            return false;
        }
    }

    private static string Normalize(string text) => string.Join(' ', text.Trim().ToLowerInvariant()
        .Replace('²', '2').Replace('³', '3').Replace("^", "").Replace('μ', 'µ')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, Unit> CreateUnits()
    {
        var units = new Dictionary<string, Unit>(StringComparer.Ordinal);
        void Add(string symbol, string dimension, decimal scale, string aliases, decimal offset = 0)
        {
            var unit = new Unit(symbol, dimension, scale, offset);
            foreach (var alias in aliases.Split('|').Prepend(symbol)) units[Normalize(alias)] = unit;
        }

        Add("mm", "length", .001m, "millimeter|millimeters|millimetre|millimetres");
        Add("cm", "length", .01m, "centimeter|centimeters|centimetre|centimetres");
        Add("m", "length", 1m, "meter|meters|metre|metres");
        Add("km", "length", 1000m, "kilometer|kilometers|kilometre|kilometres");
        Add("in", "length", .0254m, "inch|inches|\"|″");
        Add("ft", "length", .3048m, "foot|feet|'|′");
        Add("yd", "length", .9144m, "yard|yards");
        Add("mi", "length", 1609.344m, "mile|miles");
        Add("mg", "weight", .000001m, "milligram|milligrams");
        Add("g", "weight", .001m, "gram|grams");
        Add("kg", "weight", 1m, "kilogram|kilograms|kilo|kilos");
        Add("oz", "weight", .028349523125m, "ounce|ounces");
        Add("lb", "weight", .45359237m, "lbs|pound|pounds");
        Add("st", "weight", 6.35029318m, "stone|stones");
        Add("°C", "temperature", 1m, "c|celsius|deg c|degrees c|degree celsius|degrees celsius");
        Add("°F", "temperature", 5m / 9m, "f|fahrenheit|deg f|degrees f|degree fahrenheit|degrees fahrenheit", -32m);
        Add("K", "temperature", 1m, "kelvin|kelvins|°k", -273.15m);
        Add("mL", "volume", .001m, "milliliter|milliliters|millilitre|millilitres");
        Add("L", "volume", 1m, "liter|liters|litre|litres");
        Add("cm³", "volume", .001m, "cc|cubic centimeter|cubic centimeters");
        Add("m³", "volume", 1000m, "cubic meter|cubic meters|cubic metre|cubic metres");
        Add("US gal", "volume", 3.785411784m, "gal|gallon|gallons|us gallon|us gallons");
        Add("US qt", "volume", .946352946m, "qt|quart|quarts|us quart|us quarts");
        Add("US pt", "volume", .473176473m, "pt|pint|pints|us pint|us pints");
        Add("US cup", "volume", .2365882365m, "cup|cups|us cups");
        Add("US fl oz", "volume", .0295735295625m, "fl oz|fluid ounce|fluid ounces");
        Add("US tbsp", "volume", .01478676478125m, "tbsp|tablespoon|tablespoons");
        Add("US tsp", "volume", .00492892159375m, "tsp|teaspoon|teaspoons");
        Add("ms", "time", .001m, "millisecond|milliseconds");
        Add("s", "time", 1m, "sec|secs|second|seconds");
        Add("min", "time", 60m, "mins|minute|minutes");
        Add("h", "time", 3600m, "hr|hrs|hour|hours");
        Add("d", "time", 86400m, "day|days");
        Add("wk", "time", 604800m, "week|weeks");
        Add("mm²", "area", .000001m, "sq mm|square millimeter|square millimeters");
        Add("cm²", "area", .0001m, "sq cm|square centimeter|square centimeters");
        Add("m²", "area", 1m, "sq m|square meter|square meters|square metre|square metres");
        Add("km²", "area", 1000000m, "sq km|square kilometer|square kilometers");
        Add("in²", "area", .00064516m, "sq in|square inch|square inches");
        Add("ft²", "area", .09290304m, "sq ft|square foot|square feet");
        Add("acre", "area", 4046.8564224m, "acres");
        Add("ha", "area", 10000m, "hectare|hectares");
        Add("m/s", "speed", 1m, "meters per second|metres per second");
        Add("km/h", "speed", 1m / 3.6m, "kph|kmh|kilometers per hour|kilometres per hour");
        Add("mph", "speed", .44704m, "mi/h|miles per hour");
        Add("ft/s", "speed", .3048m, "fps|feet per second");
        Add("B", "data size", 1m, "byte|bytes");
        Add("KB", "data size", 1000m, "kilobyte|kilobytes");
        Add("MB", "data size", 1000000m, "megabyte|megabytes");
        Add("GB", "data size", 1000000000m, "gigabyte|gigabytes");
        Add("TB", "data size", 1000000000000m, "terabyte|terabytes");
        Add("KiB", "data size", 1024m, "kibibyte|kibibytes");
        Add("MiB", "data size", 1048576m, "mebibyte|mebibytes");
        Add("GiB", "data size", 1073741824m, "gibibyte|gibibytes");
        Add("TiB", "data size", 1099511627776m, "tebibyte|tebibytes");
        return units;
    }
}
