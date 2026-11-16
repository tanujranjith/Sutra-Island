using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Views;
using WpfMath.Controls;
using Brushes = System.Windows.Media.Brushes;
using TextBlock = System.Windows.Controls.TextBlock;
using Control = System.Windows.Controls.Control;

namespace DynamicIsland.Windows.Infrastructure;

internal static class MathRenderingVerification
{
    public static async Task RunAsync()
    {
        if (!AppDataPaths.IsPreview) throw new InvalidOperationException("Math verification requires preview isolation.");
        var output = Path.Combine(AppDataPaths.Root, "captures"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool ok, string label)
        {
            checks.Add((ok ? "PASS " : "FAIL ") + label);
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            if (!ok) throw new InvalidOperationException(label);
        }
        var reply = new AssistantReplyView { BodyFontSize = 15, BodyLineHeight = 23, ReducedMotion = true };
        reply.Resources["QText"] = Brushes.WhiteSmoke;
        reply.Resources["QMuted"] = Brushes.LightGray;
        reply.Resources["QAccent"] = Brushes.MediumPurple;
        var window = new Window { Content = new Border { Padding = new Thickness(28), Child = reply }, Width = 820, Height = 650, Background = Brushes.Black, ShowInTaskbar = false };
        async Task Set(string raw)
        {
            reply.RawText = raw; reply.Text = ResponseTextFormatter.Format(raw);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        }
        var answer = (TextBlock)reply.FindName("Answer");
        IEnumerable<FormulaControl> Formulas() => Descendants(answer).OfType<FormulaControl>();
        try
        {
            window.Show();
            await Set(@"If you mean \(f(x)=\ln(3x+1)\), then by the chain rule:
\[\boxed{f'(x)=\frac{3}{3x+1}}\]
If you mean \(\ln(3x)+1\), the derivative is \(\frac{1}{x}\).");
            File.WriteAllLines(Path.Combine(output, "formula-diagnostics.txt"),
                Formulas().Select(f => $"{f.Formula} error={f.HasError} size={f.ActualWidth}x{f.ActualHeight} errors={string.Join(";", f.Errors.Select(e => e.Message))}")
                .Prepend($"Answer {answer.ActualWidth}x{answer.ActualHeight}; inlines: {string.Join(" | ", answer.Inlines.Select(i => i is Run r ? r.Text : i.GetType().Name))}"));
            Capture(window, Path.Combine(output, "derivative.png"));
            Check(Formulas().Count() == 4 && Formulas().All(f => !f.HasError && f.ActualHeight > 0 && f.ActualWidth > 0), "Derivative example renders inline logs, boxed derivative and stacked fractions");
            Check(answer.ActualHeight > 80, "Formula height participates in text layout without clipping fractions");
            Capture(window, Path.Combine(output, "derivative.png"));
            foreach (var latex in new[] { @"\sqrt{x^2+1}", @"\sum_{n=1}^{\infty}\frac{1}{n^2}", @"\int_0^1 x^2\,dx", @"\begin{pmatrix}1&2\\3&4\end{pmatrix}", @"\begin{aligned}a&=b+c\\d&=e\end{aligned}" })
            {
                await Set("\\[" + latex + "\\]");
                Check(Formulas().Count() == 1 && !Formulas().Single().HasError, "Renders " + latex);
            }
            await Set(@"Not supported: \(\sutraUnknownMacro{x}\).");
            Check(!Formulas().Any() && answer.Inlines.OfType<Run>().Any(run => run.Text.Contains(@"\sutraUnknownMacro")), "Unsupported commands preserve their source without breaking the reply");
            reply.Active = true; reply.Status = "Responding…";
            await Set(@"Streaming \(\frac{1}{");
            Check(!Formulas().Any(), "Incomplete streamed math remains readable until its closing delimiter arrives");
            await Set(@"Streaming \(\frac{1}{x}\).");
            Check(Formulas().Count() == 1 && ((FrameworkElement)reply.FindName("StreamingCaret")).Visibility == Visibility.Visible, "Completed streamed math renders and keeps the streaming caret");
            reply.Sources = [new QWebSource("https://example.com/chain-rule", "Chain rule", CitedText: "Use the chain rule.")];
            await Set(@"Use the chain rule. \(f'(x)=\frac{3}{3x+1}\)");
            Check(Formulas().Count() == 1 && answer.Inlines.OfType<Hyperlink>().Any(), "Math and clickable source citations coexist");
            reply.Active = false; reply.Sources = [];
            await Set(@"Wide: \[a+b+c+d+e+f+g+h+i+j+k+l+m+n+o+p+q+r+s+t=u\]");
            window.Width = 360;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            Check(Formulas().Count() == 1 && Descendants(answer).OfType<System.Windows.Controls.Viewbox>().All(v => v.ActualWidth <= answer.ActualWidth + 1), "Wide equations fit a narrow reply without horizontal clipping");
            reply.Resources["QText"] = Brushes.Black;
            Check(Formulas().All(f => f.Foreground == Brushes.Black), "Cached formulas follow light and custom theme foregrounds");
            await Set("Ordinary reply without math.");
            Check(!Formulas().Any() && reply.DisplayedText == reply.Text, "Ordinary replies retain the original text path");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Capture(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
