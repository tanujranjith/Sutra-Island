using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Infrastructure;

/// <summary>Turns common LaTeX math markup into readable Unicode for WPF text controls.</summary>
public static class MathMarkupFormatter
{
    private static readonly Regex MathRegion = new(
        @"\\\[(?<display>[\s\S]*?)\\\]|(?<!\\)\$\$(?<displayDollar>[\s\S]*?)(?<!\\)\$\$|\\\((?<inline>[\s\S]*?)\\\)|(?<!\\)\$(?!\$)(?<inlineDollar>[^\r\n]*?)(?<!\\)\$(?!\$)",
        RegexOptions.Compiled);

    private static readonly Regex Fractions = new(
        @"\\(?:dfrac|tfrac|frac)\s*\{(?<numerator>[^{}]*)\}\s*\{(?<denominator>[^{}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex SquareRoots = new(
        @"\\sqrt(?:\[(?<index>[^\]]+)\])?\{(?<value>[^{}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex FormattingCommands = new(
        @"\\(?:text|textrm|mathrm|mathbf|mathit|mathsf|mathtt|operatorname)\s*\{(?<value>[^{}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex GroupedScript = new(@"(?<operator>[_^])\{(?<value>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex SingleScript = new(@"(?<operator>[_^])(?<value>[A-Za-z0-9+\-=()])", RegexOptions.Compiled);

    private static readonly (string Markup, string Symbol)[] Symbols =
    [
        (@"\Leftrightarrow", "⇔"), (@"\leftrightarrow", "↔"), (@"\Rightarrow", "⇒"),
        (@"\rightarrow", "→"), (@"\leftarrow", "←"), (@"\longrightarrow", "⟶"),
        (@"\Longrightarrow", "⟹"), (@"\approx", "≈"), (@"\equiv", "≡"),
        (@"\notin", "∉"), (@"\subseteq", "⊆"), (@"\supseteq", "⊇"),
        (@"\cdots", "⋯"), (@"\ldots", "…"), (@"\dots", "…"),
        (@"\times", "×"), (@"\cdot", "·"), (@"\pm", "±"), (@"\mp", "∓"),
        (@"\leq", "≤"), (@"\geq", "≥"), (@"\neq", "≠"), (@"\ne", "≠"),
        (@"\le", "≤"), (@"\ge", "≥"), (@"\infty", "∞"), (@"\nabla", "∇"),
        (@"\forall", "∀"), (@"\exists", "∃"), (@"\therefore", "∴"),
        (@"\because", "∵"), (@"\land", "∧"), (@"\lor", "∨"), (@"\neg", "¬"),
        (@"\parallel", "∥"), (@"\perp", "⟂"), (@"\angle", "∠"),
        (@"\langle", "⟨"), (@"\rangle", "⟩"), (@"\vert", "|"),
        (@"\sum", "∑"), (@"\prod", "∏"), (@"\int", "∫"),
        (@"\Alpha", "Α"), (@"\Beta", "Β"), (@"\Gamma", "Γ"), (@"\Delta", "Δ"),
        (@"\Theta", "Θ"), (@"\Lambda", "Λ"), (@"\Sigma", "Σ"), (@"\Omega", "Ω"),
        (@"\alpha", "α"), (@"\beta", "β"), (@"\gamma", "γ"), (@"\delta", "δ"),
        (@"\epsilon", "ε"), (@"\varepsilon", "ϵ"), (@"\zeta", "ζ"), (@"\eta", "η"),
        (@"\theta", "θ"), (@"\vartheta", "ϑ"), (@"\iota", "ι"), (@"\kappa", "κ"),
        (@"\lambda", "λ"), (@"\mu", "μ"), (@"\nu", "ν"), (@"\xi", "ξ"),
        (@"\pi", "π"), (@"\rho", "ρ"), (@"\sigma", "σ"), (@"\tau", "τ"),
        (@"\upsilon", "υ"), (@"\phi", "φ"), (@"\varphi", "ϕ"), (@"\chi", "χ"),
        (@"\psi", "ψ"), (@"\omega", "ω"), (@"\ell", "ℓ"), (@"\hbar", "ℏ")
    ];

    private static readonly IReadOnlyDictionary<char, char> Subscripts = new Dictionary<char, char>
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
        ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['b'] = 'ᵦ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ',
        ['j'] = 'ⱼ', ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ',
        ['o'] = 'ₒ', ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ',
        ['u'] = 'ᵤ', ['v'] = 'ᵥ', ['x'] = 'ₓ', ['y'] = 'ᵧ'
    };

    private static readonly IReadOnlyDictionary<char, char> Superscripts = new Dictionary<char, char>
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
        ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ', ['d'] = 'ᵈ', ['e'] = 'ᵉ',
        ['f'] = 'ᶠ', ['g'] = 'ᵍ', ['h'] = 'ʰ', ['i'] = 'ⁱ', ['j'] = 'ʲ',
        ['k'] = 'ᵏ', ['l'] = 'ˡ', ['m'] = 'ᵐ', ['n'] = 'ⁿ', ['o'] = 'ᵒ',
        ['p'] = 'ᵖ', ['r'] = 'ʳ', ['s'] = 'ˢ', ['t'] = 'ᵗ', ['u'] = 'ᵘ',
        ['v'] = 'ᵛ', ['w'] = 'ʷ', ['x'] = 'ˣ', ['y'] = 'ʸ', ['z'] = 'ᶻ'
    };

    public static string Format(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var output = new StringBuilder(text.Length);
        var position = 0;
        foreach (Match match in MathRegion.Matches(text))
        {
            output.Append(text, position, match.Index - position);
            var isDisplay = match.Groups["display"].Success || match.Groups["displayDollar"].Success;
            var content = match.Groups["display"].Success ? match.Groups["display"].Value
                : match.Groups["displayDollar"].Success ? match.Groups["displayDollar"].Value
                : match.Groups["inline"].Success ? match.Groups["inline"].Value
                : match.Groups["inlineDollar"].Value;

            if (isDisplay && output.Length > 0 && output[^1] != '\n') output.AppendLine();
            output.Append(FormatExpression(content));
            if (isDisplay && (output.Length == 0 || output[^1] != '\n')) output.AppendLine();
            position = match.Index + match.Length;
        }
        output.Append(text, position, text.Length - position);

        var formatted = output.ToString()
            .Replace(@"\[", string.Empty, StringComparison.Ordinal)
            .Replace(@"\]", string.Empty, StringComparison.Ordinal)
            .Replace(@"\(", string.Empty, StringComparison.Ordinal)
            .Replace(@"\)", string.Empty, StringComparison.Ordinal);
        return Regex.Replace(formatted, @"(?:[ \t]*\r?\n){3,}", Environment.NewLine + Environment.NewLine).Trim();
    }

    private static string FormatExpression(string expression)
    {
        var formatted = expression.Replace(@"\\", Environment.NewLine, StringComparison.Ordinal)
            .Replace(@"\%", "%", StringComparison.Ordinal)
            .Replace(@"\$", "$", StringComparison.Ordinal)
            .Replace(@"\_", "_", StringComparison.Ordinal)
            .Replace(@"\{", "{", StringComparison.Ordinal)
            .Replace(@"\}", "}", StringComparison.Ordinal)
            .Replace(@"\&", "&", StringComparison.Ordinal);

        for (var pass = 0; pass < 8; pass++)
        {
            var next = FormattingCommands.Replace(formatted, "${value}");
            next = Fractions.Replace(next, match => $"({match.Groups["numerator"].Value})/({match.Groups["denominator"].Value})");
            next = SquareRoots.Replace(next, match => match.Groups["index"].Success
                ? $"√[{match.Groups["index"].Value}]({match.Groups["value"].Value})"
                : $"√({match.Groups["value"].Value})");
            if (next == formatted) break;
            formatted = next;
        }

        formatted = ReplaceAccent(formatted, @"\vec", "⃗");
        formatted = ReplaceAccent(formatted, @"\hat", "̂");
        formatted = ReplaceAccent(formatted, @"\bar", "̄");

        foreach (var (markup, symbol) in Symbols)
            formatted = formatted.Replace(markup, symbol, StringComparison.Ordinal);

        formatted = Regex.Replace(formatted, @"\\(?:left|right|displaystyle|textstyle|scriptstyle)\b", string.Empty);
        formatted = formatted.Replace(@"\qquad", " ", StringComparison.Ordinal)
            .Replace(@"\quad", " ", StringComparison.Ordinal)
            .Replace(@"\,", " ", StringComparison.Ordinal)
            .Replace(@"\;", " ", StringComparison.Ordinal)
            .Replace(@"\:", " ", StringComparison.Ordinal)
            .Replace(@"\!", string.Empty, StringComparison.Ordinal);

        formatted = GroupedScript.Replace(formatted, match => FormatScript(match.Groups["value"].Value,
            match.Groups["operator"].Value == "_"));
        formatted = SingleScript.Replace(formatted, match => FormatScript(match.Groups["value"].Value,
            match.Groups["operator"].Value == "_"));

        // Keep an unfamiliar command readable instead of showing a raw TeX escape.
        return Regex.Replace(formatted, @"\\([A-Za-z]+)", "$1").Trim();
    }

    private static string ReplaceAccent(string text, string command, string accent) =>
        Regex.Replace(text, Regex.Escape(command) + @"\s*\{(?<value>[^{}]+)\}", match => match.Groups["value"].Value + accent);

    private static string FormatScript(string value, bool subscript)
    {
        var symbols = subscript ? Subscripts : Superscripts;
        if (value.All(symbols.ContainsKey))
            return new string(value.Select(character => symbols[character]).ToArray());

        return subscript ? $"₍{value}₎" : $"⁽{value}⁾";
    }
}
