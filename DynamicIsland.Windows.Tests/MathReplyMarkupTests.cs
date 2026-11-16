using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class MathReplyMarkupTests
{
    [Theory]
    [InlineData(@"\boxed{f'(x)=\frac{3}{3x+1}}", @"f'(x)=\frac{3}{3x+1}", true)]
    [InlineData(@"\boxed {x}", "x", true)]
    [InlineData(@"\boxed{x}+1", @"\boxed{x}+1", false)]
    [InlineData(@"\boxed{x", @"\boxed{x", false)]
    public void OnlyUnwrapsAnEntireBalancedBox(string raw, string expected, bool boxed)
    {
        Assert.Equal((expected, boxed), MathReplyMarkup.UnwrapBox(raw));
    }
    [Theory]
    [InlineData(@"Before \(x^2\) after", false)]
    [InlineData(@"Before $x^2$ after", false)]
    [InlineData(@"Before \[x^2\] after", true)]
    [InlineData(@"Before $$x^2$$ after", true)]
    public void RecognizesStandardDelimiters(string raw, bool display)
    {
        var doc = MathReplyMarkup.Prepare(raw);
        var expression = Assert.Single(doc.Formulas);
        Assert.Equal("x^2", expression.Value.Latex);
        Assert.Equal(display, expression.Value.Display);
        Assert.Contains(expression.Key, doc.Text);
        Assert.Contains("Before", doc.Text);
        Assert.Contains("after", doc.Text);
    }

    [Fact]
    public void ProtectsNestedFractionAndBoxBeforePlainTextCleanup()
    {
        const string latex = @"\boxed{f'(x)=\frac{3}{3x+1}}";
        var doc = MathReplyMarkup.Prepare("**Chain rule**\n\n\\[" + latex + "\\]\n\nDone.");
        Assert.Equal(latex, Assert.Single(doc.Formulas).Value.Latex);
        Assert.DoesNotContain("**", doc.Text);
        Assert.EndsWith("Done.", doc.Text);
        Assert.DoesNotContain("boxed", doc.Text);
    }

    [Theory]
    [InlineData("```latex\n\\frac{1}{2} and $x$\n```")]
    [InlineData("~~~latex\n$x$\n~~~")]
    [InlineData("`$x$` and `\\(x\\)`")]
    [InlineData("```latex\n$x$")]
    [InlineData(@"It costs $5 and $10 today.")]
    [InlineData(@"It costs \$5 and \$10.")]
    [InlineData(@"Incomplete \(\frac{3}{3x+1}")]
    public void CodeCurrencyAndIncompleteMathStayText(string raw) => Assert.Empty(MathReplyMarkup.Prepare(raw).Formulas);

    [Fact]
    public void PreservesMathUnderscoresAndAcceptsSpacedParenthesisDelimiters()
    {
        var doc = MathReplyMarkup.Prepare(@"\( x_{i} + \mathbf{v} \)");
        Assert.Equal(@"x_{i} + \mathbf{v}", Assert.Single(doc.Formulas).Value.Latex);
    }

    [Fact]
    public void MathAfterCodeStillRendersAndExistingPrivateCharactersArePreserved()
    {
        var doc = MathReplyMarkup.Prepare("\uE000 `$code$` then $x$");
        var formula = Assert.Single(doc.Formulas);
        Assert.NotEqual('\uE000', formula.Key);
        Assert.StartsWith("\uE000", doc.Text);
        Assert.Equal("x", formula.Value.Latex);
    }
}
