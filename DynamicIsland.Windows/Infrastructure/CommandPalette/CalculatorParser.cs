using System.Globalization;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Evaluates basic arithmetic without executing user supplied code.</summary>
public static class CalculatorParser
{
    public static bool IsCandidate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || !expression.Any(char.IsAsciiDigit))
            return false;

        return expression.All(character =>
            char.IsWhiteSpace(character) || char.IsAsciiDigit(character) ||
            character is '.' or '+' or '-' or '*' or '/' or '%' or '(' or ')' or '×' or '÷');
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
        private int _position;

        public decimal Evaluate()
        {
            var result = ParseExpression();
            SkipWhitespace();
            if (_position != input.Length)
                throw new FormatException();
            return result;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+')) value += ParseTerm();
                else if (Take('-')) value -= ParseTerm();
                else return value;
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Take('*') || Take('×')) value *= ParseUnary();
                else if (Take('/') || Take('÷')) value /= ParseUnary();
                else if (Take('%')) value %= ParseUnary();
                else return value;
            }
        }

        private decimal ParseUnary()
        {
            SkipWhitespace();
            if (Take('+')) return ParseUnary();
            if (Take('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private decimal ParsePrimary()
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
            return number;
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
    }
}
