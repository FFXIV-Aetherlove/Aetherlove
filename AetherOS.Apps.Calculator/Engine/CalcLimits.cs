namespace AetherOS.Apps.Calculator.Engine;

/// <summary>Hard ceilings that keep a pathological expression from stalling the draw thread.</summary>
internal static class CalcLimits
{
    internal const int MaxSourceLength = 512;
    internal const int MaxTokens = 1024;
    internal const int MaxDepth = 64;
    internal const int MaxArguments = 8;
    internal const int MaxFactorial = 170;

    /// <summary>Ceiling on nCr/nPr operands. The result overflows long before this, but the loop is per
    /// evaluation and a graph evaluates once per plot column, so the iteration count is what needs the cap.</summary>
    internal const int MaxCombinatorial = 1000;
}
