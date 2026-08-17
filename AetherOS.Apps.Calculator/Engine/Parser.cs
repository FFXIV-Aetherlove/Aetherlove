using System.Collections.Generic;

namespace AetherOS.Apps.Calculator.Engine;

/// <summary>
/// Recursive descent over the token list. Unary minus binds looser than exponentiation (-2^2 is -4),
/// exponentiation is right associative (2^3^2 is 512) and implicit multiplication sits at the same
/// level as an explicit one (2x, 2(3), (1+2)(3+4)).
/// </summary>
internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private int _depth;
    private string _error = CalcErrors.None;

    private Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    internal static bool TryParse(List<Token> tokens, out Node? root, out string error)
    {
        var parser = new Parser(tokens);
        root = parser.ParseExpression();
        if (root is null)
        {
            error = parser._error == CalcErrors.None ? CalcErrors.Syntax : parser._error;
            return false;
        }

        if (parser.Peek().Kind != TokenKind.End)
        {
            root = null;
            error = CalcErrors.Syntax;
            return false;
        }

        error = CalcErrors.None;
        return true;
    }

    private Token Peek()
    {
        return _pos < _tokens.Count ? _tokens[_pos] : Token.Simple(TokenKind.End);
    }

    private TokenKind PreviousKind()
    {
        return _pos > 0 ? _tokens[_pos - 1].Kind : TokenKind.End;
    }

    private void Advance()
    {
        if (_pos < _tokens.Count)
        {
            _pos++;
        }
    }

    private bool Enter()
    {
        _depth++;
        if (_depth > CalcLimits.MaxDepth)
        {
            _error = CalcErrors.Depth;
            return false;
        }

        return true;
    }

    private void Leave()
    {
        _depth--;
    }

    private Node? Fail(string error)
    {
        if (_error == CalcErrors.None)
        {
            _error = error;
        }

        return null;
    }

    private Node? ParseExpression()
    {
        if (!Enter())
        {
            return null;
        }

        var left = ParseTerm();
        if (left is null)
        {
            Leave();
            return null;
        }

        while (true)
        {
            var kind = Peek().Kind;
            if (kind != TokenKind.Plus && kind != TokenKind.Minus)
            {
                break;
            }

            Advance();
            var right = ParseTerm();
            if (right is null)
            {
                Leave();
                return null;
            }

            left = new BinaryNode(kind == TokenKind.Plus ? BinOp.Add : BinOp.Sub, left, right);
        }

        Leave();
        return left;
    }

    private Node? ParseTerm()
    {
        var left = ParseUnary();
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            var kind = Peek().Kind;
            if (kind == TokenKind.Star || kind == TokenKind.Slash)
            {
                Advance();
                var right = ParseUnary();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(kind == TokenKind.Star ? BinOp.Mul : BinOp.Div, left, right);
                continue;
            }

            if (StartsImplicitProduct(kind))
            {
                var right = ParseUnary();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(BinOp.Mul, left, right);
                continue;
            }

            break;
        }

        return left;
    }

    private bool StartsImplicitProduct(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Number:
                return PreviousKind() != TokenKind.Number;
            case TokenKind.Function:
            case TokenKind.Variable:
            case TokenKind.Constant:
            case TokenKind.Ans:
            case TokenKind.LParen:
                return true;
            default:
                return false;
        }
    }

    private Node? ParseUnary()
    {
        if (!Enter())
        {
            return null;
        }

        var kind = Peek().Kind;
        if (kind == TokenKind.Minus)
        {
            Advance();
            var inner = ParseUnary();
            Leave();
            return inner is null ? null : new NegNode(inner);
        }

        if (kind == TokenKind.Plus)
        {
            Advance();
            var inner = ParseUnary();
            Leave();
            return inner;
        }

        var node = ParsePower();
        Leave();
        return node;
    }

    private Node? ParsePower()
    {
        var baseNode = ParsePostfix();
        if (baseNode is null)
        {
            return null;
        }

        if (Peek().Kind != TokenKind.Caret)
        {
            return baseNode;
        }

        Advance();
        var exponent = ParseUnary();
        if (exponent is null)
        {
            return null;
        }

        return new BinaryNode(BinOp.Pow, baseNode, exponent);
    }

    private Node? ParsePostfix()
    {
        var node = ParsePrimary();
        if (node is null)
        {
            return null;
        }

        while (true)
        {
            var kind = Peek().Kind;
            if (kind == TokenKind.Bang)
            {
                Advance();
                node = new FactorialNode(node);
                continue;
            }

            if (kind == TokenKind.Percent)
            {
                Advance();
                node = new PercentNode(node);
                continue;
            }

            break;
        }

        return node;
    }

    private Node? ParsePrimary()
    {
        var token = Peek();
        switch (token.Kind)
        {
            case TokenKind.Number:
            case TokenKind.Constant:
                Advance();
                return new ConstNode(token.Number);
            case TokenKind.Variable:
                Advance();
                return new VarNode(token.Text);
            case TokenKind.Ans:
                Advance();
                return new AnsNode();
            case TokenKind.LParen:
                return ParseGroup();
            case TokenKind.Function:
                return ParseCall(token);
            default:
                return Fail(CalcErrors.Syntax);
        }
    }

    private Node? ParseGroup()
    {
        Advance();
        var inner = ParseExpression();
        if (inner is null)
        {
            return null;
        }

        if (Peek().Kind != TokenKind.RParen)
        {
            return Fail(CalcErrors.Syntax);
        }

        Advance();
        return inner;
    }

    private Node? ParseCall(Token token)
    {
        var info = token.Func;
        if (info is null)
        {
            return Fail(CalcErrors.Undefined);
        }

        Advance();
        if (Peek().Kind != TokenKind.LParen)
        {
            return Fail(CalcErrors.Syntax);
        }

        Advance();
        var args = new List<Node>();
        while (true)
        {
            if (args.Count >= CalcLimits.MaxArguments)
            {
                return Fail(CalcErrors.Syntax);
            }

            var arg = ParseExpression();
            if (arg is null)
            {
                return null;
            }

            args.Add(arg);
            if (Peek().Kind == TokenKind.Comma)
            {
                Advance();
                continue;
            }

            break;
        }

        if (Peek().Kind != TokenKind.RParen)
        {
            return Fail(CalcErrors.Syntax);
        }

        Advance();
        if (args.Count < info.MinArgs || args.Count > info.MaxArgs)
        {
            return Fail(CalcErrors.Syntax);
        }

        return new CallNode(info, args.ToArray());
    }
}
