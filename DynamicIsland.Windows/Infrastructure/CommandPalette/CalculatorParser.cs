using System.Globalization;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Evaluates basic arithmetic without executing user supplied code.</summary>
public static class CalculatorParser
{
    public static bool IsCandidate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || !expression.Any(char.IsAsciiDigit))
            return false;

        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (char.IsWhiteSpace(character) || char.IsAsciiDigit(character) ||
                character is '.' or '+' or '-' or '*' or '/' or '%' or '(' or ')' or '×' or '÷') continue;
            if (expression.AsSpan(index).StartsWith("mod", StringComparison.OrdinalIgnoreCase)) index += 2;
            else if (expression.AsSpan(index).StartsWith("of", StringComparison.OrdinalIgnoreCase)) index++;
            else return false;
        }
        return true;
    }

    public static bool TryEvaluate(string expression, out decimal result, out string? error)
    {
        result = 0;
        error = null;
        if (!IsCandidate(expression))
            return false;
        if (expression.Length > 256)
        {
            error = "Expression is too long.";
            return false;
        }

        try
        {
            result = new Parser(expression).Evaluate();
            return true;
        }
        catch (DivideByZeroException)
        {
            error = "Cannot divide by zero.";
        }
        catch (OverflowException)
        {
            error = "That result is too large.";
        }
        catch (FormatException)
        {
            error = "Complete the expression to calculate.";
        }

        return false;
    }

    public static string Format(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private sealed class Parser(string input)
    {
        private readonly record struct Operand(decimal Value, bool IsPercentage = false);
        private int _position;

        public decimal Evaluate()
        {
            var result = ParseExpression();
            SkipWhitespace();
            if (_position != input.Length)
                throw new FormatException();
            return result.Value;
        }

        private Operand ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+'))
                {
                    var right = ParseTerm();
                    value = new(value.Value + (right.IsPercentage ? value.Value * right.Value : right.Value));
                }
                else if (Take('-'))
                {
                    var right = ParseTerm();
                    value = new(value.Value - (right.IsPercentage ? value.Value * right.Value : right.Value));
                }
                else return value;
            }
        }

        private Operand ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Take('*') || Take('×')) value = new(value.Value * ParseUnary().Value);
                else if (Take('/') || Take('÷')) value = new(value.Value / ParseUnary().Value);
                else if (TakeWord("mod")) value = new(value.Value % ParseUnary().Value);
                else return value;
            }
        }

        private Operand ParseUnary()
        {
            SkipWhitespace();
            if (Take('+')) return ParseUnary();
            if (Take('-')) { var value = ParseUnary(); return value with { Value = -value.Value }; }
            var primary = ParsePrimary();
            SkipWhitespace();
            if (!Take('%')) return primary;
            var fraction = primary.Value / 100m;
            SkipWhitespace();
            // A following amount means "percent of"; otherwise this is a postfix percentage.
            if (TakeWord("of") || _position < input.Length &&
                (char.IsAsciiDigit(input[_position]) || input[_position] is '.' or '('))
                return new(fraction * ParseUnary().Value);
            return new(fraction, true);
        }

        private Operand ParsePrimary()
        {
            SkipWhitespace();
            if (Take('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!Take(')')) throw new FormatException();
                return value;
            }

            var start = _position;
            var sawDigit = false;
            var sawPoint = false;
            while (_position < input.Length)
            {
                var character = input[_position];
                if (char.IsAsciiDigit(character))
                {
                    sawDigit = true;
                    _position++;
                }
                else if (character == '.' && !sawPoint)
                {
                    sawPoint = true;
                    _position++;
                }
                else break;
            }

            if (!sawDigit || !decimal.TryParse(input.AsSpan(start, _position - start),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                throw new FormatException();
            return new(number);
        }

        private void SkipWhitespace()
        {
            while (_position < input.Length && char.IsWhiteSpace(input[_position]))
                _position++;
        }

        private bool Take(char expected)
        {
            if (_position >= input.Length || input[_position] != expected)
                return false;
            _position++;
            return true;
        }

        private bool TakeWord(string expected)
        {
            if (!input.AsSpan(_position).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) return false;
            var end = _position + expected.Length;
            if (end < input.Length && char.IsAsciiLetter(input[end])) return false;
            _position = end;
            return true;
        }
    }
}
