using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using DynamicIsland.Windows.Infrastructure;
using WpfMath.Controls;
using Viewbox = System.Windows.Controls.Viewbox;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using Binding = System.Windows.Data.Binding;
using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Views;

public partial class AssistantReplyView
{
    private string? _mathSource;
    private MathReplyDocument _mathDocument = new("", new Dictionary<char, ReplyFormula>());
    private readonly Dictionary<char, (ReplyFormula Formula, double Size, InlineUIContainer Inline)> _formulaViews = [];

    private MathReplyDocument GetMathDocument()
    {
        var raw = RawText ?? "";
        if (raw != _mathSource)
        {
            _mathSource = raw;
            _mathDocument = MathReplyMarkup.Prepare(raw);
            foreach (var key in _formulaViews.Keys.Where(key => !_mathDocument.Formulas.ContainsKey(key)).ToArray()) _formulaViews.Remove(key);
        }
        return _mathDocument;
    }

    private void AddFormula(char token, ReplyFormula expression)
    {
        var size = BodyFontSize + (expression.Display ? 3 : 1);
        if (_formulaViews.TryGetValue(token, out var cached) && cached.Formula == expression && cached.Size == size)
        {
            Answer.Inlines.Add(cached.Inline);
            return;
        }
        // FormulaControl parses locally and catches unsupported or incomplete TeX.
        // Preserve that source verbatim rather than losing part of an equation.
        var (latex, boxed) = MathReplyMarkup.UnwrapBox(expression.Latex);
        // Map common amsmath environments to the renderer's native align/matrix.
        latex = Regex.Replace(latex, @"\\begin\{(?:aligned|align\*)\}", @"\begin{align}");
        latex = Regex.Replace(latex, @"\\end\{(?:aligned|align\*)\}", @"\end{align}");
        latex = Regex.Replace(latex, @"\\begin\{(?:gathered|gather\*?)\}", @"\matrix{");
        latex = Regex.Replace(latex, @"\\end\{(?:gathered|gather\*?)\}", "}");
        var formula = new FormulaControl { Scale = size, Formula = latex, Focusable = false, IsHitTestVisible = false };
        formula.SetResourceReference(Control.ForegroundProperty, "QText");
        if (formula.HasError)
        {
            Answer.Inlines.Add(new Run(expression.Latex) { FontFamily = new FontFamily("Consolas") });
            return;
        }
        var frame = new Border { Child = formula, BorderThickness = new Thickness(boxed ? 1 : 0), Padding = new Thickness(boxed ? 7 : 0) };
        frame.SetResourceReference(Border.BorderBrushProperty, "QText");
        var sizing = new Viewbox { Child = frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        sizing.SetBinding(MaxWidthProperty, new Binding(nameof(ActualWidth)) { Source = Answer });
        var host = new Border { Child = sizing, Padding = expression.Display ? new Thickness(2, 8, 2, 8) : new Thickness(1, 0, 1, 0) };
        System.Windows.Automation.AutomationProperties.SetName(host, expression.Latex);
        var inline = new InlineUIContainer(host) { BaselineAlignment = BaselineAlignment.Center };
        _formulaViews[token] = (expression, size, inline);
        Answer.Inlines.Add(inline);
    }
}
