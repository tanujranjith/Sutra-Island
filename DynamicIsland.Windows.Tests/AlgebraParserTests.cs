using DynamicIsland.Windows.Infrastructure.CommandPalette;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class AlgebraParserTests
{
    [Theory]
    [InlineData("3x+44x=1253", "x = 26.659574468085106382978723404")]
    [InlineData("2(x+3)=14", "x = 4")]
    [InlineData("3x+2=2x+7", "x = 5")]
    [InlineData("-y / 2 = 1.5", "y = -3")]
    [InlineData("5 = (X + 1) / 2", "x = 9")]
    [InlineData("(x+1)2=6", "x = 2")]
    public void SolvesLinearEquations(string equation, string expected)
    {
        Assert.True(AlgebraParser.TrySolve(equation, out var result, out var error), error);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("x + 1 = x + 1", "infinitely many")]
    [InlineData("x + 1 = x + 2", "no solution")]
    [InlineData("x*x = 9", "linear")]
    [InlineData("1/x = 2", "linear")]
    [InlineData("x+y = 2", "one variable")]
    [InlineData("x/0 = 2", "divide by zero")]
    [InlineData("2(x+1 = 3", "Complete")]
    public void ReportsUnsupportedOrInvalidEquations(string equation, string expected)
    {
        Assert.False(AlgebraParser.TrySolve(equation, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("5% of 1000")]
    [InlineData("5 kg to lb")]
    [InlineData("convert 5 kg to lb")]
    [InlineData("pause timer")]
    [InlineData("10cm = 1m")]
    public void OtherQueriesAreNotEquations(string query) => Assert.False(AlgebraParser.IsCandidate(query));
}
