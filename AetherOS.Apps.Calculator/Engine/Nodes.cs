using System;

namespace AetherOS.Apps.Calculator.Engine;

/// <summary>Carried by reference through evaluation so a whole graph pass allocates nothing.</summary>
internal struct EvalContext
{
    internal CalcEnv Env;
    internal string Error;

    internal bool Fail(string token, out double value)
    {
        Error = token;
        value = 0d;
        return false;
    }
}

internal enum BinOp
{
    Add,
    Sub,
    Mul,
    Div,
    Pow,
}

internal abstract class Node
{
    internal abstract bool Eval(ref EvalContext ctx, out double value);
}

internal sealed class ConstNode : Node
{
    private readonly double _value;

    internal ConstNode(double value)
    {
        _value = value;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        value = _value;
        return true;
    }
}

internal sealed class VarNode : Node
{
    private readonly string _name;

    internal VarNode(string name)
    {
        _name = name;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        if (ctx.Env is null || !ctx.Env.Vars.TryGetValue(_name, out value))
        {
            return ctx.Fail(CalcErrors.Undefined, out value);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        return true;
    }
}

internal sealed class AnsNode : Node
{
    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        if (ctx.Env is null)
        {
            return ctx.Fail(CalcErrors.Undefined, out value);
        }

        value = ctx.Env.Ans;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        return true;
    }
}

internal sealed class NegNode : Node
{
    private readonly Node _inner;

    internal NegNode(Node inner)
    {
        _inner = inner;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        if (!_inner.Eval(ref ctx, out var v))
        {
            value = 0d;
            return false;
        }

        value = -v;
        return true;
    }
}

internal sealed class BinaryNode : Node
{
    private readonly BinOp _op;
    private readonly Node _left;
    private readonly Node _right;

    internal BinaryNode(BinOp op, Node left, Node right)
    {
        _op = op;
        _left = left;
        _right = right;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        value = 0d;
        if (!_left.Eval(ref ctx, out var a))
        {
            return false;
        }

        if (!_right.Eval(ref ctx, out var b))
        {
            return false;
        }

        switch (_op)
        {
            case BinOp.Add:
                value = a + b;
                break;
            case BinOp.Sub:
                value = a - b;
                break;
            case BinOp.Mul:
                value = a * b;
                break;
            case BinOp.Div:
                if (b == 0d)
                {
                    return ctx.Fail(CalcErrors.DivZero, out value);
                }

                value = a / b;
                break;
            case BinOp.Pow:
                return Funcs.Power(a, b, ref ctx, out value);
            default:
                return ctx.Fail(CalcErrors.Undefined, out value);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        return true;
    }
}

internal sealed class FactorialNode : Node
{
    private readonly Node _inner;

    internal FactorialNode(Node inner)
    {
        _inner = inner;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        if (!_inner.Eval(ref ctx, out var v))
        {
            value = 0d;
            return false;
        }

        if (v < 0d || Math.Abs(v - Math.Round(v)) > 1e-9)
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        // Bound before the cast: an out-of-range double to int conversion is unspecified, so a huge v could
        // otherwise land inside the ceiling instead of tripping it.
        if (v > CalcLimits.MaxFactorial)
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        var n = (int)Math.Round(v);

        var result = 1d;
        for (var i = 2; i <= n; i++)
        {
            result *= i;
        }

        if (double.IsInfinity(result))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        value = result;
        return true;
    }
}

internal sealed class PercentNode : Node
{
    private readonly Node _inner;

    internal PercentNode(Node inner)
    {
        _inner = inner;
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        if (!_inner.Eval(ref ctx, out var v))
        {
            value = 0d;
            return false;
        }

        value = v / 100d;
        return true;
    }
}

internal sealed class CallNode : Node
{
    private readonly FuncInfo _info;
    private readonly Node[] _args;
    private readonly double[] _buffer;

    internal CallNode(FuncInfo info, Node[] args)
    {
        _info = info;
        _args = args;
        _buffer = new double[args.Length];
    }

    internal override bool Eval(ref EvalContext ctx, out double value)
    {
        value = 0d;
        for (var i = 0; i < _args.Length; i++)
        {
            if (!_args[i].Eval(ref ctx, out _buffer[i]))
            {
                return false;
            }
        }

        if (!Funcs.Invoke(_info, _buffer, ref ctx, out value))
        {
            return false;
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        return true;
    }
}
