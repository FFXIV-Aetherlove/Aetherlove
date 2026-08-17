using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Calculator.Engine;

public enum AngleMode
{
    Radians = 0,
    Degrees = 1,
}

/// <summary>The evaluation state an expression is measured against: angle unit, last answer and variables.</summary>
public sealed class CalcEnv
{
    /// <summary>The one lowercase variable, reserved for plotting.</summary>
    public const string GraphVariable = "x";

    public CalcEnv()
    {
        Vars = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Vars[c.ToString()] = 0d;
        }

        Vars[GraphVariable] = 0d;
    }

    public AngleMode Angle { get; set; }

    public double Ans { get; set; }

    public Dictionary<string, double> Vars { get; }
}
