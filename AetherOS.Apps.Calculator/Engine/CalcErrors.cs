namespace AetherOS.Apps.Calculator.Engine;

/// <summary>
/// The complete set of tokens the engine may hand back as an error. Never prose, never localized:
/// the UI maps each token to a message of its own.
/// </summary>
public static class CalcErrors
{
    public const string None = "";
    public const string Syntax = "syntax";
    public const string Domain = "domain";
    public const string DivZero = "divzero";
    public const string Overflow = "overflow";
    public const string Undefined = "undefined";
    public const string Depth = "depth";
}
