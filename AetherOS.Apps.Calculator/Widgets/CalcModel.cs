using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using AetherOS.Apps.Calculator.Engine;

namespace AetherOS.Apps.Calculator;

/// <summary>One finished line on the LCD: what was typed and what came back.</summary>
internal sealed record HistoryLine(string Source, string Result, bool IsError);

/// <summary>The plotted region in graph coordinates.</summary>
internal struct GraphWindow
{
    public double XMin;
    public double XMax;
    public double YMin;
    public double YMax;

    public static GraphWindow Standard => new()
    {
        XMin = -10d,
        XMax = 10d,
        YMin = -10d,
        YMax = 10d,
    };

    public readonly double Width => XMax - XMin;

    public readonly double Height => YMax - YMin;

    public readonly bool Valid =>
        double.IsFinite(XMin) && double.IsFinite(XMax) && double.IsFinite(YMin) && double.IsFinite(YMax)
        && XMax - XMin > 1e-9d && YMax - YMin > 1e-9d;
}

/// <summary>One Y= slot: its typed source, its compiled form and whether it is switched on.</summary>
internal sealed class GraphFunction
{
    public GraphFunction(int index, Vector4 color, Vector4 chip)
    {
        Index = index;
        Color = color;
        Chip = chip;
    }

    public int Index { get; }

    /// <summary>The ink the curve is drawn with, chosen to read on the pale LCD.</summary>
    public Vector4 Color { get; }

    /// <summary>The same slot's colour on the dark panels, where its plotting ink would disappear.</summary>
    public Vector4 Chip { get; }

    public string Source = string.Empty;

    public bool Enabled = true;

    public Expression? Compiled { get; private set; }

    public string? Error { get; private set; }

    public string Label => $"Y{Index + 1}";

    public bool Plotted => Enabled && Compiled is not null;

    public void Recompile()
    {
        var trimmed = Source.Trim();
        if (trimmed.Length == 0)
        {
            Compiled = null;
            Error = null;
            return;
        }
        if (Calc.TryCompile(trimmed, out var expr, out var error))
        {
            Compiled = expr;
            Error = null;
            return;
        }
        Compiled = null;
        Error = error;
    }
}

/// <summary>Maps the engine's lowercase error tokens onto localized lines. The engine never returns prose.</summary>
internal static class CalcErrorText
{
    public static string Text(Func<string, string> loc, string token) => loc(token switch
    {
        "syntax" => "os.calc_err_syntax",
        "domain" => "os.calc_err_domain",
        "divzero" => "os.calc_err_divzero",
        "overflow" => "os.calc_err_overflow",
        "undefined" => "os.calc_err_undefined",
        "depth" => "os.calc_err_depth",
        _ => "os.calc_err_generic",
    });

    public static bool KeepsEntry(string token) => token == "syntax";
}

/// <summary>Number formatting in the calculator's own voice: ten significant digits, scientific notation at
/// the extremes, invariant separators the way a device face has no locale.</summary>
internal static class CalcFormat
{
    public static string Number(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }
        if (double.IsInfinity(value))
        {
            return value > 0d ? "inf" : "-inf";
        }
        if (value == 0d)
        {
            return "0";
        }
        var abs = Math.Abs(value);
        if (abs >= 1e10d || abs < 1e-4d)
        {
            return value.ToString("0.#########E+0", CultureInfo.InvariantCulture);
        }
        return value.ToString("G10", CultureInfo.InvariantCulture);
    }

    public static string Axis(double value)
    {
        if (value == 0d)
        {
            return "0";
        }
        var abs = Math.Abs(value);
        if (abs >= 1e5d || abs < 1e-3d)
        {
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        }
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

/// <summary>Everything the three views share: the engine environment, the entry line, the history tape, the
/// four Y= slots, the graph window and the table setup.</summary>
internal sealed class CalcSession
{
    public const int FunctionCount = 4;
    private const int MaxHistory = 60;

    public const int EntryMaxLength = 256;

    /// <summary>Inks rather than screen colours: every curve is drawn on the pale green LCD, where a bright
    /// hue disappears.</summary>
    private static readonly Vector4[] PlotColors =
    [
        new(0.055f, 0.278f, 0.310f, 1f),
        new(0.427f, 0.196f, 0.031f, 1f),
        new(0.243f, 0.110f, 0.400f, 1f),
        new(0.435f, 0.075f, 0.180f, 1f),
    ];

    private static readonly Vector4[] ChipColors =
    [
        new(0.36f, 0.85f, 0.78f, 1f),
        new(0.98f, 0.72f, 0.30f, 1f),
        new(0.72f, 0.62f, 0.98f, 1f),
        new(0.96f, 0.48f, 0.55f, 1f),
    ];

    public CalcSession()
    {
        Functions = new GraphFunction[FunctionCount];
        for (var i = 0; i < FunctionCount; i++)
        {
            Functions[i] = new GraphFunction(i, PlotColors[i], ChipColors[i]);
        }
        Window = GraphWindow.Standard;
    }

    public CalcEnv Env { get; } = new();

    public List<HistoryLine> History { get; } = [];

    public GraphFunction[] Functions { get; }

    public GraphWindow Window;

    public double TableStart = 0d;

    public double TableStep = 1d;

    public string Entry = string.Empty;

    /// <summary>The status strip under the LCD tape: a stored-variable confirmation, a solver result, a
    /// refusal. Cleared by the next entry.</summary>
    public string? Status;

    public bool StatusIsError;

    public bool StorePending;

    public void Insert(string text)
    {
        if (Entry.Length + text.Length > EntryMaxLength)
        {
            return;
        }
        Entry += text;
    }

    public void Backspace()
    {
        if (Entry.Length == 0)
        {
            return;
        }
        Entry = Entry[..^1];
    }

    public void ToggleAngle()
    {
        Env.Angle = Env.Angle == AngleMode.Degrees ? AngleMode.Radians : AngleMode.Degrees;
        foreach (var fn in Functions)
        {
            fn.Recompile();
        }
    }

    public void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    /// <summary>Runs the entry line. A syntax error keeps the entry so it can be corrected; every other
    /// failure clears it the way a device does.</summary>
    public void Submit(Func<string, string> loc)
    {
        var source = Entry.Trim();
        if (source.Length == 0)
        {
            return;
        }
        SetStatus(null, false);
        if (!Calc.TryEvaluate(source, Env, out var value, out var error))
        {
            History.Add(new HistoryLine(source, CalcErrorText.Text(loc, error), true));
            Trim();
            if (!CalcErrorText.KeepsEntry(error))
            {
                Entry = string.Empty;
            }
            return;
        }
        Env.Ans = value;
        History.Add(new HistoryLine(source, CalcFormat.Number(value), false));
        Trim();
        Entry = string.Empty;
    }

    /// <summary>STO: banks the entry's value (or the last answer when the entry is empty) into A..Z.</summary>
    public void StoreInto(string variable, Func<string, string> loc)
    {
        StorePending = false;
        var source = Entry.Trim();
        var value = Env.Ans;
        if (source.Length > 0)
        {
            if (!Calc.TryEvaluate(source, Env, out value, out var error))
            {
                SetStatus(CalcErrorText.Text(loc, error), true);
                return;
            }
        }
        Env.Vars[variable] = value;
        Env.Ans = value;
        Entry = string.Empty;
        SetStatus(string.Format(CultureInfo.CurrentCulture, loc("os.calc_stored"), variable,
            CalcFormat.Number(value)), false);
    }

    public void ClearHistory()
    {
        History.Clear();
        SetStatus(null, false);
    }

    public bool AnyPlotted()
    {
        foreach (var fn in Functions)
        {
            if (fn.Plotted)
            {
                return true;
            }
        }
        return false;
    }

    public GraphFunction? FirstPlotted()
    {
        foreach (var fn in Functions)
        {
            if (fn.Plotted)
            {
                return fn;
            }
        }
        return null;
    }

    /// <summary>Evaluates a slot at one x without disturbing the stored variable the user may have set.</summary>
    public bool TrySample(GraphFunction fn, double x, out double y)
    {
        y = 0d;
        if (fn.Compiled is null)
        {
            return false;
        }
        var saved = Env.Vars.TryGetValue(CalcEnv.GraphVariable, out var previous) ? previous : 0d;
        Env.Vars[CalcEnv.GraphVariable] = x;
        try
        {
            if (!Calc.TryEvaluate(fn.Compiled, Env, out var value, out _))
            {
                return false;
            }
            if (!double.IsFinite(value))
            {
                return false;
            }
            y = value;
            return true;
        }
        finally
        {
            Env.Vars[CalcEnv.GraphVariable] = saved;
        }
    }

    private void Trim()
    {
        while (History.Count > MaxHistory)
        {
            History.RemoveAt(0);
        }
    }
}
