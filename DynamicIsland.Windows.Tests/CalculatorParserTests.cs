using System.Globalization;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class CalculatorParserTests
{
    [Theory]
    [InlineData("4200/10", "420")]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("-2.5 + 3 * (4 - 1)", "6.5")]
    [InlineData("20 ÷ 4 × 3", "15")]
    [InlineData("5 mod 2", "1")]
    [InlineData("5%1000", "50")]
    [InlineData("5 % 1000", "50")]
    [InlineData("5% of 1000", "50")]
    [InlineData("5%of1000", "50")]
    [InlineData("5%", "0.05")]
    [InlineData("1000 * 5%", "50")]
    [InlineData("1000 / 5%", "20000")]
    [InlineData("1000 + 5%", "1050")]
    [InlineData("1000 - 5%", "950")]
    [InlineData("200 + 10% + 5%", "231")]
    [InlineData("100 + (5+5)%", "110")]
    [InlineData("100 + (10%)", "110")]
    [InlineData("200 + 1000*5%", "250")]
    [InlineData("5%(500+500)", "50")]
    [InlineData("-5%1000", "-50")]
    [InlineData("5% of -1000", "-50")]
    [InlineData("5%0", "0")]
    [InlineData("0%1000", "0")]
    [InlineData("2.5%200", "5")]
    [InlineData("5mod2", "1")]
    [InlineData("20 MOD 6", "2")]
    public void EvaluatesBasicExpressions(string expression, string expected)
    {
        Assert.True(CalculatorParser.TryEvaluate(expression, out var actual, out var error), error);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void DivisionByZeroReturnsHelpfulError()
    {
        Assert.False(CalculatorParser.TryEvaluate("1 / 0", out _, out var error));

        Assert.Equal("Cannot divide by zero.", error);
    }

    [Theory]
    [InlineData("5% of")]
    [InlineData("5%%")]
    [InlineData("5% of (1000")]
    [InlineData("5% of *1000")]
    public void IncompletePercentageExpressionsDoNotProduceMisleadingResults(string expression)
    { Assert.False(CalculatorParser.TryEvaluate(expression, out _, out var error)); Assert.NotNull(error); }

    [Fact]
    public void OnlyArithmeticLookingQueriesAreCalculatorCandidates()
    {
        Assert.True(CalculatorParser.IsCandidate("4200/10"));
        Assert.False(CalculatorParser.IsCandidate("pause timer"));
        Assert.False(CalculatorParser.IsCandidate(""));
    }
}
