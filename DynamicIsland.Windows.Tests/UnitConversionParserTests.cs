using System.Globalization;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class UnitConversionParserTests
{
    [Theory]
    [InlineData("12 in to ft", "1 ft")]
    [InlineData("12 inches to feet", "1 ft")]
    [InlineData("12in to ft", "1 ft")]
    [InlineData("  CONVERT 12 IN TO FT  ", "1 ft")]
    [InlineData("convert\t12 inches to feet", "1 ft")]
    [InlineData("12 in in ft", "1 ft")]
    [InlineData("12 in into ft", "1 ft")]
    [InlineData("2.54 cm to in", "1 in")]
    [InlineData("1 mile to meters", "1609.344 m")]
    [InlineData("6' to inches", "72 in")]
    [InlineData("12\" to feet", "1 ft")]
    [InlineData(".5 kg to g", "500 g")]
    [InlineData("1 lb to kg", "0.45359237 kg")]
    [InlineData("32 F to C", "0 °C")]
    [InlineData("212 fahrenheit to celsius", "100 °C")]
    [InlineData("-40 °C to °F", "-40 °F")]
    [InlineData("0 C to F", "32 °F")]
    [InlineData("273.15 K to C", "0 °C")]
    [InlineData("0 C to K", "273.15 K")]
    [InlineData("1 gallon to L", "3.785411784 L")]
    [InlineData("1 m^3 to L", "1000 L")]
    [InlineData("2 cups to fl oz", "16 US fl oz")]
    [InlineData("1 tbsp to tsp", "3 US tsp")]
    [InlineData("2 hours to minutes", "120 min")]
    [InlineData("90s to min", "1.5 min")]
    [InlineData("1 ft² to m²", "0.09290304 m²")]
    [InlineData("1 ft^2 to m2", "0.09290304 m²")]
    [InlineData("1 square foot to square meters", "0.09290304 m²")]
    [InlineData("1 acre to sq ft", "43560 ft²")]
    [InlineData("60 mph to km/h", "96.56064 km/h")]
    [InlineData("36 km/h to m/s", "10 m/s")]
    [InlineData("1 GB to MB", "1000 MB")]
    [InlineData("1 GiB to MiB", "1024 MiB")]
    [InlineData("1,000 mm to m", "1 m")]
    [InlineData("1e3 mm to m", "1 m")]
    [InlineData("0 in to ft", "0 ft")]
    [InlineData("-12 in to ft", "-1 ft")]
    [InlineData("+12 in to ft", "1 ft")]
    [InlineData("0.00000000000001 m to m", "1E-14 m")]
    public void ConvertsCommonUnits(string query, string expected)
    {
        Assert.True(UnitConversionParser.IsCandidate(query));
        Assert.True(UnitConversionParser.TryConvert(query, out var result, out var error), error);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("12 in to kg", "same kind")]
    [InlineData("12 unknown to ft", "Unknown unit")]
    [InlineData("12 in to unknown", "Unknown unit")]
    [InlineData("12 in to", "Use a conversion")]
    [InlineData("convert", "Use a conversion")]
    [InlineData("1,00 in to ft", "Use a conversion")]
    [InlineData("12 in to ft trailing text", "Unknown unit")]
    [InlineData("999999999999999999999999999999999 in to ft", "too large")]
    [InlineData("79228162514264337593543950335 km to mm", "too large")]
    public void RejectsInvalidQueriesWithGuidance(string query, string expectedMessage)
    {
        Assert.False(UnitConversionParser.TryConvert(query, out var result, out var error));
        Assert.Empty(result);
        Assert.Contains(expectedMessage, error);
    }

    [Theory]
    [InlineData("pause timer 10 min")]
    [InlineData("timer 12 minutes")]
    [InlineData("ask convert inches to feet")]
    [InlineData("2 + 3 * 4")]
    [InlineData("4200/10")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesCommandsAndArithmeticAlone(string? query) =>
        Assert.False(UnitConversionParser.IsCandidate(query));

    [Fact]
    public void UsesInvariantNumbersRegardlessOfSystemCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(UnitConversionParser.TryConvert("1.5 m to cm", out var result, out _));
            Assert.Equal("150 cm", result);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RejectsUnboundedInput()
    {
        Assert.False(UnitConversionParser.TryConvert("convert " + new string('1', 300), out _, out var error));
        Assert.Equal("Conversion is too long.", error);
    }
}
