namespace AetherOS.Apps.Calculator.Engine;

/// <summary>A compiled expression, evaluated as often as a plot needs. Call nodes reuse one argument buffer,
/// so evaluation is single-threaded and non-reentrant: everything here runs on the draw thread.</summary>
public sealed class Expression
{
    internal Expression(string source, Node root)
    {
        Source = source;
        Root = root;
    }

    public string Source { get; }

    internal Node Root { get; }
}
