using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Calculator.Engine;

/// <summary>Compile and evaluate. Never throws: every failure comes back as a <see cref="CalcErrors"/> token.</summary>
public static class Calc
{
    private const int CacheSlots = 4;

    [ThreadStatic]
    private static Expression?[]? _cache;

    [ThreadStatic]
    private static int _cacheNext;

    public static bool TryCompile(string source, out Expression? expr, out string error)
    {
        expr = null;
        error = CalcErrors.None;
        if (source is null)
        {
            error = CalcErrors.Syntax;
            return false;
        }

        try
        {
            var tokens = new List<Token>();
            if (!Lexer.TryTokenize(source, tokens, out error))
            {
                return false;
            }

            if (!Parser.TryParse(tokens, out var root, out error))
            {
                return false;
            }

            expr = new Expression(source, root!);
            error = CalcErrors.None;
            return true;
        }
        catch (Exception)
        {
            expr = null;
            error = CalcErrors.Syntax;
            return false;
        }
    }

    public static bool TryEvaluate(Expression expr, CalcEnv env, out double value, out string error)
    {
        value = 0d;
        error = CalcErrors.None;
        if (expr is null || env is null)
        {
            error = CalcErrors.Undefined;
            return false;
        }

        var ctx = new EvalContext
        {
            Env = env,
            Error = CalcErrors.None,
        };

        try
        {
            if (!expr.Root.Eval(ref ctx, out var raw))
            {
                error = string.IsNullOrEmpty(ctx.Error) ? CalcErrors.Undefined : ctx.Error;
                value = 0d;
                return false;
            }

            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                error = CalcErrors.Overflow;
                value = 0d;
                return false;
            }

            value = raw;
            return true;
        }
        catch (DivideByZeroException)
        {
            error = CalcErrors.DivZero;
        }
        catch (OverflowException)
        {
            error = CalcErrors.Overflow;
        }
        catch (InsufficientExecutionStackException)
        {
            error = CalcErrors.Depth;
        }
        catch (Exception)
        {
            error = CalcErrors.Undefined;
        }

        value = 0d;
        return false;
    }

    public static bool TryEvaluate(string source, CalcEnv env, out double value, out string error)
    {
        value = 0d;
        error = CalcErrors.None;
        if (source is null)
        {
            error = CalcErrors.Syntax;
            return false;
        }

        var expr = Cached(source);
        if (expr is null)
        {
            if (!TryCompile(source, out var compiled, out error))
            {
                return false;
            }

            expr = compiled!;
            Remember(expr);
        }

        return TryEvaluate(expr, env, out value, out error);
    }

    private static Expression? Cached(string source)
    {
        var cache = _cache;
        if (cache is null)
        {
            return null;
        }

        for (var i = 0; i < cache.Length; i++)
        {
            var candidate = cache[i];
            if (candidate is not null && string.Equals(candidate.Source, source, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void Remember(Expression expr)
    {
        _cache ??= new Expression?[CacheSlots];
        _cache[_cacheNext] = expr;
        _cacheNext = (_cacheNext + 1) % CacheSlots;
    }
}
