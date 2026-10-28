using System.Globalization;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Solves single-variable linear equations without executing user input.</summary>
public static class AlgebraParser
{
    public static bool IsCandidate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains('=') || !input.Any(char.IsAsciiLetter)) return false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsAsciiLetter(c) && i + 1 < input.Length && char.IsAsciiLetter(input[i + 1])) return false;
            if (!char.IsWhiteSpace(c) && !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '+' or '-' or '*' or '/' or '×' or '÷' or '(' or ')' or '=')) return false;
        }
        return true;
    }

    public static bool TrySolve(string input, out string result, out string? error)
    {
        result = "";
        error = null;
        if (!IsCandidate(input)) return false;
        if (input.Length > 256) { error = "Equation is too long."; return false; }
        try
        {
            var parser = new Parser(input);
            var (left, right) = parser.Parse();
            var coefficient = left.Coefficient - right.Coefficient;
            var constant = right.Constant - left.Constant;
            if (coefficient == 0)
            {
                error = constant == 0 ? "This equation has infinitely many solutions." : "This equation has no solution.";
                return false;
            }
            result = $"{parser.Variable} = {CalculatorParser.Format(constant / coefficient)}";
            return true;
        }
        catch (DivideByZeroException) { error = "Cannot divide by zero."; }
        catch (OverflowException) { error = "That result is too large."; }
        catch (NotSupportedException) { error = "Only linear equations with one variable are supported."; }
        catch (FormatException) { error = "Complete the equation, for example 3x + 2 = 14."; }
        return false;
    }

    private readonly record struct Linear(decimal Coefficient, decimal Constant);

    private sealed class Parser(string input)
    {
        private int _position;
        public char Variable { get; private set; }

        public (Linear Left, Linear Right) Parse()
        {
            var left = Expression();
            Space();
            if (!Take('=')) throw new FormatException();
            var right = Expression();
            Space();
            if (_position != input.Length || Variable == '\0') throw new FormatException();
            return (left, right);
        }

        private Linear Expression()
        {
            var value = Term();
            while (true)
            {
                Space();
                if (Take('+')) { var r = Term(); value = new(value.Coefficient + r.Coefficient, value.Constant + r.Constant); }
                else if (Take('-')) { var r = Term(); value = new(value.Coefficient - r.Coefficient, value.Constant - r.Constant); }
                else return value;
            }
        }

        private Linear Term()
        {
            var value = Unary();
            while (true)
            {
                Space();
                if (Take('/') || Take('÷'))
                {
                    var r = Unary();
                    if (r.Coefficient != 0) throw new NotSupportedException();
                    value = new(value.Coefficient / r.Constant, value.Constant / r.Constant);
                }
                else if (Take('*') || Take('×') || _position < input.Length &&
                    (input[_position] == '(' || char.IsAsciiLetter(input[_position]) ||
                     char.IsAsciiDigit(input[_position]) && PreviousIsCloseParen()))
                {
                    var r = Unary();
                    if (value.Coefficient != 0 && r.Coefficient != 0) throw new NotSupportedException();
                    value = new(value.Coefficient * r.Constant + value.Constant * r.Coefficient, value.Constant * r.Constant);
                }
                else return value;
            }
        }

        private Linear Unary()
        {
            Space();
            if (Take('+')) return Unary();
            if (Take('-')) { var value = Unary(); return new(-value.Coefficient, -value.Constant); }
            if (Take('('))
            {
                var value = Expression(); Space();
                if (!Take(')')) throw new FormatException();
                return value;
            }
            if (_position < input.Length && char.IsAsciiLetter(input[_position]))
            {
                var variable = char.ToLowerInvariant(input[_position++]);
                if (Variable != '\0' && Variable != variable) throw new NotSupportedException();
                Variable = variable;
                return new(1, 0);
            }
            var start = _position;
            while (_position < input.Length && (char.IsAsciiDigit(input[_position]) || input[_position] == '.')) _position++;
            if (!decimal.TryParse(input.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) throw new FormatException();
            return new(0, number);
        }

        private void Space() { while (_position < input.Length && char.IsWhiteSpace(input[_position])) _position++; }
        private bool PreviousIsCloseParen()
        {
            var i = _position - 1;
            while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
            return i >= 0 && input[i] == ')';
        }
        private bool Take(char c) { if (_position >= input.Length || input[_position] != c) return false; _position++; return true; }
    }
}
