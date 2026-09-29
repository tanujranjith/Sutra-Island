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
    [InlineData("5 % 2", "1")]
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

    [Fact]
    public void OnlyArithmeticLookingQueriesAreCalculatorCandidates()
    {
        Assert.True(CalculatorParser.IsCandidate("4200/10"));
        Assert.False(CalculatorParser.IsCandidate("pause timer"));
        Assert.False(CalculatorParser.IsCandidate(""));
    }
}
