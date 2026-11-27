using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

/// <summary>The expression could not be understood or calculated. The message is safe to show to people.</summary>
public sealed class ExpressionException : ActionException
{
    public ExpressionException(string message) : base(message) { }
}

/// <summary>
/// A small, safe calculator: + - × ÷ ^ %, brackets, constants (pi, e) and functions (sqrt, abs, round, min, max, sin, cos, tan, log, ln …).
/// It is a hand-written parser; nothing is ever compiled or executed. Trigonometry uses degrees unless asked otherwise.
/// </summary>
public static class ExpressionEvaluator
{
    private const int MaxLength = 2000;
    private const int MaxDepth = 120;

    public static double Evaluate(string expression, bool degrees = true)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ThereIsNothingTo"));
        if (expression.Length > MaxLength) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ThatCalculationIsToo"));
        var p = new Parser(expression.Trim().TrimEnd('=').Trim(), degrees);
        return p.Run();
    }

    public static bool TryEvaluate(string expression, out double result, bool degrees = true)
    {
        try { result = Evaluate(expression, degrees); return true; }
        catch (ExpressionException) { result = 0; return false; }
    }

    /// <summary>True when the text looks like a calculation worth evaluating (has an operator or function, not just a number). For Quick Run.</summary>
    public static bool LooksLikeCalculation(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
        var hasDigit = text.Any(char.IsDigit);
        var hasOp = text.IndexOfAny(new[] { '+', '-', '*', '/', '^', '%', '×', '÷', '(', '√' }) > 0 || text.StartsWith('(') || text.StartsWith('√');
        var hasFn = Functions.Keys.Any(f => text.Contains(f + "(", StringComparison.OrdinalIgnoreCase));
        return (hasDigit && hasOp) || hasFn;
    }

    private static readonly Dictionary<string, (int Min, int Max)> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqrt"] = (1, 1), ["cbrt"] = (1, 1), ["abs"] = (1, 1), ["round"] = (1, 2), ["floor"] = (1, 1), ["ceil"] = (1, 1),
        ["ceiling"] = (1, 1), ["trunc"] = (1, 1), ["sign"] = (1, 1), ["min"] = (1, 64), ["max"] = (1, 64), ["sum"] = (1, 64),
        ["avg"] = (1, 64), ["average"] = (1, 64), ["sin"] = (1, 1), ["cos"] = (1, 1), ["tan"] = (1, 1), ["asin"] = (1, 1),
        ["acos"] = (1, 1), ["atan"] = (1, 1), ["log"] = (1, 2), ["log10"] = (1, 1), ["log2"] = (1, 1), ["ln"] = (1, 1),
        ["exp"] = (1, 1), ["pow"] = (2, 2), ["mod"] = (2, 2), ["fact"] = (1, 1),
    };

    private sealed class Parser
    {
        private readonly string _s;
        private readonly bool _deg;
        private int _i;
        private int _depth;

        public Parser(string s, bool degrees) { _s = Normalize(s); _deg = degrees; }

        private static string Normalize(string s) =>
            s.Replace('−', '-').Replace('–', '-').Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('∕', '/')
             .Replace("**", "^").Replace('π', 'p');

        public double Run()
        {
            var v = ParseExpression(out _);
            SkipWs();
            if (_i < _s.Length) throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_DidnTUnderstand0", _s[_i]));
            return Check(v);
        }

        private static double Check(double v)
        {
            if (double.IsNaN(v)) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ThatCalculationDoesnT"));
            if (double.IsInfinity(v)) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_TheAnswerIsToo"));
            return v == 0 ? 0 : v;
        }

        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

        private double ParseExpression(out bool endsWithPercent)
        {
            if (++_depth > MaxDepth) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ThatCalculationIsNested"));
            var left = ParseTerm(out endsWithPercent);
            while (true)
            {
                var c = Peek();
                if (c != '+' && c != '-') break;
                _i++;
                var right = ParseTerm(out var rightIsPercent);
                // "200 + 10%" means 200 plus 10% of 200.
                if (rightIsPercent) right = left * right;
                left = c == '+' ? left + right : left - right;
                endsWithPercent = false;
            }
            _depth--;
            return left;
        }

        private double ParseTerm(out bool endsWithPercent)
        {
            var left = ParseUnary(out endsWithPercent);
            while (true)
            {
                var c = Peek();
                if (c == '*' || c == '/')
                {
                    _i++;
                    var right = ParseUnary(out endsWithPercent);
                    if (c == '/')
                    {
                        if (right == 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTDivideBy"));
                        left /= right;
                    }
                    else left *= right;
                    endsWithPercent = false;
                }
                else if (c == '%' && StartsOperand(_i + 1, false))
                {
                    _i++;
                    var right = ParseUnary(out endsWithPercent);
                    if (right == 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTDivideBy"));
                    left = left - right * Math.Floor(left / right);
                    endsWithPercent = false;
                }
                else if ((c == 'x' || c == 'X') && !IdentContinues(_i + 1) && StartsOperand(_i + 1))
                {
                    _i++;
                    left *= ParseUnary(out endsWithPercent);
                    endsWithPercent = false;
                }
                else if (c == '(' || (IsIdentStart(c) && !(c is 'x' or 'X' && !IdentContinues(_i + 1))))
                {
                    left *= ParseUnary(out endsWithPercent); // implicit multiplication: 2(3+4), 2pi
                    endsWithPercent = false;
                }
                else break;
            }
            return left;
        }

        private bool IdentContinues(int at) => at < _s.Length && char.IsLetter(_s[at]);

        // Is there something that can start an operand after position 'from' (after spaces)?
        private bool StartsOperand(int from, bool allowSign = true)
        {
            while (from < _s.Length && char.IsWhiteSpace(_s[from])) from++;
            if (from >= _s.Length) return false;
            var c = _s[from];
            return char.IsDigit(c) || c == '.' || c == '(' || (allowSign && (c == '-' || c == '+')) || char.IsLetter(c) || c == '√';
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '√';

        private double ParseUnary(out bool endsWithPercent)
        {
            var c = Peek();
            if (c == '-') { _i++; var v = ParseUnary(out endsWithPercent); return -v; }
            if (c == '+') { _i++; return ParseUnary(out endsWithPercent); }
            return ParsePower(out endsWithPercent);
        }

        private double ParsePower(out bool endsWithPercent)
        {
            var b = ParsePostfix(out endsWithPercent);
            if (Peek() == '^')
            {
                _i++;
                var e = ParseUnary(out _);
                endsWithPercent = false;
                var r = Math.Pow(b, e);
                if (double.IsNaN(r) && b < 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTRaiseA"));
                return Check(r);
            }
            return b;
        }

        private double ParsePostfix(out bool endsWithPercent)
        {
            var v = ParsePrimary();
            endsWithPercent = false;
            while (true)
            {
                var c = Peek();
                if (c == '%' && !StartsOperand(_i + 1, false)) { _i++; v /= 100; endsWithPercent = true; }
                else if (c == '!' ) { _i++; v = Factorial(v); endsWithPercent = false; }
                else break;
            }
            return v;
        }

        private double ParsePrimary()
        {
            var c = Peek();
            if (c == '\0') throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_TheCalculationEndsToo"));
            if (c == '(')
            {
                _i++;
                var v = ParseExpression(out _);
                if (Peek() != ')') throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ABracketIsMissing"));
                _i++;
                return v;
            }
            if (char.IsDigit(c) || c == '.') return ParseNumber();
            if (c == '√')
            {
                _i++;
                var arg = ParsePower(out _);
                if (arg < 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTTakeThe"));
                return Math.Sqrt(arg);
            }
            if (char.IsLetter(c))
            {
                var start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                var name = _s[start.._i];
                if (Peek() == '(') return CallFunction(name);
                switch (name.ToLowerInvariant())
                {
                    case "pi": case "p": return Math.PI;
                    case "e": return Math.E;
                    case "tau": return Math.Tau;
                }
                if (Functions.ContainsKey(name)) throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_0NeedsBracketsLike", name, name));
                throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_DidnTUnderstand02", name));
            }
            throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_DidnTUnderstand0", c));
        }

        private double ParseNumber()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                var j = _i + 1;
                if (j < _s.Length && (_s[j] == '+' || _s[j] == '-')) j++;
                if (j < _s.Length && char.IsDigit(_s[j]))
                {
                    while (j < _s.Length && char.IsDigit(_s[j])) j++;
                    _i = j;
                }
            }
            var text = _s[start.._i];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_0IsnTA", text));
            return d;
        }

        private double CallFunction(string name)
        {
            if (!Functions.TryGetValue(name, out var arity))
                throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_DonTKnowThe", name));
            _i++; // (
            var args = new List<double>();
            if (Peek() != ')')
            {
                while (true)
                {
                    args.Add(ParseExpression(out _));
                    var c = Peek();
                    if (c == ',' || c == ';') { _i++; continue; }
                    break;
                }
            }
            if (Peek() != ')') throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_ABracketIsMissing2", name));
            _i++;
            if (args.Count < arity.Min || args.Count > arity.Max)
                throw new ExpressionException(arity.Min == arity.Max
                    ? Strings.Format("ExpressionEvaluator_Error_0Needs1Value", name, arity.Min, (arity.Min == 1 ? "" : "s"))
                    : Strings.Format("ExpressionEvaluator_Error_0NeedsBetween1", name, arity.Min, arity.Max));
            return Check(Apply(name.ToLowerInvariant(), args));
        }

        private double ToRad(double x) => _deg ? x * Math.PI / 180 : x;
        private double FromRad(double x) => _deg ? x * 180 / Math.PI : x;

        private double Apply(string f, List<double> a)
        {
            var x = a[0];
            switch (f)
            {
                case "sqrt":
                    if (x < 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTTakeThe"));
                    return Math.Sqrt(x);
                case "cbrt": return Math.Cbrt(x);
                case "abs": return Math.Abs(x);
                case "round":
                    {
                        var places = a.Count > 1 ? (int)Math.Clamp(a[1], -15, 15) : 0;
                        return places >= 0 ? Math.Round(x, places, MidpointRounding.AwayFromZero)
                            : Math.Round(x / Math.Pow(10, -places), MidpointRounding.AwayFromZero) * Math.Pow(10, -places);
                    }
                case "floor": return Math.Floor(x);
                case "ceil": case "ceiling": return Math.Ceiling(x);
                case "trunc": return Math.Truncate(x);
                case "sign": return Math.Sign(x);
                case "min": return a.Min();
                case "max": return a.Max();
                case "sum": return a.Sum();
                case "avg": case "average": return a.Average();
                case "sin": return CleanTrig(Math.Sin(ToRad(x)));
                case "cos": return CleanTrig(Math.Cos(ToRad(x)));
                case "tan":
                    {
                        var r = ToRad(x);
                        if (Math.Abs(Math.Cos(r)) < 1e-12) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_TheTangentOfThat"));
                        return CleanTrig(Math.Tan(r));
                    }
                case "asin":
                    if (x < -1 || x > 1) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_AsinOnlyWorksFor"));
                    return FromRad(Math.Asin(x));
                case "acos":
                    if (x < -1 || x > 1) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_AcosOnlyWorksFor"));
                    return FromRad(Math.Acos(x));
                case "atan": return FromRad(Math.Atan(x));
                case "log" when a.Count == 2:
                    if (x <= 0 || a[1] <= 0 || a[1] == 1) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_ThatLogarithmIsnT"));
                    return Math.Log(x) / Math.Log(a[1]);
                case "log": case "log10":
                    if (x <= 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanOnlyTakeThe"));
                    return Math.Log10(x);
                case "log2":
                    if (x <= 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanOnlyTakeThe"));
                    return Math.Log2(x);
                case "ln":
                    if (x <= 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanOnlyTakeThe"));
                    return Math.Log(x);
                case "exp": return Math.Exp(x);
                case "pow": return Math.Pow(x, a[1]);
                case "mod":
                    if (a[1] == 0) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_CanTDivideBy"));
                    return x - a[1] * Math.Floor(x / a[1]);
                case "fact": return Factorial(x);
                default: throw new ExpressionException(Strings.Format("ExpressionEvaluator_Error_DonTKnowThe2", f));
            }
        }

        // sin(180°) should be 0, not 1.2e-16.
        private static double CleanTrig(double v) => Math.Abs(v) < 1e-14 ? 0 : Math.Round(v, 14);

        private static double Factorial(double x)
        {
            if (x < 0 || x != Math.Floor(x)) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_FactorialsOnlyWorkFor"));
            if (x > 170) throw new ExpressionException(Strings.Get("ExpressionEvaluator_Error_TheAnswerIsToo"));
            double r = 1;
            for (var i = 2; i <= (int)x; i++) r *= i;
            return r;
        }
    }
}
