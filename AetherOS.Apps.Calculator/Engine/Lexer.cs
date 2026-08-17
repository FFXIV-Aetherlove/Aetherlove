using System;
using System.Collections.Generic;
using System.Globalization;

namespace AetherOS.Apps.Calculator.Engine;

internal enum TokenKind
{
    Number,
    Function,
    Variable,
    Constant,
    Ans,
    Plus,
    Minus,
    Star,
    Slash,
    Caret,
    LParen,
    RParen,
    Comma,
    Bang,
    Percent,
    End,
}

internal readonly struct Token
{
    internal Token(TokenKind kind, double number, string text, FuncInfo? func)
    {
        Kind = kind;
        Number = number;
        Text = text;
        Func = func;
    }

    internal TokenKind Kind { get; }

    internal double Number { get; }

    internal string Text { get; }

    internal FuncInfo? Func { get; }

    internal static Token Simple(TokenKind kind)
    {
        return new Token(kind, 0d, string.Empty, null);
    }
}

internal static class Lexer
{
    private const int MaxNameDigits = 2;

    internal static bool TryTokenize(string source, List<Token> tokens, out string error)
    {
        error = CalcErrors.None;
        if (string.IsNullOrWhiteSpace(source))
        {
            error = CalcErrors.Syntax;
            return false;
        }

        if (source.Length > CalcLimits.MaxSourceLength)
        {
            error = CalcErrors.Syntax;
            return false;
        }

        var i = 0;
        while (i < source.Length)
        {
            if (tokens.Count > CalcLimits.MaxTokens)
            {
                error = CalcErrors.Syntax;
                return false;
            }

            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < source.Length && char.IsAsciiDigit(source[i + 1])))
            {
                if (!TryReadNumber(source, ref i, tokens, out error))
                {
                    return false;
                }

                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                if (!TryReadName(source, ref i, tokens, out error))
                {
                    return false;
                }

                continue;
            }

            var kind = Punctuation(c);
            if (kind == TokenKind.End)
            {
                if (c == 'π')
                {
                    tokens.Add(new Token(TokenKind.Constant, Math.PI, "pi", null));
                    i++;
                    continue;
                }

                if (c == '√' && Funcs.TryResolve("sqrt", out var root))
                {
                    tokens.Add(new Token(TokenKind.Function, 0d, root.Name, root));
                    i++;
                    continue;
                }

                error = CalcErrors.Syntax;
                return false;
            }

            tokens.Add(Token.Simple(kind));
            i++;
        }

        tokens.Add(Token.Simple(TokenKind.End));
        return true;
    }

    private static TokenKind Punctuation(char c)
    {
        switch (c)
        {
            case '+':
                return TokenKind.Plus;
            case '-':
            case '−':
                return TokenKind.Minus;
            case '*':
            case '×':
            case '·':
                return TokenKind.Star;
            case '/':
            case '÷':
                return TokenKind.Slash;
            case '^':
                return TokenKind.Caret;
            case '(':
                return TokenKind.LParen;
            case ')':
                return TokenKind.RParen;
            case ',':
                return TokenKind.Comma;
            case '!':
                return TokenKind.Bang;
            case '%':
                return TokenKind.Percent;
            default:
                return TokenKind.End;
        }
    }

    private static bool TryReadNumber(string source, ref int i, List<Token> tokens, out string error)
    {
        error = CalcErrors.Syntax;
        var start = i;
        var sawDigit = false;
        while (i < source.Length && char.IsAsciiDigit(source[i]))
        {
            i++;
            sawDigit = true;
        }

        if (i < source.Length && source[i] == '.')
        {
            i++;
            while (i < source.Length && char.IsAsciiDigit(source[i]))
            {
                i++;
                sawDigit = true;
            }
        }

        if (!sawDigit)
        {
            return false;
        }

        if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
        {
            var j = i + 1;
            if (j < source.Length && (source[j] == '+' || source[j] == '-'))
            {
                j++;
            }

            if (j < source.Length && char.IsAsciiDigit(source[j]))
            {
                while (j < source.Length && char.IsAsciiDigit(source[j]))
                {
                    j++;
                }

                i = j;
            }
        }

        if (!double.TryParse(source.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            error = CalcErrors.Overflow;
            return false;
        }

        error = CalcErrors.None;
        tokens.Add(new Token(TokenKind.Number, value, string.Empty, null));
        return true;
    }

    private static bool TryReadName(string source, ref int i, List<Token> tokens, out string error)
    {
        error = CalcErrors.None;
        var start = i;
        while (i < source.Length && char.IsAsciiLetter(source[i]))
        {
            i++;
        }

        var run = TakeFunctionDigits(source, ref i, source[start..i]);
        var callable = NextIsOpenParen(source, i);
        var at = 0;
        while (at < run.Length)
        {
            var rest = run[at..];
            if (callable && Funcs.TryResolve(rest, out var info))
            {
                tokens.Add(new Token(TokenKind.Function, 0d, info.Name, info));
                at = run.Length;
                continue;
            }

            if (rest.Length >= 3 && rest.StartsWith("ans", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(Token.Simple(TokenKind.Ans));
                at += 3;
                continue;
            }

            if (rest.Length >= 2 && rest.StartsWith("pi", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(TokenKind.Constant, Math.PI, "pi", null));
                at += 2;
                continue;
            }

            var ch = rest[0];
            if (ch == 'e')
            {
                tokens.Add(new Token(TokenKind.Constant, Math.E, "e", null));
                at++;
                continue;
            }

            if (ch == 'x' || (ch >= 'A' && ch <= 'Z'))
            {
                tokens.Add(new Token(TokenKind.Variable, 0d, ch.ToString(), null));
                at++;
                continue;
            }

            error = CalcErrors.Undefined;
            return false;
        }

        return true;
    }

    /// <summary>Pulls digits into a name only when they complete a callable function (log10, atan2), never otherwise.</summary>
    private static string TakeFunctionDigits(string source, ref int i, string run)
    {
        if (run.Length == 0)
        {
            return run;
        }

        for (var take = MaxNameDigits; take >= 1; take--)
        {
            var after = i + take;
            if (after > source.Length)
            {
                continue;
            }

            var allDigits = true;
            for (var k = 0; k < take; k++)
            {
                if (!char.IsAsciiDigit(source[i + k]))
                {
                    allDigits = false;
                    break;
                }
            }

            if (!allDigits)
            {
                continue;
            }

            if (after < source.Length && char.IsAsciiDigit(source[after]))
            {
                continue;
            }

            if (!NextIsOpenParen(source, after))
            {
                continue;
            }

            var extended = run + source[i..after];
            if (!EndsWithFunction(extended))
            {
                continue;
            }

            i = after;
            return extended;
        }

        return run;
    }

    private static bool EndsWithFunction(string text)
    {
        for (var at = 0; at < text.Length; at++)
        {
            if (Funcs.TryResolve(text[at..], out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NextIsOpenParen(string source, int from)
    {
        var j = from;
        while (j < source.Length && char.IsWhiteSpace(source[j]))
        {
            j++;
        }

        return j < source.Length && source[j] == '(';
    }
}
